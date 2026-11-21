using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FFOne.Editor
{
    public static class NativeCollisionRefinementJob
    {
        static Queue<string> pending;
        static int completed, meshes;
        public static string Status { get; private set; }
        public static bool Running => pending != null;
        public static void Begin()
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Collision refinement is not ready.");
            pending = new Queue<string>(JArray.Parse(File.ReadAllText(NativeSceneCollisionRebuild.ReportRoot + "/oversized-scenes.json"))
                .Select(p => (string)p["scene"]));
            completed = meshes = 0; Status = "Queued " + pending.Count + " cells";
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            try
            {
                if (pending.Count == 0)
                {
                    Status = $"PASS: {completed} scenes, {meshes} oversized meshes refined without changing their surfaces.";
                    File.WriteAllText(NativeSceneCollisionRebuild.ReportRoot + "/refinement-result.txt", Status);
                    pending = null; EditorApplication.update -= Tick; return;
                }
                Refine(pending.Dequeue()); completed++;
                Status = $"Verified {completed} cells; remaining {pending.Count}; refined meshes {meshes}";
                File.WriteAllText(NativeSceneCollisionRebuild.ReportRoot + "/refinement-progress.txt", Status);
                if (completed % 5 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
            }
            catch (Exception error)
            {
                Status = "FAILED: " + error;
                File.WriteAllText(NativeSceneCollisionRebuild.ReportRoot + "/refinement-result.txt", Status);
                pending = null; EditorApplication.update -= Tick; Debug.LogException(error);
            }
        }
        static bool Oversized(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
                for (int k = 0; k < 3; k++) if ((v[t[i + k]] - v[t[i + (k + 1) % 3]]).sqrMagnitude > 500 * 500) return true;
            return false;
        }
        static double Area(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles; double area = 0;
            for (int i = 0; i < t.Length; i += 3) area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * .5;
            return area;
        }
        static void Refine(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var hashes = new HashSet<string>();
            string reportPath = NativeSceneCollisionRebuild.ReportRoot + "/" + Path.GetFileNameWithoutExtension(path) + ".json";
            var report = JObject.Parse(File.ReadAllText(reportPath));
            try
            {
                var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).ToArray();
                foreach (var mesh in colliders.Select(c => c.sharedMesh).Where(m => m).Distinct().Where(Oversized))
                {
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) throw new InvalidDataException("Expected a scene-owned native mesh: " + mesh.name);
                    string before = NativeSceneCollisionRebuild.GeometryHash(mesh);
                    double area = Area(mesh); var bounds = mesh.bounds;
                    NativeCollisionMeshRefinement.Refine(mesh);
                    if (Math.Abs(Area(mesh) - area) > Math.Max(.001, area * .00001) || Vector3.Distance(mesh.bounds.min, bounds.min) > .001f || Vector3.Distance(mesh.bounds.max, bounds.max) > .001f)
                        throw new InvalidDataException("Refinement changed surface area/bounds: " + mesh.name);
                    string after = NativeSceneCollisionRebuild.GeometryHash(mesh); hashes.Add(after);
                    foreach (var record in report["Colliders"].OfType<JObject>().Where(r => (string)r["GeometrySha256"] == before))
                    {
                        record["BeforeRefinementSha256"] = before; record["GeometrySha256"] = after;
                        record["Vertices"] = mesh.vertexCount; record["Indices"] = mesh.triangles.Length;
                    }
                    foreach (var collider in colliders.Where(c => c.sharedMesh == mesh))
                    { collider.sharedMesh = null; collider.sharedMesh = mesh; EditorUtility.SetDirty(collider); }
                    EditorUtility.SetDirty(mesh); meshes++;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save " + path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var nativeMeshes = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).Select(c => c.sharedMesh).Distinct().ToArray();
                if (nativeMeshes.Any(Oversized)) throw new InvalidDataException("Oversized triangle survived: " + path);
                var actual = new HashSet<string>(nativeMeshes.Select(NativeSceneCollisionRebuild.GeometryHash));
                if (!hashes.IsSubsetOf(actual)) throw new InvalidDataException("Refined geometry did not survive reload: " + path);
                File.WriteAllText(reportPath, report.ToString());
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
