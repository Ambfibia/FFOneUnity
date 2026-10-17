using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class TerrainTreeAuthoring
    {
        public static bool TryTransform(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, Terrain terrain, out TreeInstance instance)
        {
            instance = default;
            if (!terrain || !terrain.terrainData || Quaternion.Angle(terrain.transform.rotation, Quaternion.identity) > .01f ||
                (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > .00001f) return false;
            if (Vector3.Dot(worldRotation * Vector3.up, Vector3.up) < .99999f || worldScale.x <= 0 || worldScale.y <= 0 ||
                Mathf.Abs(worldScale.x - worldScale.z) > .0001f * Mathf.Max(1, worldScale.x)) return false;
            Vector3 size = terrain.terrainData.size, p = worldPosition - terrain.transform.position;
            p = new Vector3(p.x / size.x, p.y / size.y, p.z / size.z);
            if (p.x < 0 || p.x > 1 || p.z < 0 || p.z > 1 || p.y < 0 || p.y > 1) return false;
            instance = new TreeInstance { position = p, widthScale = worldScale.x, heightScale = worldScale.y,
                rotation = Mathf.Repeat(worldRotation.eulerAngles.y, 360) * Mathf.Deg2Rad, color = Color.white, lightmapColor = Color.white };
            return true;
        }
        [MenuItem("FFOne/Environment/Convert Selected Trees To Terrain Trees")]
        public static void ConvertSelected()
        {
            var selected = Selection.gameObjects;
            var terrain = selected.Select(g => g.GetComponent<Terrain>()).FirstOrDefault(t => t);
            if (!terrain) throw new InvalidOperationException("Выберите Terrain Vegetation и экземпляры деревьев в сцене.");
            Convert(selected.Where(g => g != terrain.gameObject).ToArray(), terrain);
        }
        public static int Convert(GameObject[] objects, Terrain terrain)
        {
            if (objects.Length == 0) return 0;
            var library = EnvironmentLibraryBuilder.Load();
            if (!library) throw new InvalidOperationException("Build the environment library first.");
            var treeGuids = new HashSet<string>(library.Entries.Where(e => e.Category == "Vegetation/Trees").Select(e => e.PrefabGuid));
            var plans = new List<(GameObject Instance, GameObject Source, MeshRenderer Renderer, TreeInstance Tree)>();
            foreach (var go in objects)
            {
                if (!go.scene.IsValid() || go.scene != terrain.gameObject.scene) throw new InvalidOperationException("Trees and Terrain must belong to the same editing scene.");
                string path = ContentPaths.Legacy(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go));
                if (string.IsNullOrEmpty(path) || !treeGuids.Contains(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path))) || PrefabUtility.GetNearestPrefabInstanceRoot(go) != go)
                    throw new InvalidOperationException("Требуется корень экземпляра дерева из библиотеки окружения: " + go.name);
                if (!TryTransform(go.transform.position, go.transform.rotation, go.transform.lossyScale, terrain, out var tree))
                    throw new InvalidOperationException("Terrain Trees не сохраняет этот наклон, масштаб или положение: " + go.name);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
                var renderers = source.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length != 1 || source.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshRenderer || c is MeshFilter || c is FFOne.World.WorldModelBasis)))
                    throw new InvalidOperationException("Дерево содержит дополнительные компоненты или несколько визуальных частей: " + go.name);
                var renderer = renderers[0];
                Matrix4x4 relative = source.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) if (Mathf.Abs(relative[i] - Matrix4x4.identity[i]) > .0001f)
                    throw new InvalidOperationException("Прототип дерева требует преобразования локальной геометрии: " + go.name);
                if (!renderer.GetComponent<MeshFilter>() || !renderer.GetComponent<MeshFilter>().sharedMesh)
                    throw new InvalidOperationException("Tree mesh missing.");
                // Authored renderer/material overrides must not disappear during conversion.
                var actual = go.GetComponentsInChildren<MeshRenderer>(true);
                if (actual.Length != 1 || !actual[0].GetComponent<MeshFilter>() || !actual[0].sharedMaterials.SequenceEqual(renderer.sharedMaterials) ||
                    actual[0].GetComponent<MeshFilter>().sharedMesh != renderer.GetComponent<MeshFilter>().sharedMesh || go.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Экземпляр имеет индивидуальную геометрию, материалы или коллизии: " + go.name);
                var actualRelative = go.transform.worldToLocalMatrix * actual[0].transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) if (Mathf.Abs(actualRelative[i] - Matrix4x4.identity[i]) > .0001f)
                    throw new InvalidOperationException("Экземпляр имеет индивидуальное смещение визуальной части: " + go.name);
                if (!actual[0].enabled || !actual[0].gameObject.activeInHierarchy || go.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshRenderer || c is MeshFilter || c is FFOne.World.WorldModelBasis)))
                    throw new InvalidOperationException("Экземпляр дерева содержит скрытые части или дополнительные компоненты: " + go.name);
                plans.Add((go, source, renderer, tree));
            }
            var data = terrain.terrainData;
            if (ContentPaths.Legacy(AssetDatabase.GetAssetPath(data)).StartsWith("Assets/Resources/World/Tiles/", StringComparison.Ordinal))
                throw new InvalidOperationException("Откройте ячейку через редактор окружения: для изменения состава деревьев нужны данные редактируемой сцены.");
            var prototypes = data.treePrototypes.ToList(); var trees = data.treeInstances.ToList();
            foreach (var plan in plans)
            {
                string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(plan.Source))));
                string path = EnvironmentAssetRules.Root + "/Vegetation/TreePrototypes/" + guid + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
                if (!prefab)
                {
                    EnvironmentLibraryBuilder.Folder(Path.GetDirectoryName(path));
                    var root = new GameObject(plan.Source.name);
                    try
                    {
                        root.AddComponent<MeshFilter>().sharedMesh = plan.Renderer.GetComponent<MeshFilter>().sharedMesh;
                        var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterials = plan.Renderer.sharedMaterials;
                        renderer.shadowCastingMode = plan.Renderer.shadowCastingMode; renderer.receiveShadows = plan.Renderer.receiveShadows;
                        prefab = PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(path));
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                int index = prototypes.FindIndex(p => p.prefab == prefab);
                if (index < 0) { index = prototypes.Count; prototypes.Add(new TreePrototype { prefab = prefab }); }
                var tree = plan.Tree; tree.prototypeIndex = index; trees.Add(tree);
            }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Convert environment trees");
            Undo.RegisterCompleteObjectUndo(data, "Convert environment trees");
            data.treePrototypes = prototypes.ToArray();
            data.SetTreeInstances(trees.ToArray(), false); // Preserve source Y instead of snapping tree pivots to the surface.
            foreach (var plan in plans) Undo.DestroyObjectImmediate(plan.Instance);
            Undo.CollapseUndoOperations(group);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
            return plans.Count;
        }
    }
}
