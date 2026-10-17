"""Backup, register and verify the reviewed texture move (Unity performs the moves)."""
import argparse, gzip, hashlib, json, shutil, zipfile
from pathlib import Path
from content_paths import ROOT,layout,resolve
R=ROOT/'Tools/ContentMigration'
B=ROOT.parent/'MigrationBackups/FFOneUnity-before-unassigned-textures-20261014'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def backup():
    plan=read(R/'unassigned-texture-plan.json');B.mkdir(exist_ok=False)
    for m in plan['moves']:
        assert sha(ROOT/m['from'])==m['sha256']
        assert sha(ROOT/(m['from']+'.meta'))==m['metaSha256']
        assert not (ROOT/m['to']).exists()
    with zipfile.ZipFile(B/'assets-and-metadata.zip','w',compression=zipfile.ZIP_DEFLATED) as z:
        for m in plan['moves']:
            for suffix in ('','.meta'):z.write(ROOT/(m['from']+suffix),m['from']+suffix)
    for name in ['layout.json.gz','ownership-unassigned-textures.json','unassigned-texture-plan.json']:
        shutil.copy2(R/name,B/name)
    scenes={p.relative_to(ROOT).as_posix():sha(p) for p in (ROOT/'Assets/FFOne/Worlds').rglob('*.unity')}
    (B/'authoring-scenes.json').write_text(json.dumps(scenes,indent=2))
    print('Backed up',len(plan['moves']),'assets, metadata, registry and',len(scenes),'scene hashes')
def register():
    plan=read(R/'unassigned-texture-plan.json');d=layout();byold={e['old']:e for e in d['entries']}
    assert B.is_dir()
    for m in plan['moves']:
        assert not (ROOT/m['from']).exists()
        assert sha(ROOT/m['to'])==m['sha256']
        assert sha(ROOT/(m['to']+'.meta'))==m['metaSha256']
        assert byold[m['old']]['guid']==m['guid']
        byold[m['old']]['path']=m['to'];d.setdefault('previousPaths',{})[m['from']]=m['old']
    temp=R/'unassigned-layout.updated.json.gz'
    with gzip.open(temp,'wt',encoding='utf-8') as f:json.dump(d,f,ensure_ascii=False,separators=(',',':'))
    temp.replace(R/'layout.json.gz')
    # Retire only verified empty folders, keeping their metadata in the backup.
    prior=read(B/'unassigned-texture-plan.json')
    parents={p for m in plan['moves'] for p in (ROOT/m['from']).parents if p.is_relative_to(ROOT/'Assets/FFOne/Content')}
    parents.update(p for m in prior['moves'] for p in (ROOT/m['to']).parents if p.is_relative_to(ROOT/'Assets/FFOne/Content'))
    with zipfile.ZipFile(B/'empty-folder-metadata.zip','w') as z:
        for p in sorted(parents,key=lambda p:len(str(p)),reverse=True):
            assert p.resolve().is_relative_to((ROOT/'Assets/FFOne/Content').resolve())
            if not p.exists() or any(p.iterdir()):continue
            meta=Path(str(p)+'.meta')
            if meta.exists():z.write(meta,meta.relative_to(ROOT).as_posix());meta.unlink()
            p.rmdir()
    (R/'ownership-unassigned-textures.json').write_text(json.dumps({'count':0,'textures':[],
        'resolvedCount':295,'resolution':'unassigned-texture-plan.json','originalInventoryBackup':str(B/'ownership-unassigned-textures.json')},indent=2)+'\n',encoding='utf-8')
    print('Registered 295 moves and historical aliases')
def verify():
    plan=read(R/'unassigned-texture-plan.json');errors=[]
    for m in plan['moves']:
        for suffix,key in [('', 'sha256'),('.meta','metaSha256')]:
            p=ROOT/(m['to']+suffix)
            if not p.exists() or sha(p)!=m[key]:errors.append(m['to']+suffix)
        if resolve(m['from'])!=ROOT/m['to'] or resolve(m['old'])!=ROOT/m['to']:errors.append('Alias '+m['from'])
        if (ROOT/m['from']).exists():errors.append('Old path '+m['from'])
    for path,h in read(B/'authoring-scenes.json').items():
        if sha(ROOT/path)!=h:errors.append('Authored scene changed '+path)
    unassigned=[e['path'] for e in layout()['entries'] if '/Unassigned/' in e['path']]
    errors+=unassigned
    evidence=read(R/'unassigned-texture-evidence.json')['textures'];mapped={m['from']:m['to'] for m in plan['moves']}
    groups={}
    for t in evidence:
        if t['byteMatches']:
            g=groups.setdefault(t['sha256'],{'sha256':t['sha256'],'paths':set(),'reviewedTargets':set()})
            g['paths'].update(mapped.get(p,p) for p in [t['path']]+t['byteMatches']);g['reviewedTargets'].add(mapped[t['path']])
    for g in groups.values():
        g['paths']=sorted(g['paths']);g['reviewedTargets']=sorted(g['reviewedTargets'])
        normalized=[]
        import re
        for path in g['paths']:
            if sha(ROOT/path)!=g['sha256']:errors.append('Duplicate changed '+path)
            normalized.append(re.sub(r'^guid: .*\n','',Path(str(ROOT/path)+'.meta').read_text(),flags=re.M))
        g['sameImportSettings']=len(set(normalized))==1
    duplicates={'scopePngFiles':30755,'targetFilesWithDuplicates':sum(bool(t['byteMatches']) for t in evidence),
        'baseTexturesWithDuplicates':sum(bool(t['byteMatches']) and '.mips/' not in t['path'] for t in evidence),
        'groups':list(groups.values()),'additionalPixelOnlyMatches':sum(set(t['pixelMatches'])!=set(t['byteMatches']) for t in evidence),
        'mergedOrDeleted':0,'note':'Exact duplicate candidates; original GUIDs and source addresses are retained. Lower mip matches alone do not make their base textures equivalent.'}
    (R/'unassigned-texture-duplicates.json').write_text(json.dumps(duplicates,indent=2)+'\n',encoding='utf-8')
    result={'passed':not errors,'files':295,'baseTextures':plan['baseTextures'],'mipLevels':plan['mipLevels'],
        'remainingUnassigned':len(unassigned),'assetAndMetaBytesPreserved':not errors,'sceneHashesVerified':len(read(B/'authoring-scenes.json')),
        'errors':errors,'missingModelOwners':[d['destination'] for d in plan['decisions'] if d['ownerStatus']=='model-not-imported'],'backup':str(B)}
    (R/'unassigned-texture-verification.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result));raise SystemExit(0 if not errors else 1)
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('action',choices=['backup','register','verify']);a=p.parse_args();globals()[a.action]()
