using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Runtime.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    // Native Terrain renders/edits foliage; the original mesh retains its surface, material and collision.
    // Details preserve density cells, not the per-blade random positions of the previous renderer.
    public static class TerrainVegetationImport
    {
        public const string ChildName = "Terrain Vegetation";
        public static string Status { get; private set; } = "";
        static Queue<string> pending;
        public static int Converted { get; private set; }
        public static int Deferred { get; private set; }
        public static bool IsRunning => pending != null;
        public static string DataPath(string tile) => "Assets/Resources/World/Tiles/" + tile + "/Vegetation.asset";

        public static string Incompatibility(JObject definition)
        {
            if ((definition["heightmap"]?["vertexShifts"] as JArray)?.Count > 0) return "Рельеф содержит горизонтальные сдвиги вершин.";
            int width = (int)definition["dimensions"]["width"], height = (int)definition["dimensions"]["height"];
            if (width != height || width < 33 || width > 4097 || ((width - 1) & (width - 2)) != 0) return "Неподдерживаемая сетка высот.";
            var detail = definition["detailAndTrees"];
            if ((string)detail?["status"] != "serializedAssetClosureExported") return "Нет исходной карты растительности.";
            if (((int?)detail["trees"]?["instanceCount"] ?? 0) != 0) return "Требуется отдельный импорт исходных деревьев.";
            foreach (var p in detail["prototypes"] ?? new JArray())
            {
                if (((long?)p["density"]?["densityTotal"] ?? 0) == 0) continue;
                if (((int?)p["serialized"]?["usePrototypeMesh"] ?? 0) != 0) return "Требуется проверка mesh-прототипа Details.";
                if ((int)p["density"]["width"] != (int)p["density"]["height"]) return "Несовместимая карта плотности.";
            }
            return null;
        }
        public static bool Attach(string tile, GameObject root, JObject definition, JToken instance, string source, string folder)
        {
            if (Incompatibility(definition) != null) return false;
            if (Quaternion.Angle(root.transform.rotation, Quaternion.identity) > .01f || (root.transform.lossyScale - Vector3.one).sqrMagnitude > .00001f)
                return false;
            var prototypes = (definition["detailAndTrees"]["prototypes"] ?? new JArray()).Where(p => (long?)p["density"]?["densityTotal"] > 0).ToArray();
            if (prototypes.Length == 0) return false;
            string path = DataPath(tile);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(ContentPaths.Resolve(path));
            if (!data)
            {
                if (File.Exists(ContentPaths.FilePath(path))) throw new IOException("Vegetation data path is occupied: " + path);
                data = BuildData(definition, prototypes, source, folder);
                data.name = tile + " Vegetation";
                EnvironmentLibraryBuilder.Folder(Path.GetDirectoryName(path));
                try { AssetDatabase.CreateAsset(data, ContentPaths.Resolve(path)); }
                catch { Object.DestroyImmediate(data); throw; }
            }
            Transform existing = root.transform.Find(ChildName);
            var child = existing ? existing.gameObject : new GameObject(ChildName);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero; child.transform.localRotation = Quaternion.identity; child.transform.localScale = Vector3.one;
            var terrain = child.GetComponent<Terrain>();
            if (!terrain) terrain = child.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.drawHeightmap = false;
            terrain.drawTreesAndFoliage = true;
            terrain.detailObjectDensity = 1;
            terrain.detailObjectDistance = (float?)instance?["terrainRenderContract"]?["serializedFields"]?["m_DetailObjectDistance"] ?? 80;
            terrain.treeDistance = 500;
            terrain.allowAutoConnect = false;
            var surface = child.GetComponent<TerrainVegetationSurface>();
            if (!surface) surface = child.AddComponent<TerrainVegetationSurface>();
            surface.Synchronize(); // Painting surface in Edit mode; original mesh collision owns gameplay.
            foreach (var grass in root.GetComponentsInChildren<WorldTerrainGrassVisibility>(true)) Object.DestroyImmediate(grass.gameObject);
            return true;
        }
        static TerrainData BuildData(JObject definition, JToken[] layers, string source, string folder)
        {
            int resolution = (int)definition["dimensions"]["width"];
            var heights = TutorialHeightmap.Read(File.ReadAllBytes(ContentPaths.FilePath(Resolve(source, folder, (string)definition["heightmap"]["path"]))), resolution);
            float highest = Math.Max(1f, heights.Cast<float>().Max());
            for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++) heights[z, x] /= highest;
            var data = new TerrainData { heightmapResolution = resolution, size = new Vector3((float)definition["scale"]["extentX"], (float)definition["scale"]["heightScale"] * highest, (float)definition["scale"]["extentZ"]) };
            try
            {
                data.SetHeights(0, 0, heights);
                int detailResolution = (int)layers[0]["density"]["width"];
                data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                data.SetDetailResolution(detailResolution, 16);
                var prototypes = new List<DetailPrototype>();
                foreach (var layer in layers)
                {
                    if ((int)layer["density"]["width"] != detailResolution) throw new InvalidDataException("Detail layers have different resolutions.");
                    string texturePath = Resolve(source, folder, (string)layer["prototypeTexture"]["path"]);
                    string assetPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), texturePath).Replace('\\', '/');
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ContentPaths.Resolve(assetPath));
                    if (!texture) throw new InvalidDataException("Missing grass texture: " + assetPath);
                    var s = layer["serialized"];
                    float a = (float)s["minWidth"], b = (float)s["maxWidth"], c = (float)s["minHeight"], d = (float)s["maxHeight"];
                    prototypes.Add(new DetailPrototype { prototypeTexture = texture, renderMode = DetailRenderMode.GrassBillboard,
                        minWidth = Mathf.Min(a, b), maxWidth = Mathf.Max(a, b), minHeight = Mathf.Min(c, d), maxHeight = Mathf.Max(c, d),
                        healthyColor = ColorValue(s["healthyColor"]), dryColor = ColorValue(s["dryColor"]), noiseSpread = (float)s["noiseSpread"],
                        noiseSeed = (int)layer["index"], positionJitter = 1, useDensityScaling = false });
                }
                data.detailPrototypes = prototypes.ToArray();
                for (int layer = 0; layer < layers.Length; layer++)
                {
                    var density = layers[layer]["density"];
                    int[,] map = ReadDensity(Resolve(source, folder, (string)density["path"]), detailResolution, (long)density["densityTotal"]);
                    if (map.Cast<int>().Max() > data.maxDetailScatterPerRes) throw new InvalidDataException("Detail density exceeds Unity instance-count capacity.");
                    data.SetDetailLayer(0, 0, layer, map);
                    var restored = data.GetDetailLayer(0, 0, detailResolution, detailResolution, layer);
                    if (!map.Cast<int>().SequenceEqual(restored.Cast<int>())) throw new InvalidDataException("Unity changed the imported density map.");
                }
                return data;
            }
            catch { Object.DestroyImmediate(data); throw; }
        }
        public static int[,] ReadDensity(string path, int resolution, long expectedCount)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(ContentPaths.FilePath(path))) || texture.width != resolution || texture.height != resolution) throw new InvalidDataException("Invalid grass density PNG.");
                var pixels = texture.GetPixels32();
                var result = new int[resolution, resolution]; long total = 0;
                for (int z = 0; z < resolution; z++) for (int x = 0; x < resolution; x++)
                { int value = pixels[(resolution - 1 - z) * resolution + x].r; result[z, x] = value; total += value; }
                if (total != expectedCount) throw new InvalidDataException("Grass density count differs from source.");
                return result;
            }
            finally { Object.DestroyImmediate(texture); }
        }
        static string Resolve(string source, string folder, string relative) => NativeContentImport.ResolveSafe(source,
            relative.StartsWith("map/", StringComparison.Ordinal) ? relative : folder + "/" + relative);
        static Color ColorValue(JToken c) => new Color((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]);

        public static bool UpgradeTile(string tile)
        {
            if (File.Exists(EnvironmentWorldWorkspace.DefinitionPath(tile))) return false;
            string folder = "map/tiles/" + tile + "/terrain";
            const string source = "Assets/Resources";
            var definition = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(source + "/" + folder + "/terrain.json")));
            if (Incompatibility(definition) != null) return false;
            var scene = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(source + "/map/tiles/" + tile + "/scene.json")));
            string prefabPath = "Assets/Resources/World/Tiles/" + tile + "/Terrain.prefab";
            var root = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(prefabPath));
            try
            {
                if (!Attach(tile, root, definition, scene["nativeTerrain"], source, folder)) return false;
                PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(prefabPath)); return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        [MenuItem("FFOne/Environment/Import Compatible Terrain Details")]
        public static void BeginAll()
        {
            if (IsRunning) return;
            pending = new Queue<string>(ContentPaths.GetDirectories("Assets/Resources/World/Tiles").Select(Path.GetFileName).OrderBy(x => x));
            Converted = Deferred = 0; EditorApplication.update += Advance;
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EnvironmentLibraryBuilder.IsRunning) return;
            try
            {
                string tile = pending.Dequeue();
                if (UpgradeTile(tile)) Converted++; else Deferred++;
                Status = $"Terrain Details: {Converted} тайлов; без преобразования {Deferred}; осталось {pending.Count}.";
                if (pending.Count == 0) { pending = null; EditorApplication.update -= Advance; AssetDatabase.SaveAssets(); Debug.Log(Status); }
            }
            catch (Exception error) { pending = null; EditorApplication.update -= Advance; Status = error.Message; Debug.LogException(error); }
        }
    }
}
