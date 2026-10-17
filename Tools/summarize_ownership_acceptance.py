"""Summarize concrete ownership-migration evidence after tests and player smoke."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from content_paths import ROOT, layout

R=ROOT/'Tools/ContentMigration'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
plan=read(R/'ownership-plan.json');files=read(R/'ownership-verification.json')
source=read(R/'source-reference-audit.json');smoke=read(ROOT/'Logs/ContentLayoutPlayerSmoke.json')
tests=ET.parse(ROOT/'Logs/ContentOwnershipTests.xml').getroot().attrib
build=read(R/'whole-world-build.json')
layout_path=max((ROOT/'Library/com.unity.addressables/BuildReports').glob('buildlayout_*.json'),key=lambda p:p.stat().st_mtime)
build_layout=read(layout_path)
paths=[e['path'] for e in layout()['entries']]
shared=[p for p in paths if p.startswith('Assets/FFOne/Content/') and '/shared/' in p.lower()]
result={
    'passed':not files['errors'] and not source['errors'] and not source['unmappedBaselineUris'] and not shared
        and tests['failed']=='0' and tests['result']=='Passed' and smoke['passed'] and smoke['nanoModelsChecked']
        and build['result']=='Succeeded' and build['errors']==0 and build_layout['DuplicatedAssetCount']==0 and not build_layout['BuildError'],
    'movedFiles':len(plan['moves']),'nanoFamilies':plan['nanoFamilies'],'nanoModelIdentities':69,
    'movesByReason':plan['counts'],'unassignedSourceTextures':plan['counts']['unassigned-source-texture'],
    'perCellTerrainMeshes':sum(m['to'].startswith('Assets/FFOne/Worlds/') for m in plan['moves']),
    'fileVerification':files,'sourceModels':{k:source[k] for k in ('models','uris','maximumAssetPath')},
    'tests':{k:tests.get(k) for k in ('total','passed','failed','result')},'windowsBuild':build,
    'addressables':{'duplicatedAssetCount':build_layout['DuplicatedAssetCount'],'report':str(layout_path.relative_to(ROOT))},
    'player':smoke,
    'backup':str(ROOT.parent/'MigrationBackups/FFOneUnity-before-ownership-20261014'),
    'limitations':['No deduplication or content deletion. Eddy retains two source model identities in one family.',
        'Unassigned source textures have no proven individual owner; absence from prefab dependencies does not imply unused content.',
        'Original shared routes inside import metadata and generic data tables preserve source provenance.',
        'Gameplay behavior and animation completeness are not established by a folder migration or dependency load checks.']}
(R/'ownership-acceptance.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result,ensure_ascii=False));raise SystemExit(0 if result['passed'] else 1)
