import json
from pathlib import Path
from collections import defaultdict
from content_paths import ROOT,layout
R=ROOT/'Tools/ContentMigration'
targets=json.loads((R/'ownership-unassigned-textures.json').read_text())['textures']
xdt=json.loads((ROOT/'Assets/FFOne/Data/Gameplay/Source/tables/xdt.json').read_text(encoding='utf-8'))
names={Path(t['path']).stem for t in targets if '.mips/' not in t['path']}
records=defaultdict(list)
for table,body in xdt.items():
    if table=='_ffone':continue
    for section,rows in body.items():
        if not isinstance(rows,list):continue
        for i,row in enumerate(rows):
            if not isinstance(row,dict):continue
            for key,value in row.items():
                if isinstance(value,str) and value.lower() in names:
                    records[value.lower()].append({'table':table,'section':section,'row':i,'field':key,'values':row})
(R/'unassigned-xdt-owners.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
for name in sorted(names):
    hits=records[name];models=sorted({v for h in hits for k,v in h['values'].items() if 'MeshModel' in k and isinstance(v,str)})
    print(name,':',','.join(models),'rows',len(hits))
