"""Assemble the dated transport acceptance only after all final reports exist.

The 151-map geometry baseline deliberately keeps its older player identity and
original HNPC 646 gap. The final binary is proven by the general gameplay run,
the focused map-131 rerun, five scene-only captures and the latest Editor cases.
This script does not launch Unity, a server, or any network fixture.
"""
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
BASELINE_DLL = 'f48396cacd5c0990357b336d3ea596159f8d640cfb45aae34f32d3019ca28e78'
SCENE_MAPS = {146, 147, 148, 149, 151}
SCENE_ONLY = {2, 4, 5, 8, 18, 130, 136, 142, 143, 144, 146, 147, 148, 149, 151, 152}
REPORTS = {
    'geometryBaseline': 'Logs/InstanceValidationGeometryBaseline.json',
    'finalGameplay': 'Logs/LiveGameplayResult.json',
    'finalHnpc131': 'Logs/InstanceValidationHnpc131.json',
    'finalSceneViews': 'Logs/InstanceValidationSceneViewsFinal.json',
}
EDITOR_REPORTS = ['Logs/LiveGameplayEditorTests.xml', 'Logs/RecallPointEditorTests.xml',
                  'Logs/TransportEditorTests.xml', 'Logs/FinalContentEditorTests.xml']
OUTPUT = ROOT / 'Tools/LiveGameplay/acceptance-20261016-slider-vehicles-instances-recall.json'


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def reference(path):
    path = Path(path).resolve()
    return {'path': str(path), 'sha256': sha(path)}


def load(relative):
    return json.loads((ROOT / relative).read_text(encoding='utf-8-sig'))


def clean_final(report, name, instance=False):
    require(report['passed'] is True and report['serverStopped'] is True, name + ': run/server shutdown failed')
    require(not report.get('error'), name + ': runner error')
    require(report['playerAssemblySha256'] == report['playerAssemblySha256After'], name + ': player changed during run')
    require(not report['playerExceptions'] and not report['serverScriptErrors'], name + ': runtime/script errors')
    folder = Path(report['directory'])
    server_log = folder / ('instance-server.log' if instance else 'server.log')
    player_log = folder / ('instance-player.log' if instance else 'player.log')
    require(not re.search(r'panicked|AI script error', server_log.read_text(encoding='utf-8', errors='replace')), name + ': server error in retained log')
    require(not re.search(r'^\w+(?:\.\w+)*Exception:.*$', player_log.read_text(encoding='utf-8', errors='replace'), re.MULTILINE), name + ': exception in retained player log')
    if instance:
        require(not report['serverPanics'], name + ': server panic')
        require(report['instanceNetwork']['passed'] and report['instancePlayer']['passed'], name + ': matrix failed')
        for row in report['instancePlayer']['results']:
            require(row['status'] == 'passed' and not row.get('contentErrors'), name + ': incomplete map ' + str(row['map']))
            for key in ('evidence', 'freshLoad', 'first', 'second'):
                if key in row:
                    view = row[key]
                    require(not view['LastError'] and view['ColliderCount'] > 0 and view['VisualCount'] > 0,
                            name + ': missing scene geometry ' + str(row['map']))
    else:
        require(report['exercise']['passed'] and report['player']['passed'], name + ': gameplay failed')
        require(report['player']['contentComplete'] is True and not report['player']['contentErrors'] and not report['player']['error'],
                name + ': general gameplay content incomplete')
        require(report['normalAccountLevels'] == [99] * 4, name + ': requires four ordinary accounts')


