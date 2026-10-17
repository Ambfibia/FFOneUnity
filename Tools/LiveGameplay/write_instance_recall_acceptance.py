"""Publish full final-binary instance/Recall acceptance from completed real runs."""
from datetime import datetime, timezone
from pathlib import Path
import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools'))
from run_instance_validation import source_hashes
from run_live_gameplay import player_content_hashes


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def reference(path):
    path = Path(path).resolve()
    return dict(path=str(path), sha256=sha(path))


def load(path):
    return json.loads(Path(path).read_text('utf-8-sig'))


REQUIRED_RECALL_NETWORK = {
    'RustyFusion accepts registration at the real Recall Point',
    'Recall ACK contains the network NPC identity and authored coordinates, independently of map 75 and type 1386',
    'registered Recall Nano power returns the player through real GOTO_SUCC',
    'Recall power returns to registered NPC coordinates rather than the pre-warp overworld entrance',
}
REQUIRED_RECALL_PLAYER = {
    'RustyFusion accepts the real Recall registration and its decoded NPC identity selects authored ES811',
    'rejected live registration preserves the accepted Recall ID and ES811 marker',
    'native Recall action resolves self without hostile aim and RustyFusion returns to the registered NPC coordinates',
    'successful authored exit clears the native Recall registration',
}


def require_recall_checks(gameplay):
    require(REQUIRED_RECALL_NETWORK <= set(gameplay['exercise']['checks']), 'Missing actual server Recall registration or power checks.')
    require(REQUIRED_RECALL_PLAYER <= set(gameplay['player']['checks']), 'Missing actual native Recall registration, power or lifecycle checks.')


