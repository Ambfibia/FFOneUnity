"""Summarize recorded migration, editor, seam and built-player evidence."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ROOT / 'Tools/ContentMigration'


def read(name):
    return json.loads((REPORTS / f'whole-world-{name}.json').read_text(encoding='utf-8-sig'))


def main():
    scenes, seams, build, player = (read(name) for name in ('scenes', 'seam-audit', 'build', 'player'))
    tests = ET.parse(ROOT / 'Logs/WholeWorldEditorTests.xml').getroot().attrib
    layout_path = max((ROOT / 'Library/com.unity.addressables/BuildReports').glob('buildlayout_*.json'), key=lambda p: p.stat().st_mtime)
    layout = json.loads(layout_path.read_text(encoding='utf-8-sig'))
    bundles = [r['data'] for r in layout['references']['RefIds'] if r['type']['class'] == 'BuildLayout/Bundle']
    applied = read('seam-applied')
    editor_cycle = read('editor-cycle')
    boundary_path = REPORTS / 'world-boundary-applied.json'
    boundary_repairs = json.loads(boundary_path.read_text()) if boundary_path.exists() else {}
    accepted_hashes_match = True
    preserved = []
    for row in scenes:
        path = ROOT / f"Assets/FFOne/Worlds/Overworld/Cells/{row['id']}/{row['id']}.unity"
        expected = boundary_repairs.get(row['id'], {}).get('sceneSha256', applied.get(row['id'], {}).get('sceneSha256', row['acceptedSha256']))
        accepted_hashes_match &= hashlib.sha256(path.read_bytes()).hexdigest() == expected
        if row['kind'] == 'preserved-existing':
            preserved.append({'id': row['id'], 'unchanged': hashlib.sha256(path.read_bytes()).hexdigest() == row['beforeSha256']})
    source_gaps = seams.get('retainedSourceDiscontinuities', [])
    passed = all((
        len(scenes) == 170, accepted_hashes_match, all(r['unchanged'] for r in preserved),
        tests.get('result') == 'Passed' and tests.get('failed') == '0',
        build['result'] == 'Succeeded' and build['errors'] == 0,
        player['passed'], len(player.get('centersVisited', [])) == 170,
        editor_cycle['passed'],
        seams.get('smallGapRepairPassed', False), not seams['conflicts'],
        not layout['BuildError'], layout['DuplicatedAssetCount'] == 0,
    ))
    report = {
        'migrationAndStreamingPassed': passed,
        'sourceBehaviorCoverageComplete': player['passed'] and not player.get('sourceCoverage', []),
        'terrainIsGloballySeamless': not source_gaps and seams['maximumGap'] <= .001,
        'cells': len(scenes),
        'finalSceneHashesMatchAcceptedState': accepted_hashes_match,
        'subsequentBoundaryRepairs': {'cells': len(boundary_repairs), 'report': 'Tools/ContentMigration/world-boundary-acceptance.json' if boundary_repairs else None},
        'newScenes': sum(r['kind'] == 'created' for r in scenes),
        'upgradedExistingScenes': [r['id'] for r in scenes if r['kind'] == 'upgraded-existing'],
        'preservedExistingScenes': preserved,
        'editorTests': {k: tests.get(k) for k in ('total', 'passed', 'failed', 'result')},
        'editorPlayStopCycle': editor_cycle,
        'build': build,
        'addressables': {'source': str(layout_path.relative_to(ROOT)), 'duplicatedAssetCount': layout['DuplicatedAssetCount'], 'buildError': layout['BuildError'], 'bundleCount': len(bundles), 'bundleBytes': sum(b['FileSize'] for b in bundles)},
        'player': {k: v for k, v in player.items() if k not in ('observations', 'visited', 'centersVisited')},
        'seams': {'sharedEdgeCoordinates': seams['sharedEdgeCoordinates'], 'adjacentEdges': len(seams['adjacentEdges']), 'maximumRemainingSourceGap': seams['maximumGap'], 'retainedSourceCoordinates': len(source_gaps), 'smallGapRepairPassed': seams.get('smallGapRepairPassed', False), 'edgesWithShiftedSampling': sum(e['sharedCoordinates'] < 129 for e in seams['adjacentEdges']), 'verticesAdjusted': sum(r['verticesAdjusted'] for r in applied.values()), 'cellsAdjusted': len(applied)},
        'limitations': [
            'The seams block describes the initial whole-world seam audit; subsequent reviewed repairs and pairwise classification are recorded separately under subsequentBoundaryRepairs.report.',
            'Large pre-existing source terrain discontinuities are reported, not flattened; migration success does not mean globally seamless terrain.',
            'No automatic three-way source merge; existing authoring scenes are authoritative.',
            'Accepted source animation references without published geometry are reported under player.sourceCoverage; missing geometry has not been reconstructed.',
            'Network gameplay, AI navigation, trigger semantics and mobile performance are outside this acceptance.',
            'Render checks detect empty/black frames; they do not establish original visual fidelity for every cell.',
        ],
    }
    output = REPORTS / 'whole-world-acceptance.json'
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False))
    raise SystemExit(0 if passed else 1)


if __name__ == '__main__':
    main()