def editor_overlay():
    latest, history, refs = {}, [], []
    for index, relative in enumerate(EDITOR_REPORTS):
        path = ROOT / relative
        root = ET.parse(path).getroot()
        cases = root.findall('.//test-case')
        refs.append({**reference(path), 'reportedCases': len(cases), 'originalResult': root.get('result')})
        for case in cases:
            name = case.attrib['fullname']
            item = {'fullname': name, 'result': case.attrib['result'], 'report': relative,
                    'endTime': case.get('end-time', root.get('end-time', ''))}
            key = (item['endTime'], index)
            if name not in latest or key >= latest[name][0]:
                if name in latest and latest[name][1]['result'] != 'Passed':
                    history.append({'previous': latest[name][1], 'replacedBy': item})
                latest[name] = (key, item)
    outcomes = [latest[name][1] for name in sorted(latest)]
    require(len(outcomes) == 275, 'Expected 275 unique latest Editor cases, got ' + str(len(outcomes)))
    require(all(item['result'] == 'Passed' for item in outcomes), 'Latest Editor cases contain failures/skips')
    required = ['RecallPointContentTests.RecallServerRegistrationSelectsInstanceIdentityAndRejectsMalformedStateAtomically',
                'LiveGameplayContractTests.PlumberHnpcBackpackRetainsItsWeightedIndependentSkeletonAndStartsItsSourceLoop',
                'LiveGameplayContractTests.EveryRenderableAcceptedHnpcAppearanceAssemblesWithItsDeclaredSkinOwnership']
    for suffix in required:
        require('FFOne.Tests.' + suffix in latest, 'Missing decisive Editor regression: ' + suffix)
    return {'reports': refs, 'overlay': 'Latest test-case end time per fullname; report order breaks ties.',
            'uniqueCases': len(outcomes), 'passed': len(outcomes), 'outcomes': outcomes, 'supersededFailures': history}


def checked_hashes(mapping, description):
    for path, expected in mapping.items():
        require(sha(path) == expected, description + ': changed file ' + path)
    return mapping


def captures(report, instance=False):
    folder = Path(report['playerProfile']) if instance else Path(report['directory']) / 'player-profile'
    paths = sorted(folder.glob('*.png'))
    require(paths, 'Missing screenshots in ' + str(folder))
    return {str(path): sha(path) for path in paths}


