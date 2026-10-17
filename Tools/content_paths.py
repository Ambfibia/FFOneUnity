"""Read-only identity resolution for standalone import/audit tools."""
from functools import lru_cache
from pathlib import Path
import fnmatch, json, gzip
ROOT=Path(__file__).resolve().parents[1]

@lru_cache(maxsize=1)
def layout():
    receipt=ROOT/'Tools/ContentMigration/layout.json'
    compressed=Path(str(receipt)+'.gz')
    if compressed.exists():
        with gzip.open(compressed,'rt',encoding='utf-8') as f:return json.load(f)
    return json.loads(receipt.read_text(encoding='utf-8')) if receipt.exists() else {'entries':[],'directories':{}}

@lru_cache(maxsize=1)
def mappings():
    result={}
    for e in layout()['entries']:
        result[e['old']]=e['path']; result[e['old']+'.meta']=e['path']+'.meta'
    for previous,original in layout().get('previousPaths',{}).items():
        result[previous]=result[original];result[previous+'.meta']=result[original+'.meta']
    result.update(layout().get('directories',{}))
    return result

def resolve(path):
    p=Path(path)
    if p.is_absolute():
        try:relative=p.relative_to(ROOT).as_posix()
        except ValueError:return p
    else:relative=p.as_posix()
    return ROOT/mappings().get(relative,relative)

def files(root,pattern='*',recursive=True):
    p=Path(root)
    try:relative=p.relative_to(ROOT).as_posix() if p.is_absolute() else p.as_posix()
    except ValueError:return list(p.rglob(pattern) if recursive else p.glob(pattern))
    prefix=relative.rstrip('/')+'/'
    matches=[]
    for e in layout()['entries']:
        if not e['old'].startswith(prefix):continue
        tail=e['old'][len(prefix):]
        if not recursive and '/' in tail:continue
        if fnmatch.fnmatch(Path(tail).name,pattern) and (ROOT/e['path']).exists():matches.append(ROOT/e['path'])
    if not matches and resolve(p).exists():return list(resolve(p).rglob(pattern) if recursive else resolve(p).glob(pattern))
    return matches
