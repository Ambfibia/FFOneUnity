"""Reviewed ownership migration. Preserve asset bytes, GUIDs and legacy identities."""
import argparse
from collections import Counter
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import zipfile
from content_paths import layout, ROOT, resolve

REPORTS=ROOT/'Tools/ContentMigration'
CONTENT='Assets/FFOne/Content/'
NANO=CONTENT+'Characters/Nanos/'
NAMES=dict(zip(
    '4arms alienx ampfibian belladonna buttercup computress deedee ghostfreak holonano humongousaur iceking jackolantern johnnybravo johnnytest juniper_lee mojojo numbuhfive numbuhfour numbuhone numbuhthree numbuhtwo princessbubblegum samuraijack swampfire ultimatebigchill vankleiss waybig zaksaturday'.split(),
    'FourArms AlienX AmpFibian Belladonna Buttercup Computress DeeDee Ghostfreak HoloNano Humungousaur IceKing JackOLantern JohnnyBravo JohnnyTest JuniperLee MojoJojo NumbuhFive NumbuhFour NumbuhOne NumbuhThree NumbuhTwo PrincessBubblegum SamuraiJack Swampfire UltimateBigChill VanKleiss WayBig ZakSaturday'.split()))

def sha(p):
    with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

def nano_name(key):
    key=key.removeprefix('nano_')
    return NAMES.get(key,key[:1].upper()+key[1:])

def nano_path(entry):
    p=entry['path']; old=entry['old']
    if not p.startswith(NANO):return p
    if '/characters/nano/' in old:
        name='Eddy'; variant='LegacyImport/'
    else:
        name=nano_name(p[len(NANO):].removeprefix('Appearances/').split('/')[0]);variant=''
    ext=PurePosixPath(p).suffix; filename=PurePosixPath(p).name
    if ext=='.glb':role='Models/Source/'
    elif ext=='.prefab':role='Prefabs/';filename=name+('_LegacyImport' if variant else '')+'.prefab'
    elif ext=='.anim':role='Animation/'
    elif ext=='.mat':role='Materials/'
    elif ext=='.asset':role='Models/'
    elif ext=='.png':role='Textures/'
    else:role='Definitions/Source/'
    if '.mips/' in p:filename=p.split('/')[-2]+'/'+filename
    return NANO+name+'/'+role+(variant if ext!='.prefab' else '')+filename

def owner(path):
    if '/Cells/' in path and path.endswith('/Terrain.prefab'):
        return path.split('/Imported/')[0]+'/Terrain/Imported'
    if '/shared/' in path.lower():return None
    for marker in ('/Assemblies/','/Prefabs/','/Models/','/Materials/','/Animation/','/Textures/'):
        if marker in path:return path.split(marker)[0]
    return str(PurePosixPath(path).parent)

def common_owner(paths):
    values={owner(p) for p in paths};values.discard(None)
    specific={v for v in values if '/Technical/' not in v and '/SourceLibrary/' not in v and '/NeedsClassification/' not in v}
    if specific:values=specific
    return os.path.commonpath(sorted(values)).replace('\\','/') if values else None

def texture_tail(p):
    parts=p.split('/')
    return '/'.join(parts[-2:]) if '.mips/' in p else parts[-1]

