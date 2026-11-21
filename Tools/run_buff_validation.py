"""Run real buff transactions and optional rendered Unity player acceptance."""
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

from run_live_gameplay import ROOT, SERVER, HARNESS, port, run, player_content_hashes
sys.path.insert(0, str(ROOT / 'Tools/LiveGameplay'))
import buffs


def source_hashes():
    paths = list((ROOT / 'Assets/FFOne/Runtime/Network').glob('*.cs'))
    paths += list((ROOT / 'Assets/FFOne/Runtime/UI').glob('Frontend*.cs'))
    paths += list((ROOT / 'Assets/FFOne/Content/UI').rglob('*.uss'))
    paths += list((ROOT / 'Assets/FFOne/Content/UI').rglob('*.uxml'))
    paths += [ROOT / relative for relative in ['Tools/run_buff_validation.py', 'Tools/LiveGameplay/buffs.py', 'Tools/LiveGameplay/Program.Buffs.cs',
        'Tools/LiveGameplay/Program.cs', 'Tools/LiveGameplay/LiveGameplay.csproj',
        'Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayBuffSmoke.cs',
        'Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayPlayerSmoke.cs', 'Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayInstanceSmoke.cs',
        'Assets/FFOne/Runtime/UI/FrontendBuffEffects.cs', 'Assets/FFOne/Runtime/UI/FrontendGameplay.cs', 'Assets/FFOne/Runtime/UI/FrontendWorldFlow.cs',
        'Assets/FFOne/Runtime/UI/Gameplay/GameplayBuffState.cs', 'Assets/FFOne/Runtime/UI/Gameplay/GameplayLiveState.cs',
        'Assets/FFOne/Runtime/UI/Gameplay/GameplayUiCatalog.cs', 'Assets/FFOne/Runtime/World/NativeBuffPresentation.cs',
        'Assets/FFOne/Runtime/World/WorldSkillCatalog.cs', 'Assets/FFOne/Runtime/UI/Gameplay/GameplayVehicle.cs',
        'Assets/FFOne/Runtime/UI/RetrobutionAudioController.cs', 'Assets/FFOne/Runtime/Characters/Humanoids/NativePlayerAnimator.cs',
        'Assets/FFOne/Runtime/Characters/Humanoids/NativeAvatarEmote.cs',
        'Assets/FFOne/Runtime/UI/Gameplay/GameplayNanoRenderStage.cs',
        'Assets/FFOne/Runtime/UI/SettingsPreferences.cs', 'Assets/FFOne/Runtime/UI/SettingsWindow.cs',
        'Assets/FFOne/Runtime/Characters/Creation/CharacterCreationController.cs', 'Assets/FFOne/Runtime/Core/Integration/PreviewStage.cs',
        'Assets/FFOne/Content/UI/Settings/MainPanel.asset',
        'Assets/FFOne/Runtime/Gameplay/Tutorial/TutorialEffectAssets.cs', 'Assets/FFOne/Runtime/Gameplay/Tutorial/TutorialEffectPlayback.cs',
        'Assets/FFOne/Runtime/World/NetworkWorldBuffEffects.cs', 'Assets/FFOne/Runtime/World/NetworkWorldProjectiles.cs',
        'Assets/FFOne/Runtime/World/NetworkWorldController.cs', 'Assets/FFOne/Runtime/World/NetworkWorldPersonalVehicle.cs',
        'Assets/FFOne/Runtime/World/NetworkWorldEntities.cs', 'Assets/FFOne/Runtime/UI/Gameplay/GameplayItemUse.cs', 'Assets/FFOne/Tests/Editor/BuffEffectsTests.cs']]
    paths += [SERVER / relative for relative in ['src/skills.rs', 'src/entity/player.rs', 'src/entity/npc.rs', 'src/net/ffconnection.rs',
        'src/server/shard/item.rs', 'src/server/shard/pc.rs', 'src/server/shard/nano.rs', 'src/server/shard/combat.rs',
        'src/server/shard/group.rs', 'src/server/shard/mod.rs', 'src/chunk.rs', 'src/scripting/entity.rs',
        'src/skills_buff_tests.rs', 'src/server/shard/item_buff_tests.rs', 'scripts/lib/behaviors.luau', 'tabledata/xdt.json']]
    return {str(path.relative_to(ROOT.parent)): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths if path.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path)
    parser.add_argument('--player-only', action='store_true', help='Debug the supplied player on a fresh fixture without exercising the network harness.')
    scenarios = parser.add_mutually_exclusive_group()
    scenarios.add_argument('--nano-targets', action='store_true', help='Exercise damage targeting and both detectors in the actual player.')
    scenarios.add_argument('--combat-audio', action='store_true', help='Exercise rejected mount, avatar damage and Phoenix revival using the actual audio device.')
    scenarios.add_argument('--player-health', action='store_true', help='Exercise the local avatar reaction to a nonlethal server-confirmed hit.')
    parser.add_argument('--profile-bundle', type=Path, help='Validate a separately published player animation profile bundle in the health scenario.')
    parser.add_argument('--skip-build', action='store_true')
    parser.add_argument('--result-path', type=Path, default=ROOT / 'Logs/BuffValidationResult.json')
    args = parser.parse_args()
    if (args.player_only or args.nano_targets or args.combat_audio or args.player_health) and not args.player:
        parser.error('A selected player scenario requires --player')
    if args.profile_bundle and (not args.player_health or not args.profile_bundle.is_file()):
        parser.error('--profile-bundle requires --player-health and an existing player bundle')
    folder = SERVER / 'target' / ('unity-gameplay-buffs-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
    folder.mkdir(parents=True)
    tables = folder / 'tabledata'; shutil.copytree(SERVER / 'tabledata', tables, ignore=shutil.ignore_patterns('.git'))
    document = json.loads((tables / 'mobs.json').read_text('utf-8')); buffs.place_mobs(document)
    (tables / 'mobs.json').write_text(json.dumps(document), encoding='utf-8')
    npcs = json.loads((tables / 'npcs.json').read_text('utf-8')); key = str(max(map(int, npcs['NPCs'])) + 1)
    npcs['NPCs'][key] = dict(iNPCType=640, iX=632032, iY=187177, iZ=-5500, iAngle=0, iMapNum=0)
    (tables / 'npcs.json').write_text(json.dumps(npcs), encoding='utf-8')
    if args.nano_targets:
        eggs_path = tables / 'eggs.json'
        eggs = json.loads(eggs_path.read_text('utf-8'))
        eggs['Eggs'][str(max(map(int, eggs['Eggs'])) + 1)] = dict(iType=60, iX=631732, iY=187177, iZ=-5500)
        eggs_path.write_text(json.dumps(eggs), encoding='utf-8')
    plan_path = folder / 'buff-plan.json'; plan = buffs.plan(SERVER / 'tabledata', plan_path)
    if not args.skip_build:
        run(['cargo', 'build', '--bin', 'hybrid', '--locked'], cwd=SERVER)
        run(['dotnet', 'build', str(ROOT / 'Tools/LiveGameplay/LiveGameplay.csproj'), '--nologo', '-v', 'quiet'], cwd=ROOT)
    login, shard = port(), port()
    while shard == login: shard = port()
    database = folder / 'database.db'; config = folder / 'config.toml'
    config.write_bytes((SERVER / 'config.toml').read_bytes())
    command = [str(SERVER / 'target/debug/hybrid.exe'), 'config=' + str(config), 'general.enable_tui=false',
               'general.db_path=' + str(database), 'general.table_data_path=' + str(tables), 'general.combined_log_path=',
               'login.listen_addr=127.0.0.1:' + str(login), 'login.allow_plaintext_passwords=true',
               'shard.listen_addr=127.0.0.1:' + str(shard), 'shard.external_addr=127.0.0.1:' + str(shard),
               'shard.login_server_addr=127.0.0.1:' + str(login), 'general.live_check_time=5']
    fixture = 'Three normal isolated accounts, original XDT and mob stats, additional source-type mobs and one Nano station.'
    if args.nano_targets:
        fixture += ' Added one type-60 E.G.G. and granted the caster Nano 16 with skill 46.'
    result = dict(passed=False, playerOnly=args.player_only, directory=str(folder), loginPort=login, shardPort=shard, database=str(database),
                  plan=plan, fixture=fixture,
                  originalXdtSha256=hashlib.sha256((SERVER / 'tabledata/xdt.json').read_bytes()).hexdigest(),
                  fixtureXdtSha256=hashlib.sha256((tables / 'xdt.json').read_bytes()).hexdigest(),
                  serverBinarySha256=hashlib.sha256((SERVER / 'target/debug/hybrid.exe').read_bytes()).hexdigest(),
                  harnessAssemblySha256=hashlib.sha256(HARNESS.read_bytes()).hexdigest())
    result['sourceHashes'] = source_hashes()
    print('Buff fixture:', folder, flush=True)
    with (folder / 'buff-server.log').open('w', encoding='utf-8') as output:
        process = subprocess.Popen(command, cwd=SERVER, stdout=output, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
        result['ownedProcessId'] = process.pid
        try:
            if result['originalXdtSha256'] != result['fixtureXdtSha256']:
                raise AssertionError('Buff fixture XDT differs from the accepted original XDT')
            deadline = time.monotonic() + 90
            while True:
                if process.poll() is not None: raise RuntimeError('Buff server exited during startup')
                startup = (folder / 'buff-server.log').read_text('utf-8', errors='replace')
                if 'Connected to database' in startup and ' as shard ' in startup:
                    with socket.create_connection(('127.0.0.1', login), timeout=1): break
                if time.monotonic() >= deadline: raise TimeoutError('Buff server handshake not ready')
                time.sleep(.2)
            run(['dotnet', str(HARNESS), 'create-buffs', str(login), str(folder / 'buff-create.json'), str(plan_path)], cwd=ROOT, timeout=120)
            time.sleep(.5); buffs.seed(database)
            if args.player_only:
                result['networkSkippedReason'] = 'Explicit player-only debug run; full acceptance requires a separate complete network receipt.'
                result['passed'] = True
            else:
                run(['dotnet', str(HARNESS), 'exercise-buffs', str(login), str(folder / 'buff-network.json'), str(plan_path)], cwd=ROOT, timeout=600)
                result['network'] = json.loads((folder / 'buff-network.json').read_text('utf-8')); result['passed'] = result['network']['passed']
            if args.player:
                time.sleep(.5); buffs.seed(database)
                if args.nano_targets:
                    import sqlite3
                    with sqlite3.connect(database) as db:
                        db.execute("INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) SELECT PlayerID,16,46,150 FROM Players WHERE FirstName='Buffa'")
                player = args.player.resolve(); assembly = player.parent / (player.stem + '_Data/Managed/FFOne.Runtime.dll')
                profile = folder / 'buff-profile'; profile.mkdir()
                result.update(playerExecutable=str(player), playerProfile=str(profile), playerExecutableSha256=hashlib.sha256(player.read_bytes()).hexdigest(),
                              playerAssemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest(), playerContentCatalogHashes=player_content_hashes(player))
                startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = subprocess.SW_HIDE
                environment = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:' + str(login), FFONE_DEV_PROFILES=str(profile), FFONE_BUFF_PLAN=str(plan_path))
                mode = ['-batchmode', '--live-buff-smoke']
                if args.nano_targets: mode += ['-force-d3d11', '--nano-targets']
                if args.combat_audio: mode = ['-screen-fullscreen', '0', '-force-d3d11', '--live-buff-smoke', '--combat-audio']
                if args.player_health: mode += ['-force-d3d11', '--player-health']
                if args.profile_bundle:
                    profile_bundle = args.profile_bundle.resolve()
                    environment['FFONE_TEST_PROFILE_BUNDLE'] = str(profile_bundle)
                    result['profileBundle'] = str(profile_bundle)
                    result['profileBundleSha256'] = hashlib.sha256(profile_bundle.read_bytes()).hexdigest()
                result['playerScenario'] = 'nano-targets' if args.nano_targets else 'combat-audio' if args.combat_audio else 'player-health' if args.player_health else 'full'
                run([str(player), *mode, '-screen-width', '1280', '-screen-height', '720', '-logFile', str(folder / 'buff-player.log')],
                    cwd=ROOT, env=environment, startupinfo=startup, timeout=900)
                result['player'] = json.loads((profile / 'buffs-result.json').read_text('utf-8')); result['passed'] &= result['player']['passed']
                result['playerAssemblySha256After'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
                if args.profile_bundle and hashlib.sha256(profile_bundle.read_bytes()).hexdigest() != result['profileBundleSha256']:
                    raise AssertionError('Inspected animation profile bundle changed during acceptance')
                if result['playerAssemblySha256After'] != result['playerAssemblySha256'] or result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']:
                    raise AssertionError('Buff player identity/content changed during acceptance')
        except Exception as error:
            result['passed'] = False; result['error'] = str(error); raise
        finally:
            if process.poll() is None: process.terminate(); process.wait(timeout=20)
            result['serverStopped'] = process.poll() is not None
            result['serverBinarySha256After'] = hashlib.sha256((SERVER / 'target/debug/hybrid.exe').read_bytes()).hexdigest()
            result['harnessAssemblySha256After'] = hashlib.sha256(HARNESS.read_bytes()).hexdigest()
            result['sourceHashesAfter'] = source_hashes()
            result['originalXdtSha256After'] = hashlib.sha256((SERVER / 'tabledata/xdt.json').read_bytes()).hexdigest()
            result['fixtureXdtSha256After'] = hashlib.sha256((tables / 'xdt.json').read_bytes()).hexdigest()
            if not (result['originalXdtSha256'] == result['fixtureXdtSha256'] == result['originalXdtSha256After'] == result['fixtureXdtSha256After']):
                result['passed'] = False; result['xdtIdentityError'] = 'Original or fixture XDT changed, or fixture native values differ from the accepted original.'
            if (result['serverBinarySha256After'] != result['serverBinarySha256'] or result['harnessAssemblySha256After'] != result['harnessAssemblySha256']
                    or result['sourceHashesAfter'] != result['sourceHashes']):
                result['passed'] = False; result['identityError'] = 'Server, harness or relevant sources changed during acceptance.'
            for key, filename in [('create', 'buff-create.json'), ('network', 'buff-network.json')]:
                if (folder / filename).exists(): result[key] = json.loads((folder / filename).read_text('utf-8'))
            if result.get('playerProfile') and (Path(result['playerProfile']) / 'buffs-result.json').exists():
                result['player'] = json.loads((Path(result['playerProfile']) / 'buffs-result.json').read_text('utf-8'))
                if not result['player']['passed'] or result['player'].get('contentErrors') or not result['player'].get('contentComplete', False): result['passed'] = False
            if result.get('playerExecutable'):
                player = Path(result['playerExecutable']); assembly = player.parent / (player.stem + '_Data/Managed/FFOne.Runtime.dll')
                result['playerAssemblySha256After'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
                if result['playerAssemblySha256After'] != result['playerAssemblySha256'] or result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']: result['passed'] = False
            log = (folder / 'buff-server.log').read_text('utf-8', errors='replace')
            result['serverErrors'] = [line for line in log.splitlines() if 'AI script error' in line or 'panicked' in line]
            if (folder / 'buff-player.log').exists():
                result['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', (folder / 'buff-player.log').read_text('utf-8', errors='replace'), re.MULTILINE)
                if result['playerExceptions']: result['passed'] = False
            if result['serverErrors']: result['passed'] = False
            (folder / 'buff-result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
            args.result_path.resolve().write_text(json.dumps(result, indent=2), encoding='utf-8')
            print('Buff result:', folder / 'buff-result.json', flush=True)
    if not result['passed']: raise SystemExit(1)


if __name__ == '__main__': main()
