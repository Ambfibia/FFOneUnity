"""Exercise all six native weapon families and original GM PvP in an isolated server.

The optional Windows development player uses its actual frontend attack controller,
projectile motors, HUD and rendered replicas. Every mutable fixture is owned below
RustyFusion/target/unity-weapons-*; production accounts and tuning are never edited.
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
import weapon_combat
from run_live_gameplay import ROOT, SERVER, HARNESS, port, run, player_content_hashes


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path)
    parser.add_argument('--skip-build', action='store_true')
    profile = parser.add_mutually_exclusive_group()
    profile.add_argument('--weapon-change-only', action='store_true',
                         help='Exercise the WeaponChange input in the player using the isolated inventory fixture.')
    profile.add_argument('--grenade-only', action='store_true',
                         help='Run only the grenade impact, observer and damage checks in the supplied player.')
    parser.add_argument('--result-path', type=Path, default=ROOT / 'Logs/WeaponValidationResult.json')
    args = parser.parse_args()
    if args.weapon_change_only and not args.player:
        parser.error('--weapon-change-only requires --player')
    if args.grenade_only and not args.player:
        parser.error('--grenade-only requires --player')
    if not args.skip_build:
        run(['cargo', 'build', '--bin', 'hybrid', '--locked'], cwd=SERVER)
        run(['dotnet', 'build', str(ROOT / 'Tools/LiveGameplay/LiveGameplay.csproj'), '--nologo', '-v', 'quiet'], cwd=ROOT)
    folder = SERVER / 'target' / ('unity-weapons-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
    folder.mkdir(parents=True)
    tables = folder / 'tabledata'
    shutil.copytree(SERVER / 'tabledata', tables, ignore=shutil.ignore_patterns('.git'))
    mob_path = tables / 'mobs.json'
    mobs = json.loads(mob_path.read_text('utf-8'))
    weapon_combat.add_mobs(mobs)
    mob_path.write_text(json.dumps(mobs), encoding='utf-8')
    database = folder / 'database.db'
    config = folder / 'config.toml'
    config.write_text((SERVER / 'config.toml').read_text('utf-8'), encoding='utf-8')
    login, shard = port(), port()
    while login == shard:
        shard = port()
    command = [str(SERVER / 'target/debug/hybrid.exe'), 'config=' + str(config),
               'general.enable_tui=false', 'general.db_path=' + str(database),
               'general.table_data_path=' + str(tables), 'general.combined_log_path=',
               'login.listen_addr=127.0.0.1:' + str(login), 'login.allow_plaintext_passwords=true',
               'shard.listen_addr=127.0.0.1:' + str(shard), 'shard.external_addr=127.0.0.1:' + str(shard),
               'shard.login_server_addr=127.0.0.1:' + str(login), 'general.live_check_time=5']
    source_paths = [ROOT / 'Tools/run_weapon_validation.py', ROOT / 'Tools/LiveGameplay/weapon_combat.py',
                    ROOT / 'Tools/LiveGameplay/Program.cs', ROOT / 'Tools/LiveGameplay/LiveGameplay.csproj',
                    ROOT / 'Tools/LiveGameplay/Program.WeaponCombat.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayWeaponSmoke.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/FrontendCombat.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayLiveState.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayUiSession.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayNanoHudController.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayNanoRenderStage.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/SettingsPreferences.cs',
                    ROOT / 'Assets/FFOne/Runtime/UI/SettingsWindow.cs',
                    ROOT / 'Assets/FFOne/Runtime/Characters/Creation/CharacterCreationController.cs',
                    ROOT / 'Assets/FFOne/Runtime/Core/Integration/PreviewStage.cs',
                    ROOT / 'Assets/FFOne/Content/UI/Settings/MainPanel.asset',
                    ROOT / 'Assets/FFOne/Runtime/Gameplay/Tutorial/TutorialEffectPlayback.cs',
                    ROOT / 'Assets/FFOne/Runtime/Gameplay/Tutorial/TutorialEffectAssets.cs',
                    ROOT / 'Assets/FFOne/Runtime/World/NetworkWorldProjectiles.cs',
                    ROOT / 'Assets/FFOne/Runtime/World/WorldWeaponCatalog.cs',
                    ROOT / 'Assets/FFOne/Runtime/Network/ProtocolProjectiles.cs',
                    ROOT / 'Assets/FFOne/Runtime/Network/ProtocolGameplay.cs',
                    ROOT / 'Assets/FFOne/Tests/Editor/WeaponCombatProtocolTests.cs',
                    SERVER / 'src/net/ffconnection.rs',
                    SERVER / 'src/server/shard/combat.rs', SERVER / 'src/server/shard/weapon_combat_tests.rs',
                    SERVER / 'src/skills.rs', SERVER / 'src/entity/player.rs']
    source_paths += list((ROOT / 'Assets/FFOne/Runtime/UI').glob('Frontend*.cs'))
    if args.weapon_change_only:
        source_paths += [ROOT / 'Assets/InputSystem_Actions.inputactions',
                         ROOT / 'Assets/FFOne/Runtime/UI/GameplayInput.cs',
                         ROOT / 'Assets/FFOne/Runtime/UI/Gameplay/GameplayInventoryDrag.cs',
                         SERVER / 'src/server/shard/item.rs']
    source_paths += list((ROOT / 'Assets/FFOne/Content/UI').rglob('*.uss'))
    source_paths += list((ROOT / 'Assets/FFOne/Content/UI').rglob('*.uxml'))
    source_paths = sorted(set(source_paths))
    result = dict(passed=False, directory=str(folder), database=str(database), loginPort=login, shardPort=shard,
                  fixture='Three disposable characters; Weapa/Weapb normal99, dedicated Weapgm source GM30; six original item rows and six added original type59 mobs.',
                  sourceHashes={str(p.relative_to(ROOT.parent)): sha(p) for p in source_paths},
                  serverBinarySha256=sha(SERVER / 'target/debug/hybrid.exe'), harnessAssemblySha256=sha(HARNESS),
                  originalXdtSha256=sha(SERVER / 'tabledata/xdt.json'), fixtureXdtSha256=sha(tables / 'xdt.json'),
                  ownedProcessIds=[], serverStopped=True)
    player = args.player.resolve() if args.player else None
    if player:
        assembly = player.parent / (player.stem + '_Data') / 'Managed/FFOne.Runtime.dll'
        result.update(playerExecutable=str(player), playerExecutableSha256=sha(player), playerAssemblySha256=sha(assembly),
                      playerContentCatalogHashes=player_content_hashes(player))
    process = None
    print('Isolated weapon/PvP acceptance:', folder, flush=True)
    try:
        for stage in ('network', 'player') if player else ('network',):
            # A fresh owned process respawns the authored fixture mobs between
            # network and player, without changing their live combat statistics.
            if stage == 'player':
                result['accountLevels'] = weapon_combat.seed(database)
            with (folder / (stage + '-server.log')).open('w', encoding='utf-8') as log:
                process = subprocess.Popen(command, cwd=SERVER, stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
                result['ownedProcessIds'].append(process.pid)
                result['serverStopped'] = False
                deadline = time.monotonic() + 90
                while True:
                    startup = (folder / (stage + '-server.log')).read_text('utf-8', errors='replace')
                    if process.poll() is not None:
                        raise RuntimeError('Isolated server exited during startup')
                    if 'Connected to database' in startup and ' as shard ' in startup:
                        with socket.create_connection(('127.0.0.1', login), timeout=1):
                            break
                    if time.monotonic() > deadline:
                        raise TimeoutError('Isolated database/shard handshake not ready')
                    time.sleep(.2)
                if stage == 'network':
                    run(['dotnet', str(HARNESS), 'create-weapons', str(login), str(folder / 'create.json')], cwd=ROOT, timeout=120)
                    time.sleep(.5)
                    result['accountLevels'] = weapon_combat.seed(database)
                    if not args.weapon_change_only:
                        run(['dotnet', str(HARNESS), 'exercise-weapons', str(login), str(folder / 'network.json')], cwd=ROOT, timeout=300)
                        result['network'] = json.loads((folder / 'network.json').read_text('utf-8'))
                else:
                    profile = folder / 'player-profile'
                    profile.mkdir()
                    env = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:' + str(login), FFONE_DEV_PROFILES=str(profile))
                    startup = subprocess.STARTUPINFO()
                    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                    startup.wShowWindow = subprocess.SW_HIDE
                    mode = ['-batchmode', '--live-grenade-smoke' if args.grenade_only else '--live-weapon-smoke']
                    if args.weapon_change_only:
                        mode = ['-screen-fullscreen', '0', '--live-weapon-change-smoke']
                        startup = None
                    run([str(player), *mode, '-screen-width', '1280', '-screen-height', '720',
                         '-logFile', str(folder / 'player.log')], cwd=ROOT, env=env, startupinfo=startup, timeout=900)
                    result['player'] = json.loads((profile / 'weapons-result.json').read_text('utf-8'))
                    result['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', (folder / 'player.log').read_text('utf-8', errors='replace'), re.MULTILINE)
                    if result['playerExceptions']:
                        raise AssertionError('Unexpected C# exception in live player')
                process.terminate()
                process.wait(timeout=20)
                result['serverStopped'] = True
                server_log = (folder / (stage + '-server.log')).read_text('utf-8', errors='replace')
                script_errors = [line for line in server_log.splitlines() if 'AI script error' in line]
                result[stage + 'ServerScriptErrors'] = script_errors
                if script_errors:
                    raise AssertionError('AI script failure during weapon acceptance')
                panics = [line for line in server_log.splitlines() if 'panicked at' in line or 'fatal runtime error' in line]
                result[stage + 'ServerPanics'] = panics
                if panics:
                    raise AssertionError('Server panic during weapon acceptance')
        if player:
            result['playerAssemblySha256After'] = sha(assembly)
            result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
            if result['playerAssemblySha256After'] != result['playerAssemblySha256'] or result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']:
                raise AssertionError('Player assembly or content changed during acceptance')
        result['sourceHashesAfter'] = {str(p.relative_to(ROOT.parent)): sha(p) for p in source_paths}
        result['serverBinarySha256After'] = sha(SERVER / 'target/debug/hybrid.exe')
        result['harnessAssemblySha256After'] = sha(HARNESS)
        if result['sourceHashesAfter'] != result['sourceHashes'] or result['serverBinarySha256After'] != result['serverBinarySha256'] or result['harnessAssemblySha256After'] != result['harnessAssemblySha256']:
            raise AssertionError('Weapon runtime source or binaries changed during acceptance')
        if args.weapon_change_only:
            result['passed'] = result['player']['passed']
        else:
            result['passed'] = result['network']['passed'] and (not player or result['player']['passed'])
        if not result['passed']:
            raise AssertionError('Weapon/PvP acceptance reported failed checks')
    except Exception as error:
        result['passed'] = False
        result['error'] = str(error)
        raise
    finally:
        if process and process.poll() is None:
            process.terminate()
            process.wait(timeout=20)
        result['serverStopped'] = not process or process.poll() is not None
        for label, path in (('network', folder / 'network.json'), ('player', folder / 'player-profile/weapons-result.json')):
            if path.exists():
                result[label] = json.loads(path.read_text('utf-8'))
        (folder / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        args.result_path.resolve().parent.mkdir(parents=True, exist_ok=True)
        args.result_path.resolve().write_text(json.dumps(result, indent=2), encoding='utf-8')
        print('Result:', folder / 'result.json', flush=True)


if __name__ == '__main__':
    main()
