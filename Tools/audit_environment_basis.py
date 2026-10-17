"""Read-only placement evidence for source model up axes."""
from pathlib import Path
from content_paths import resolve, files
import re, json, collections, math

root = Path(__file__).resolve().parents[1]
library = resolve(root/'Assets/FFOne/Editor/Environment/EnvironmentLibrary.asset').read_text(encoding='utf-8')
rows = re.findall(r'  - Identity: (.*?)\n    Name:.*?\n    Category: (.*?)\n    PrefabGuid: (.*?)\n', library, re.S)
models = {guid.strip(): dict(identity=identity.strip(), category=cat.strip(), counts=collections.Counter(), samples=[]) for identity,cat,guid in rows}
for path in files(root/'Assets/Resources/World/Tiles','Content.asset'):
    text = path.read_text(encoding='utf-8').split('  Colliders:')[0]
    for block in text.split('  - Name:')[1:]:
        guid = re.search(r'    Model: .*?guid: (\w+)', block)
        rotation = re.search(r'    Rotation: \{x: (.*?), y: (.*?), z: (.*?), w: (.*?)\}', block)
        if not guid or not rotation or guid[1] not in models: continue
        x,y,z,w = map(float,rotation.groups()); norm=x*x+y*y+z*z+w*w
        up = (2*(x*y+w*z)/norm, 1-2*(x*x+z*z)/norm, 2*(y*z-w*x)/norm)
        axis = max(range(3),key=lambda i:abs(up[i]))
        key = ('+' if up[axis]>0 else '-')+'XYZ'[axis] if abs(up[axis])>.995 else 'tilted'
        entry=models[guid[1]]; entry['counts'][key]+=1
        if len(entry['samples'])<2: entry['samples'].append(dict(tile=path.parent.name,rotation=[x,y,z,w],up=up))
summary=collections.Counter()
for entry in models.values():
    counts=entry['counts']; axes=[k for k in counts if k!='tilted']
    entry['axis']=axes[0] if len(axes)==1 else 'ambiguous' if axes else 'unplaced/tilted'
    summary[entry['axis']]+=1
out=root/'Logs/EnvironmentBasisAudit.json'; out.write_text(json.dumps(models,indent=2),encoding='utf-8')
print(json.dumps(dict(models=len(models),axes=summary,examples=[e for e in models.values() if 'dt_cpch_chair/models/dt_cpch_chair/' in e['identity'] or 'dt_cpfl_tree_01_standard_1_dt_cpfltree_01/models/' in e['identity']]),indent=2))
