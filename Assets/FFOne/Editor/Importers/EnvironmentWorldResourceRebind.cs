using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class EnvironmentWorldResourceRebind
    {
        public static object Run()
        {
            var models = new Dictionary<string, string>();
            var meshes = new Dictionary<string, string>();
            foreach (string path in ContentPaths.GetFiles(NativeAssetPublisher.ManifestRoot + "/objects", "publication.json", SearchOption.AllDirectories))
            {
                var m = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(path)));
                models[m.SourceGuid] = m.PrefabPath;
                foreach (var entry in m.Assets.Where(e => e.Type == "UnityEngine.Mesh")) meshes[m.SourceGuid + ":" + entry.SourceLocalId] = entry.Path;
            }
            int changedVisuals = 0, changedColliders = 0, tiles = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WorldTileContent", ContentPaths.Resolve(new[] { "Assets/Resources/World/Tiles" })))
            {
                var tile = AssetDatabase.LoadAssetAtPath<WorldTileContent>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(guid))));
                bool changed = false;
                foreach (var placement in tile.Visuals)
                    if (placement.Model && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(placement.Model, out string source, out long _) && models.TryGetValue(source, out string path))
                    { placement.Model = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path)); changedVisuals++; changed = true; }
                foreach (var collider in tile.Colliders)
                    if (collider.Mesh && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(collider.Mesh, out string source, out long local) && meshes.TryGetValue(source + ":" + local, out string path))
                    { collider.Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ContentPaths.Resolve(path)); changedColliders++; changed = true; }
                if (changed) { EditorUtility.SetDirty(tile); AssetDatabase.SaveAssetIfDirty(tile); }
                tiles++;
            }
            int changedWater = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WorldCatalog", ContentPaths.Resolve(new[] { "Assets/Resources/World" })))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<WorldCatalog>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(guid)))); bool changed = false;
                foreach (var water in catalog.NeighborWater)
                    foreach (var placement in water.Visuals)
                        if (placement.Model && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(placement.Model, out string source, out long _) && models.TryGetValue(source, out string path))
                        { placement.Model = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path)); changedWater++; changed = true; }
                if (changed) { EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
            }
            var result = new { tiles, changedVisuals, changedColliders, changedWater };
            File.WriteAllText(ContentPaths.FilePath("Logs/EnvironmentWorldResourceRebind.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
            return result;
        }
    }
}
