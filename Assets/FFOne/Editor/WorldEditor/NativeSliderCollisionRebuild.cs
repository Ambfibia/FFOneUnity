using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class NativeSliderCollisionRebuild
    {
        [MenuItem("FFOne/World/Rebuild Native Slider Collider")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            var visual = NativeModelAssets.Load(ProtocolSlider.Model);
            string path = AssetDatabase.GetAssetPath(visual);
            var source = NativeModelAssets.Load(ProtocolSlider.Model.Replace(".glb", ".collision.glb"));
            var filter = source.GetComponentsInChildren<MeshFilter>(true).Single();
            var original = filter.sharedMesh;
            if (original.vertexCount != 134 || original.triangles.Length != 531)
                throw new InvalidDataException("Slider differs from the accepted collision source.");
            string backup = NativeSceneCollisionRebuild.ReportRoot + "/Before/slider.prefab";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(path, backup);
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().SingleOrDefault(m => m.name == "Native slider collision");
            if (!mesh)
            {
                mesh = new Mesh { name = "Native slider collision" };
                AssetDatabase.AddObjectToAsset(mesh, path);
            }
            mesh.vertices = original.vertices.Select(filter.transform.TransformPoint).ToArray();
            mesh.triangles = original.triangles; mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
            string hash = NativeSceneCollisionRebuild.GeometryHash(mesh);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var node = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "collision");
                var collider = node.GetComponent<MeshCollider>();
                if (!collider) collider = node.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh; collider.enabled = true; collider.convex = false; collider.isTrigger = false;
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("Cannot save slider prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshCollider>(true).Single();
            if (AssetDatabase.GetAssetPath(saved.sharedMesh) != path || NativeSceneCollisionRebuild.GeometryHash(saved.sharedMesh) != hash)
                throw new InvalidDataException("Native slider geometry did not survive prefab reload.");
            File.WriteAllText(NativeSceneCollisionRebuild.ReportRoot + "/slider-result.txt",
                "PASS: native MeshCollider embedded in " + path + "; 134 vertices, 531 indices; SHA256=" + hash);
        }
    }
}
