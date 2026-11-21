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
        [MenuItem("FFOne/Content/Rebuild Addressable Catalog")]
        public static void Rebuild()
        {
            NativeEffectPublication.RebuildAll();
            WorldCellPublication.RebuildAll();
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
            var groups=new Dictionary<string,AddressableAssetGroup>();
            var registered=new HashSet<string>(StringComparer.Ordinal);
            // Dependency queries can require an import. Resolve them before pausing
            // the importer with StartAssetEditing, otherwise Unity can wait forever.
            var sharedUiArtwork = AssetDatabase.GetDependencies(Directory.GetFiles("Assets/FFOne/Content/UI/Styles", "*.uss", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/')).ToArray(), true);
            var nativeDocuments = AssetDatabase.FindAssets("t:NativeDataDocument",new[]{"Assets/FFOne/Data/Tables"});
            var authoredAssets = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject",new[]{"Assets/FFOne/Content","Assets/FFOne/Worlds","Assets/FFOne/Data"})
                .Concat(AssetDatabase.FindAssets("t:AnimationClip",new[]{"Assets/FFOne/Content/Environment/Animation/Imported"})).ToArray();
            Debug.Log("CONTENT CATALOG: discovery complete; publishing migrated addresses.");
            AddressableAssetGroup GetGroup(string name)
            {
                if(groups.TryGetValue(name,out var result))return result;
                result=settings.FindGroup(name);
                if(!result)result=settings.CreateGroup(name,false,false,false,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
                ContentBundleLayout.Configure(settings,result);
                groups.Add(name,result);return result;
            }
            AssetDatabase.StartAssetEditing();
            try
            {
            foreach(var entry in ContentPaths.Entries)
            {
                if (entry.addressAlias) continue;
                string path=ContentPaths.CurrentPath(entry);
                if(IsPublishedVfxSource(path)) { settings.RemoveAssetEntry(entry.guid,false); continue; }
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
                string groupName=Group(path);
                var group=GetGroup(groupName);
                var asset=settings.CreateOrMoveEntry(guid,group,false,false);
                asset.SetAddress(key,false);
                registered.Add(guid);
            }
            Debug.Log("CONTENT CATALOG: migrated addresses complete; publishing native UI.");
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
                settings.CreateOrMoveEntry(guid, GetGroup(Group(path)), false, false).SetAddress(key, false);
                registered.Add(guid);
            }
            // Publish newly authored native documents by the same identity used
            // by NativeData.Load; GUID-only addresses cannot resolve those calls.
            // Source-bound USS and UXML can both reference the same native artwork.
            // Give those textures a single explicit owner instead of implicit copies
            // in UI-Styles and UI-Views bundles.
            foreach (string path in sharedUiArtwork)
            {
                if (!path.StartsWith("Assets/FFOne/Content/UI/Artwork/Retrobution/", StringComparison.Ordinal) ||
                    !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (registered.Contains(guid)) continue;
                string key = "ffone/" + guid;
                settings.CreateOrMoveEntry(guid, GetGroup("UI-Artwork-Retrobution"), false, false).SetAddress(key, false);
                registered.Add(guid); keys.Add(key);
            }
            Debug.Log("CONTENT CATALOG: native UI complete; publishing native documents.");
            foreach(string guid in nativeDocuments)
            {
                if(IsPublishedVfxSource(AssetDatabase.GUIDToAssetPath(guid))) { settings.RemoveAssetEntry(guid,false); continue; }
                if(registered.Contains(guid))continue;
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var document=AssetDatabase.LoadAssetAtPath<NativeDataDocument>(path);
                if(!document || string.IsNullOrEmpty(document.Identity))throw new InvalidDataException("Native data identity is missing: "+path);
                string key=NativeData.Resource(document.Identity);
                if(!keys.Add(key))throw new InvalidDataException("Duplicate native data address: "+key);
                settings.CreateOrMoveEntry(guid,GetGroup(Group(path)),false,false).SetAddress(key,false);
                registered.Add(guid);
            }
            Debug.Log("CONTENT CATALOG: native documents complete; publishing authored assets.");
            foreach(string guid in authoredAssets)
            {
                if(registered.Contains(guid))continue;
                string path=AssetDatabase.GUIDToAssetPath(guid);
                if(IsPublishedVfxSource(path)) { settings.RemoveAssetEntry(guid,false); continue; }
                if(path.Contains("/Source/") || path.Contains("/Incoming/") || path.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))continue;
                string key="ffone/"+guid;
                settings.CreateOrMoveEntry(guid,GetGroup(Group(path)),false,false).SetAddress(key,false);
                registered.Add(guid);keys.Add(key);
            }
            foreach(string guid in AssetDatabase.FindAssets("t:NativeEffectDefinition t:Texture2D t:Material",new[]{NativeEffectPublication.Root}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var definition=AssetDatabase.LoadAssetAtPath<NativeEffectDefinition>(path);
                string key=definition?(definition.Identity.StartsWith("vfx/",StringComparison.Ordinal)?definition.Identity:NativeEffectDefinition.Resource(definition.Id,path.Contains("/Projectiles/"))):"ffone/"+guid;
                settings.CreateOrMoveEntry(guid,GetGroup("VFX-Published-"+path.Substring(NativeEffectPublication.Root.Length+1).Split('/')[0]),false,false).SetAddress(key,false);
                keys.Add(key);registered.Add(guid);
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
            const string hologram = "Assets/FFOne/Rendering/Shaders/DexterShipHologram.shader";
            string hologramGuid = AssetDatabase.AssetPathToGUID(hologram);
            if (string.IsNullOrEmpty(hologramGuid)) throw new InvalidDataException("Missing tutorial hologram shader.");
            settings.CreateOrMoveEntry(hologramGuid, GetGroup("Rendering-Shaders"), false, false).SetAddress("Tutorial/DexterShipHologram", false);
            keys.Add("Tutorial/DexterShipHologram");
            PublishAddressAliases(settings, keys);
            Debug.Log("CONTENT CATALOG: authored assets complete; saving groups.");
            // A rebuild can move the last entry out of a former broad group. Remove
            // only our retired empty groups, preserving custom and default groups.
            foreach (var group in settings.groups.ToArray())
                if (group && group.entries.Count == 0 && group != settings.DefaultGroup &&
                    new[] { "UI-Artwork", "UI-Icons", "Audio-Voice", "Characters-Humanoids-Equipment", "Characters-Humanoids-Base", "Environment-SourceLibrary" }.Contains(group.Name))
                    settings.RemoveGroup(group);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification,null,true,true);
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            Debug.Log("CONTENT LAYOUT: "+keys.Count+" stable addresses; "+groups.Count+" local groups.");
        }
        static void PublishAddressAliases(AddressableAssetSettings settings, HashSet<string> keys)
        {
            foreach (var entry in ContentPaths.Entries.Where(entry => entry.addressAlias))
            {
                string key = ContentPaths.Resource(entry.old);
                var sharedAsset = settings.FindAssetEntry(entry.guid);
                if (sharedAsset == null || !keys.Add(key))
                    throw new InvalidDataException("Invalid shared Addressables alias: " + key);

                // A unique label preserves the old lookup key while the bundles
                // carry one copy under the shared asset's primary address.
                settings.AddLabel(key, false);
                sharedAsset.SetLabel(key, true, false, false);
                sharedAsset.parentGroup.GetSchema<BundledAssetGroupSchema>().IncludeLabelsInCatalog = true;
            }
        }
        static bool IsPublishedVfxSource(string path)
        {
            string normal=path.Replace('\\','/');
            return normal.EndsWith(".closure.json",StringComparison.OrdinalIgnoreCase)||normal.EndsWith(".closure.json.asset",StringComparison.OrdinalIgnoreCase)
                || normal.StartsWith("Assets/FFOne/Data/Tables/map/tiles/",StringComparison.Ordinal)&&normal.EndsWith("/behaviour.json.asset",StringComparison.Ordinal)
                || normal.StartsWith("Assets/FFOne/Worlds/",StringComparison.Ordinal)&&normal.EndsWith("/Source/behaviour.json",StringComparison.Ordinal);
        }
        static string Group(string path)
        {
            string specialized = ContentBundleLayout.SpecializedGroup(path);
            if (specialized != null) return specialized;
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