def main():
    # Load all four first, so an incomplete final run never publishes a partial receipt.
    reports = {name: load(path) for name, path in REPORTS.items()}
    baseline, gameplay, hnpc, scenes = (reports[name] for name in REPORTS)
    require(baseline['passed'] is True and baseline['serverStopped'] is True, 'Geometry baseline did not finish')
    require(baseline['playerAssemblySha256'] == BASELINE_DLL, 'Unexpected geometry baseline binary')
    require(baseline['instancePlan']['maps'] == 151 and baseline['instancePlan']['dispositions'] == {'eligible': 135, 'blocked': 16}, 'Baseline coverage changed')
    rows = baseline['instancePlayer']['results']
    require(len(rows) == 151 and len({row['map'] for row in rows}) == 151 and all(row['status'] == 'passed' for row in rows), 'Baseline must retain 151 distinct accepted map rows')
    kinds = Counter(row['acceptance'] for row in rows)
    require(kinds == {'native-portals-loading-scene-colliders-exit-reconnect': 135, 'scene-load-colliders-unload-fresh-reload': 16}, 'Baseline route/scene split changed')
    blocked = {row['map'] for row in rows if row['acceptance'] == 'scene-load-colliders-unload-fresh-reload'}
    require(blocked == SCENE_ONLY and all(row['portalRoute'] == 'unverified' for row in rows if row['map'] in blocked), 'Scene-only routes must remain explicitly unverified')
    require(baseline['contentComplete'] is False and set(baseline['contentErrors']) == {'131'}
            and len(baseline['contentErrors']['131']) == 1 and 'HNPC type 646' in baseline['contentErrors']['131'][0],
            'Preserve the sole historical HNPC 646 gap without rewriting history')
    require(not baseline['playerExceptions'] and not baseline['serverScriptErrors'] and not baseline['serverPanics'], 'Accepted baseline contains runtime errors')
    require({row['map'] for row in baseline['instancePlan']['unresolvedMapReferences']} == {150}, 'Missing map-150 limitation changed')
    checked_hashes(baseline['partitionReportHashes'], 'Historical partition evidence')
    checked_hashes(baseline['captureHashes'], 'Geometry baseline screenshots')
    require(set(baseline['mapCaptures']) == {str(row['map']) for row in rows}, 'Each baseline map needs its own capture reference')

    clean_final(gameplay, 'Final gameplay')
    clean_final(hnpc, 'Final map 131', instance=True)
    clean_final(scenes, 'Final scene captures', instance=True)
    final_dll = gameplay['playerAssemblySha256']
    require(final_dll != BASELINE_DLL, 'Final HNPC metadata fix must have a distinct player identity')
    require(hnpc['playerAssemblySha256'] == scenes['playerAssemblySha256'] == final_dll, 'Final runs use different DLLs')
    require(hnpc['playerExecutableSha256'] == scenes['playerExecutableSha256'] == gameplay['playerExecutableSha256'], 'Final player executable identities differ')
    require({row['map'] for row in hnpc['instancePlayer']['results']} == {131}, 'Expected focused map-131 rerun')
    require(all(not row['contentErrors'] for row in hnpc['instancePlayer']['results']), 'HNPC 646 gap remains on map 131')
    require({row['map'] for row in scenes['instancePlayer']['results']} == SCENE_MAPS, 'Expected five final scene-only views')
    require(all(row['portalRoute'] == 'unverified' for row in scenes['instancePlayer']['results']), 'Scene captures cannot claim missing portal routes')
    require(scenes['sceneViewpointSha256'] == sha(ROOT / 'Tools/LiveGameplay/instance-scene-viewpoints-20261016.json')
            and scenes['sceneViewpointIndex'] == 1, 'Final camera replay file differs from the accepted candidate')
    assembly = Path(gameplay['playerExecutable']).parent / (Path(gameplay['playerExecutable']).stem + '_Data') / 'Managed/FFOne.Runtime.dll'
    require(sha(assembly) == final_dll, 'Surviving player is not the accepted final DLL')

    source_sets = {'finalGameplay': gameplay['networkAndAiSourceHashes'], 'finalHnpc131': hnpc['sourceHashes'], 'finalSceneViews': scenes['sourceHashes']}
    overlaps = {}
    for name, sources in source_sets.items():
        overlap = set(baseline['sourceHashes']) & set(sources)
        require(overlap, name + ': no map-core source overlap')
        require(all(baseline['sourceHashes'][key] == sources[key] for key in overlap), name + ': baseline map-core source changed')
        overlaps[name] = {key: sources[key] for key in sorted(overlap)}
        for relative, expected in sources.items():
            require(sha(ROOT.parent / relative) == expected, name + ': current source differs: ' + relative)
    for name, report in [('finalHnpc131', hnpc), ('finalSceneViews', scenes)]:
        require(report['instancePlan']['sourceHashes'] == baseline['sourceTableHashes'], name + ': source portal tables changed')
    require(hnpc['serverBinarySha256'] == scenes['serverBinarySha256'] == baseline['serverBinarySha256'], 'Instance server binary changed')

    editor = editor_overlay()
    pictures = {'geometryBaseline': baseline['captureHashes'], 'finalGameplay': captures(gameplay),
                'finalHnpc131': captures(hnpc, True), 'finalSceneViews': captures(scenes, True)}
    required_pictures = {'slider-riding.png', 'board_vehicle.png', 'scooter_vehicle.png', 'recall-point-446.png', 'recall-point-811-presentation.png'}
    require(required_pictures <= {Path(path).name for path in pictures['finalGameplay']}, 'Missing transport/Recall captures')
    for key, maps in [('finalHnpc131', {131}), ('finalSceneViews', SCENE_MAPS)]:
        names = {Path(path).name for path in pictures[key]}
        for map_id in maps:
            require(any(name in names for name in [f'instance-{map_id}.png', f'instance-{map_id}-scene-only.png']), 'Missing final screenshot for map ' + str(map_id))
    for text in ['Slider', 'vehicle', 'Recall Point']:
        require(any(text.lower() in check.lower() for check in gameplay['player']['checks']), 'Final gameplay did not exercise ' + text)

    supporting = ['README.md', 'Docs/slider.md', 'Docs/personal-vehicles.md', 'Docs/instance-acceptance.md', 'Docs/recall-point-rxcom.md',
                  'Assets/FFOne/Editor/Validation/InstanceSceneViewpoints.cs',
                  'Tools/LiveGameplay/instance-scene-viewpoints-20261016.json',
                  'Tools/run_gameplay_validation.py', 'Tools/run_instance_validation.py', 'Tools/run_live_gameplay.py',
                  'Tools/summarize_instance_acceptance.py', 'Tools/recover_recall_point.py',
                  'Tools/LiveGameplay/Program.cs', 'Tools/LiveGameplay/Program.Instances.cs', 'Tools/LiveGameplay/Program.Slider.cs',
                  'Tools/LiveGameplay/Program.Vehicle.cs', 'Tools/LiveGameplay/instances.py',
                  'Assets/FFOne/Tests/Editor/RecallPointContentTests.cs', 'Assets/FFOne/Tests/Editor/SliderTests.cs',
                  'Assets/FFOne/Tests/Editor/GameplayVehicleTests.cs', 'Assets/FFOne/Tests/Editor/GameplayInstanceAcceptanceTests.cs',
                  'Assets/FFOne/Tests/Editor/LiveGameplayContractTests.cs', str(Path(__file__).relative_to(ROOT))]
    result = {
        'schema': 1, 'acceptanceDate': '2026-10-16', 'generatedAtUtc': datetime.now(timezone.utc).isoformat(),
        'passed': True, 'contentGapResolved': True, 'fullMatrixRerunOnFinalBinary': False,
        'finalPlayerAssemblySha256': final_dll,
        'reports': {name: {**reference(ROOT / REPORTS[name]), 'playerAssemblySha256': report['playerAssemblySha256'], 'report': report} for name, report in reports.items()},
        'coverage': {'geometryMaps': 151, 'publishedCells': baseline['instancePlan']['geometryCells'], 'serverPortalRoutes': 135,
                     'sceneOnlyMaps': sorted(SCENE_ONLY), 'portalRouteCoverageComplete': False, 'missingMapDefinitions': [150],
                     'finalBinaryFocusedMaps': sorted({131} | SCENE_MAPS)},
        'contentResolution': {'historicalBaselineContentComplete': False, 'historicalBaselineContentErrors': baseline['contentErrors'],
                              'fix': 'Declared ActorSkinCombiner clothes ownership preserves the independent weighted back_plumber skeleton and original nif-default loop.',
                              'evidence': 'Final map-131 portal/scene/interest rerun has no content errors; final general gameplay is content complete; latest Editor regressions assemble all 195 nonempty HNPC appearances.'},
        'unchangedMapCoreSourceHashes': overlaps, 'editorTests': editor, 'screenshotHashes': pictures,
        'supportingFileHashes': {relative: sha(ROOT / relative) for relative in supporting},
        'rejectedCameraAttempt': reference(ROOT / 'Logs/InstanceValidationSceneViewsFinal-static-anchor-failed.json'),
        'scopeLimits': [
            'The 151-map geometry/route baseline used the earlier DLL recorded in geometryBaseline; it was not rerun in full on the final DLL.',
            'Sixteen maps have scene loading/colliders/unload/fresh-reload acceptance only; their original portal routes remain unverified.',
            'Warp 317 references missing map definition 150; no map or portal was invented.',
            'The geometry baseline retains its original HNPC 646 gap and superseded failures; final focused map-131 and Editor evidence resolve that gap separately.',
            'Five final scene captures validate camera views, not multi-stage mission progression or the unavailable portal routes.',
            'Static ray checks admitted flat camera frames which the player rejected. Retained renderer-bound candidate 1 passes actual pixels; these close views establish scene rendering, not a full room survey.',
            'Recall effect 811 is presentation QA and controlled protocol regression; no live server Recall registration is claimed.',
            'Primary client parity uses shipped static evidence; no paired original-client runtime capture establishes visual or timing parity.',
            'The isolated fixtures establish the listed paths; they do not establish every mission, combat encounter or enemy script in each instance.'
        ],
    }
    temporary = OUTPUT.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    temporary.replace(OUTPUT)
    print(str(OUTPUT))
    print(f'Accepted: {editor["uniqueCases"]} latest Editor cases; 151 historical geometry maps, 135 routes, 16 scene-only; final DLL {final_dll}')


if __name__ == '__main__':
    main()
