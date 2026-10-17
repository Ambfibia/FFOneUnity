"""Validate preserved GUIDs and source GLB URI resolution without loading Unity."""
from pathlib import Path, PurePosixPath
import json, os, struct
from content_paths import layout
ROOT=Path(__file__).resolve().parents[1]
d=layout()
byold={e['old']:e for e in d['entries']}
errors=[];baseline_missing=[];models=0;uris=0
for e in d['entries']:
    if not e['old'].endswith('.glb'):continue
    models+=1
    p=ROOT/e['path']
    if not p.exists():errors.append({'missing':e['path']});continue
    with p.open('rb') as f:
        head=f.read(20)
        if len(head)<20:errors.append({'badGlb':e['path']});continue
        length,kind=struct.unpack('<II',head[12:20])
        if kind!=0x4e4f534a:errors.append({'badJsonChunk':e['path']});continue
        doc=json.loads(f.read(length).rstrip(b' \0'))
    values=[]
    def walk(v):
        if isinstance(v,dict):
            for k,x in v.items():
                if k=='uri' and isinstance(x,str):values.append(x)
                else:walk(x)
        elif isinstance(v,list):
            for x in v:walk(x)
    walk(doc)
    for uri in set(values):
        if uri.startswith('data:'):continue
        uris+=1
        old=os.path.normpath(str(PurePosixPath(e['old']).parent/uri)).replace('\\','/')
        target=byold.get(old)
        if target is None:baseline_missing.append({'source':e['old'],'uri':uri})
        elif not (ROOT/target['path']).exists():errors.append({'source':e['old'],'uri':uri,'target':target['path']})
report={'models':models,'uris':uris,'errors':errors,'unmappedBaselineUris':baseline_missing,'maximumAssetPath':max(len(e['path']) for e in d['entries'])}
(ROOT/'Tools/ContentMigration/source-reference-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v if not isinstance(v,list) else len(v) for k,v in report.items()}))
if errors:raise SystemExit(1)
