from pathlib import Path
import re
from update_content_consumers import end_call,split_args
ROOT=Path(__file__).resolve().parents[1]
for folder in ['Assets/FFOne/Editor','Assets/FFOne/Tests']:
    for p in (ROOT/folder).rglob('*.cs'):
        s=p.read_text(encoding='utf-8-sig')
        if 'using ContentPaths = ' not in s:continue
        matches=list(re.finditer(r'AssetDatabase\.CreateFolder\(',s))
        for m in reversed(matches):
            end=end_call(s,m.end()-1);args=split_args(s[m.end():end])
            parent=args[0].strip()
            if parent.startswith('ContentPaths.Resolve('):parent=parent[len('ContentPaths.Resolve('):-1]
            s=s[:m.start()]+'ContentPaths.CreateFolder('+parent+','+args[1]+')'+s[end+1:]
        p.write_text(s,encoding='utf-8')
for name,variable in [('NativeContentImport','texture'),('SettingsArtworkImporter','importer')]:
    p=ROOT/f'Assets/FFOne/Editor/Importers/{name}.cs';s=p.read_text(encoding='utf-8')
    marker=f'var {variable} = (TextureImporter)assetImporter;'
    s=s.replace(marker,marker+f'\n            if (!{variable}.importSettingsMissing) return;',1)
    p.write_text(s,encoding='utf-8')
# Converted script-invocation validation uses the same boundary resolver.
p=ROOT/'Tools/validate_imported_terrain.csx';s=p.read_text(encoding='utf-8')
apis=['System.IO.File.ReadAllText','System.IO.File.ReadAllBytes','UnityEditor.AssetDatabase.LoadAssetAtPath','UnityEditor.AssetDatabase.LoadAllAssetsAtPath','UnityEditor.AssetDatabase.AssetPathToGUID']
pat=re.compile('('+'|'.join(re.escape(x) for x in apis)+r')(?:<[^()\n]+>)?\(')
for m in reversed(list(pat.finditer(s))):
    end=end_call(s,m.end()-1);args=split_args(s[m.end():end]);args[0]='FFOne.Content.ContentPaths.Resolve('+args[0]+')'
    s=s[:m.end()]+','.join(args)+s[end:]
p.write_text(s,encoding='utf-8')
print('Folder creation, import defaults and standalone terrain validation updated')
