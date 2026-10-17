"""Collect migration evidence without committing Unity's large Build Layout report."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DESTINATION = ROOT / 'Tools/ContentMigration/acceptance.json'


def read(relative):
    return json.loads((ROOT / relative).read_text(encoding='utf-8-sig'))


def main():
    reports = ROOT / 'Library/com.unity.addressables/BuildReports'
    report = max(reports.glob('buildlayout_*.json'), key=lambda path: path.stat().st_mtime)
    with report.open(encoding='utf-8-sig') as source:
        layout = json.load(source)
    references = layout['references']['RefIds']
    bundles = [row['data'] for row in references if row['type']['class'] == 'BuildLayout/Bundle']
    tests = ET.parse(ROOT / 'Logs/ContentLayoutFinalEditorTests.xml').getroot()
    full_tests = ET.parse(ROOT / 'Logs/ContentLayoutEditorTests.xml').getroot()
    smoke = read('Logs/ContentLayoutPlayerSmoke.json')
    result = {
        'scope': 'Whole-library asset layout migration; gameplay architecture remains partially implemented.',
        'verification': read('Tools/ContentMigration/verification.json'),
        'sourceReferences': read('Tools/ContentMigration/source-reference-audit.json'),
        'addressables': {
            'report': str(report.relative_to(ROOT)).replace('\\', '/'),
            'unityVersion': layout['UnityVersion'],
            'packageVersion': layout['PackageVersion'],
            'buildStarted': layout['BuildStartTime'],
            'durationSeconds': layout['Duration'],
            'buildError': layout['BuildError'],
            'duplicatedAssetCount': layout['DuplicatedAssetCount'],
            'bundleCount': len(bundles),
            'bundleBytes': sum(bundle['FileSize'] for bundle in bundles),
            'largestBundles': [
                {'name': bundle['Name'], 'bytes': bundle['FileSize']}
                for bundle in sorted(bundles, key=lambda item: item['FileSize'], reverse=True)[:5]
            ],
        },
        'windowsDevelopmentBuild': read('Logs/ContentLayoutBuildResult.json'),
        'initialFullEditorTests': {key: full_tests.attrib.get(key) for key in ('total', 'passed', 'failed', 'result')},
        'finalFocusedEditorTests': {key: tests.attrib.get(key) for key in ('total', 'passed', 'failed', 'result')},
        'testNote': 'The two initial EnvironmentSharingTests failures were fixed; their entire fixture and ContentLayoutTests were rerun. The full suite was not rerun after the fixes.',
        'playerSmoke': {key: smoke[key] for key in ('passed', 'exitCode', 'seconds', 'errors', 'knownBaselineDiagnostics')},
        'limitations': [
            'Authored cell scenes do not yet replace WorldTileContent in the runtime streamer.',
            'NPC/Nano animation state fidelity, typed gameplay definitions, audio events/mixers and trigger semantics remain unfinished.',
            'Ambiguous content still requires manual classification; mass source deduplication was not performed.',
            'The player smoke validates loading, not visual fidelity, network gameplay or mobile performance.',
            'Zero duplicated assets refers to the Addressables Build Layout, not every possible byte duplicate or player/bundle overlap.',
        ],
    }
    DESTINATION.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    failed = (result['verification']['errors'] or result['sourceReferences']['errors']
              or result['addressables']['buildError'] or tests.attrib.get('failed') != '0'
              or result['windowsDevelopmentBuild']['result'] != 'Succeeded' or not smoke['passed'])
    raise SystemExit(1 if failed else 0)


if __name__ == '__main__':
    main()
