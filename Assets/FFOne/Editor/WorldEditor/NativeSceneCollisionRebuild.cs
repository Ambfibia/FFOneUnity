using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFOne.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    // Migration only: the player uses ordinary serialized Unity MeshColliders.
    // Meshes are embedded in each scene, with no runtime collision conversion
    // and no dependency on external GLB/cooked collision meshes for physics.
    public static class NativeSceneCollisionRebuild
    {
        public const string ReportRoot = "Logs/NativeSceneCollision";
        sealed class Record
        {
            public string Name, SourceNode, PreviousMesh, GeometrySha256;
            public bool Reversed, Enabled, Trigger, Convex;
            public float[] Matrix;
            public int Vertices, Indices;
        }
        sealed class Report
        {
            public string Cell, Scene, PreviousSceneSha256;
            public int Placements, Meshes;
            public bool ReloadVerified;
            public Record[] Colliders;
        }
        static Queue<string> pending;
        static int completed, placements;
        public static bool Running => pending != null;
        public static string Status { get; private set; } = "Idle";

        [MenuItem("FFOne/World/Rebuild Native Scene Colliders")]
        public static void BeginAll()
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode and any active collision rebuild first.");
            var paths = AssetDatabase.FindAssets("t:WorldTileContent", new[] { "Assets/FFOne/Worlds" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            pending = new Queue<string>(paths); completed = placements = 0;
            Directory.CreateDirectory(ReportRoot);
            Status = "Queued " + paths.Length + " cells";
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            try
            {
                if (pending.Count == 0)
                {
                    Status = $"PASS: {completed} cells, {placements} native MeshColliders saved and reload-verified.";
                    File.WriteAllText(ReportRoot + "/result.txt", Status);
                    pending = null; EditorApplication.update -= Tick; Debug.Log(Status); return;
                }
                string path = pending.Dequeue();
                placements += RebuildCell(path);
                completed++;
                Status = $"Verified {completed} cells; remaining {pending.Count}; colliders {placements}";
                File.WriteAllText(ReportRoot + "/progress.txt", Status);
                if (completed % 5 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
            }
            catch (Exception error)
            {
                Status = "FAILED: " + error;
                File.WriteAllText(ReportRoot + "/result.txt", Status);
                pending = null; EditorApplication.update -= Tick; Debug.LogException(error);
            }
        }

        public static int RebuildCell(string contentPath)
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>(contentPath);
            if (!content) throw new InvalidDataException("Missing cell content: " + contentPath);
            string cellFolder = Path.GetDirectoryName(Path.GetDirectoryName(contentPath)).Replace('\\', '/');
            string scenePath = cellFolder + "/" + content.Id + ".unity";
            if (!File.Exists(scenePath)) throw new FileNotFoundException("Missing authored cell", scenePath);
            var scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.isLoaded) throw new InvalidOperationException("Close the cell before rebuilding: " + scenePath);
            Directory.CreateDirectory(ReportRoot + "/Before");
            string backup = ReportRoot + "/Before/" + content.Id + ".unity";
            if (!File.Exists(backup)) File.Copy(scenePath, backup);
            var report = new Report { Cell = content.Id, Scene = scenePath, PreviousSceneSha256 = Hash(File.ReadAllBytes(backup)) };
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var owned = new List<Mesh>();
            try
            {
                var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).ToArray();
                var bindings = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnvironmentCellBehaviour>(true))
                    .SelectMany(b => b.Bindings).Where(b => !b.Visual && b.Target).GroupBy(b => b.Target)
                    .ToDictionary(g => g.Key, g => g.First().SourceNode);
                var definitions = content.Colliders.ToDictionary(c => c.SourceNode);
                var byName = content.Colliders.ToDictionary(c => c.Name);
                var meshes = new Dictionary<string, Mesh>();
                var records = new List<Record>();
                var matched = new HashSet<WorldCollider>();
                foreach (var collider in colliders)
                {
                    if (!collider.sharedMesh) throw new InvalidDataException("Empty MeshCollider: " + collider.name);
                    WorldCollider definition = null;
                    if (bindings.TryGetValue(collider.transform, out var node)) definitions.TryGetValue(node, out definition);
                    if (definition == null) byName.TryGetValue(collider.name, out definition);
                    Mesh source = definition?.Mesh ? definition.Mesh : collider.sharedMesh;
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    bool water = definition != null && (definition.Name.StartsWith("ffWater", StringComparison.OrdinalIgnoreCase) || definition.Name.StartsWith("ffPoison", StringComparison.OrdinalIgnoreCase));
                    bool reverse = definition != null && !sourcePath.StartsWith(NativeWorldColliderImport.Root + "/", StringComparison.Ordinal);
                    if (water) reverse = HorizontalArea(source) < 0;
                    if (definition != null) matched.Add(definition);
                    string key = source.GetInstanceID() + "/" + reverse;
                    if (!meshes.TryGetValue(key, out var mesh))
                    {
                        // Only the position/index streams participate in physics.
                        // Do not copy visual materials, UVs or stale cooking data.
                        mesh = new Mesh { name = "Native collision " + source.name, indexFormat = source.indexFormat };
                        mesh.vertices = source.vertices;
                        var indices = source.triangles;
                        for (int i = 0; i < indices.Length; i += 3)
                            if (reverse) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                        mesh.triangles = indices; mesh.RecalculateBounds();
                        NativeCollisionMeshRefinement.Refine(mesh);
                        meshes.Add(key, mesh); owned.Add(mesh);
                    }
                    // Keep authored transforms, material, activation, trigger and
                    // behaviour bindings. Never match geometry by proximity.
                    var matrix = collider.transform.localToWorldMatrix;
                    records.Add(new Record { Name = collider.name, SourceNode = definition?.SourceNode,
                        PreviousMesh = sourcePath, Reversed = reverse, GeometrySha256 = GeometryHash(mesh),
                        Vertices = mesh.vertexCount, Indices = mesh.triangles.Length,
                        Matrix = Enumerable.Range(0, 16).Select(i => matrix[i]).ToArray(),
                        Enabled = collider.enabled, Trigger = collider.isTrigger, Convex = collider.convex });
                    collider.sharedMesh = null;
                    collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
                        MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
                    collider.sharedMesh = mesh;
                    if (PrefabUtility.IsPartOfPrefabInstance(collider)) PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                    EditorUtility.SetDirty(collider);
                }
                if (matched.Count != content.Colliders.Length)
                    throw new InvalidDataException($"{content.Id}: matched {matched.Count}/{content.Colliders.Length} original colliders.");
                report.Placements = colliders.Length; report.Meshes = meshes.Count; report.Colliders = records.ToArray();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); foreach (var mesh in owned) if (mesh) UnityEngine.Object.DestroyImmediate(mesh); }

            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).ToArray();
                if (colliders.Length != report.Colliders.Length) throw new InvalidDataException("Collider count changed after save: " + scenePath);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i]; var expected = report.Colliders[i];
                    if (collider.name != expected.Name || !collider.sharedMesh ||
                        !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(collider.sharedMesh)) ||
                        GeometryHash(collider.sharedMesh) != expected.GeometrySha256 ||
                        collider.enabled != expected.Enabled || collider.isTrigger != expected.Trigger || collider.convex != expected.Convex)
                        throw new InvalidDataException("Collider serialization mismatch: " + collider.name);
                    var matrix = collider.transform.localToWorldMatrix;
                    for (int j = 0; j < 16; j++) if (Mathf.Abs(matrix[j] - expected.Matrix[j]) > .0001f)
                        throw new InvalidDataException("Collider transform changed: " + collider.name);
                }
                report.ReloadVerified = true;
                File.WriteAllText(ReportRoot + "/" + content.Id + ".json", JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            return report.Placements;
        }

        static double HorizontalArea(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles; double area = 0;
            for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).y;
            if (double.IsNaN(area) || Math.Abs(area) < 1e-12) throw new InvalidDataException("Invalid water surface: " + mesh.name);
            return area;
        }
        public static string GeometryHash(Mesh mesh)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                foreach (var v in mesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                foreach (int index in mesh.triangles) writer.Write(index);
            }
            return Hash(stream.ToArray());
        }
        static string Hash(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
    }
}
