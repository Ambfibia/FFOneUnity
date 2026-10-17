"""Require real weapon/PvP and buff player runs on one unchanged accepted build."""
import argparse
import base64
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools'))
from run_live_gameplay import player_content_hashes


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def ref(path):
    path = Path(path).resolve()
    return dict(path=str(path), sha256=sha(path))


def validate_run(report, name):
    require(not report.get('playerOnly'), name + ': debug-only player run cannot establish full acceptance')
    require(report.get('passed') and report.get('serverStopped'), name + ': run or owned-server cleanup failed')
    require(not report.get('error'), name + ': runner error')
    require(report.get('originalXdtSha256') and report['originalXdtSha256'] == report.get('fixtureXdtSha256'), name + ': fixture changed original XDT')
    require(sha(ROOT.parent / 'RustyFusion/tabledata/xdt.json') == report['originalXdtSha256'], name + ': accepted XDT changed after the run')
    require(sha(Path(report['directory']) / 'tabledata/xdt.json') == report['fixtureXdtSha256'], name + ': fixture XDT changed after the run')
    for key in ('playerExceptions', 'serverErrors', 'serverPanics', 'serverScriptErrors', 'networkServerScriptErrors', 'playerServerScriptErrors', 'networkServerPanics', 'playerServerPanics', 'identityError', 'xdtIdentityError'):
        require(not report.get(key), name + ': ' + key)
    for stage in ('network', 'player'):
        require(report.get(stage, {}).get('passed'), name + ': missing successful real ' + stage)
        require(report[stage].get('checks'), name + ': empty ' + stage + ' check list')
    require(report['player'].get('contentComplete') is True and not report['player'].get('contentErrors'), name + ': content incomplete')
    player = Path(report['playerExecutable'])
    require(report['playerExecutableSha256'] == sha(player), name + ': player executable changed')
    assembly = player.parent / (player.stem + '_Data/Managed/FFOne.Runtime.dll')
    require(report['playerAssemblySha256'] == sha(assembly), name + ': player DLL changed')
    require(report['playerAssemblySha256After'] == report['playerAssemblySha256'], name + ': DLL changed during run')
    require(report['playerContentCatalogHashes'] == report['playerContentCatalogHashesAfter'] == player_content_hashes(player), name + ': content catalog changed')
    require(report.get('sourceHashes'), name + ': missing source fingerprints')
    require(report['sourceHashes'] == report.get('sourceHashesAfter'), name + ': source fingerprints changed during run')
    for path, digest in report['sourceHashes'].items():
        require(sha(ROOT.parent / path) == digest, name + ': source changed after acceptance: ' + path)
    require(sha(ROOT.parent / 'RustyFusion/target/debug/hybrid.exe') == report['serverBinarySha256'], name + ': server changed')
    require(report['serverBinarySha256'] == report.get('serverBinarySha256After'), name + ': server changed during run')
    require(sha(ROOT / 'Logs/LiveGameplayHarness/bin/net10.0/LiveGameplay.dll') == report['harnessAssemblySha256'], name + ': harness changed')
    require(report['harnessAssemblySha256'] == report.get('harnessAssemblySha256After'), name + ': harness changed during run')
    if name == 'weapons':
        player_server_log = (Path(report['directory']) / 'player-server.log').read_text('utf-8-sig', errors='replace')
        impact_rejections = ('Malformed projectile targets', 'Projectile slot is not owned or has expired',
                             'Projectile instance or target count does not match its launch',
                             'Projectile impact is beyond its flight envelope', 'Rocket impact is outside its launch path',
                             'Invalid projectile blast target')
        require(not any(message in player_server_log for message in impact_rejections), 'weapons: a rendered player projectile was rejected by the server')


