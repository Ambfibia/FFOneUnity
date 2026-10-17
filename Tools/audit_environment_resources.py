"""Compare complete serialized payloads, retaining mesh channels and texture sampling/mips."""
from pathlib import Path
from content_paths import resolve, files
import re, json, hashlib, collections

root=Path(__file__).resolve().parents[1]
def guid(path):return re.search(r'^guid: (\w+)',Path(str(path)+'.meta').read_text(),re.M)[1]
def payload(path):return re.sub(r'^  m_Name:.*(?:\n    .*?)*\n', '  m_Name: <identity>\n',path.read_text(encoding='utf-8'),count=1,flags=re.M)
def digest(text):return hashlib.sha256(text.encode()).hexdigest()
def groups(rows):
    by=collections.defaultdict(list)
    for row in rows:by[row['hash']].append(row)
    return [v for v in by.values() if len(v)>1]

textures=[]
for path in files(root/'Assets/Generated/WorldModelTextures','*.asset'):
    textures.append(dict(path=path.relative_to(root).as_posix(),guid=guid(path),hash=digest(payload(path)),bytes=path.stat().st_size))
texture_groups=groups(textures); texture_alias={r['guid']:sorted(g,key=lambda r:r['path'])[0]['guid'] for g in texture_groups for r in g}
by_guid={}; manifests=[]
for path in files(root/'Assets/Editor/NativePublications/objects','publication.json'):
    m=json.loads(path.read_text());m['record']=path.relative_to(root).as_posix();manifests.append(m)
    for a in m['Assets']:
        if a['Type'] not in ['UnityEngine.Mesh','UnityEngine.Material','UnityEngine.AnimationClip']:continue
        if a['Guid'] in by_guid:continue
        p=resolve(root/a['Path']);text=payload(p)
        if a['Type']=='UnityEngine.Material':
            text=re.sub(r'guid: (\w+)',lambda x:'guid: '+texture_alias.get(x[1],x[1]),text)
        by_guid[a['Guid']]=dict(path=a['Path'],guid=a['Guid'],name=a['Name'],type=a['Type'],hash=digest(text),bytes=p.stat().st_size)
resources=list(by_guid.values());duplicates=groups(resources)
visuals=[]
for m in manifests:
    mesh=[by_guid[a['Guid']]['hash'] for a in m['Assets'] if a['Type']=='UnityEngine.Mesh']
    mats=[by_guid[a['Guid']]['hash'] for a in m['Assets'] if a['Type']=='UnityEngine.Material']
    visuals.append(dict(identity=m['SourcePath'][len('Assets/Resources/'):-4],prefab=m['PrefabPath'],geometry=mesh,materials=mats))
families=collections.defaultdict(list)
for v in visuals:families[tuple(v['geometry'])].append(v)
variants=[g for key,g in families.items() if key and len(g)>1 and len({tuple(v['materials']) for v in g})>1]
summary=dict(models=len(manifests),resources=len(resources),textures=len(textures),textureDuplicateGroups=len(texture_groups),textureCopies=sum(len(g)-1 for g in texture_groups),
    duplicateGroups=dict(collections.Counter(g[0]['type'] for g in duplicates)),redundantCopies=dict(collections.Counter(r['type'] for g in duplicates for r in g[1:])),
    sameGeometryDifferentMaterials=len(variants))
result=dict(summary=summary,resourceGroups=duplicates,textureGroups=texture_groups,variants=variants,models=visuals)
(root/'Logs/EnvironmentResourceAudit.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
print(json.dumps([g[:4] for g in variants if any('dt_cpch_chair/' in v['identity'] for v in g)],indent=2)[:4000])
