using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFOne.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class EnvironmentModelRepair
    {
        static Queue<string> pending;
        static int completed, total;
        public static string Status { get; private set; }
        public static bool IsRunning => pending != null;
        static string FilePath(string path) => Application.platform == RuntimePlatform.WindowsEditor ? @"\\?\" + Path.GetFullPath(path) : path;
        public static int RepairColorMasks()
        {
            string shader = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve("Assets/Shaders/NativeWorld.shader")); int count = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in ContentPaths.GetFiles("Assets/Resources", "*.mat", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(ContentPaths.FilePath(FilePath(path)));
                    if (!text.Contains("guid: " + shader) || !Regex.IsMatch(text, @"_ColorMask: 7\r?$", RegexOptions.Multiline)) continue;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(ContentPaths.Resolve(path));
                    if (!material || material.GetFloat("_ColorMask") != 7) continue;
                    material.SetFloat("_ColorMask", (int)(UnityEngine.Rendering.ColorWriteMask.Red | UnityEngine.Rendering.ColorWriteMask.Green | UnityEngine.Rendering.ColorWriteMask.Blue));
                    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); count++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return count;
        }
        public static void Begin()
        {
            if (IsRunning) throw new InvalidOperationException("Repair already running");
            var audit = JObject.Parse(File.ReadAllText(ContentPaths.FilePath("Logs/EnvironmentBasisAudit.json")));
            var candidates = audit.Properties().Select(p => p.Value).Where(e => (string)e["axis"] == "+Z" &&
                !new[] { "Technical/", "Special/", "Water/" }.Any(prefix => ((string)e["category"]).StartsWith(prefix, StringComparison.Ordinal)))
                .Select(e => (string)e["identity"]).ToArray();
            pending = new Queue<string>(candidates); completed = 0; total = pending.Count;
            EditorApplication.update += Advance; Status = "Ready " + total;
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                // Bounded batches leave the editor responsive and make progress observable.
                var batch = new List<string>();
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int i = 0; i < 30 && pending.Count > 0; i++)
                    { string identity = pending.Peek(); if (Repair(identity, true)) batch.Add(identity); pending.Dequeue(); completed++; }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (string identity in batch) RefreshManifest(identity);
                if (batch.Count > 0) EnvironmentSharedAssets.SaveIndex();
                Status = "GLB Y-up: " + completed + "/" + total;
                if (completed % 300 == 0) { AssetDatabase.SaveAssets(); EditorUtility.UnloadUnusedAssetsImmediate(); }
                if (pending.Count == 0) { EditorApplication.update -= Advance; pending = null; SynchronizeOpenMapScenes(); Debug.Log(Status); }
            }
            catch (Exception e) { EditorApplication.update -= Advance; pending = null; Status = e.ToString(); Debug.LogException(e); }
        }
        public static bool Repair(string identity, bool batch = false)
        {
            if (!EnvironmentAssetRules.IsEnvironment(identity)) throw new ArgumentException("Environment source required");
            string source = "Assets/Resources/" + identity + ".glb";
            string record = NativeAssetPublisher.ManifestRoot + "/" + identity + "/publication.json";
            var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(record)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(manifest.PrefabPath));
            var doc = NativeGlbDocument.Read(source);
            EnvironmentGlbBasis.Validate(doc.Json);
            if (prefab.GetComponent<WorldModelBasis>() && EnvironmentGlbBasis.IsNormalized(doc.Json)) return false;
            if (prefab.GetComponent<WorldModelBasis>()) throw new InvalidDataException("Normalized prefab has an unnormalized source; review source replacement: " + identity);
            if (!prefab.GetComponent<WorldModelBasis>())
            {
                var meshes = manifest.Assets.Where(e => e.Type == typeof(Mesh).FullName).Select(e => AssetDatabase.LoadAssetAtPath<Mesh>(ContentPaths.Resolve(e.Path))).ToArray();
                if (meshes.Any(m => !m || m.blendShapeCount != 0 || m.bindposes.Length != 0)) throw new InvalidDataException("Unsupported editable mesh " + identity);
                if (prefab.GetComponentsInChildren<Transform>(true).Any(t => t.localPosition != Vector3.zero || Quaternion.Angle(t.localRotation, Quaternion.identity) > .001f || t.localScale != Vector3.one))
                    throw new InvalidDataException("Edited/transformed prefab requires review " + identity);
                // A basis change belongs to this source model, not every model sharing its old mesh.
                var replacements = new Dictionary<Mesh, Mesh>();
                var created = new List<string>();
                EnvironmentGlbBasis.Bake(source);
                foreach (var original in meshes)
                {
                    bool shared = ContentPaths.Legacy(AssetDatabase.GetAssetPath(original)).StartsWith(EnvironmentSharedAssets.Root + "/", StringComparison.Ordinal);
                    var mesh = shared ? UnityEngine.Object.Instantiate(original) : original;
                    mesh.vertices = mesh.vertices.Select(EnvironmentGlbBasis.Rotate).ToArray();
                    if (mesh.normals.Length != 0) mesh.normals = mesh.normals.Select(EnvironmentGlbBasis.Rotate).ToArray();
                    if (mesh.tangents.Length != 0) mesh.tangents = mesh.tangents.Select(t => new Vector4(t.x,t.z,-t.y,t.w)).ToArray();
                    mesh.RecalculateBounds();
                    if (shared)
                    {
                        var replacement = (Mesh)EnvironmentSharedAssets.Acquire(mesh, created, out string sharedPath);
                        foreach (var entry in manifest.Assets.Where(e => e.Guid == AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(original))))))
                        { entry.Path = sharedPath; entry.Guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(entry.Path)); }
                        replacements[original] = replacement; UnityEngine.Object.DestroyImmediate(mesh);
                    }
                    else { EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
                }
                var root = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(manifest.PrefabPath));
                try
                {
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) if (replacements.TryGetValue(filter.sharedMesh, out var replacement)) filter.sharedMesh = replacement;
                    foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true)) if (collider.sharedMesh && replacements.TryGetValue(collider.sharedMesh, out var replacement)) collider.sharedMesh = replacement;
                    root.AddComponent<WorldModelBasis>(); PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(manifest.PrefabPath));
                    File.WriteAllText(ContentPaths.FilePath(record), JsonConvert.SerializeObject(manifest, Formatting.Indented));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.ImportAsset(ContentPaths.Resolve(source), batch ? ImportAssetOptions.Default : ImportAssetOptions.ForceSynchronousImport);
            if (!batch) { RefreshManifest(identity); EnvironmentSharedAssets.SaveIndex(); }
            return true;
        }
        static void RefreshManifest(string identity)
        {
            string source = "Assets/Resources/" + identity + ".glb";
            string record = NativeAssetPublisher.ManifestRoot + "/" + identity + "/publication.json";
            var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(record)));
            foreach (var entry in manifest.Assets) if (string.IsNullOrEmpty(entry.Guid)) entry.Guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(entry.Path));
            manifest.SourceDependencyHash = AssetDatabase.GetAssetDependencyHash(ContentPaths.Resolve(source)).ToString();
            manifest.SourceContentHash = EnvironmentSharedAssets.SourceContentHash(source);
            File.WriteAllText(ContentPaths.FilePath(record), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            // The JSON is editor provenance, not a runtime dependency. Unity's next refresh imports it in one batch.
        }
        public static int SynchronizeOpenMapScenes()
        {
            int changed = 0;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.path.StartsWith("Assets/FFOne/Worlds/Overworld/Cells/", StringComparison.Ordinal)) continue;
                foreach (var basis in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldModelBasis>(true)))
                {
                    if (basis.SourceBasisApplied || !basis.name.Contains("[BuildPlayer-", StringComparison.Ordinal)) continue;
                    WorldModelBasis.ApplySource(basis.gameObject); PrefabUtility.RecordPrefabInstancePropertyModifications(basis);
                    foreach (Transform child in basis.transform) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                    changed++;
                }
                if (changed > 0) EditorSceneManager.SaveScene(scene);
            }
            return changed;
        }
    }
}
