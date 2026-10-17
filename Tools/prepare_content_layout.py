"""Finish the reviewed manifest and prepare the edit/reimport compatibility map."""
from pathlib import Path, PurePosixPath
import json, os, re
ROOT=Path(__file__).resolve().parents[1]
receipt=ROOT/'Tools/ContentMigration/layout.json'
d=json.loads(receipt.read_text(encoding='utf-8'))
byold={e['old']:e for e in d['entries']}
source_owners={}
for e in d['entries']:
    if e['old'].startswith('Assets/Resources/objects/') and e['old'].endswith('.glb'):
        owner=e['path'].split('/Models/Source/')[0]
        if owner!=e['path']:
            parent=str(PurePosixPath(e['old']).parent)
            if parent not in source_owners or e['old'].endswith('/visual.glb'):source_owners[parent]=owner
for e in d['entries']:
    old=e['old']; path=e['path']
    for branch,folder in [('ImportedCells','Imported'),('SourceCells','Source')]:
        prefix='Assets/FFOne/Worlds/Overworld/'+branch+'/'
        if path.startswith(prefix):
            cell,slash,rest=path[len(prefix):].partition('/')
            path='Assets/FFOne/Worlds/Overworld/Cells/'+cell+'/'+folder+slash+rest
    if old.startswith('Assets/Resources/objects/') and not old.endswith('.glb'):
        p=PurePosixPath(old).parent
        while str(p).startswith('Assets/Resources/objects/'):
            if str(p) in source_owners:
                role='Textures/Source' if old.endswith('.png') else 'Definitions/Source'
                path=source_owners[str(p)]+'/'+role+'/'+old[len(str(p))+1:]
                break
            p=p.parent
    e['path']=path
targets={}
for e in d['entries']:
    k=e['path'].casefold()
    if k in targets:raise ValueError('Collision: '+str([targets[k],e['old'],e['path']]))
    targets[k]=e['old']
directories={}
for e in d['entries']:
    old=str(PurePosixPath(e['old']).parent); new=str(PurePosixPath(e['path']).parent)
    while old.startswith('Assets/'):
        directories[old]=os.path.commonpath([directories[old],new]).replace('\\','/') if old in directories else new
        old=str(PurePosixPath(old).parent)
d['directories']=directories
receipt.write_text(json.dumps(d,ensure_ascii=False,indent=1),encoding='utf-8')
# Update only concrete Unity scene/UI references. Imported JSON keeps its original
# identities, and publication records are resolved at their compatibility boundary.
mapping={e['old']:e['path'] for e in d['entries'] if e['old']!=e['path']}
for base in ['ProjectSettings','Assets/Resources/ui','Assets/UI','Assets/UI Toolkit']:
    for p in (ROOT/base).rglob('*'):
        if not p.is_file() or p.suffix not in ('.asset','.uxml','.uss','.tss'):continue
        try:s=p.read_text(encoding='utf-8-sig')
        except UnicodeDecodeError:continue
        n=re.sub(r'Assets/[^"\s?<>]+',lambda m:mapping.get(m[0],m[0]),s)
        if n!=s:p.write_text(n,encoding='utf-8')
print(json.dumps({'assets':len(d['entries']),'directories':len(directories),'targets':len(targets)}))
