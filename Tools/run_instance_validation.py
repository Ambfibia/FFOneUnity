"""Run the independent instance matrix without replaying combat/economy/social fixtures.

Every mutable server file and database stays in one owned ignored target directory.
Existing source portal actors and gates are preserved unchanged.
"""
from datetime import datetime
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent / 'LiveGameplay'))
import instances
from run_live_gameplay import ROOT, SERVER, HARNESS, port, run, player_content_hashes


def source_hashes():
    paths = [ROOT / 'Tools/LiveGameplay/instances.py',
             ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayInstanceSmoke.cs',
             ROOT / 'Assets/FFOne/Runtime/World/WorldStreamer.cs', ROOT / 'Assets/FFOne/Runtime/World/WorldTravelCatalog.cs',
             SERVER / 'src/chunk.rs', SERVER / 'src/server/shard/pc.rs', SERVER / 'src/server/shard/transport.rs']
    paths += sorted((ROOT / 'Tools/LiveGameplay').glob('Program.Instance*.cs'))
    paths += sorted((ROOT / 'Tools/LiveGameplay').glob('instance_*.py'))
    paths += sorted((ROOT / 'Assets/FFOne/Runtime/UI/Gameplay').glob('LiveGameplayInstanceSmoke.*.cs'))
    paths += [SERVER / 'src/server/shard/mission.rs', SERVER / 'src/mission.rs', SERVER / 'src/entity/player.rs',
              ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayMissionState.cs',
              ROOT / 'Assets/FFOne/Runtime/World/WorldMissionCatalog.cs']
    return {str(path.relative_to(ROOT.parent)): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path)
    parser.add_argument('--reuse-folder', type=Path, help='Reuse only a stopped isolated unity-gameplay-* server fixture.')
    parser.add_argument('--skip-build', action='store_true', help='Use the already validated harness and server binaries.')
    parser.add_argument('--maps', help='Comma-separated map IDs for a focused matrix rerun.')
    parser.add_argument('--result-path', type=Path, default=ROOT / 'Logs/InstanceValidationResult.json',
                        help='Summary destination; use distinct paths when running independent map partitions.')
    parser.add_argument('--noncombat', action='store_true', help='Omit hostile mob spawns only in the isolated fixture; preserve portal NPCs and XDT gates.')
    parser.add_argument('--scene-viewpoints', type=Path, help='Editor-exported authored renderer anchors for scene-only camera QA.')
    parser.add_argument('--viewpoint-index', type=int, default=0)
    parser.add_argument('--require-routes', action='store_true',
                        help='Reject incomplete plans and require a successful server route and player route for every selected map.')
    args = parser.parse_args()
    target = (SERVER / 'target').resolve()
    if args.reuse_folder:
        folder = args.reuse_folder.resolve()
        if folder.parent != target or not folder.name.startswith('unity-gameplay-') or not folder.is_dir():
            raise ValueError('Reuse requires an existing isolated RustyFusion/target/unity-gameplay-* directory.')
        if (folder / 'instance-running.lock').exists():
            raise ValueError('An instance runner owns this folder or did not finish shutdown; use a new isolated folder.')
        # Refuse to share an active fixture with a runner still using its database.
        for report in ('instance-result.json', 'result.json'):
            report_path = folder / report
            if report_path.exists():
                prior = json.loads(report_path.read_text('utf-8'))
                pid = prior.get('ownedProcessId')
                if pid and not prior.get('serverStopped', False):
                    raise ValueError('The recorded fixture owner has not confirmed server shutdown.')
        history = folder / 'previous-runs' / (datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
        history.mkdir(parents=True)
        for name in ('instance-plan.json', 'instance-create.json', 'instance-network.json', 'instance-result.json', 'instance-server.log', 'instance-player.log'):
            if (folder / name).exists():
                shutil.copy2(folder / name, history / name)
    else:
        folder = target / ('unity-gameplay-instances-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
        folder.mkdir(parents=True)
    tables = folder / 'tabledata'
    if not tables.exists():
        shutil.copytree(SERVER / 'tabledata', tables, ignore=shutil.ignore_patterns('.git'))
    for name in ('NPCs.json', 'xdt.json'):
        if hashlib.sha256((tables / name).read_bytes()).digest() != hashlib.sha256((SERVER / 'tabledata' / name).read_bytes()).digest():
            raise ValueError('Reused table data differs from current original sources; use a new isolated folder.')
    plan_path = folder / 'instance-plan.json'
    matrix = instances.plan(SERVER / 'tabledata', plan_path, {int(m) for m in args.maps.split(',')} if args.maps else None)
    if args.require_routes:
        blocked = [c['map'] for c in matrix['cases'] if c['disposition'] != 'eligible']
        if blocked:
            raise ValueError('Full server-route acceptance cannot include scene-only maps: ' + str(blocked))
        if not args.player:
            raise ValueError('Full server-route acceptance also requires --player.')
    if args.scene_viewpoints:
        viewpoints = json.loads(args.scene_viewpoints.read_text('utf-8-sig'))
        for case in matrix['cases']:
            if case['disposition'] == 'eligible':
                continue
            view = next((v for v in viewpoints['maps'] if v['map'] == case['map']), None)
            if view is None:
                continue
            if view['cell'] != case['cell']:
                raise ValueError('Scene viewpoint must match the original map cell: ' + str(case['map']))
            candidate = view['candidates'][args.viewpoint_index]
            case['scenePoint'] = candidate['point']
            case['sceneViewpoint'] = candidate
        plan_path.write_text(json.dumps(matrix, indent=2), encoding='utf-8')
    combat_fixture = dict(mode='original-hostile-spawns')
    if args.noncombat:
        xdt = json.loads((tables / 'xdt.json').read_text('utf-8'))
        hostile = {n['m_iNpcNumber'] for n in xdt['m_pNpcTable']['m_pNpcData'] if n['m_iTeam'] == 2}
        mob_path = tables / 'mobs.json'
        mobs = json.loads((SERVER / 'tabledata/mobs.json').read_text('utf-8'))
        before = {key: len(mobs[key]) for key in ('mobs', 'groups')}
        # Mission 842's mandatory escort encounter remains a real hostile kill.
        mission_hostiles = {3337}
        mobs['mobs'] = {key: row for key, row in mobs['mobs'].items()
                        if row['iNPCType'] not in hostile or row['iNPCType'] in mission_hostiles}
        mobs['groups'] = {key: row for key, row in mobs['groups'].items()
                          if (row['iNPCType'] not in hostile or row['iNPCType'] in mission_hostiles) and
                          not any(f['iNPCType'] in hostile and f['iNPCType'] not in mission_hostiles for f in row.get('aFollowers', []))}
        mob_path.write_text(json.dumps(mobs), encoding='utf-8')
        combat_fixture = dict(mode='hostile-spawns-omitted-for-scene-acceptance',
                              omitted={key: before[key] - len(mobs[key]) for key in before},
                              originalSha256=hashlib.sha256((SERVER / 'tabledata/mobs.json').read_bytes()).hexdigest(),
                              fixtureSha256=hashlib.sha256(mob_path.read_bytes()).hexdigest())
        combat_fixture['retainedMissionHostileTypes'] = sorted(mission_hostiles)
    elif (tables / 'mobs.json').read_bytes() != (SERVER / 'tabledata/mobs.json').read_bytes():
        raise ValueError('A noncombat fixture cannot be reused as an original-combat fixture.')
    if not args.skip_build:
        run(['cargo', 'build', '--bin', 'hybrid', '--locked'], cwd=SERVER)
        run(['dotnet', 'build', str(ROOT / 'Tools/LiveGameplay/LiveGameplay.csproj'), '--nologo', '-v', 'quiet'], cwd=ROOT)
    login, shard = port(), port()
    while login == shard:
        shard = port()
    database = folder / 'database.db'
    config = folder / 'config.toml'
    if not config.exists():
        config.write_text((SERVER / 'config.toml').read_text('utf-8'), encoding='utf-8')
    command = [str(SERVER / 'target/debug/hybrid.exe'), 'config=' + str(config),
               'general.enable_tui=false', 'general.db_path=' + str(database),
               'general.table_data_path=' + str(tables), 'general.combined_log_path=',
               'login.listen_addr=127.0.0.1:' + str(login), 'login.allow_plaintext_passwords=true',
               'shard.listen_addr=127.0.0.1:' + str(shard), 'shard.external_addr=127.0.0.1:' + str(shard),
               'shard.login_server_addr=127.0.0.1:' + str(login), 'general.live_check_time=5']
    result = dict(passed=False, directory=str(folder), database=str(database), loginPort=login, shardPort=shard,
                  fixture='Original portal NPC placements and XDT; normal isolated accounts with saved level/task/key prerequisites.',
                  instancePlan={k: v for k, v in matrix.items() if k != 'cases'}, serverIncludesExistingWorkingChanges=True,
                  serverBinarySha256=hashlib.sha256((SERVER / 'target/debug/hybrid.exe').read_bytes()).hexdigest(),
                  harnessAssemblySha256=hashlib.sha256(HARNESS.read_bytes()).hexdigest())
    result['sourceHashes'] = source_hashes()
    result['combatFixture'] = combat_fixture
    if args.scene_viewpoints:
        result['sceneViewpointFile'] = str(args.scene_viewpoints.resolve())
        result['sceneViewpointSha256'] = hashlib.sha256(args.scene_viewpoints.read_bytes()).hexdigest()
        result['sceneViewpointIndex'] = args.viewpoint_index
    print('Independent instance matrix:', folder, flush=True)
    owner_lock = folder / 'instance-running.lock'
    with owner_lock.open('x', encoding='utf-8') as owner:
        owner.write(str(os.getpid()))
    with (folder / 'instance-server.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen(command, cwd=SERVER, stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
        result['ownedProcessId'] = process.pid
        result['serverStopped'] = False
        (folder / 'instance-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        try:
            deadline = time.monotonic() + 90
            while True:
                if process.poll() is not None:
                    raise RuntimeError('Isolated server exited during startup.')
                startup = (folder / 'instance-server.log').read_text('utf-8', errors='replace')
                if 'Connected to database' in startup and ' as shard ' in startup:
                    with socket.create_connection(('127.0.0.1', login), timeout=1):
                        break
                if time.monotonic() >= deadline:
                    raise TimeoutError('Isolated database/shard handshake did not finish.')
                time.sleep(.2)
            # The first inter-server heartbeat can expose a failed handshake even
            # after both sockets listened. Reject it before testing any character.
            time.sleep(6)
            startup = (folder / 'instance-server.log').read_text('utf-8', errors='replace')
            if process.poll() is not None or 'No login server connected' in startup or 'disconnected' in startup:
                raise RuntimeError('Shard/login heartbeat failed during startup; instance exercise did not begin.')
            run(['dotnet', str(HARNESS), 'create-instances', str(login), str(folder / 'instance-create.json'), str(plan_path)], cwd=ROOT, timeout=600)
            time.sleep(.5)
            if 'No login server connected' in (folder / 'instance-server.log').read_text('utf-8', errors='replace'):
                raise RuntimeError('Shard/login connection failed during startup; instance exercise did not begin.')
            instances.seed(database, plan_path)
            run(['dotnet', str(HARNESS), 'exercise-instances', str(login), str(folder / 'instance-network.json'), str(plan_path)], cwd=ROOT, timeout=3600)
            result['instanceNetwork'] = json.loads((folder / 'instance-network.json').read_text('utf-8'))
            result['passed'] = result['instanceNetwork']['passed']
            if args.player:
                time.sleep(.5)
                instances.seed(database, plan_path)
                profile = folder / ('instance-profile-' + datetime.now().strftime('%H%M%S'))
                profile.mkdir()
                result['playerProfile'] = str(profile)
                player = args.player.resolve()
                assembly = player.parent / (player.stem + '_Data') / 'Managed/FFOne.Runtime.dll'
                result['playerExecutable'] = str(player)
                result['playerExecutableSha256'] = hashlib.sha256(player.read_bytes()).hexdigest()
                result['playerAssemblySha256'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashes'] = player_content_hashes(player)
                result['playerPlanSha256'] = hashlib.sha256(plan_path.read_bytes()).hexdigest()
                # Retain exact build identity even if the player fails or is
                # interrupted before producing its final matrix report.
                (folder / 'instance-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
                environment = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:' + str(login),
                                   FFONE_DEV_PROFILES=str(profile), FFONE_INSTANCE_PLAN=str(plan_path))
                startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = subprocess.SW_HIDE
                run([str(player), '-batchmode', '--live-instance-smoke', '-screen-width', '1280', '-screen-height', '720',
                     '-logFile', str(folder / 'instance-player.log')], cwd=ROOT, env=environment, startupinfo=startup, timeout=3600)
                result['instancePlayer'] = json.loads((profile / 'instances-result.json').read_text('utf-8'))
                result['passed'] = result['passed'] and result['instancePlayer']['passed']
                content_errors = {str(row['map']): row['contentErrors'] for row in result['instancePlayer']['results']
                                  if row.get('contentErrors')}
                result['contentComplete'] = not content_errors
                result['contentErrors'] = content_errors
                result['passed'] &= not content_errors
                if args.require_routes:
                    result['passed'] &= (result['instanceNetwork']['coverageComplete'] and
                                         result['instancePlayer']['routeCoverageComplete'] and
                                         len(result['instancePlayer']['results']) == matrix['maps'])
                result['playerAssemblySha256After'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
                if result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']:
                    raise AssertionError('Native instance Addressables content changed during acceptance.')
                if result['playerAssemblySha256After'] != result['playerAssemblySha256']:
                    raise AssertionError('Native instance player assembly changed while acceptance was running.')
                text = (folder / 'instance-player.log').read_text('utf-8', errors='replace')
                result['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', text, re.MULTILINE)
                if result['playerExceptions']:
                    raise AssertionError('Unexpected C# exception in native instance matrix.')
            if source_hashes() != result['sourceHashes']:
                raise AssertionError('Instance sources changed while acceptance was running.')
        except Exception as error:
            result['passed'] = False; result['error'] = str(error)
            raise
        finally:
            if process.poll() is None:
                process.terminate(); process.wait(timeout=20)
            result['serverStopped'] = process.poll() is not None
            for key, name in [('instanceCreate', 'instance-create.json'), ('instanceNetwork', 'instance-network.json')]:
                if (folder / name).exists():
                    result[key] = json.loads((folder / name).read_text('utf-8'))
            if result.get('playerProfile') and (Path(result['playerProfile']) / 'instances-result.json').exists():
                result['instancePlayer'] = json.loads((Path(result['playerProfile']) / 'instances-result.json').read_text('utf-8'))
            if result.get('playerExecutable'):
                player = Path(result['playerExecutable'])
                assembly = player.parent / (player.stem + '_Data') / 'Managed/FFOne.Runtime.dll'
                result['playerAssemblySha256After'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
                if result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']:
                    result['passed'] = False
                if result['playerAssemblySha256After'] != result['playerAssemblySha256']:
                    result['passed'] = False
                player_log = (folder / 'instance-player.log').read_text('utf-8', errors='replace')
                result['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', player_log, re.MULTILINE)
                if result['playerExceptions']:
                    result['passed'] = False
            result['serverScriptErrors'] = [line for line in (folder / 'instance-server.log').read_text('utf-8', errors='replace').splitlines() if 'AI script error' in line]
            result['serverPanics'] = [line for line in (folder / 'instance-server.log').read_text('utf-8', errors='replace').splitlines() if 'panicked' in line]
            if result['serverScriptErrors'] or result['serverPanics']:
                result['passed'] = False
            (folder / 'instance-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
            args.result_path.resolve().write_text(json.dumps(result, indent=2), encoding='utf-8')
            print('Instance result:', folder / 'instance-result.json', flush=True)
            if result['serverStopped']:
                owner_lock.unlink()
    if not result['passed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
