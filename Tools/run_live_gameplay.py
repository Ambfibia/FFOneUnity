"""Build and exercise the production Unity C# transport against an isolated RustyFusion.

Only the unique ignored RustyFusion/target/unity-gameplay-* directory is mutated.
Two normal accounts are tested; the automatic first Master account is not used for gameplay.
"""
from datetime import datetime
from pathlib import Path
import json
import argparse
import os
import hashlib
import re
import shutil
import socket
import sqlite3
import subprocess
import time
import uuid
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent / 'LiveGameplay'))
import instances

ROOT = Path(__file__).resolve().parents[1]
SERVER = ROOT.parent / 'RustyFusion'
HARNESS = ROOT / 'Logs/LiveGameplayHarness/bin/net10.0/LiveGameplay.dll'


def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


def run(command, **kwargs):
    subprocess.run(command, check=True, creationflags=subprocess.CREATE_NO_WINDOW, **kwargs)


def player_content_hashes(player):
    player = Path(player).resolve()
    root = player.parent / (player.stem + '_Data') / 'StreamingAssets/aa'
    paths = [root / 'settings.json'] + sorted(p for p in root.glob('catalog*')
                                             if p.is_file() and p.suffix in ('.bin', '.json', '.hash'))
    if len(paths) < 2 or not all(p.is_file() for p in paths):
        raise ValueError('The accepted player requires its built Addressables settings and catalog.')
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--player', type=Path)
    parser.add_argument('--result-path', type=Path, default=ROOT / 'Logs/LiveGameplayResult.json')
    parser.add_argument('--skip-build', action='store_true', help='Use frozen validated server/harness binaries without replacing a running executable.')
    parser.add_argument('--instances', action='store_true', help='Run the separate source-map instance matrix as well.')
    args = parser.parse_args()
    folder = SERVER / 'target' / ('unity-gameplay-' + datetime.now().strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:6])
    folder.mkdir(parents=True)
    login, shard = port(), port()
    while shard == login:
        shard = port()
    database = folder / 'database.db'
    tables = folder / 'tabledata'
    shutil.copytree(SERVER / 'tabledata', tables, ignore=shutil.ignore_patterns('.git'))
    mobs = json.loads((tables / 'mobs.json').read_text('utf-8'))
    mobs['mobs'][str(max(map(int, mobs['mobs'])) + 1)] = {
        'iNPCType': 1, 'iX': 632832, 'iY': 187177, 'iZ': -5500, 'iAngle': 0, 'iMapNum': 0}
    (tables / 'mobs.json').write_text(json.dumps(mobs), encoding='utf-8')
    npcs = json.loads((tables / 'npcs.json').read_text('utf-8'))
    next_npc = max(map(int, npcs['NPCs'])) + 1
    for i, npc_type in enumerate((3076, 3075, 651, 686, 2586, 2507, 3124, 3129, 967, 968, 995, 2591, 680, 2567)):
        npcs['NPCs'][str(next_npc + i)] = {'iNPCType': npc_type, 'iX': 632032, 'iY': 187277 + 100 * i,
                                          'iZ': -5500, 'iAngle': 0, 'iMapNum': 0}
    npcs['NPCs'][str(next_npc + 10)].update(iX=444813,iY=346594,iZ=-5264)
    (tables / 'npcs.json').write_text(json.dumps(npcs), encoding='utf-8')
    config = folder / 'config.toml'
    config.write_text((SERVER / 'config.toml').read_text('utf-8'), encoding='utf-8')
    if not args.skip_build:
        run(['cargo', 'build', '--bin', 'hybrid', '--locked'], cwd=SERVER)
        run(['dotnet', 'build', str(ROOT / 'Tools/LiveGameplay/LiveGameplay.csproj'), '--nologo', '-v', 'quiet'], cwd=ROOT)
    command = [str(SERVER / 'target/debug/hybrid.exe'), 'config=' + str(config),
               'general.enable_tui=false', 'general.db_path=' + str(database),
               'general.table_data_path=' + str(tables), 'general.combined_log_path=',
               'login.listen_addr=127.0.0.1:' + str(login), 'login.allow_plaintext_passwords=true',
               'shard.listen_addr=127.0.0.1:' + str(shard), 'shard.external_addr=127.0.0.1:' + str(shard),
               'shard.login_server_addr=127.0.0.1:' + str(login), 'general.live_check_time=5']
    result = {'passed': False, 'directory': str(folder), 'database': str(database), 'loginPort': login,
              'shardPort': shard, 'fixture': 'one additional type-1 mob; mission/vendor/bank/SCAMPER/Skyway/portal/escort NPC placements; four normal accounts with isolated inventories, currency, cards and Nano',
              'serverIncludesExistingWorkingChanges': True}
    with (folder / 'server.log').open('w', encoding='utf-8') as output:
        process = subprocess.Popen(command, cwd=SERVER, stdout=output, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        result['ownedProcessId'] = process.pid
        try:
            deadline = time.monotonic() + 90
            while True:
                if process.poll() is not None:
                    raise RuntimeError('RustyFusion exited during startup; see ' + str(folder / 'server.log'))
                try:
                    with socket.create_connection(('127.0.0.1', login), timeout=.2):
                        break
                except OSError:
                    if time.monotonic() > deadline:
                        raise TimeoutError('Isolated login server not ready')
                    time.sleep(.2)
            # Listening sockets precede SQLite migration and the login/shard handshake.
            # Do not race the first account creation against those initialization steps.
            while True:
                startup_log = (folder / 'server.log').read_text('utf-8', errors='replace')
                if 'Connected to database' in startup_log and ' as shard ' in startup_log:
                    break
                if process.poll() is not None or time.monotonic() > deadline:
                    raise TimeoutError('Isolated database/shard handshake not ready')
                time.sleep(.2)
            print('Isolated server:', folder, flush=True)
            run(['dotnet', str(HARNESS), 'create', str(login), str(folder / 'create.json')], cwd=ROOT, timeout=120)
            time.sleep(.5)
            with sqlite3.connect(database, timeout=20) as db:
                accounts = db.execute("SELECT AccountID, AccountLevel FROM Accounts WHERE Login IN ('unityalice','unitybobby','unitytrava','unitytravb')").fetchall()
                if len(accounts) != 4 or any(level != 99 for _, level in accounts):
                    raise AssertionError('Gameplay accounts must have normal privileges: ' + str(accounts))
                players = db.execute("SELECT PlayerID FROM Players WHERE FirstName IN ('Alice','Bobby') ORDER BY FirstName").fetchall()
                if len(players) != 2:
                    raise AssertionError('Expected two disposable players')
                for i, (uid,) in enumerate(players):
                    for slot, item in ((47, 1), (48, 4)):
                        db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,1,0)', (uid, 9 + slot, item, 10))
                    db.execute('UPDATE Players SET TutorialFlag=1, XCoordinate=?,YCoordinate=?,ZCoordinate=?,HP=700,Taros=1000,BatteryW=100,BatteryN=100,Nano1=1,Nano2=0,Nano3=0 WHERE PlayerID=?', (632032 + i * 100, 187177, -5500, uid))
                    db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,?,0) ON CONFLICT(PlayerID,Slot) DO UPDATE SET ID=excluded.ID,Type=excluded.Type,Opt=excluded.Opt,TimeLimit=0', (uid, 9, 67, 1, 1))
                    db.execute('INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) VALUES(?,1,1,150) ON CONFLICT(PlayerID,ID) DO UPDATE SET Skill=1,Stamina=150', (uid,))
                    if i == 0:
                        db.execute('UPDATE Players SET Nano2=9 WHERE PlayerID=?', (uid,))
                        db.execute('INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) VALUES(?,9,25,150) ON CONFLICT(PlayerID,ID) DO UPDATE SET Skill=25,Stamina=150', (uid,))
                        for slot,item in ((40,67),(41,68)):
                            db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,1,0)',(uid,9+slot,item,1))
                    if i == 1:
                        for slot, card in enumerate((5, 29, 30, 32), 10):
                            db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,1,0)', (uid, 9 + slot, card, 7))
                        for slot in range(41, 45):
                            db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,1,0)', (uid, 9 + slot, 67, 1))
                for uid, in db.execute("SELECT PlayerID FROM Players WHERE FirstName IN ('Trava','Travb')").fetchall():
                    db.execute('UPDATE Players SET TutorialFlag=1,XCoordinate=632032,YCoordinate=187177,ZCoordinate=-5500,HP=700,Taros=100000 WHERE PlayerID=?',(uid,))
                    db.execute('UPDATE Players SET Nano2=9 WHERE PlayerID=?', (uid,))
                    db.execute('INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) VALUES(?,9,25,150) ON CONFLICT(PlayerID,ID) DO UPDATE SET Skill=25,Stamina=150', (uid,))
                    for slot,item in ((40,67),(41,68),(42,67),(43,67)):
                        db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,?,1,0)',(uid,9+slot,item,1))
            run(['dotnet', str(HARNESS), 'exercise', str(login), str(folder / 'exercise.json')], cwd=ROOT, timeout=420)
            result['exercise'] = json.loads((folder / 'exercise.json').read_text('utf-8'))
            result['passed'] = result['exercise']['passed']
            result['normalAccountLevels'] = [level for _, level in accounts]
            result['serverHead'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=SERVER, text=True).strip()
            if args.player:
                player = args.player.resolve()
                assembly = player.parent / (player.stem + '_Data') / 'Managed/FFOne.Runtime.dll'
                result['playerExecutable'] = str(player)
                result['playerExecutableSha256'] = hashlib.sha256(player.read_bytes()).hexdigest()
                result['playerAssemblySha256'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashes'] = player_content_hashes(player)
                profile = folder / 'player-profile'
                profile.mkdir()
                env = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:' + str(login), FFONE_DEV_PROFILES=str(profile))
                startup = subprocess.STARTUPINFO()
                startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                startup.wShowWindow = subprocess.SW_HIDE
                run([str(args.player.resolve()), '-batchmode', '--live-gameplay-smoke', '-screen-width', '1280',
                     '-screen-height', '720', '-logFile', str(folder / 'player.log')], cwd=ROOT, env=env, startupinfo=startup, timeout=600)
                result['player'] = json.loads((profile / 'result.json').read_text('utf-8'))
                result['passed'] = result['passed'] and result['player']['passed']
                log = (folder / 'player.log').read_text('utf-8', errors='replace')
                result['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', log, re.MULTILINE)
                if result['playerExceptions']:
                    raise AssertionError('Unexpected C# exception in live player')
                result['playerAssemblySha256After'] = hashlib.sha256(assembly.read_bytes()).hexdigest()
                result['playerContentCatalogHashesAfter'] = player_content_hashes(player)
                if result['playerContentCatalogHashesAfter'] != result['playerContentCatalogHashes']:
                    raise AssertionError('Player Addressables content changed during gameplay acceptance.')
                if result['playerAssemblySha256After'] != result['playerAssemblySha256']:
                    raise AssertionError('Gameplay player assembly changed while acceptance was running.')
            if args.instances:
                plan_path = folder / 'instance-plan.json'
                matrix = instances.plan(SERVER / 'tabledata', plan_path)
                result['instancePlan'] = {k: v for k, v in matrix.items() if k != 'cases'}
                run(['dotnet', str(HARNESS), 'create-instances', str(login), str(folder / 'instance-create.json'), str(plan_path)], cwd=ROOT, timeout=600)
                time.sleep(.5)
                instances.seed(database, plan_path)
                run(['dotnet', str(HARNESS), 'exercise-instances', str(login), str(folder / 'instance-network.json'), str(plan_path)], cwd=ROOT, timeout=3600)
                result['instanceNetwork'] = json.loads((folder / 'instance-network.json').read_text('utf-8'))
                result['passed'] = result['passed'] and result['instanceNetwork']['passed']
                if args.player:
                    time.sleep(.5)
                    instances.seed(database, plan_path)
                    profile = folder / 'instance-profile'
                    profile.mkdir()
                    env = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:' + str(login), FFONE_DEV_PROFILES=str(profile), FFONE_INSTANCE_PLAN=str(plan_path))
                    run([str(args.player.resolve()), '-batchmode', '--live-instance-smoke', '-screen-width', '1280', '-screen-height', '720',
                         '-logFile', str(folder / 'instance-player.log')], cwd=ROOT, env=env, startupinfo=startup, timeout=3600)
                    result['instancePlayer'] = json.loads((profile / 'instances-result.json').read_text('utf-8'))
                    result['passed'] = result['passed'] and result['instancePlayer']['passed']
            server_log = (folder / 'server.log').read_text('utf-8', errors='replace')
            result['serverScriptErrors'] = [line for line in server_log.splitlines() if 'AI script error' in line]
            if result['serverScriptErrors']:
                raise AssertionError('AI script failure during the live gameplay cycle')
            sources = list((ROOT / 'Assets/FFOne/Runtime/Network').glob('*.cs')) + [
                SERVER / 'src/scripting/entity.rs', SERVER / 'scripts/lib/behaviors.luau', SERVER / 'src/server/shard/item.rs',
                SERVER / 'src/item.rs', SERVER / 'src/economy.rs', SERVER / 'src/server/shard/email.rs',
                SERVER / 'src/server/shard/buddy.rs', SERVER / 'src/server/shard/group.rs', SERVER / 'src/server/shard/login.rs', SERVER / 'src/server/shard/transport.rs']
            sources += list((ROOT / 'Assets/FFOne/Runtime/UI/Gameplay').glob('*.cs')) + [ROOT / ('Assets/FFOne/Runtime/UI/' + name) for name in ('FrontendGameplay.cs', 'FrontendController.cs', 'FrontendWorldFlow.cs', 'FrontendTravel.cs')]
            sources += list((ROOT / 'Assets/FFOne/Runtime/World').glob('*.cs'))
            sources += list((ROOT / 'Assets/FFOne/Runtime/Characters/Humanoids').glob('*.cs'))
            sources += [ROOT / ('Tools/LiveGameplay/' + name) for name in ('Program.cs', 'Program.Slider.cs', 'Program.Vehicle.cs', 'Program.Recall.cs')]
            sources += [SERVER / 'src/chunk.rs', SERVER / 'src/entity/slider.rs', SERVER / 'src/server/shard/pc.rs']
            sources += [SERVER / 'src/server/shard/nano.rs', SERVER / 'src/server/shard/combat.rs', SERVER / 'src/entity/player.rs', SERVER / 'src/skills.rs']
            result['networkAndAiSourceHashes'] = {str(p.relative_to(ROOT.parent)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
        except Exception as error:
            result['passed'] = False
            result['error'] = str(error)
            raise
        finally:
            # Clients use acknowledged saved exit first; terminate only this runner's owned isolated process.
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=20)
            result['serverStopped'] = process.poll() is not None
            if (folder / 'exercise.json').exists():
                result['exercise'] = json.loads((folder / 'exercise.json').read_text('utf-8'))
            if (folder / 'player-profile/result.json').exists():
                result['player'] = json.loads((folder / 'player-profile/result.json').read_text('utf-8'))
            (folder / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
            args.result_path.resolve().write_text(json.dumps(result, indent=2), encoding='utf-8')
            print('Result:', folder / 'result.json', flush=True)


if __name__ == '__main__':
    main()
