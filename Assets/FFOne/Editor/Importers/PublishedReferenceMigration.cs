using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFOne.Content;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class PublishedReferenceMigration
    {
        [MenuItem("FFOne/Content/Bind Published Models to Existing Content")]
        public static void Run()
        {
            var map = new Dictionary<(string, long), (string Path, Type Type)>();
            var sourceGuids = new HashSet<string>();
            foreach (string file in ContentPaths.GetFiles(NativeAssetPublisher.ManifestRoot, "publication.json", SearchOption.AllDirectories))
            {
                var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(file)));
                if (!manifest.PrefabPath.StartsWith(NativeAssetPublisher.DefaultDestination + "/", StringComparison.Ordinal) &&
                    !manifest.PrefabPath.StartsWith(EnvironmentAssetRules.Root + "/", StringComparison.Ordinal)) continue;
                sourceGuids.Add(manifest.SourceGuid);
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(manifest.SourcePath));
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long id);
                map[(guid, id)] = (manifest.PrefabPath, typeof(GameObject));
                foreach (var asset in manifest.Assets) map[(guid, asset.SourceLocalId)] = (asset.Path, asset.Type.Contains("Mesh") ? typeof(Mesh) : asset.Type.Contains("Material") ? typeof(Material) : typeof(AnimationClip));
                if (sourceGuids.Count % 50 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
            }
            int filesChanged = 0, references = 0;
            // Native publications are independent already. Only migrate existing authored
            // containers, not source GLB or the individual assets created by publication.
            var paths = ContentPaths.GetFiles("Assets/Resources", "*", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/')).Where(p => (p.EndsWith(".asset") || p.EndsWith(".prefab")) &&
                    !p.StartsWith(NativeAssetPublisher.DefaultDestination + "/", StringComparison.Ordinal) &&
                    !p.StartsWith(EnvironmentAssetRules.Root + "/", StringComparison.Ordinal) &&
                    !p.StartsWith("Assets/Resources/NativeData/", StringComparison.Ordinal)).ToArray();
            foreach (string path in paths)
            {
                using (var input = File.OpenText(ContentPaths.FilePath(path))) if (input.ReadLine() != "%YAML 1.1") continue;
                if (!Regex.Matches(File.ReadAllText(ContentPaths.FilePath(path)), @"guid: ([a-f0-9]{32})").Cast<Match>().Any(m => sourceGuids.Contains(m.Groups[1].Value))) continue;
                bool changed = false;
                int Remap(Object owner)
                {
                    if (!owner) return 0;
                    using var serialized = new SerializedObject(owner); var property = serialized.GetIterator(); int updated = 0;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue || property.propertyPath.StartsWith("m_Prefab", StringComparison.Ordinal)) continue;
                        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceValue, out string guid, out long id) && map.TryGetValue((guid, id), out var destination))
                        {
                            var replacement = AssetDatabase.LoadAssetAtPath(ContentPaths.Resolve(destination.Path), destination.Type);
                            if (!replacement) throw new InvalidDataException("Missing published replacement " + destination.Path);
                            property.objectReferenceValue = replacement; updated++;
                        }
                    }
                    if (updated > 0) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(owner); }
                    return updated;
                }
                if (path.EndsWith(".prefab", StringComparison.Ordinal))
                {
                    var root = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(path));
                    try
                    {
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true).Reverse())
                            if (PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject) && ContentPaths.Legacy(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject)).EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                            { PrefabUtility.UnpackPrefabInstance(transform.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); changed = true; }
                        foreach (var component in root.GetComponentsInChildren<Component>(true)) { int n = Remap(component); references += n; changed |= n > 0; }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(path));
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(path))) { int n = Remap(asset); references += n; changed |= n > 0; if (n > 0) AssetDatabase.SaveAssetIfDirty(asset); }
                if (changed) filesChanged++;
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"PUBLISHED REFERENCES BOUND: {references} references across {filesChanged} authored containers; hierarchy and scalar values preserved.");
        }
    }
}