def map_capture_paths(matrix, expected):
    """Resolve one specifically named rendered capture for each accepted map."""
    if 'mapCaptures' in matrix:
        require(set(matrix['mapCaptures']) == {str(m) for m in expected}, 'Map capture identities do not cover the exact accepted maps.')
        paths = {m: Path(matrix['mapCaptures'][str(m)]) for m in expected}
    else:
        paths = {m: Path(matrix['playerProfile']) / f'instance-{m}.png' for m in expected}
    for map_id, path in paths.items():
        require(path.name == f'instance-{map_id}.png' and path.is_file(), 'Missing final rendered map capture: ' + str(path))
    return paths


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix', type=Path, required=True)
    parser.add_argument('--gameplay', type=Path, required=True)
    parser.add_argument('--editor-tests', type=Path, required=True)
    parser.add_argument('--server-tests', type=Path, default=ROOT.parent / 'RustyFusion/target/instance-recall-tests.log')
    parser.add_argument('--output', type=Path, default=ROOT / 'Tools/LiveGameplay/acceptance-20261016-instance-routes-recall.json')
    args = parser.parse_args()
    matrix, gameplay = load(args.matrix), load(args.gameplay)
    xdt = load(ROOT.parent / 'RustyFusion/tabledata/xdt.json')
    expected = {r['m_iInstanceNameID'] for r in xdt['m_pInstanceTable']['m_pInstanceData']} - {0}
    require(len(expected) == 151, 'The accepted map identities must remain the same 151 maps.')
    require(matrix['passed'] and matrix['serverStopped'], 'Full matrix or isolated server shutdown failed.')
    require(gameplay['passed'] and gameplay['serverStopped'], 'Final gameplay/Recall or isolated server shutdown failed.')
    network, player = matrix['instanceNetwork'], matrix['instancePlayer']
    require(network['coverageComplete'] and player['routeCoverageComplete'], 'Every map requires a real admitted route.')
    for label, report in [('network', network), ('player', player)]:
        require(report['passed'], label + ': final matrix report failed.')
        rows = report['results']
        require(len(rows) == 151 and {r['map'] for r in rows} == expected, label + ': missing or duplicate map identity.')
        require(all(r['status'] == 'passed' for r in rows), label + ': some maps failed.')
    require(all(r['acceptance'] == 'native-portals-loading-scene-colliders-exit-reconnect' for r in player['results']),
            'Scene-only checks cannot count as server route acceptance.')
    require(all(not r.get('contentErrors') for r in player['results']), 'Matrix NPC content is incomplete.')
    require(matrix.get('contentComplete') is True and not matrix.get('contentErrors'), 'Final matrix content is incomplete.')
    require(gameplay['exercise']['passed'] and gameplay['player']['passed'] and gameplay['player']['contentComplete'],
            'Final gameplay/Recall is incomplete.')
    require(not gameplay['player']['contentErrors'], 'Final gameplay has content errors.')
    identity = gameplay['playerAssemblySha256']
    require(matrix['playerAssemblySha256'] == identity, 'Full matrix and Recall must use the same final DLL.')
    for name, report in [('matrix', matrix), ('gameplay', gameplay)]:
        require(not report.get('error'), name + ': runner error.')
        for field in ('playerExceptions', 'serverScriptErrors', 'serverPanics'):
            require(not report.get(field), name + ': ' + field)
        if 'playerAssemblySha256After' in report:
            require(report['playerAssemblySha256After'] == identity, name + ': player changed during the run.')
    current_dll = ROOT / 'Builds/Windows/FFOne_Data/Managed/FFOne.Runtime.dll'
    require(sha(current_dll) == identity, 'Delivered DLL differs from the accepted final binary.')
    content_hashes = matrix['playerContentCatalogHashes']
    require(content_hashes == gameplay['playerContentCatalogHashes'] == gameplay['playerContentCatalogHashesAfter'] ==
            player_content_hashes(ROOT / 'Builds/Windows/FFOne.exe'),
            'Full matrix, Recall and delivered player must have identical built content catalogs.')
    publication_path = ROOT / 'Assets/FFOne/Editor/ImportMetadata/NativeDataPublications/data/tables/xdt.json'
    publication = load(publication_path)
    xdt_hash = sha(ROOT.parent / 'RustyFusion/tabledata/xdt.json')
    require(publication['SourceHash'].lower() == xdt_hash and
            sha(ROOT / 'Assets/FFOne/Data/Gameplay/Source/tables/xdt.json') == xdt_hash,
            'Client publication and production server XDT revisions differ.')
    for path, digest in source_hashes().items():
        require(matrix['sourceHashes'].get(path) == digest, 'Matrix source changed: ' + path)
    gameplay_sources = gameplay.get('networkAndAiSourceHashes', {})
    required_sources = {
        'FFOneUnity/Assets/FFOne/Runtime/Network/ProtocolRecall.cs',
        'FFOneUnity/Assets/FFOne/Runtime/Network/ProtocolNpcSkill.cs',
        'FFOneUnity/Assets/FFOne/Runtime/Network/ProtocolNanoSkill.cs',
        'FFOneUnity/Assets/FFOne/Runtime/UI/Gameplay/LiveGameplayRecallSmoke.cs',
        'RustyFusion/src/server/shard/nano.rs', 'RustyFusion/src/server/shard/combat.rs', 'RustyFusion/src/skills.rs',
    }
    require(required_sources <= {Path(p).as_posix() for p in gameplay_sources}, 'Gameplay report omits required Recall/packet source fingerprints.')
    for path, digest in gameplay_sources.items():
        require(sha(ROOT.parent / path) == digest, 'Gameplay source changed: ' + path)
    for field, path in (
        ('playerExecutableSha256', ROOT / 'Builds/Windows/FFOne.exe'),
        ('serverBinarySha256', ROOT.parent / 'RustyFusion/target/debug/hybrid.exe'),
        ('harnessAssemblySha256', ROOT / 'Logs/LiveGameplayHarness/bin/net10.0/LiveGameplay.dll'),
    ):
        require(matrix.get(field) == sha(path), 'Delivered executable differs from the accepted matrix: ' + field)
    tests = ET.parse(args.editor_tests).getroot()
    cases = tests.findall('.//test-case')
    require(cases and tests.get('result') == 'Passed' and all(c.get('result') == 'Passed' for c in cases),
            'Final Editor test run must pass without skipped or failed selected cases.')
    server_log = args.server_tests.read_text('utf-8-sig')
    server_result = re.search(r'test result: ok\. (\d+) passed; 0 failed; 0 ignored; 0 measured; 0 filtered out;', server_log)
    require(server_result, 'The final server library suite must pass.')
    require(int(server_result.group(1)) == 210, 'The final acceptance requires the complete 210-test server library suite.')
    require('test result: FAILED' not in server_log and 'error: test failed' not in server_log,
            'The final server log also contains a failed test run.')
    for test in ('recall_nano_wire_request_resolves_self_charges_once_and_rejects_missing_or_expired_point',
                 'recall_rejections_preserve_last_registration_and_return_fail',
                 'mission_3337_avoided_mega_emits_exact_empty_npc_hit_frame'):
        require(re.search(r'test [^\r\n]*::' + re.escape(test) + r' \.\.\. ok', server_log), 'Final server suite omits required regression: ' + test)
    selected_classes = {c.get('classname', c.get('fullname', '').rsplit('.', 1)[0]) for c in cases}
    require({'FFOne.Tests.RecallPointContentTests', 'FFOne.Tests.NpcSkillProtocolTests',
             'FFOne.Tests.GameplayInstanceAcceptanceTests', 'FFOne.Tests.MissionFailureContractTests',
             'FFOne.Tests.GameplayTravelTests', 'FFOne.Tests.LiveGameplayContractTests'} <= selected_classes,
            'Final Editor selection omits required Recall, instance, mission or protocol regression families.')
    required_methods = {'RecallRegistrationProtocolValidatesIdentityAndExactFrameSize',
                        'RecallServerRegistrationSelectsInstanceIdentityAndRejectsMalformedStateAtomically',
                        'AvoidedOriginalBossMegaAcceptsExactEmptyNpcHit',
                        'EmptyNpcHitStillRejectsTruncatedTrailingNegativeOversizedAndUnknownFrames',
                        'EmptyNpcMissDoesNotPermitEmptyNanoUseOrCorruptionReplies'}
    require(required_methods <= {c.get('methodname') for c in cases}, 'Final Editor selection omits required protocol regressions.')
    require_recall_checks(gameplay)
    recall_network = [c for c in gameplay['exercise']['checks'] if 'recall' in c.lower() or 'rxcom' in c.lower()]
    recall_player = [c for c in gameplay['player']['checks'] if 'recall' in c.lower() or 'rxcom' in c.lower()]
    captures = {}
    for folder in [Path(gameplay['directory']) / 'player-profile'] + [Path(r['playerProfile']) for r in
                   ([matrix] if 'playerProfile' in matrix else [load(p) for p in matrix['partitionReports']])]:
        for image in sorted(folder.glob('*.png')):
            captures[str(image)] = sha(image)
    for path in map_capture_paths(matrix, expected).values():
        require(str(path) in captures, 'Final map capture is absent from the accepted capture manifest: ' + str(path))
    recall_capture_root = Path(gameplay['directory']) / 'player-profile'
    for name in ('recall-point-446.png', 'recall-point-811-registered.png', 'recall-registration.png'):
        require((recall_capture_root / name).is_file(), 'Missing actual Recall registration capture: ' + name)
    result = dict(schema=1, generatedAtUtc=datetime.now(timezone.utc).isoformat(), passed=True,
                  fullMatrixRerunOnFinalBinary=True, playerAssemblySha256=identity,
                  playerContentCatalogHashes=content_hashes,
                  reports=dict(matrix=reference(args.matrix), gameplay=reference(args.gameplay), editor=reference(args.editor_tests), serverTests=reference(args.server_tests)),
                  xdtPublication=dict(**reference(publication_path), serverAndClientSourceSha256=xdt_hash, guid=publication['Guid']),
                  coverage=dict(maps=151, serverRoutes=151, nativePlayerRoutes=151,
                                publishedCells=len({r['cell'] for r in player['results']}), routeCoverageComplete=True),
                  editorTests=dict(total=len(cases), passed=len(cases)),
                  serverTests=dict(total=int(server_result.group(1)), passed=int(server_result.group(1))),
                  gameplayChecks=dict(network=len(gameplay['exercise']['checks']), player=len(gameplay['player']['checks'])),
                  recall=dict(networkChecks=recall_network, playerChecks=recall_player),
                  sourceHashes=source_hashes(), gameplaySourceHashes=gameplay_sources, captureHashes=captures,
                  maps=[dict(map=r['map'], cell=r['cell'], checks=r['checks']) for r in player['results']],
                  limits=['Added server routes and source-table repairs are documented separately from original-client parity.',
                          'The instance fixture isolates route/scene acceptance from unrelated hostile overworld spawns.',
                          'One selected route is exercised for each of the 151 map identities; this does not establish every alternative warp, mission or encounter in every instance.',
                          'No paired original-client runtime recording establishes full visual or timing parity.'])
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: result[k] for k in ('passed', 'fullMatrixRerunOnFinalBinary', 'coverage', 'editorTests', 'gameplayChecks')}))


if __name__ == '__main__':
    main()
