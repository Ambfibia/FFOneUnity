"""Apply explicit path resolution at the legacy importer boundary.

The transform only targets known APIs, parses balanced C# argument lists and is
idempotent. Runtime loading is routed through ContentAssets; no runtime path map.
"""
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parents[1]

def end_call(s,start):
    depth=1; i=start+1; quote=None; verbatim=False
    while i<len(s):
        ch=s[i]
        if quote:
            if ch=='\\' and not verbatim: i+=2; continue
            if ch==quote:
                if verbatim and i+1<len(s) and s[i+1]==quote: i+=2; continue
                quote=None
        elif ch in ('"',"'"):
            quote=ch; verbatim=i>0 and s[i-1]=='@'
        elif ch=='(': depth+=1
        elif ch==')':
            depth-=1
            if not depth:return i
        i+=1
    raise ValueError('Unbalanced call')

def split_args(s):
    depth=0; quote=None; start=0; result=[]; i=0
    while i<len(s):
        c=s[i]
        if quote:
            if c=='\\':i+=2;continue
            if c==quote:quote=None
        elif c in ('"',"'"):quote=c
        elif c in '([{':depth+=1
        elif c in ')]}':depth-=1
        elif c==',' and depth==0:result.append(s[start:i]);start=i+1
        i+=1
    result.append(s[start:]);return result

FIRST={
 'File':['Copy','Create','Delete','Exists','OpenRead','OpenText','ReadAllBytes','ReadAllText','WriteAllBytes','WriteAllText'],
 'Directory':['CreateDirectory','Delete','EnumerateFileSystemEntries'],
 'AssetDatabase':['AddObjectToAsset','AssetPathToGUID','CopyAsset','CreateFolder','DeleteAsset','GenerateUniqueAssetPath','GetAssetDependencyHash','GetDependencies','GetMainAssetTypeAtPath','ImportAsset','IsValidFolder','LoadAllAssetsAtPath','LoadAssetAtPath','LoadMainAssetAtPath','MoveAsset'],
 'AssetImporter':['GetAtPath'],
 'PrefabUtility':['LoadPrefabContents'],
 'EditorSceneManager':['OpenScene']}

def transform(text,editor):
    text=re.sub(r'\bResources\.(Load|LoadAll|LoadAsync)(?=[<(])',r'FFOne.Content.ContentAssets.\1',text)
    text=text.replace('ResourceRequest pending;','FFOne.Content.ContentAssets.Request pending;')
    if not editor:return text
    if 'using ContentPaths = FFOne.Content.ContentPaths;' in text:return text
    names=[]; arg_indices={}
    for owner,methods in FIRST.items():
        for method in methods:
            n=owner+'.'+method;names.append(re.escape(n));arg_indices[n]=[0]
    # AddObjectToAsset's first argument is an object; its target may also be an object.
    arg_indices.pop('AssetDatabase.AddObjectToAsset')
    names.remove(re.escape('AssetDatabase.AddObjectToAsset'))
    for name,ids in {'File.Copy':[0,1],'AssetDatabase.CopyAsset':[0,1],
                     'AssetDatabase.MoveAsset':[0,1], 'AssetDatabase.CreateAsset':[1],
                     'PrefabUtility.SaveAsPrefabAsset':[1], 'EditorSceneManager.SaveScene':[1],
                     'AssetDatabase.FindAssets':[1], 'new FileInfo':[0], 'new FileStream':[0],
                     'context.DependsOnSourceAsset':[0]}.items():
        if name not in arg_indices:names.append(re.escape(name))
        arg_indices[name]=ids
    pattern=re.compile(r'(?<![\w.])('+ '|'.join(names)+r')\s*(?:<[^;\n()]+>)?\s*\(')
    # Innermost calls first, repeatedly updating offsets by doing the outer parse afresh.
    matches=list(pattern.finditer(text))
    for m in reversed(matches):
        opening=m.end()-1; end=end_call(text,opening)
        args=split_args(text[opening+1:end])
        for index in arg_indices[m.group(1)]:
            if index>=len(args):continue
            a=args[index]; leading=a[:len(a)-len(a.lstrip())]; value=a.strip()
            if value.startswith('ContentPaths.Resolve('):continue
            # SaveScene(scene) has no path argument, FindAssets(filter) has no folders.
            args[index]=leading+'ContentPaths.Resolve('+value+')'
        text=text[:opening+1]+','.join(args)+text[end:]
    output=re.compile(r'(?<![\w.])(?:AssetDatabase\.(?:GetAssetPath|GUIDToAssetPath|GetAllAssetPaths|GetDependencies)|PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot)\s*\(')
    for m in reversed(list(output.finditer(text))):
        end=end_call(text,m.end()-1)
        text=text[:m.start()]+'ContentPaths.Legacy('+text[m.start():end+1]+')'+text[end+1:]
    for name in ('GetFiles','EnumerateFiles','GetDirectories','Exists'):
        target={'EnumerateFiles':'GetFiles','Exists':'DirectoryExists'}.get(name,name)
        text=re.sub(r'(?<![\w.])Directory\.'+name+r'\(', 'ContentPaths.'+target+'(',text)
    text='using ContentPaths = FFOne.Content.ContentPaths;\n'+text
    return text

def main():
    count=0
    for folder,editor in [('Assets/Scripts/Runtime',False),('Assets/Scripts/Editor',True),('Assets/Tests/Editor',True)]:
        for path in (ROOT/folder).rglob('*.cs'):
            try: old=path.read_text(encoding='utf-8-sig')
            except UnicodeDecodeError: old=path.read_text(encoding='cp1251')
            new=transform(old,editor)
            if old!=new:path.write_text(new,encoding='utf-8');count+=1
    print('Updated',count,'consumers')

if __name__=='__main__':main()
