using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    public static class EnvironmentCellBinding
    {
        public static EnvironmentCellBehaviour Configure(Scene scene, string tile)
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>(ContentPaths.Resolve("Assets/Resources/World/Tiles/" + tile + "/Content.asset"));
            if (!content) throw new InvalidOperationException("Missing world cell " + tile);
            var roots = scene.GetRootGameObjects();
            var behaviour = roots.SelectMany(r => r.GetComponentsInChildren<EnvironmentCellBehaviour>(true)).FirstOrDefault();
            if (behaviour && behaviour.Definition) return behaviour;
            if (!behaviour)
            {
                var root = new GameObject("Environment Behaviour"); SceneManager.MoveGameObjectToScene(root, scene);
                behaviour = root.AddComponent<EnvironmentCellBehaviour>();
            }
            // Map placements have source-node IDs in their names; do not associate colliders by proximity.
            var visuals = roots.FirstOrDefault(r => r.name == "Environment");
            var collision = roots.FirstOrDefault(r => r.name == "Collision");
            var bindings = new List<EnvironmentCellBehaviour.Binding>();
            Dictionary<string, Transform> Children(GameObject root) => root ? root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First()) : new Dictionary<string, Transform>();
            var visualNames = Children(visuals); var colliderNames = Children(collision);
            // Original collider children move into their owning compound prefab.
            foreach (var pair in visualNames) if (!colliderNames.ContainsKey(pair.Key)) colliderNames.Add(pair.Key, pair.Value);
            foreach (var placement in content.Visuals)
                if (visualNames.TryGetValue(placement.Name, out var target)) bindings.Add(new EnvironmentCellBehaviour.Binding { LegacyId = placement.LegacyId, SourceNode = placement.SourceNode, Target = target });
            foreach (var placement in content.Colliders)
                if (colliderNames.TryGetValue(placement.Name, out var target))
                {
                    bindings.Add(new EnvironmentCellBehaviour.Binding { LegacyId = placement.LegacyId, SourceNode = placement.SourceNode, Target = target });
                    var collider = target.GetComponent<MeshCollider>();
                    if (collider && collider.sharedMesh && ContentPaths.Legacy(AssetDatabase.GetAssetPath(collider.sharedMesh)).StartsWith("Assets/Resources/objects/", StringComparison.Ordinal))
                    { collider.sharedMesh = placement.Mesh; EditorUtility.SetDirty(collider); }
                }
            behaviour.TileId = tile; behaviour.Bindings = bindings.ToArray();
            behaviour.View = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            EditorUtility.SetDirty(behaviour); EditorSceneManager.MarkSceneDirty(scene);
            return behaviour;
        }
    }
}
