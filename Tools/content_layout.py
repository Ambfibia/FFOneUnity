"""Plan, apply and verify the GUID-preserving FFOne content layout migration.

No deduplication or changes to imported binary content. The receipt is also the
compatibility map for source identities used by the existing import tools.
"""
from __future__ import annotations
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import uuid
from content_paths import layout as read_layout

ROOT = Path(__file__).resolve().parents[1]
RECEIPT = ROOT / 'Tools/ContentMigration/layout.json'
CONTENT = 'Assets/FFOne/Content/'
META = 'Assets/FFOne/Editor/ImportMetadata/'

def digest(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def character_owner(parts):
    kind, *tail = parts
    mapping = {'mobs':'Mobs', 'npcs':'NPCs', 'fusions':'Fusions', 'nano':'Nanos',
               'nanos':'Nanos/Appearances', 'hnpc':'Humanoids/Appearances/HNPC',
               'shinies':'Collectibles', 'shared':'Shared', 'transportation':'Vehicles'}
    if kind == 'player':
        part = tail.pop(0) if tail else 'shared'
        group = {'male':'Base/Male','female':'Base/Female','items':'Equipment',
                 'appearance':'Customization','hnpc':'Appearances/HNPC',
                 'hnpc-runtime-textures':'Appearances/HNPC/Textures',
                 'table-textures':'Equipment/Textures','rendering':'Shared/Rendering',
                 'traversal':'Animation/Traversal','shared':'Shared'}.get(part, part)
        return CONTENT + 'Characters/Humanoids/' + group, tail
    group = mapping.get(kind, 'NeedsClassification/' + kind)
    base = CONTENT + ('Vehicles/Characters' if kind == 'transportation' else 'Characters/' + group)
    if tail:
        base += '/' + tail.pop(0)
    return base, tail

def environment_path(path):
    changes = {'Geology':'Rocks','Architecture':'Structures',
               'Infrastructure':'Structures/Infrastructure','Review':'NeedsClassification',
               'Special':'Interactables','Technical':'Technical','Attractions':'Structures/Attractions'}
    head, sep, tail = path.partition('/')
    if head == 'Vehicles': return CONTENT + 'Vehicles/Environment/' + tail
    if head == 'Special': return CONTENT + 'Interactables/Environment/' + tail
    if path.startswith('Infrastructure/Roads/'):
        return CONTENT + 'Environment/Roads/' + path[len('Infrastructure/Roads/'):]
    return CONTENT + 'Environment/' + changes.get(head, head) + (sep + tail)

def role(path):
    name = PurePosixPath(path).name
    ext = PurePosixPath(path).suffix.lower()
    if ext == '.prefab': return 'Prefabs'
    if ext == '.mat': return 'Materials'
    if ext in ('.anim','.controller','.overridecontroller'): return 'Animation'
    if name.startswith('mesh_'): return 'Models'
    if ext in ('.png','.jpg','.tga','.exr'): return 'Textures'
    if ext == '.glb': return 'Models/Source'
    if ext == '.json': return 'Definitions/Source'
    return 'Source'

def target(path, publications):
    if path.startswith('Assets/Scripts/Runtime/'):
        rest = path[len('Assets/Scripts/Runtime/'):]
        rest = re.sub(r'^Creation/', 'Characters/Creation/', rest)
        rest = re.sub(r'^Tutorial/', 'Gameplay/Tutorial/', rest)
        rest = re.sub(r'^Integration/', 'Core/Integration/', rest)
        if rest.startswith('Content/Native') and any(x in rest for x in ('Avatar','Player')):
            rest = 'Characters/Humanoids/' + rest.split('/')[-1]
        return 'Assets/FFOne/Runtime/' + rest
    if path.startswith('Assets/Scripts/Editor/'):
        rest = path[len('Assets/Scripts/Editor/'):]
        name = PurePosixPath(rest).name
        if name.endswith('.asmdef'): return 'Assets/FFOne/Editor/' + name
        if rest.startswith('Integration/'): return 'Assets/FFOne/Editor/Development/' + rest[len('Integration/'):]
        if any(x in name for x in ('Workspace','CellBinding','TerrainTree')): group = 'WorldEditor'
        elif 'Validation' in name: group = 'Validation'
        elif 'Inspector' in name or name == 'NativeDataDocumentEditor.cs': group = 'Inspectors'
        elif any(x in name for x in ('Library','AssetRules','AssetWindow','FrontendLayoutEditor','LocalizationEditor')): group = 'AssetBrowser'
        else: group = 'Importers'
        return 'Assets/FFOne/Editor/' + group + '/' + rest
    fixed = {'Assets/Editor/':META, 'Assets/Tests/':'Assets/FFOne/Tests/',
             'Assets/Shaders/':'Assets/FFOne/Rendering/Shaders/',
             'Assets/Settings/':'Assets/FFOne/Settings/Rendering/',
             'Assets/UI/':CONTENT+'UI/Settings/', 'Assets/UI Toolkit/':CONTENT+'UI/Styles/',
             'Assets/Generated/WorldModelTextures/':CONTENT+'Environment/Shared/Textures/ExactSamplers/',
             'Assets/Generated/WorldTerrainShared/':CONTENT+'Environment/Terrain/Shared/'}
    for old, new in fixed.items():
        if path.startswith(old): return new + path[len(old):]
    if path.startswith('Assets/Scenes/'):
        return 'Assets/FFOne/Scenes/' + ('Bootstrap/' if path.endswith('/Main.unity') else 'Development/') + PurePosixPath(path).name
    if not path.startswith('Assets/Resources/'): return path
    relative = path[len('Assets/Resources/'):]
    if relative in ('NativeContent.asset','NativeEditable/Index.asset'):
        return 'Assets/FFOne/Data/Catalogs/' + ('NativeContent.asset' if relative == 'NativeContent.asset' else 'NativeModels.asset')
    if relative.startswith('Environment/'):
        tail = relative[len('Environment/'):]
        parent, _, name = tail.rpartition('/')
        # Existing assemblies and shared assets already have their own owners.
        return environment_path(parent) + '/' + role(name) + '/' + name
    if relative.startswith('NativeEditable/'):
        tail = relative[len('NativeEditable/'):]
        parts = tail.split('/')
        if parts[0] == 'characters':
            owner, rest = character_owner(parts[1:])
            return owner + '/' + role(tail) + '/' + '/'.join(rest)
        if parts[0] == 'map': return CONTENT+'Environment/Terrain/Models/'+'/'.join(parts[1:])
        return CONTENT+'VFX/'+role(tail)+'/'+'/'.join(parts[1:])
    if relative.startswith('NativeData/'):
        tail = relative[len('NativeData/'):]
        if tail.startswith('localization/'):
            return 'Assets/FFOne/Data/Localization/Documents/' + tail[len('localization/'):]
        return 'Assets/FFOne/Data/Tables/' + tail
    if relative.startswith('characters/'):
        owner, rest = character_owner(relative.split('/')[1:])
        return owner + '/' + role(relative) + '/' + '/'.join(rest)
    if relative.startswith('objects/'):
        parts = relative.split('/')
        # Use already published/classified identities, never infer semantic category here.
        if len(parts) >= 5:
            identity = str(PurePosixPath(relative).with_suffix(''))
            published = publications.get(identity)
            if published:
                new_prefab = target(published, {})
                return new_prefab.rsplit('/Prefabs/',1)[0] + '/' + role(relative) + '/' + parts[-1]
        return CONTENT+'Environment/SourceLibrary/'+'/'.join(parts[1:])
    groups = {'audio/music/':CONTENT+'Audio/Music/', 'audio/sfx/':CONTENT+'Audio/SFX/',
              'audio/voice/':CONTENT+'Audio/Voice/', 'audio/':CONTENT+'Audio/Source/',
              'ui/views/':CONTENT+'UI/Views/', 'ui/styles/':CONTENT+'UI/Styles/',
              'ui/':CONTENT+'UI/Artwork/', 'icons/':CONTENT+'UI/Icons/', 'fonts/':CONTENT+'UI/Fonts/',
              'effects/':CONTENT+'VFX/Source/', 'localization/':'Assets/FFOne/Data/Localization/Source/',
              'data/hnpc/':CONTENT+'Characters/Humanoids/Appearances/HNPC/Definitions/',
              'data/':'Assets/FFOne/Data/Gameplay/Source/',
              'NativeBinary/':'Assets/FFOne/Data/Tables/Binary/',
              'NativeShaderSources/':'Assets/FFOne/Rendering/Source/',
              'shaders/':'Assets/FFOne/Rendering/Source/Shaders/',
              'World/Tiles/':'Assets/FFOne/Worlds/Overworld/ImportedCells/',
              'World/':'Assets/FFOne/Worlds/Overworld/Settings/',
              'Tutorial/':'Assets/FFOne/Worlds/Tutorial/Content/',
              'map/tiles/':'Assets/FFOne/Worlds/Overworld/SourceCells/',
              'map/':CONTENT+'Environment/Terrain/Source/'}
    for old,new in groups.items():
        if relative.startswith(old): return new+relative[len(old):]
    raise ValueError('Unmapped asset: '+path)

def files():
    for directory, _, names in os.walk(ROOT/'Assets'):
        for name in names:
            p = Path(directory)/name
            if p.suffix != '.meta': yield p

def plan():
    if RECEIPT.exists() or Path(str(RECEIPT)+'.gz').exists(): raise RuntimeError('Receipt exists; refusing to overwrite baseline')
    publications = {}
    for p in (ROOT/'Assets/Editor/NativePublications').rglob('publication.json'):
        data=json.loads(p.read_text(encoding='utf-8-sig'))
        publications[data['SourcePath'].removeprefix('Assets/Resources/').removesuffix('.glb')] = data['PrefabPath']
    entries=[]; destinations={}
    for p in files():
        old=p.relative_to(ROOT).as_posix(); new=target(old,publications)
        if new.casefold() in destinations: raise ValueError(f'Collision: {old} / {destinations[new.casefold()]} => {new}')
        destinations[new.casefold()]=old
        meta=Path(str(p)+'.meta')
        guid=re.search(r'^guid: ([0-9a-f]+)',meta.read_text(),re.M).group(1) if meta.exists() else ''
        entries.append(dict(old=old,path=new,guid=guid,size=p.stat().st_size,sha256=digest(p),metaSha256=digest(meta) if meta.exists() else ''))
        if len(entries)%10000==0: print(f'Inventoried {len(entries)} assets',flush=True)
    directories={}
    for e in entries:
        old=str(PurePosixPath(e['old']).parent); new=str(PurePosixPath(e['path']).parent)
        while old.startswith('Assets/'):
            if old in directories:
                directories[old]=os.path.commonpath([directories[old],new]).replace('\\','/')
            else: directories[old]=new
            old=str(PurePosixPath(old).parent)
    RECEIPT.parent.mkdir(parents=True,exist_ok=True)
    RECEIPT.write_text(json.dumps(dict(version=1,entries=entries,directories=directories),ensure_ascii=False,indent=1),encoding='utf-8')
    print(json.dumps(dict(assets=len(entries),moves=sum(e['old']!=e['path'] for e in entries),bytes=sum(e['size'] for e in entries))))

def checked(relative):
    p=(ROOT/relative).resolve()
    if not p.is_relative_to(ROOT/'Assets'): raise ValueError('Path outside Assets: '+relative)
    return p

def apply():
    data=read_layout()
    # Validate the complete write set before mutation. Also supports safe resume.
    for e in data['entries']:
        src,dst=checked(e['old']),checked(e['path'])
        if src==dst: continue
        if src.exists() and dst.exists(): raise ValueError('Occupied: '+str(dst))
        if not src.exists() and not dst.exists(): raise FileNotFoundError(e['old'])
    journal=RECEIPT.parent/'moves.jsonl'
    with journal.open('a',encoding='utf-8') as log:
        for n,e in enumerate(data['entries']):
            src,dst=checked(e['old']),checked(e['path'])
            if src==dst: continue
            dst.parent.mkdir(parents=True,exist_ok=True)
            if src.exists(): src.rename(dst)
            sm,dm=Path(str(src)+'.meta'),Path(str(dst)+'.meta')
            if sm.exists():
                if dm.exists(): raise ValueError('Meta occupied: '+str(dm))
                sm.rename(dm)
            log.write(json.dumps({'old':e['old'],'path':e['path'],'guid':e['guid']})+'\n')
            if n%10000==0: log.flush(); print('Moved',n,flush=True)
    print('Asset files moved. Original folder metas are retained until folder audit.')

def verify():
    data=read_layout(); errors=[]
    for e in data['entries']:
        p=checked(e['path']); m=Path(str(p)+'.meta')
        if not p.exists(): errors.append('Missing '+e['path']); continue
        if e['guid'] and (not m.exists() or f"guid: {e['guid']}" not in m.read_text()): errors.append('GUID '+e['path'])
        if p.suffix.lower() in ('.glb','.png','.ogg','.bin','.bytes','.otf','.ttf') and digest(p)!=e['sha256']: errors.append('Binary changed '+e['path'])
    report={'assets':len(data['entries']),'errors':errors}
    (RECEIPT.parent/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps({'assets':len(data['entries']),'errors':len(errors),'first':errors[:10]}))
    if errors: raise SystemExit(1)

if __name__=='__main__':
    a=argparse.ArgumentParser(); a.add_argument('action',choices=['plan','apply','verify']); args=a.parse_args()
    globals()[args.action]()
