using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class EnvironmentAssemblyBuilder
    {
        static Queue<EnvironmentLibrary.Entry> pending;
        static EnvironmentLibrary library;
        static int completed, created;
        public static string Status { get; private set; } = "";
        public static bool Running => pending != null;
        public static bool NonBlocking(string category) => category.StartsWith("Technical/") || category.StartsWith("Water/") ||
            category == "Vegetation/Grass" || category == "Vegetation/Flowers" || category == "Vegetation/Vines";
        [MenuItem("FFOne/Environment/Build Placeable Prefabs")]
        public static void Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build environment prefabs outside Play Mode.");
            if (Running) return;
            library = EnvironmentLibraryBuilder.Load();
            pending = new Queue<EnvironmentLibrary.Entry>(library.Entries.Where(e => !e.Category.StartsWith("Technical/")));
            completed = created = 0; EditorApplication.update += Advance;
        }
        public static GameObject Build(EnvironmentLibrary.Entry entry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build environment prefabs outside Play Mode.");
            if (!EnvironmentAssetRules.IsEnvironment(entry.Identity)) throw new InvalidDataException("Characters cannot become environment objects.");
            string modelPath = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid));
            string path = Path.GetDirectoryName(modelPath).Replace('\\', '/') + "/environment.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
            if (existing)
            {
                var owner = existing.GetComponent<EnvironmentObject>();
                if (!owner || owner.SourceIdentity != entry.Identity) throw new IOException("Placeable prefab path belongs to another object: " + path);
                entry.AssemblyGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path)); entry.AssemblyStatus = owner.CollisionNotes;
                return existing; // Rebuild does not overwrite collider/material/behaviour edits.
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(modelPath));
            var staging = new GameObject("Environment prefab staging") { hideFlags = HideFlags.HideAndDontSave };
            staging.SetActive(false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.transform.SetParent(staging.transform, false);
            try
            {
                var metadata = instance.AddComponent<EnvironmentObject>(); metadata.SourceIdentity = entry.Identity;
                metadata.OriginalAnimations = instance.GetComponentsInChildren<Animation>(true);
                metadata.OriginalStates = metadata.OriginalAnimations.SelectMany(a => a.Cast<AnimationState>().Select(s => s.name)).Distinct().ToArray();
                bool animated = metadata.OriginalAnimations.Length > 0 || instance.GetComponentsInChildren<Animator>(true).Length > 0 || instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                if (entry.Category.StartsWith("Review/")) metadata.CollisionNotes = "Объект требует классификации; автоматическая физическая коллизия не добавлена.";
                else if (NonBlocking(entry.Category)) metadata.CollisionNotes = "РќРµР±Р»РѕРєРёСЂСѓСЋС‰РµРµ РѕРєСЂСѓР¶РµРЅРёРµ: С„РёР·РёС‡РµСЃРєР°СЏ РєРѕР»Р»РёР·РёСЏ РЅРµ РґРѕР±Р°РІР»СЏРµС‚СЃСЏ.";
                else if (animated) metadata.CollisionNotes = "РђРЅРёРјРёСЂРѕРІР°РЅРЅС‹Р№ РѕР±СЉРµРєС‚: СЃРѕС…СЂР°РЅРµРЅС‹ РёСЃС…РѕРґРЅС‹Рµ СЃРѕСЃС‚РѕСЏРЅРёСЏ; РєРѕР»Р»РёР·РёСЋ РЅСѓР¶РЅРѕ РЅР°СЃС‚СЂРѕРёС‚СЊ РїРѕРґ РґРІРёР¶РµРЅРёРµ.";
                else
                {
                    int count = 0;
                    foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0 || filter.sharedMesh.subMeshCount == 0) continue;
                        var renderer = filter.GetComponent<Renderer>();
                        if (!renderer || !renderer.enabled || filter.GetComponent<Collider>()) continue;
                        if (Enumerable.Range(0, filter.sharedMesh.subMeshCount).Any(i => filter.sharedMesh.GetTopology(i) != MeshTopology.Triangles)) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh; count++;
                    }
                    metadata.CollisionNotes = count == 0 ? "РќРµС‚ РїРѕРґС…РѕРґСЏС‰РµР№ СЃС‚Р°С‚РёС‡РµСЃРєРѕР№ РіРµРѕРјРµС‚СЂРёРё РґР»СЏ РєРѕР»Р»РёР·РёРё." :
                        "РЎС‚Р°С‚РёС‡РµСЃРєРёР№ MeshCollider РїРѕ РІРёР·СѓР°Р»СЊРЅРѕР№ РіРµРѕРјРµС‚СЂРёРё. Р­С‚Рѕ Р°РІС‚РѕСЂСЃРєР°СЏ РєРѕР»Р»РёР·РёСЏ, РЅРµ РІРѕСЃСЃС‚Р°РЅРѕРІР»РµРЅРЅР°СЏ РєРѕР»Р»РёР·РёСЏ РєР°СЂС‚С‹; РґР»СЏ РјРѕР±РёР»СЊРЅРѕР№ РІРµСЂСЃРёРё РјРѕР¶РЅРѕ Р·Р°РјРµРЅРёС‚СЊ РїСЂРёРјРёС‚РёРІР°РјРё.";
                }
                var result = PrefabUtility.SaveAsPrefabAsset(instance, ContentPaths.Resolve(path), out bool success);
                if (!success) throw new IOException("Could not save environment prefab: " + path);
                entry.AssemblyGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path)); entry.AssemblyStatus = metadata.CollisionNotes;
                created++; return result;
            }
            finally { Object.DestroyImmediate(staging); }
        }
        public static void Advance()
        {
            if (!Running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var batch = new List<EnvironmentLibrary.Entry>();
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int i = 0; i < 24 && pending.Count > 0 && timer.ElapsedMilliseconds < 500; i++)
                    { var entry = pending.Peek(); Build(entry); batch.Add(entry); pending.Dequeue(); completed++; }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var entry in batch)
                {
                    string path = Path.GetDirectoryName(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid))).Replace('\\', '/') + "/environment.prefab";
                    entry.AssemblyGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path));
                    if (string.IsNullOrEmpty(entry.AssemblyGuid)) throw new IOException("Placeable prefab failed to import: " + path);
                }
                if (completed % 240 < batch.Count) { EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); }
                Status = $"Placeable environment: {completed} processed, {pending.Count} remaining, {created} created";
                if (pending.Count == 0)
                {
                    pending = null; EditorApplication.update -= Advance;
                    EnvironmentLibraryGrouping.Rebuild(library);
                    EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
                    File.WriteAllText(ContentPaths.FilePath("Logs/EnvironmentAssemblies.json"), JsonConvert.SerializeObject(new { completed, created, status = "complete" }, Formatting.Indented));
                    Debug.Log(Status);
                }
            }
            catch (Exception e)
            {
                pending = null; EditorApplication.update -= Advance; Status = e.ToString();
                EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); Debug.LogException(e);
            }
        }
    }
}
