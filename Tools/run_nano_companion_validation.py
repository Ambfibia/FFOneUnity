"""Exercise world Nano companions with two rendered Unity players on an isolated shard."""
from argparse import ArgumentParser
from contextlib import contextmanager
from datetime import datetime
from hashlib import sha256
from pathlib import Path
import json
import os
import re
import shutil
import socket
import sqlite3
import subprocess
import time
import uuid

from run_live_gameplay import ROOT, SERVER, HARNESS, player_content_hashes, port, run


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


@contextmanager
def server(command, log_path, login_port):
    with log_path.open('w', encoding='utf-8') as log:
        proc = subprocess.Popen(command, cwd=SERVER, stdout=log, stderr=subprocess.STDOUT,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            deadline = time.monotonic() + 90
            while time.monotonic() < deadline:
                if proc.poll() is not None:
                    raise RuntimeError(f'Shard exited during startup. See {log_path}')
                output = log_path.read_text(encoding='utf-8', errors='replace')
                if 'Connected to database' in output and ' as shard ' in output:
                    with socket.create_connection(('127.0.0.1', login_port), timeout=2):
                        break
                time.sleep(.25)
            else:
                raise TimeoutError(f'Shard did not start. See {log_path}')
            yield proc
        finally:
            if proc.poll() is None:
                proc.terminate()
            proc.wait(timeout=20)


def portal(tables):
    warps = json.loads((tables / 'xdt.json').read_text('utf-8'))['m_pInstanceTable']['m_pWarpData']
    gate, = [w for w in warps if w['m_iWarpNumber'] == 30]
    placements = json.loads((tables / 'NPCs.json').read_text('utf-8'))['NPCs'].values()
    npc, = [n for n in placements if n['iNPCType'] == gate['m_iNpcNumber'] and not n.get('iMapNum')]
    return dict(npcType=npc['iNPCType'], npcX=int(npc['iX']), npcY=int(npc['iY']),
                npcZ=int(npc['iZ']), warp=gate['m_iWarpNumber'], map=gate['m_iToMapNum'])


def seed(database, gate, second):
    with sqlite3.connect(database) as db:
        for index, account in enumerate(('unityweapa', 'unityweapb')):
            (uid, level), = db.execute('SELECT p.PlayerID,a.AccountLevel FROM Players p JOIN Accounts a '
                                       'ON a.AccountID=p.AccountID WHERE a.Login=?', (account,))
            if level != 99:
                raise ValueError('Companion check requires two ordinary characters')
            db.execute('UPDATE Players SET TutorialFlag=1,Level=9,HP=1200,Taros=1000,BatteryN=1000,'
                       'Nano1=1,Nano2=?,Nano3=0,XCoordinate=?,YCoordinate=?,ZCoordinate=? WHERE PlayerID=?',
                       (second, gate['npcX'] + 200 + 150 * index, gate['npcY'], gate['npcZ'], uid))
            for nano, skill in ((1, 1), (second, {6: 16, 9: 25}[second])):
                db.execute('INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) VALUES(?,?,?,150) '
                           'ON CONFLICT(PlayerID,ID) DO UPDATE SET Skill=excluded.Skill,Stamina=150', (uid, nano, skill))


def exercise(player, folder, login, gate, result):
    processes = {}
    sync = folder / 'sync'
    sync.mkdir()
    try:
        for role in ('observer', 'owner'):
            profile = folder / role
            profile.mkdir()
            (profile / 'unity-options.json').write_text(json.dumps({
                'schema': 'ffone.unity-options.v1', 'voice_locale': 'en' if role == 'owner' else 'ru',
                'sound': {'master': 9, 'voice': 9, 'effects': 9, 'ambient': 0, 'music': 0},
            }), encoding='utf-8')
            env = dict(os.environ, FFONE_DEV_SERVER=f'127.0.0.1:{login}', FFONE_DEV_PROFILES=str(profile),
                       FFONE_NANO_SYNC=str(sync), FFONE_NANO_WARP=json.dumps(gate))
            display = ['-screen-fullscreen', '0']
            args = [str(player), *display, '-screen-width', '960', '-screen-height', '540',
                    '--live-nano-companion-' + role, '-logFile', str(profile / 'player.log')]
            processes[role] = subprocess.Popen(args, cwd=ROOT, env=env, creationflags=subprocess.CREATE_NO_WINDOW)
            if role == 'observer':
                deadline = time.monotonic() + 110
                log = profile / 'player.log'
                while not log.exists() or 'frontend ready' not in log.read_text(encoding='utf-8', errors='replace'):
                    if time.monotonic() >= deadline or processes[role].poll() is not None:
                        raise RuntimeError('Observer did not initialize its rendering and audio devices')
                    time.sleep(.25)
        result['playerPids'] = {role: p.pid for role, p in processes.items()}
        deadline = time.monotonic() + 600
        while any(p.poll() is None for p in processes.values()):
            for role, process in processes.items():
                if process.poll() not in (None, 0):
                    raise RuntimeError(f'{role} player exited with {process.returncode}. See {folder / role}')
            if time.monotonic() > deadline:
                raise TimeoutError('Two-player exercise timed out')
            time.sleep(.5)
        for role, process in processes.items():
            report = json.loads((folder / role / 'nano-companion-result.json').read_text('utf-8-sig'))
            if process.returncode or not report['passed']:
                raise RuntimeError(f'{role} did not pass')
            log = (folder / role / 'player.log').read_text('utf-8', errors='replace')
            if re.search(r'^\w+(?:\.\w+)*Exception:', log, flags=re.MULTILINE):
                raise RuntimeError(f'{role} player logged an exception')
            result[role] = report
    finally:
        for process in processes.values():
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=20)