def destination(e,uses):
    p=e['path'];old=e['old'];name=PurePosixPath(p).name
    if p.startswith(NANO):return nano_path(e),'nano-family'
    if not p.startswith(CONTENT) or '/shared/' not in p.lower():return p,'unchanged'
    if '/Humanoids/Shared/' in p:
        root=CONTENT+'Characters/Humanoids/'
        if '/Rendering/' in p:return 'Assets/FFOne/Rendering/Textures/ToonRamps/'+name,'rendering-ramp'
        if name.endswith('-avatar.asset'):return root+'Base/'+name.split('-')[0].title()+'/Rig/'+name,'humanoid-rig'
        if name.endswith('-profile.asset'):return root+'Animation/Profiles/'+name,'humanoid-animation'
        if '/Animation/animation/' in p:return root+'Animation/'+p.split('/Animation/animation/')[1],'humanoid-animation'
        return root+('Base/Rig/Definitions/' if name=='player_rig_contract.json' else 'Animation/Definitions/')+name,'humanoid-definition'
    if '/Terrain/Source/shared/environment/skyboxes/' in p:
        return CONTENT+'Environment/Sky/Textures/'+p.split('/skyboxes/')[1],'sky'
    if '/Terrain/Source/shared/terrain/' in p:
        rest=p.split('/Source/shared/terrain/')[1];kind,_,tail=rest.partition('/')
        return CONTENT+('Environment/Vegetation/GroundCover/' if kind=='details' else 'Environment/Terrain/TextureLayers/')+tail,'terrain-'+kind
    if '/map/shared/editor/collision-box' in old:
        role='Models/Source' if p.endswith('.glb') else ('Prefabs' if p.endswith('.prefab') else 'Models')
        return CONTENT+'Environment/Technical/Collision/Box/'+role+'/'+name,'collision-tool'
    if '/Terrain/Shared/' in p:
        kind=name.split('_')[0];root=CONTENT+'Environment/Terrain/'
        if kind=='Mesh':
            cells={v.split('/Imported/')[0] for v in uses.get(p,[]) if v.endswith('/Terrain.prefab') and '/Cells/' in v}
            root=next(iter(cells))+'/Terrain/Imported/' if len(cells)==1 else root
        role={'Mesh':'Models','Lightmap':'Textures/Lightmaps','Weights':'Textures/LayerWeights','Layers':'Textures/LayerArrays',
              'GrassTexture':'Textures/GroundCover','TerrainMaterial':'Materials/Surface','GrassMaterial':'Materials/GroundCover'}[kind]
        return root+role+'/'+name,'terrain-'+kind
    if '/Audio/Voice/en/shared/' in p:return CONTENT+'Audio/Voice/en/NanoCom/'+name,'voice-nanocom'
    if '/UI/Artwork/' in p:
        prefix,tail=p.split('/shared/')
        category='HUD' if '/gameplay' in prefix else 'Controls'
        return prefix.removesuffix('/gameplay')+'/'+category+'/'+tail,'ui-'+category
    if '/NPCs/nanomachine/' in p:
        return CONTENT+'Characters/NPCs/nanomachine/Textures/'+texture_tail(p),'nanomachine'
    owned=common_owner(uses.get(p,[]))
    if owned and owned.startswith(NANO):
        # Owners are measured before the move; translate the Nano family as well.
        key=owned[len(NANO):].removeprefix('Appearances/').split('/')[0]
        owned=NANO+nano_name(key)
    if '/Environment/Shared/' in p:
        root=owned or CONTENT+'Environment'
        kind=p.split('/Environment/Shared/')[1].split('/')[0]
        role={'Models':'Models','Materials':'Materials','Textures':'Textures/ExactSamplers'}[kind]
        if root==CONTENT.rstrip('/'):
            root='Assets/FFOne/Rendering'
            if role=='Models':role='Geometry'
        return root+'/'+role+'/'+name,'dependency-owner' if owned else 'environment-unassigned'
    if owned:
        return owned+'/Textures/'+texture_tail(p),'dependency-owner'
    # Raw mip levels inherit the exact owner of their base texture.
    if '.mips/' in p:
        base=p.split('.mips/')[0]+'.png'
        owned=common_owner(uses.get(base,[]))
        if owned:return owned+'/Textures/'+texture_tail(p),'mip-owner'
    if '/VFX/Source/shared/' in p:return CONTENT+'VFX/Textures/Unassigned/'+texture_tail(p),'unassigned-source-texture'
    # Absence from prefab dependencies is not proof of lack of runtime use.
    domain=p.split('/Characters/')[1].split('/')[0]
    base=CONTENT+'Characters/'+('' if domain=='Shared' else domain+'/')
    return base+'Textures/Unassigned/'+texture_tail(p),'unassigned-source-texture'

def plan():
    output=REPORTS/'ownership-plan.json'
    if output.exists():raise RuntimeError('Plan already exists; preserve accepted review')
    d=layout();uses=json.loads((REPORTS/'ownership-dependencies.json').read_text())['uses']
    moves=[];destinations={};unchanged={e['path'].casefold() for e in d['entries'] if destination(e,uses)[0]==e['path']}
    for e in d['entries']:
        target,reason=destination(e,uses)
        if target==e['path']:continue
        # Keep long original semantic names within the project's Windows path budget.
        parent,_,name=target.rpartition('/');stem,ext=os.path.splitext(name)
        budget=190-len(parent)-1-len(ext)
        if budget<32:
            match=re.match(r'^(.*?)/(Models|Materials|Textures)(/.*)?$',parent)
            if match:
                family=match[1];role='/'+match[2]+(match[3] or '')
                while len(family+role)>150:family=family.rpartition('/')[0]
                parent=family+role;target=parent+'/'+name;budget=190-len(parent)-1-len(ext)
        if budget<16:raise ValueError('Owner is too long: '+parent)
        if len(stem)>budget:target=parent+'/'+stem[:budget-13]+'_'+e['guid'][:12]+ext
        if target.casefold() in destinations or target.casefold() in unchanged or ((ROOT/target).exists() and target!=e['path']):
            target=parent+'/'+stem[:max(1,budget-13)]+'_'+e['guid'][:12]+ext
        if target.casefold() in destinations or target.casefold() in unchanged:raise ValueError('Destination collision '+target)
        destinations[target.casefold()]=e['path']
        moves.append({'from':e['path'],'to':target,'old':e['old'],'guid':e['guid'],'reason':reason,
                      'sha256':sha(ROOT/e['path']),'metaSha256':sha(Path(str(ROOT/e['path'])+'.meta'))})
    report={'moves':moves,'counts':dict(Counter(m['reason'] for m in moves)),
            'nanoFamilies':len({m['to'].split('/')[5] for m in moves if m['to'].startswith(NANO)}),
            'bytes':sum((ROOT/m['from']).stat().st_size for m in moves)}
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='moves'}))

