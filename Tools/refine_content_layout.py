"""Correct reviewed exceptional paths with Unity closed, then update the receipt."""
from pathlib import Path, PurePosixPath
import json, os, hashlib, sys, zipfile
from content_layout import environment_path,role
ROOT=Path(__file__).resolve().parents[1]
p=ROOT/'Tools/ContentMigration/layout.json'
d=json.loads(p.read_text(encoding='utf-8'));changes=[]
for e in d['entries']:
    new=e['path']
    if e['old']=='Assets/Resources/characters/nano/nano_eddy.glb':
        new='Assets/FFOne/Content/Characters/Nanos/nano_eddy/Models/Source/nano_eddy.glb'
    prefix='Assets/FFOne/Content/Audio/Source/ambient/'
    if new.startswith(prefix):new='Assets/FFOne/Content/Audio/Ambience/'+new[len(prefix):]
    if e['old']=='Assets/Resources/World/Catalog.asset':new='Assets/FFOne/Worlds/Overworld/WorldDefinition.asset'
    if e['old']=='Assets/FFOne/Editor/Environment/EnvironmentLibrary.asset':new='Assets/FFOne/Editor/AssetBrowser/EnvironmentLibrary.asset'
    new=new.replace('Content/Environment/Rocks/Rocks/','Content/Environment/Rocks/')
    prefix='Assets/Resources/Environment/Shared/'
    if e['old'].startswith(prefix):
        tail=e['old'][len(prefix):];kind,_,rest=tail.partition('/')
        new='Assets/FFOne/Content/Environment/Shared/'+{'Meshes':'Models'}.get(kind,kind)+'/'+rest
    prefix='Assets/Resources/Environment/Assemblies/'
    if e['old'].startswith(prefix):
        parts=e['old'][len(prefix):].split('/')
        if len(parts)>=5:
            owner=environment_path('/'.join(parts[:3])).replace('Environment/Rocks/Rocks/','Environment/Rocks/')
            new=owner+'/Assemblies/'+'/'.join(parts[3:-1])+'/'+role(e['old'])+'/'+parts[-1]
    for published in ('','NativeEditable/'):
        prefix='Assets/Resources/'+published+'characters/player/items/'
        if e['old'].startswith(prefix):
            parts=e['old'][len(prefix):].split('/')
            if len(parts)>=3:
                new='Assets/FFOne/Content/Characters/Humanoids/Equipment/'+'/'.join(parts[:2])+'/'+role(e['old'])+'/'+'/'.join(parts[2:])
        for branch,owner in [('projectiles','Projectiles'),('effects','World')]:
            prefix='Assets/Resources/'+published+'map/shared/'+branch+'/'
            if e['old'].startswith(prefix):
                tail=e['old'][len(prefix):]
                new='Assets/FFOne/Content/VFX/'+owner+('/Published/' if published else '/Source/')+tail
    new=new.rstrip('/')
    if new!=e['path']:changes.append((e,new))
proposed={id(e):new for e,new in changes}
# Shorten only folders which actually exceed the Windows budget. Apply each
# folder rename to all its descendants, keeping an object's resources together.
destinations=[proposed.get(id(e),e['path']) for e in d['entries']]
for _ in range(10):
    long=sorted((p for p in destinations if len(p)>190),key=len,reverse=True)
    if not long:break
    replacements={}
    for path in long:
        parts=path.split('/');choices=[i for i in range(3,len(parts)-1) if len(parts[i])>16]
        if not choices:continue
        i=max(choices,key=lambda x:len(parts[x]));prefix='/'.join(parts[:i+1]);oldpart=parts[i]
        if prefix in replacements:continue
        length=max(16,len(oldpart)-(len(path)-190))
        parts[i]=oldpart[:length-9]+'_'+hashlib.sha256(oldpart.encode()).hexdigest()[:8]
        replacements[prefix]='/'.join(parts[:i+1])
    def shorten(path):
        parent=path.rpartition('/')[0]
        while '/' in parent:
            if parent in replacements and path.startswith(parent+'/'):path=replacements[parent]+path[len(parent):]
            parent=parent.rpartition('/')[0]
        return path
    destinations=[shorten(p) for p in destinations]
changes=[(e,new) for e,new in zip(d['entries'],destinations) if e['path']!=new]
if len(set(p.casefold() for p in destinations))!=len(destinations):raise ValueError('Refined destinations collide')
if '--plan' in sys.argv:
    print(json.dumps({'changes':len(changes),'maximumAssetPath':max(map(len,destinations)),'longest':sorted(destinations,key=len,reverse=True)[:4]},indent=1));raise SystemExit(0)
for e,new in changes:
    source=(ROOT/e['path']).resolve();dest=(ROOT/new).resolve()
    if not source.is_relative_to(ROOT/'Assets/FFOne') or not dest.is_relative_to(ROOT/'Assets/FFOne'):raise ValueError('Outside content root')
    if dest.exists():raise ValueError('Occupied '+new)
folder_targets={}
recovery=ROOT/'Tools/ContentMigration/path-refinement-moves.json'
recovery.write_text(json.dumps([{'old':e['old'],'from':e['path'],'to':new,'guid':e['guid']} for e,new in changes],indent=1),encoding='utf-8')
print('Validated',len(changes),'refinements; recovery journal saved',flush=True)
for n,(e,new) in enumerate(changes):
    source=ROOT/e['path'];dest=ROOT/new;dest.parent.mkdir(parents=True,exist_ok=True)
    oldfolder=source.parent;newfolder=dest.parent
    while oldfolder!=ROOT/'Assets/FFOne' and oldfolder.is_relative_to(ROOT/'Assets/FFOne'):
        key=str(oldfolder)
        folder_targets[key]=Path(os.path.commonpath([folder_targets[key],newfolder])) if key in folder_targets else newfolder
        oldfolder=oldfolder.parent
    source.rename(dest);Path(str(source)+'.meta').rename(Path(str(dest)+'.meta'))
    e['path']=new
    if n%5000==0:print('Refined',n,flush=True)
with zipfile.ZipFile(ROOT/'Tools/ContentMigration/retired-folder-metadata.zip','a',compression=zipfile.ZIP_DEFLATED) as z:
    archived=set(z.namelist())
    for old in sorted(folder_targets,key=len,reverse=True):
        folder=Path(old)
        if not folder.is_relative_to(ROOT/'Assets/FFOne') or not folder.exists() or any(folder.iterdir()):continue
        meta=Path(old+'.meta');newmeta=Path(str(folder_targets[old])+'.meta')
        if meta.exists():
            if folder_targets[old].is_dir() and not newmeta.exists():meta.rename(newmeta)
            else:
                name=meta.relative_to(ROOT).as_posix()
                if name not in archived:z.write(meta,name);archived.add(name)
                meta.unlink()
        folder.rmdir()
directories={}
for e in d['entries']:
    old=str(PurePosixPath(e['old']).parent);new=str(PurePosixPath(e['path']).parent)
    while old.startswith('Assets/'):
        directories[old]=os.path.commonpath([directories[old],new]).replace('\\','/') if old in directories else new
        old=str(PurePosixPath(old).parent)
d['directories']=directories
p.write_text(json.dumps(d,ensure_ascii=False,indent=1),encoding='utf-8')
(ROOT/'Tools/ContentMigration/path-refinements.json').write_text(json.dumps([{'old':e['old'],'path':new} for e,new in changes],indent=1),encoding='utf-8')
print('Refined',len(changes),'paths; asset and meta bytes preserved')
