"""Verify every baked GLB against the independent FFOneClient source, including untouched bytes."""
from pathlib import Path
from content_paths import resolve, files
import json, struct, collections
import numpy as np

root=Path(__file__).resolve().parents[1]
audit=json.loads((root/'Logs/EnvironmentBasisAudit.json').read_text())
def glb(path):
    raw=path.read_bytes(); offset=12; doc=None; buffer=None
    while offset<len(raw):
        size,kind=struct.unpack_from('<II',raw,offset); data=raw[offset+8:offset+8+size];offset+=8+size
        if kind==0x4e4f534a:doc=json.loads(data)
        elif kind==0x004e4942:buffer=data
    return doc,buffer

checked=0; vertices=0; failures=[]; categories=collections.Counter()
for entry in audit.values():
    identity=entry['identity']; local=resolve(root/'Assets/Resources'/(identity+'.glb'))
    new,actual=glb(local)
    if new.get('extras',{}).get('ffoneAuthoringBasis')!='sourceZUpToYUp':continue
    old,original=glb(root.parent/'FFOneClient/assets/game'/(identity+'.glb'))
    expected=bytearray(original); seen=set()
    for mesh in old['meshes']:
        for primitive in mesh['primitives']:
            for semantic in ['POSITION','NORMAL','TANGENT']:
                index=primitive['attributes'].get(semantic)
                if index is None or index in seen:continue
                seen.add(index); a=old['accessors'][index]; view=old['bufferViews'][a['bufferView']]
                width=4 if semantic=='TANGENT' else 3; stride=view.get('byteStride',width*4)
                values=np.ndarray((a['count'],width),dtype='<f4',buffer=expected,offset=view.get('byteOffset',0)+a.get('byteOffset',0),strides=(stride,4))
                before=values.copy();values[:,1]=before[:,2];values[:,2]=-before[:,1]
                if semantic=='POSITION':vertices+=a['count']
                for bound,func in [('min',np.min),('max',np.max)]:
                    if bound in a:
                        expected_bounds=func(values[:,:3],axis=0)
                        if not np.array_equal(expected_bounds,np.array(new['accessors'][index][bound][:3],dtype='<f4')):
                            failures.append(identity+': invalid '+bound)
    if bytes(expected)!=actual:failures.append(identity+': binary geometry or unrelated stream mismatch')
    for field in ['materials','textures','images','samplers','nodes','scenes','scene','meshes','bufferViews','buffers']:
        if old.get(field)!=new.get(field):failures.append(identity+': unexpected '+field+' change')
    checked+=1;categories[entry['category']]+=1
result=dict(normalizedModels=checked,vertices=vertices,categories=categories,failures=failures)
(root/'Logs/EnvironmentBasisValidation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
if failures:raise SystemExit(1)