def main():
    parser = ArgumentParser(description=__doc__)
    parser.add_argument('--player', required=True, type=Path)
    parser.add_argument('--second-nano', type=int, choices=(6, 9), default=6,
                        help='Wilt has localized EN/RU recordings; Edd exercises the fallback.')
    parser.add_argument('--skip-build', action='store_true', help='Reuse existing server and fixture harness binaries')
    args = parser.parse_args()
    player = args.player.resolve(strict=True)
    binary = SERVER / 'target/debug/hybrid.exe'
    if not args.skip_build:
        run(['cargo', 'build', '--locked', '--bin', 'hybrid'], cwd=SERVER)
        run(['dotnet', 'build', 'Tools/LiveGameplay/LiveGameplay.csproj', '--nologo', '-v', 'quiet'], cwd=ROOT)
    folder = SERVER / 'target' / ('unity-world-nano-' + datetime.now().strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:6])
    folder.mkdir()
    tables = folder / 'tabledata'
    shutil.copytree(SERVER / 'tabledata', tables, ignore=shutil.ignore_patterns('.git'))
    gate = portal(tables)
    mob_table = tables / 'mobs.json'
    mob_data = json.loads(mob_table.read_text(encoding='utf-8'))
    mob_data['mobs'][str(max(map(int, mob_data['mobs'])) + 1)] = {
        'iNPCType': 59, 'iX': gate['npcX'] + 550, 'iY': gate['npcY'], 'iZ': gate['npcZ'],
        'iAngle': 0, 'iMapNum': 0}
    mob_table.write_text(json.dumps(mob_data), encoding='utf-8')
    database = folder / 'database.db'
    config = folder / 'config.toml'
    shutil.copyfile(SERVER / 'config.toml', config)
    login, shard = port(), port()
    while shard == login:
        shard = port()
    second = args.second_nano
    args = [str(binary), 'config=' + str(config), 'general.enable_tui=false', 'general.db_path=' + str(database),
            'general.table_data_path=' + str(tables), 'general.combined_log_path=', 'general.live_check_time=10',
            f'login.listen_addr=127.0.0.1:{login}', 'login.allow_plaintext_passwords=true',
            f'shard.listen_addr=127.0.0.1:{shard}', f'shard.external_addr=127.0.0.1:{shard}',
            f'shard.login_server_addr=127.0.0.1:{login}']
    result = dict(passed=False, directory=str(folder), database=str(database), portal=gate, loginPort=login,
                  shardPort=shard, player=str(player), playerHash=digest(player), catalog=player_content_hashes(player),
                  runtimeSha256=digest(player.parent / (player.stem + '_Data/Managed/FFOne.Runtime.dll')),
                  serverHash=digest(binary))
    print('Two-player Nano acceptance:', folder, flush=True)
    try:
        with server(args, folder / 'create-server.log', login):
            run(['dotnet', str(HARNESS), 'create-weapons', str(login), str(folder / 'accounts.json')], timeout=120)
        seed(database, gate, second)
        with server(args, folder / 'world-server.log', login) as process:
            result['serverPid'] = process.pid
            exercise(player, folder, login, gate, result)
        result['passed'] = True
    except Exception as error:
        result['error'] = str(error)
        raise
    finally:
        (folder / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        print('Report:', folder / 'result.json', flush=True)


if __name__ == '__main__':
    main()
