using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace FFOne.Editor
{
    public static class ContentLayoutSetup
    {
        const string IndexPath="Assets/FFOne/Settings/Resources/FFOneContentIndex.json";
        [Serializable] sealed class AddressIndex { public string[] keys; public string[] fonts; }
        [MenuItem("FFOne/Content/Rebuild Addressable Catalog")]
        public static void Rebuild()
        {
            var settings=AddressableAssetSettingsDefaultObject.GetSettings(true);
            settings.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            foreach(string original in new[]{"Assets/Resources/NativeEditable/Index.asset","Assets/Resources/World/Catalog.asset"})
            {
                string path=ContentPaths.Resolve(original);
                var catalog=AssetDatabase.LoadMainAssetAtPath(path);
                string name=Path.GetFileNameWithoutExtension(path);
                if(catalog && catalog.name!=name){ catalog.name=name;EditorUtility.SetDirty(catalog); }
            }
            var keys=new HashSet<string>(StringComparer.Ordinal);
            var fonts=new List<string>();
            var groups=new Dictionary<string,AddressableAssetGroup>();
            var registered=new HashSet<string>(StringComparer.Ordinal);
            AddressableAssetGroup GetGroup(string name)
            {
                if(groups.TryGetValue(name,out var result))return result;
                result=settings.FindGroup(name);
                if(!result)result=settings.CreateGroup(name,false,false,false,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
                var schema=result.GetSchema<BundledAssetGroupSchema>();
                schema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kLocalBuildPath);
                schema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kLocalLoadPath);
                schema.BundleMode=name=="Terrain-Materials" || name=="Terrain-Textures"
                    ? BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                    : BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                schema.Compression=BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                groups.Add(name,result);return result;
            }
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach(var entry in ContentPaths.Entries)
            {
                string path=ContentPaths.CurrentPath(entry);
                if(path.Contains("/Editor/") || path.Contains("/Tests/") || path.Contains("/Runtime/"))
                {
                    // A previously published source can become editor-only (for
                    // example, extracted mip PNGs). Remove its old explicit entry.
                    if(!string.IsNullOrEmpty(entry.guid)) settings.RemoveAssetEntry(entry.guid,false);
                    continue;
                }
                string extension=Path.GetExtension(path).ToLowerInvariant();
                bool originalResource=entry.old.StartsWith("Assets/Resources/",StringComparison.Ordinal);
                if(!originalResource && !entry.old.StartsWith("Assets/Generated/",StringComparison.Ordinal) && !entry.old.StartsWith("Assets/Shaders/",StringComparison.Ordinal)) continue;
                if(entry.old.EndsWith(".glb",StringComparison.OrdinalIgnoreCase) || extension==".bin" || extension==".wgsl") continue;
                string guid=AssetDatabase.AssetPathToGUID(path);
                if(string.IsNullOrEmpty(guid)) throw new InvalidDataException("Missing migrated asset: "+path);
                string key=originalResource ? ContentPaths.Resource(entry.old) : "ffone/"+guid;
                if(!keys.Add(key)) throw new InvalidDataException("Duplicate content address: "+key);
                if(extension==".ttf" || extension==".otf") fonts.Add(key);
                string groupName=Group(path);
                var group=GetGroup(groupName);
                var asset=settings.CreateOrMoveEntry(guid,group,false,false);
                asset.SetAddress(key,false);
                registered.Add(guid);
            }
            // Native UI authoring must not depend on adding fictitious legacy Resources entries.
            foreach(var route in new[]{("Views", "ui/views/", "*.uxml"), ("Styles", "ui/styles/", "*.uss")})
            {
                string directory="Assets/FFOne/Content/UI/"+route.Item1;
                foreach(string file in Directory.GetFiles(directory,route.Item3,SearchOption.AllDirectories))
                {
                    string path=file.Replace('\\','/');
                    string guid=AssetDatabase.AssetPathToGUID(path);
                    if(registered.Contains(guid))continue;
                    if(string.IsNullOrEmpty(guid))throw new InvalidDataException("UI asset has not imported: "+path);
                    string relative=path.Substring(directory.Length+1);
                    string key=route.Item2+relative.Substring(0,relative.LastIndexOf('.'));
                    if(!keys.Add(key))throw new InvalidDataException("Duplicate UI address: "+key);
                    settings.CreateOrMoveEntry(guid,GetGroup("UI-"+route.Item1),false,false).SetAddress(key,false);
                    registered.Add(guid);
                }
            }
            // Source-bound artwork added after the migration has a native semantic key,
            // without inventing an entry in the legacy Resources migration manifest.
            const string artwork = "Assets/FFOne/Content/UI/Artwork/en";
            foreach (string file in Directory.GetFiles(artwork, "*.png", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (registered.Contains(guid)) continue;
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("UI artwork has not imported: " + path);
                string relative = path.Substring(artwork.Length + 1);
                string key = "ui/en/" + relative.Substring(0, relative.LastIndexOf('.'));
                if (!keys.Add(key)) throw new InvalidDataException("Duplicate UI artwork address: " + key);
                settings.CreateOrMoveEntry(guid, GetGroup("UI-Artwork"), false, false).SetAddress(key, false);
                registered.Add(guid);
            }
            // Publish newly authored native documents by the same identity used
            // by NativeData.Load; GUID-only addresses cannot resolve those calls.
            foreach(string guid in AssetDatabase.FindAssets("t:NativeDataDocument",new[]{"Assets/FFOne/Data/Tables"}))
            {
                if(registered.Contains(guid))continue;
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var document=AssetDatabase.LoadAssetAtPath<NativeDataDocument>(path);
                if(!document || string.IsNullOrEmpty(document.Identity))throw new InvalidDataException("Native data identity is missing: "+path);
                string key=NativeData.Resource(document.Identity);
                if(!keys.Add(key))throw new InvalidDataException("Duplicate native data address: "+key);
                settings.CreateOrMoveEntry(guid,GetGroup(Group(path)),false,false).SetAddress(key,false);
                registered.Add(guid);
            }
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject",new[]{"Assets/FFOne/Content","Assets/FFOne/Worlds","Assets/FFOne/Data"}))
            {
                if(registered.Contains(guid))continue;
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(path.Contains("/Source/") || path.Contains("/Incoming/") || path.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))continue;
                string key="ffone/"+guid;
                settings.CreateOrMoveEntry(guid,GetGroup(Group(path)),false,false).SetAddress(key,false);
                registered.Add(guid);keys.Add(key);
            }
            // Terrain detail shaders otherwise become implicit copies in every cell
            // containing Unity vegetation. Keep the package-owned originals shared.
            foreach(string shader in new[]{"TerrainDetailLit.shader","WavingGrass.shader","WavingGrassBillboard.shader"})
            {
                string path="Packages/com.unity.render-pipelines.universal/Shaders/Terrain/"+shader;
                string guid=AssetDatabase.AssetPathToGUID(path);
                if(string.IsNullOrEmpty(guid))throw new InvalidDataException("Missing URP terrain shader: "+path);
                string key="ffone/"+guid;
                settings.CreateOrMoveEntry(guid,GetGroup("Rendering-TerrainShaders"),false,false).SetAddress(key,false);
                registered.Add(guid);keys.Add(key);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(IndexPath));
            File.WriteAllText(IndexPath,JsonUtility.ToJson(new AddressIndex { keys=keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray(), fonts=fonts.ToArray() }));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification,null,true,true);
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(IndexPath);
            Debug.Log("CONTENT LAYOUT: "+keys.Count+" stable addresses; "+groups.Count+" local groups.");
        }
        static string Group(string path)
        {
            string relative=path.Replace("Assets/FFOne/","");
            var parts=relative.Split('/');
            // Bundle packing follows use and source identity, not an artist's folder move.
            if(ContentPaths.Legacy(path).StartsWith("Assets/Generated/WorldTerrainShared/",StringComparison.Ordinal))
            {
                string file=Path.GetFileNameWithoutExtension(path);
                if(file.StartsWith("Mesh_",StringComparison.Ordinal))return "Terrain-Meshes-"+file.Substring(5,1);
                if(file.StartsWith("TerrainMaterial_",StringComparison.Ordinal) || file.StartsWith("GrassMaterial_",StringComparison.Ordinal))return "Terrain-Materials";
                return "Terrain-Textures";
            }
            if(parts[0]=="Worlds" && parts.Length>4 && parts[2]=="Cells") return "World-"+parts[3];
            const string tileDocuments="Data/Tables/map/tiles/";
            if(relative.StartsWith(tileDocuments,StringComparison.Ordinal))return "World-"+relative.Substring(tileDocuments.Length).Split('/')[0];
            if(relative.StartsWith("Content/Environment/Terrain/Shared/",StringComparison.Ordinal))
            {
                string file=Path.GetFileNameWithoutExtension(path);
                if(file.StartsWith("Mesh_",StringComparison.Ordinal))return "Terrain-Meshes-"+file.Substring(5,1);
                if(file.StartsWith("TerrainMaterial_",StringComparison.Ordinal) || file.StartsWith("GrassMaterial_",StringComparison.Ordinal))return "Terrain-Materials";
                return "Terrain-Textures";
            }
            if(relative.Contains("/ExactSamplers/")) return "Environment-ExactTextures-"+Path.GetFileNameWithoutExtension(path).Substring(0,1);
            if(parts[0]=="Content" && parts.Length>3) return string.Join("-",parts.Skip(1).Take(parts[1]=="Characters" ? 3 : 2));
            if(parts[0]=="Data" && parts.Length>3) return string.Join("-",parts.Take(3));
            return string.Join("-",parts.Take(Math.Min(2,parts.Length-1)));
        }
        [MenuItem("FFOne/Content/Validate Migrated GUIDs")]
        public static void Validate()
        {
            var errors=new List<string>(); int checkedCount=0;
            foreach(var entry in ContentPaths.Entries)
            {
                string path=ContentPaths.CurrentPath(entry);
                if(!File.Exists(path)) { errors.Add("Missing "+path); continue; }
                if(!string.IsNullOrEmpty(entry.guid) && AssetDatabase.AssetPathToGUID(path)!=entry.guid) errors.Add("GUID changed "+path);
                checkedCount++;
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/ContentLayoutValidation.txt",new[]{"Checked assets: "+checkedCount,"Errors: "+errors.Count}.Concat(errors));
            if(errors.Count>0) throw new InvalidDataException(string.Join("\n",errors.Take(20)));
            Debug.Log("CONTENT LAYOUT VALID: "+checkedCount+" assets retain their GUIDs.");
        }
        [MenuItem("FFOne/Content/Validate Imported Source Models")]
        public static void ValidateSources()
        {
            int count=0,repaired=0;
            foreach(var entry in ContentPaths.Entries)
            {
                if(!entry.old.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))continue;
                string path=ContentPaths.CurrentPath(entry);
                if(AssetDatabase.GetMainAssetTypeAtPath(path)!=typeof(GameObject))
                {
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                    if(AssetDatabase.GetMainAssetTypeAtPath(path)!=typeof(GameObject))throw new InvalidDataException("Source model failed to import: "+path);
                    repaired++;
                }
                count++;
            }
            Debug.Log("CONTENT SOURCE MODELS VALID: "+count+"; repaired "+repaired+" imports.");
        }
    }
}
