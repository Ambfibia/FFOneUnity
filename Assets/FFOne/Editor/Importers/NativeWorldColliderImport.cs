using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class NativeWorldColliderImport
    {
        public const string Root = "Assets/FFOne/Content/Environment/Technical/Collision/Cooked";

        // The accepted Rust ExactTriangleMesh consumer restores the opposite
        // winding from the renderable GLB. Keep that physics copy separate from
        // shared visual/publication meshes, which can have other consumers.
        public static Mesh Publish(Mesh source, bool water = false)
        {
            if (!source || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long local))
                throw new InvalidDataException("A persistent source collider mesh is required.");
            var indices = source.triangles;
            bool reverse = true;
            if (water)
            {
                // Water sidecars already include final-native-order variants.
                // Match NativeWaterSurface cooking rather than inverting them.
                var vertices = source.vertices;
                double area = 0;
                for (int i = 0; i < indices.Length; i += 3)
                    area += Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).y;
                if (double.IsNaN(area) || double.IsInfinity(area) || Math.Abs(area) <= double.Epsilon)
                    throw new InvalidDataException("Water collider has no finite horizontal surface area: " + source.name);
                reverse = area < 0;
            }
            if (reverse)
                for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            string path = Root + "/" + guid + "_" + local + (water ? "_water" : "") + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                if (!existing.vertices.SequenceEqual(source.vertices) || !existing.triangles.SequenceEqual(indices))
                    throw new InvalidDataException("Cooked collider differs from its source; preserve and review authored changes: " + path);
                if (existing.name != source.name + " PhysX") { existing.name = source.name + " PhysX"; EditorUtility.SetDirty(existing); }
                return existing;
            }
            EnvironmentLibraryBuilder.Folder(Root);
            var cooked = Object.Instantiate(source);
            cooked.name = source.name + " PhysX";
            cooked.triangles = indices;
            AssetDatabase.CreateAsset(cooked, path);
            cooked.name = source.name + " PhysX"; EditorUtility.SetDirty(cooked);
            return cooked;
        }

        [MenuItem("FFOne/Tutorial/Repair Authored Collision Winding")]
        public static void RepairTutorial()
        {
            const string cell = "Assets/FFOne/Worlds/Overworld/Cells/map_01_01";
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>(cell + "/Imported/Content.asset");
            var document = JObject.Parse(File.ReadAllText(cell + "/Source/scene.json"));
            var paths = document["models"].ToDictionary(m => (string)m["id"], m => ContentPaths.Resolve("Assets/Resources/" + (string)m["path"]));
            var sources = new Dictionary<string, Mesh>();
            var waterSources = new HashSet<string>();
            var modelRoots = document["models"].ToDictionary(m => (string)m["id"], m => (string)m["rootName"]);
            foreach (var definition in document["colliders"])
            {
                var meshes = AssetDatabase.LoadAllAssetsAtPath(paths[(string)definition["model"]]).OfType<Mesh>();
                var mesh = meshes.Single(m => m.vertexCount == (int)definition["expectedVertexCount"] && m.GetIndexCount(0) == (uint)definition["expectedIndexCount"]);
                sources.Add((string)definition["name"], mesh);
                if (modelRoots[(string)definition["model"]] is "ffWater" or "ffPoison") waterSources.Add((string)definition["name"]);
            }
            // Check every placement before changing any asset or scene.
            foreach (var collider in content.Colliders)
            {
                if (!sources.TryGetValue(collider.Name, out var source) || !collider.Mesh ||
                    !collider.Mesh.vertices.SequenceEqual(source.vertices))
                    throw new InvalidDataException("Authored collider geometry requires review: " + collider.Name);
                var restored = source.triangles;
                for (int i = 0; i < restored.Length; i += 3) (restored[i + 1], restored[i + 2]) = (restored[i + 2], restored[i + 1]);
                if (!collider.Mesh.triangles.SequenceEqual(source.triangles) && !collider.Mesh.triangles.SequenceEqual(restored))
                    throw new InvalidDataException("Authored collider indices require review: " + collider.Name);
            }
            string scenePath = cell + "/map_01_01.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save the tutorial cell before repairing its collision.");
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var placements = content.Colliders.ToDictionary(c => c.Name);
                foreach (var collider in content.Colliders) collider.Mesh = Publish(sources[collider.Name], waterSources.Contains(collider.Name));
                int count = 0;
                foreach (var collider in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)))
                    if (placements.TryGetValue(collider.name, out var placement) && collider.sharedMesh != placement.Mesh)
                    { collider.sharedMesh = placement.Mesh; EditorUtility.SetDirty(collider); count++; }
                EditorUtility.SetDirty(content);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"Tutorial collision: {content.Colliders.Length} placements cooked; {count} scene colliders updated.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
