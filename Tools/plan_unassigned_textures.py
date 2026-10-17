"""Build reviewable owner candidates from XDT model/texture slots and source publications."""
import json
import re
from pathlib import Path
from collections import defaultdict
from content_paths import ROOT,layout
from organize_content_ownership import owner
R=ROOT/'Tools/ContentMigration'
if (R/'unassigned-texture-plan.json').exists():raise SystemExit('Accepted plan already exists; preserve it. Use finish_unassigned_textures.py verify.')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
evidence=read(R/'unassigned-texture-evidence.json')['textures']
xdt=read(ROOT/'Assets/FFOne/Data/Gameplay/Source/tables/xdt.json')
catalog=read(ROOT/'Assets/FFOne/Data/Gameplay/Source/tables/npc_texture_overrides.json')['textures']
entries=layout()['entries'];byold={e['old']:e for e in entries}
models=defaultdict(set)
for e in entries:
    if e['old'].endswith('.glb') and '/characters/' in e['old'] and '.collision.' not in e['old']:
        models[Path(e['old']).stem.lower()].add(owner(e['path']))
        family=re.search(r'/characters/(?:mobs|npcs|fusions)/([^/]+)/',e['old'])
        if family:models[family[1].lower()].add(owner(e['path']))
native=next(x['value'] for x in xdt['_ffone']['tables'] if x.get('name')=='native_asset_routes')
for row in native['m_pCharacterModelData']:
    e=byold.get('Assets/Resources/'+row['glb'])
    if e:models[row['logicalName'].lower()].add(owner(e['path']))
bindings=defaultdict(list)
for table,body in xdt.items():
    if table=='_ffone':continue
    for section,rows in body.items():
        if not isinstance(rows,list):continue
        for i,row in enumerate(rows):
            if not isinstance(row,dict):continue
            for key,value in row.items():
                if 'TextureString' not in key or not isinstance(value,str) or value=='null':continue
                gender='F' if 'FTexture' in key else 'M'
                model=row.get('m_pstr'+gender+'MeshModelString')
                if model and model!='null':bindings[value.lower()].append({'table':table,'section':section,'row':i,'field':key,'model':model,'owners':sorted(models.get(model.lower(),[]))})
rows=[]
for t in evidence:
    if '.mips/' in t['path']:continue
    aliases=[c['trueName'].lower() for c in catalog if 'Assets/Resources/'+c['path']==t['sourceIdentity']]
    # A retained duplicate can have been redirected in the current runtime catalog.
    if not aliases:aliases=[c['trueName'].lower() for c in catalog if c['sha256']==t['sha256']]
    hits=[h for alias in aliases for h in bindings.get(alias,[])]
    npc=[h for h in hits if h['table']=='m_pNpcTable']
    candidates=sorted({o for h in (npc or hits) for o in h['owners']})
    rows.append({'path':t['path'],'aliases':aliases,'bindings':hits,'candidates':candidates,'duplicates':t['byteMatches'],
                 'publications':sorted({h['source'] for h in t['records'] if h['field']=='uri' and '/Content/VFX/' in h['source']})})