def apply():
    d=layout();plan=json.loads((REPORTS/'ownership-plan.json').read_text());moves=plan['moves']
    backup=ROOT.parent/'MigrationBackups/FFOneUnity-before-ownership-20261014'
    backup.mkdir(exist_ok=False)
    for m in moves:
        src=(ROOT/m['from']).resolve();dst=(ROOT/m['to']).resolve()
        if not src.is_relative_to(ROOT/'Assets/FFOne') or not dst.is_relative_to(ROOT/'Assets/FFOne'):raise ValueError('Outside workspace')
        if dst.exists() or sha(src)!=m['sha256'] or sha(Path(str(src)+'.meta'))!=m['metaSha256']:raise ValueError('Changed or occupied '+m['from'])
    # Snapshot every moved file and its metadata before any rename.
    with zipfile.ZipFile(backup/'moved-assets.zip','w',compression=zipfile.ZIP_STORED,allowZip64=True) as archive:
        for i,m in enumerate(moves):
            for suffix in ('','.meta'):archive.write(ROOT/(m['from']+suffix),m['from']+suffix)
            if i%5000==0:print('Backed up',i,flush=True)
    shutil.copy2(REPORTS/'layout.json.gz',backup/'layout.json.gz')
    shutil.copy2(REPORTS/'ownership-plan.json',backup/'ownership-plan.json')
    byold={e['old']:e for e in d['entries']};aliases=d.setdefault('previousPaths',{})
    ancestors=set()
    with (REPORTS/'ownership-moves.jsonl').open('w',encoding='utf-8') as log:
        for i,m in enumerate(moves):
            src=ROOT/m['from'];dst=ROOT/m['to'];dst.parent.mkdir(parents=True,exist_ok=True)
            src.rename(dst);Path(str(src)+'.meta').rename(Path(str(dst)+'.meta'))
            parent=src.parent
            while parent.is_relative_to(ROOT/'Assets/FFOne/Content') and parent!=ROOT/'Assets/FFOne/Content':ancestors.add(parent);parent=parent.parent
            byold[m['old']]['path']=m['to'];aliases[m['from']]=m['old']
            log.write(json.dumps(m)+'\n')
            if i%5000==0:log.flush();print('Moved',i,flush=True)
    # Empty folder metadata is retained in the backup; never recursively delete.
    with zipfile.ZipFile(backup/'empty-folder-metadata.zip','w') as archive:
        for folder in sorted(ancestors,key=lambda p:len(str(p)),reverse=True):
            if not folder.exists() or any(folder.iterdir()):continue
            meta=Path(str(folder)+'.meta')
            if meta.exists():archive.write(meta,meta.relative_to(ROOT).as_posix());meta.unlink()
            folder.rmdir()
    directories={}
    for e in d['entries']:
        parent=str(PurePosixPath(e['old']).parent);new=str(PurePosixPath(e['path']).parent)
        while parent.startswith('Assets/'):
            directories[parent]=os.path.commonpath([directories[parent],new]).replace('\\','/') if parent in directories else new
            parent=str(PurePosixPath(parent).parent)
    # New deduplicated imports are staged by role; existing indexed GUIDs resolve to their owners.
    directories['Assets/Resources/Environment/Shared/Meshes']=CONTENT+'Incoming/Environment/Models'
    directories['Assets/Resources/Environment/Shared/Materials']=CONTENT+'Incoming/Environment/Materials'
    directories['Assets/Generated/WorldTerrainShared']=CONTENT+'Incoming/Terrain'
    directories['Assets/Generated/WorldModelTextures']=CONTENT+'Incoming/Environment/Textures/ExactSamplers'
    d['directories']=directories
    temp=REPORTS/'layout.updated.json.gz'
    with gzip.open(temp,'wt',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False,separators=(',',':'))
    temp.replace(REPORTS/'layout.json.gz')
    print('Applied',len(moves),'moves; backup',backup,flush=True)

def verify():
    plan=json.loads((REPORTS/'ownership-plan.json').read_text());errors=[]
    for m in plan['moves']:
        dst=resolve(m['old']);meta=Path(str(dst)+'.meta')
        if not dst.exists() or sha(dst)!=m['sha256']:errors.append('Asset '+m['to'])
        if not meta.exists() or sha(meta)!=m['metaSha256']:errors.append('Metadata '+m['to'])
        if (ROOT/m['from']).exists():errors.append('Old path remains '+m['from'])
    result={'files':len(plan['moves']),'errors':errors,'assetAndMetaBytesPreserved':not errors}
    (REPORTS/'ownership-verification.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result));raise SystemExit(1 if errors else 0)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['plan','apply','verify']);args=parser.parse_args();globals()[args.action]()
