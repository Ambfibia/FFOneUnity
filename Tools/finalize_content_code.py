from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1]

def edit(relative, old, new):
    p=ROOT/relative;s=p.read_text(encoding='utf-8-sig')
    if old not in s:
        if new in s:return
        raise ValueError('Missing edit: '+relative+' '+old[:80])
    p.write_text(s.replace(old,new),encoding='utf-8')

for name,refs in [('Runtime',['Unity.Addressables','Unity.ResourceManager']),('Editor',['Unity.Addressables.Editor','Unity.Addressables','Unity.ResourceManager'])]:
    p=ROOT/f'Assets/Scripts/{name}/FFOne.{name}.asmdef'
    d=json.loads(p.read_text());d['references']=list(dict.fromkeys(d['references']+refs));p.write_text(json.dumps(d,indent=2)+'\n')

edit('Assets/Scripts/Runtime/Content/NativeModelAssets.cs','string path = UnityEditor.AssetDatabase.GetAssetPath(original);','string path = ContentPaths.Legacy(UnityEditor.AssetDatabase.GetAssetPath(original));')
edit('Assets/Scripts/Editor/NativeAssetPublisher.cs','sourcePath = AssetPath(sourcePath);','sourcePath = ContentPaths.Legacy(AssetPath(sourcePath));')
edit('Assets/Scripts/Editor/WorldModelTextureAssets.cs',"string path = assetPath.Replace('\\\\','/');","string path = ContentPaths.Legacy(assetPath).Replace('\\\\','/');")
edit('Assets/Scripts/Editor/WorldModelTextureAssets.cs','string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assetPath),uri));','string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ContentPaths.Legacy(assetPath)),uri));')
for name in ['NativeContentImport','WorldTextureImport','SettingsArtworkImporter']:
    relative=f'Assets/Scripts/Editor/{name}.cs'
    edit(relative,'void OnPreprocessTexture()\n        {','void OnPreprocessTexture()\n        {\n            string assetPath = ContentPaths.Legacy(this.assetPath);')

for folder in ['Assets/Scripts','Assets/Tests']:
    for p in (ROOT/folder).rglob('*.cs'):
        s=p.read_text(encoding='utf-8-sig')
        n=s.replace('Assets/Scenes/Main.unity','Assets/FFOne/Scenes/Bootstrap/Main.unity').replace('Assets/Scenes/NativeWorld.unity','Assets/FFOne/Scenes/Development/NativeWorld.unity')
        if n!=s:p.write_text(n,encoding='utf-8')

edit('Assets/Scripts/Runtime/World/WorldStreamer.cs','internal WorldTileContent Content;','internal WorldTileContent Content;\n            internal FFOne.Content.ContentAssets.Request Lease;')
edit('Assets/Scripts/Runtime/World/WorldStreamer.cs','var request = FFOne.Content.ContentAssets.LoadAsync<WorldTileContent>(tile.Reference.Resource);','var request = FFOne.Content.ContentAssets.AcquireAsync<WorldTileContent>(tile.Reference.Resource);\n            tile.Lease = request;')
edit('Assets/Scripts/Runtime/World/WorldStreamer.cs','tiles.Remove(tile.Reference.Id); retired++;','tile.Lease?.Dispose(); tile.Lease = null;\n            tiles.Remove(tile.Reference.Id); retired++;')
edit('Assets/Scripts/Runtime/World/WorldStreamer.cs','StopAllCoroutines();','StopAllCoroutines();\n            foreach (var tile in tiles.Values) tile.Lease?.Dispose();')

# Scene authoring and data import must never silently replace accepted work.
edit('Assets/Scripts/Editor/WorldContentImport.cs','string id = (string)manifest["id"], folder = Output + "/Tiles/" + id;','string id = (string)manifest["id"], folder = Output + "/Tiles/" + id;\n            if (File.Exists(ContentPaths.Resolve(folder + "/Content.asset")))\n                throw new IOException("Accepted world cell is protected from reimport: " + id + ". Import changed source into a staging project and review the differences first.");')

print('Content loading, texture identity, scene paths and reimport protection updated')