(R/'unassigned-owner-candidates.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for r in rows:
    if len(r['candidates'])!=1:print(Path(r['path']).name, r['aliases'], r['candidates'], 'models',sorted({h['model'] for h in r['bindings']}),'duplicates',r['duplicates'][:1])
print('base textures',len(rows),'single owner',sum(len(r['candidates'])==1 for r in rows))

# Reviewed choices when several models consume a texture: keep it with the primary
# character, record every consumer, and do not copy it into each consumer folder.
C='Assets/FFOne/Content/'
primary={
    'fusion_belladonna':'Characters/Fusions/fusion_belladonna',
    'fusion_gunter':'Characters/Fusions/fusion_gunter',
    'mob_fuselord':'Characters/Mobs/mob_fuselord',
    'npc_ampfibian':'Characters/NPCs/npc_ampfibian',
    'npc_bigbilly':'Characters/NPCs/npc_bigbilly',
    'npc_boomerbango':'Characters/NPCs/npc_bomerbango',
    'npc_boomerbango_glass':'Characters/NPCs/npc_bomerbango',
    'npc_penguin':'Characters/NPCs/npc_penguin',
    'npc_sourron':'Characters/NPCs/npc_sourron'}
effect={
    'bubble2':'Characters/Humanoids/Equipment/collections/back_fish/Textures/EffectImports',
    'bubble2_variant_02':'Characters/Humanoids/Equipment/collections/back_dexstar/Textures/EffectImports',
    'marker_mask':'VFX/World/Textures/QuestMarkers',
    'pl1_mt3':'VFX/World/Textures/JumpPads',
    'spe':'VFX/World/Textures/JumpPads'}
for name in ['inventory','mojo_r_02','m_inven_0','m_inven_1','m_inven_2','m_inven_3','m_inven_avata','m_inven_nano','m_inven_nanonamea','rain_1','select','vendor_pants']:
    effect[name]='VFX/World/Textures/InventoryDisplays'
decisions={}
for r in rows:
    name=Path(r['path']).stem;destination=None;status='model-present';proof=[]
    if name in effect:
        destination=C+effect[name];status='effect-owner'
        proof=r['publications'] or r['duplicates']
        if name=='marker_mask':proof=['Assets/FFOne/Content/VFX/World/Source/es865.closure.json','Assets/FFOne/Content/VFX/World/Source/es866.closure.json']
        assert proof,name
    elif name=='npc_3460_spawn11_green':
        # Exact copy of the imported spawn11_green effect texture. An NPC-labelled
        # source filename alone is not evidence that this is unique to that NPC.
        destination=C+'VFX/World/Textures/SpawnAura';status='effect-owner'
        proof=[p for p in r['duplicates'] if '/spawn11_green/' in p];assert proof
    elif name in primary:
        selected=C+primary[name];assert selected in r['candidates'];destination=selected+'/Textures/RuntimeOverrides'
    elif len(r['candidates'])==1:destination=r['candidates'][0]+'/Textures/RuntimeOverrides'
    else:
        npc=[h for h in r['bindings'] if h['table']=='m_pNpcTable'];ms=sorted({h['model'].lower() for h in npc or r['bindings']})
        assert not r['candidates'] and len(ms)==1,(name,ms,r['candidates'])
        model=ms[0];domain='Mobs' if model.startswith('mob_') else 'NPCs'
        assert model.startswith(('npc_','mob_')) or model=='rxcom',model
        destination=C+'Characters/'+domain+'/'+model+'/Textures/RuntimeOverrides';status='model-not-imported'
    decisions[name]=dict(r,destination=destination,ownerStatus=status,proof=proof)

import hashlib
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
moves=[]
for t in evidence:
    tail=t['path'].split('/Unassigned/')[1];base=tail.split('.mips/')[0] if '.mips/' in tail else Path(tail).stem
    decision=decisions[base];target=decision['destination']+'/'+tail
    source_only='.mips/' in tail
    if source_only:target='Assets/FFOne/Editor/ImportMetadata/TextureMipSources/'+base+'/'+Path(tail).name
    assert not (ROOT/target).exists(),target
    moves.append({'from':t['path'],'to':target,'old':t['sourceIdentity'],'guid':t['guid'],'sha256':t['sha256'],
                  'metaSha256':sha(ROOT/(t['path']+'.meta')),'baseTexture':base,'ownerStatus':decision['ownerStatus'],'editorOnlySource':source_only})
assert len({m['to'].lower() for m in moves})==295
report={'moves':moves,'decisions':list(decisions.values()),'baseTextures':len(rows),'mipLevels':len(moves)-len(rows)}
output=R/'unassigned-texture-plan.json'
if output.exists():raise RuntimeError('Preserve the accepted plan; do not overwrite it')
output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Planned',len(moves),'moves; missing imported models:',[(n,d['destination']) for n,d in decisions.items() if d['ownerStatus']=='model-not-imported'])