def validate_pvp_player(player):
    hits = []
    own_battery = None
    for recipient, packet_type in (('frontend', '31000102'), ('observer', '31000103')):
        receipts = [row for row in player.get('pvpReceipts', []) if row['recipient'] == recipient and row['type'] == packet_type]
        require(len(receipts) == 1, 'PvP needs one actual owner ACK and one observer broadcast')
        row = receipts[0]
        raw = base64.b64decode(row['payloadBase64'], validate=True)
        require(len(raw) == 32 and struct.unpack_from('<i', raw, 4)[0] == 1, 'PvP receipt has an invalid native target count/width')
        kind, target, _, damage, hp, _ = struct.unpack_from('<6i', raw, 8)
        require(kind == 1 and target > 0 and damage > 0 and hp >= 0, 'PvP receipt is not a positive real player hit')
        require(row['hits'] == [dict(kind='Player', Id=target, Damage=damage, Hp=hp)], 'PvP decoded hit disagrees with raw native receipt')
        hits.append((target, damage, hp))
        if recipient == 'frontend':
            own_battery = struct.unpack_from('<i', raw)[0]
            require(own_battery == row['WeaponBattery'], 'PvP battery disagrees with raw native ACK')
    require(hits[0] == hits[1], 'PvP ACK and observer differ in target, damage or HP')
    states = [re.fullmatch(r'after-native-damage peerHp=(\d+) replicaHp=(\d+) baseline=(\d+)', row)
              for row in player.get('pvpIntentDiagnostics', [])]
    states = [state for state in states if state]
    require(len(states) == 1, 'PvP lacks its canonical owner/rendered target post-state')
    peer_hp, replica_hp, baseline = map(int, states[0].groups())
    require(peer_hp == replica_hp == hits[0][2] < baseline, 'PvP owner, rendered replica and raw receipts disagree')
    hud = [row for row in player.get('hudBatteryVisuals', []) if row['name'] == 'weapon-pvp-gm-battery-hud']
    require(len(hud) == 1 and hud[0]['battery'] == own_battery, 'PvP rendered HUD differs from authoritative native battery')
    return dict(targetId=hits[0][0], damage=hits[0][1], beforeHp=baseline, afterHp=peer_hp, weaponBattery=own_battery)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--weapons', type=Path, required=True)
    parser.add_argument('--buffs', type=Path, required=True)
    parser.add_argument('--editor-tests', type=Path, required=True)
    parser.add_argument('--server-tests', type=Path, required=True)
    parser.add_argument('--ai-tests', type=Path, required=True)
    parser.add_argument('--gameplay', type=Path, help='Broader gameplay regression on the same final player.')
    parser.add_argument('--frontend', type=Path, help='Full HD/WUXGA UI receipt repeated on the same final player.')
    parser.add_argument('--output', type=Path, default=ROOT / 'Tools/LiveGameplay/acceptance-20261017-combat-buffs.json')
    args = parser.parse_args()
    weapons, buffs = (json.loads(p.read_text('utf-8-sig')) for p in (args.weapons, args.buffs))
    for name, report in (('weapons', weapons), ('buffs', buffs)):
        validate_run(report, name)
    pvp = validate_pvp_player(weapons['player'])
    require(weapons.get('accountLevels') == {'unityweapa': 99, 'unityweapb': 99, 'unityweapgm': 30}, 'Weapon fixture altered normal account privileges')
    visuals = weapons['player'].get('projectileClosureVisuals', [])
    bullets = {68, 69, 72, 135, 149, 150, 160, 161, 162, 163, 164}
    require(len(visuals) == 22 and {(row['bulletId'], row['phase']) for row in visuals} == {(bullet, phase) for bullet in bullets for phase in ('flight', 'impact')}, 'Weapon flight/impact visual matrix incomplete')
    require(all(row['changedPixels'] > 32 and row['shaders'] for row in visuals), 'Weapon visual matrix lacks pixel or shader evidence')
    flights = weapons['player'].get('projectileFlightVisuals', [])
    require(len(flights) == 2 and {row['mode'] for row in flights} == {5, 6} and all(row['visiblePixels'] > 32 for row in flights), 'Actual rocket/grenade flights lack unobstructed rendered pixel evidence')
    battery = weapons['player'].get('hudBatteryVisuals', [])
    require(len(battery) == 7 and all(row['visiblePixels'] >= 8 and row['labelText'] == row['expectedText'] for row in battery), 'Weapon HUD lacks seven rendered authoritative battery receipts')
    effects = [366, 368, 370, 376, 411, 412, 429, 431, 434, 435, 436, 441, 447, 714, 805]
    require(buffs['player'].get('renderedClosureEffects') == effects, 'Buff visual matrix incomplete')
    buff_visuals = buffs['player'].get('closureVisuals', [])
    require(len(buff_visuals) == 15 and sorted(row['effectId'] for row in buff_visuals) == effects and all(row['changedPixels'] >= 8 and row['shaders'] for row in buff_visuals), 'Buff closures lack independent pixel or shader evidence')
    icons = buffs['player'].get('iconVisuals', [])
    icon_ids = [1, 13, 7, 3, 5, 14, 4, 18, 16, 6, 15, 15, 15]
    require(len(icons) == 13 and Counter(row['buff'] for row in icons) == Counter(icon_ids) and all(row['visiblePixels'] >= 8 and row['originalAddress'] for row in icons), 'Buff HUD lacks eleven passives and both strong/surviving source icon receipts')
    deaths = buffs['player'].get('observerDeathReceipts', [])
    require(deaths and all(row['playerId'] > 0 and row['packetType'] > 0 and bytes.fromhex(row['payloadHex']) for row in deaths), 'Phoenix lacks a typed raw observer death receipt')
    gameplay = None
    if args.gameplay:
        gameplay = json.loads(args.gameplay.read_text('utf-8-sig'))
        require(gameplay.get('passed') and gameplay.get('serverStopped'), 'Broader gameplay or server cleanup failed')
        require(gameplay.get('exercise', {}).get('passed') and gameplay.get('player', {}).get('passed'), 'Broader network/player regression missing')
        require(gameplay['player'].get('contentComplete') and not gameplay['player'].get('contentErrors'), 'Broader gameplay content incomplete')
        require(not gameplay.get('playerExceptions') and not gameplay.get('serverScriptErrors'), 'Broader gameplay runtime error')
        require(gameplay.get('normalAccountLevels') == [99, 99, 99, 99], 'Broader gameplay changed account permissions')
        for key in ('playerAssemblySha256', 'playerContentCatalogHashes'):
            require(gameplay.get(key) == gameplay.get(key + 'After') == weapons[key], 'Broader gameplay used a different ' + key)
    for field in ('serverBinarySha256', 'harnessAssemblySha256', 'playerAssemblySha256', 'playerContentCatalogHashes'):
        require(weapons[field] == buffs[field], 'Acceptance runs used different ' + field)
    editor = ET.parse(args.editor_tests).getroot()
    require(editor.get('result') == 'Passed' and int(editor.get('failed', '0')) == 0, 'Unity editor tests failed')
    cases = editor.findall('.//test-case')
    for name in ('WeaponCombatProtocolTests', 'BuffEffectsTests', 'CombatBuffContentTests'):
        selected = [case for case in cases if name in case.get('fullname', '')]
        require(selected and all(case.get('result') == 'Passed' for case in selected), name + ': missing or failing cases')
    for name in ('HudCanDisposeNanoStageAfterUnityHasDestroyedIt', 'NativeGrenadeMarkerOneKeepsEveryAcceptedGrenadeVisualAndFlightParameter', 'SharedNativeImpactPreservesTheBulletFamily', 'FireRejectsAnItemIdInPlaceOfTheNativeFamilyMarker'):
        selected = [case for case in cases if name in case.get('fullname', '')]
        require(selected and all(case.get('result') == 'Passed' for case in selected), name + ': missing final regression')
    tests = args.server_tests.read_text('utf-8-sig', errors='replace')
    require('test result: FAILED' not in tests and 'error: test failed' not in tests, 'Server suite failed')
    totals = re.findall(r'test result: ok\. (\d+) passed; 0 failed', tests)
    require(totals and max(map(int, totals)) >= 234 and 'first_service_heartbeat' in tests, 'Full server library suite and heartbeat regression were not accepted')
    ai = args.ai_tests.read_text('utf-8-sig', errors='replace')
    require('test result: ok.' in ai and 'test result: FAILED' not in ai and 'pack_followers' in ai, 'Follower regression missing')
    captures = []
    for name, report in (('weapons', weapons), ('buffs', buffs)):
        directory = Path(report.get('playerProfile', Path(report['directory']) / 'player-profile'))
        rows = (visuals + flights + battery) if name == 'weapons' else (buff_visuals + icons)
        for row in rows:
            require(row.get('screenshot') and (directory / row['screenshot']).is_file(), name + ': missing referenced visual capture')
            if row.get('hiddenScreenshot'):
                require((directory / row['hiddenScreenshot']).is_file(), name + ': missing renderer-hidden comparison')
        paths = sorted(directory.glob('*.png'))
        require(paths, name + ': missing player captures')
        captures.extend(dict(run=name, **ref(path)) for path in paths)
    if gameplay:
        paths = sorted((Path(gameplay['directory']) / 'player-profile').glob('*.png'))
        require(paths, 'Broader gameplay lacks actual player captures')
        captures.extend(dict(run='gameplay', **ref(path)) for path in paths)
    frontend = None
    if args.frontend:
        frontend = json.loads(args.frontend.read_text('utf-8-sig'))
        require(frontend.get('offline') is True and frontend.get('runtimeSha256') == weapons['playerAssemblySha256'], 'Frontend UI used a different final DLL')
        viewports = frontend.get('viewports', [])
        require(len(viewports) == 2 and {(row['width'], row['height']) for row in viewports} == {(1920, 1080), (1920, 1200)}, 'Frontend UI lacks both final resolutions')
        expected_screens = {'login', 'selection', 'selection-occupied', 'delete-character', 'generated-name', 'custom-name', 'appearance', 'appearance-girl', 'quit'}
        for row in viewports:
            require(row.get('passed') and len(row.get('screens', [])) == 9 and set(row['screens']) == expected_screens, 'Frontend UI screens are incomplete')
            log_path = Path(row['log'])
            log_text = log_path.read_text('utf-8-sig', errors='replace')
            require('NATIVE UI PLAYER SMOKE PASSED' in log_text and not re.search(r'^\w+(?:\.\w+)*Exception:', log_text, re.MULTILINE), 'Frontend UI failed or raised an unexpected exception')
            for screen in row['screens']:
                path = log_path.parent / (screen + '.png')
                data = path.read_bytes()
                require(data[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', data[16:24]) == (row['width'], row['height']), 'Frontend UI capture size differs from the requested resolution')
                captures.append(dict(run='frontend', **ref(path)))
    receipt = dict(schema=1, generatedAtUtc=datetime.now(timezone.utc).isoformat(), passed=True,
                   scope='Real server and rendered Unity-player acceptance of the implemented accepted 0104 weapon, privileged PvP and buff contracts.',
                   originalBehaviorParityComplete=False,
                   playerAssemblySha256=weapons['playerAssemblySha256'], playerContentCatalogHashes=weapons['playerContentCatalogHashes'],
                   serverBinarySha256=weapons['serverBinarySha256'], harnessAssemblySha256=weapons['harnessAssemblySha256'],
                   sourceHashes={**weapons['sourceHashes'], **buffs['sourceHashes'], **{path: sha(ROOT.parent / path) for path in [
                       'RustyFusion/scripts/ai/mob_pack_member.luau', 'RustyFusion/src/scripting/npc.rs', 'RustyFusion/src/ai_tests.rs', 'RustyFusion/src/net/ffconnection.rs']}},
                   reports={key: ref(path) for key, path in [('weapons', args.weapons), ('buffs', args.buffs), ('editor', args.editor_tests), ('server', args.server_tests), ('ai', args.ai_tests)]},
                   checks={name: {stage: len(report[stage]['checks']) for stage in ('network', 'player')} for name, report in [('weapons', weapons), ('buffs', buffs)]},
                   renderedEvidence=dict(projectileClosures=len(visuals), actualProjectileFlights=len(flights), weaponBatteryReceipts=len(battery), buffClosures=len(buff_visuals), buffIconReceipts=len(icons), phoenixObserverDeathReceipts=len(deaths)),
                   editorTests=dict(total=int(editor.get('total')), passed=int(editor.get('passed'))),
                   serverTests=max(map(int, totals)), privilegedPvp=pvp, captures=captures,
                   limitations=['Normal account PvP is denied; the local original server allows the character-attack path only through GM30 or higher privilege.',
                                'Primary BulletTable has rows 0..169; positive weapon effect references 170..174 have no source row and no substituted effect.',
                                'Grenade collision uses the current Unity raycast reconstruction; original physics timing is not proven.',
                                'Player group-aura source ownership is covered; native group-NPC passive aura ownership still requires coordinated integration.',
                                'Unproved original skill and item consumers are separately classified in RustyFusion/docs/skill-effect-coverage.json; expected rejection does not establish recovered original behavior.'])
    if gameplay:
        receipt['reports']['gameplay'] = ref(args.gameplay)
        receipt['checks']['gameplay'] = {key: len(gameplay[stage]['checks']) for key, stage in [('network', 'exercise'), ('player', 'player')]}
    if frontend:
        receipt['reports']['frontend'] = ref(args.frontend)
        receipt['checks']['frontend'] = dict(viewports=2, captures=18)
    receipt['reports']['weaponPlayerServer'] = ref(Path(weapons['directory']) / 'player-server.log')
    receipt['reports']['buffServer'] = ref(Path(buffs['directory']) / 'buff-server.log')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    print('COMBAT/BUFF ACCEPTANCE PASSED:', args.output, json.dumps(receipt['checks']))


if __name__ == '__main__':
    main()
