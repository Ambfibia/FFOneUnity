"""Evidence for the 295 retained textures: exact bytes/pixels, imports and source records."""
from pathlib import Path
from collections import defaultdict
import hashlib, json, re, struct
from PIL import Image
from content_paths import ROOT, layout

REPORT=ROOT/'Tools/ContentMigration'
def digest(data): return hashlib.sha256(data).hexdigest()
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def settings(p):
    return re.sub(r'^guid: .*\n','',Path(str(p)+'.meta').read_text(encoding='utf-8'),flags=re.M)
def pixels(p):
    with Image.open(p) as im:
        im=im.convert('RGBA');return digest(struct.pack('<II',*im.size)+im.tobytes())
def main():
    targets=read(REPORT/'ownership-unassigned-textures.json')['textures']
    if not targets:raise SystemExit('Original inventory has been resolved. Preserve the evidence; run finish_unassigned_textures.py verify.')
    entries=layout()['entries'];pngs=[e for e in entries if e['path'].lower().endswith('.png')]
    bysha=defaultdict(list);bypixel=defaultdict(list);data={}
    sizes=set()
    for t in targets:
        with Image.open(ROOT/t['path']) as im:sizes.add(im.size)
    for e in pngs:
        p=ROOT/e['path'];h=digest(p.read_bytes());bysha[h].append(e['path']);d={'sha256':h}
        with Image.open(p) as im:size=im.size
        if size in sizes:
            d['pixels']=pixels(p);bypixel[d['pixels']].append(e['path'])
        data[e['path']]=d
    names={Path(t['path']).stem.lower() for t in targets if '.mips/' not in t['path']}
    hits=defaultdict(list)
    def scan(v,source,trail='',context=None):
        if isinstance(v,dict):
            scalars={k:x for k,x in v.items() if isinstance(x,(str,int,float,bool))}
            for k,x in scalars.items():
                if isinstance(x,str):
                    n=Path(x.replace('\\','/')).stem.lower() if '/' in x or x.endswith(('.png','.dds')) else x.lower()
                    if n in names:
                        hits[n].append({'source':source,'record':trail,'field':k,'values':scalars,'parent':context})
            for k,x in v.items():
                if isinstance(x,(list,dict)):scan(x,source,trail+'/'+k,scalars or context)
        elif isinstance(v,list):
            for i,x in enumerate(v):scan(x,source,trail+'/'+str(i),context)
    sources=[ROOT/'Assets/FFOne/Data/Gameplay/Source/tables/xdt.json']
    sources += [ROOT/e['path'] for e in entries if e['path'].endswith('.json') and
        (e['path'].startswith('Assets/FFOne/Content/VFX/') or e['path'].endswith('/npc_texture_overrides.json')) and not e['path'].endswith('.closure.json')]
    for p in sources:scan(read(p),p.relative_to(ROOT).as_posix())
    rows=[]
    for t in targets:
        p=ROOT/t['path'];d=data[t['path']];same=[x for x in bysha[d['sha256']] if x!=t['path']]
        equal=[x for x in bypixel[d['pixels']] if x!=t['path']]
        rows.append(dict(t,**d,byteMatches=same,pixelMatches=equal,
            compatibleByteMatches=[x for x in same if settings(ROOT/x)==settings(p)],
            records=hits.get(p.stem.lower(),[]) if '.mips/' not in t['path'] else []))
    out={'pngsChecked':len(pngs),'textures':rows}
    (REPORT/'unassigned-texture-evidence.json').write_text(json.dumps(out,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'pngs':len(pngs),'targets':len(rows),'withByteMatches':sum(bool(x['byteMatches']) for x in rows),'withPixelMatches':sum(bool(x['pixelMatches']) for x in rows),'baseWithRecords':sum(bool(x['records']) for x in rows)}))
if __name__=='__main__':main()
