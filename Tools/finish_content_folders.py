"""Retire only empty legacy folders after file migration, preserving metadata."""
from pathlib import Path
import json, os, zipfile
ROOT=Path(__file__).resolve().parents[1]
receipt=ROOT/'Tools/ContentMigration/layout.json'
d=json.loads(receipt.read_text(encoding='utf-8'))
archive=ROOT/'Tools/ContentMigration/retired-folder-metadata.zip'
removed=[]; kept=[]
with zipfile.ZipFile(archive,'a',compression=zipfile.ZIP_DEFLATED) as z:
    known=set(z.namelist())
    for directory, subdirs, files in os.walk(ROOT/'Assets',topdown=False):
        path=Path(directory).resolve()
        if path==ROOT/'Assets' or not path.is_relative_to(ROOT/'Assets'):continue
        old=path.relative_to(ROOT).as_posix()
        if old.startswith('Assets/FFOne') or old.startswith('Assets/AddressableAssetsData'):continue
        if any(path.iterdir()):continue
        meta=Path(str(path)+'.meta')
        target=d.get('directories',{}).get(old)
        targetmeta=ROOT/(target+'.meta') if target else None
        if meta.exists():
            if target and target!=old and (ROOT/target).is_dir() and not targetmeta.exists():
                meta.rename(targetmeta); kept.append({'old':old,'path':target})
            else:
                key=meta.relative_to(ROOT).as_posix()
                if key not in known:z.write(meta,key);known.add(key)
                meta.unlink()
        path.rmdir(); removed.append(old)
report={'removedEmptyFolders':len(removed),'preservedFolderGuids':len(kept),'preserved':kept,'archive':str(archive)}
(ROOT/'Tools/ContentMigration/folders.json').write_text(json.dumps(report,indent=1),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='preserved'}))
