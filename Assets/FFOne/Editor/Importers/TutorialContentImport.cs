using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    /// <summary>Bounded import of accepted tutorial map 01_01 and its exact dependencies.</summary>
    public static class TutorialContentImport
    {
        const string ScenePath = "map/tiles/map_01_01/scene.json";
        const string TerrainPath = "map/tiles/map_01_01/terrain/terrain.json";
        const string Output = "Assets/Resources/Tutorial";
        // Original tutorial runtime NPC types; IDs are independent of mesh/voice IDs.
        static readonly int[] GameplayNpcTypes = { 2669, 2670, 2671, 2672, 2673, 2674, 2675, 2676, 2677, 2678,
            2694, 2695, 2696, 2800, 2897, 2902, 2374, 2375, 2376, 2377, 2378 };
        static int[] NpcTypes() => GameplayNpcTypes.Concat(JObject.Parse(File.ReadAllText(ContentPaths.FilePath("Assets/Resources/data/tutorial/cinematics.json")))
            .Descendants().OfType<JProperty>().Where(p => p.Name == "npc_type").Select(p => (int)p.Value)).Distinct().OrderBy(id => id).ToArray();
        static JObject Read(string source, string path) => JObject.Parse(File.ReadAllText(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, path))));
        static JObject Routes(JObject xdt) => (JObject)xdt["_ffone"]!["tables"]!.Single(t => (string)t["name"] == "native_asset_routes")["value"];
        static string AbsoluteAsset(string path) => NativeContentImport.Destination + "/" + path;
        static string VoicePath(JToken row, string language)
        {
            string path = (string)row["path"];
            return (string)row["category"] == "voice" && !path.StartsWith("audio/", StringComparison.Ordinal)
                ? "audio/voice/" + language + "/" + path : path.Replace("/en/", "/" + language + "/");
        }
        static IEnumerable<string> CueNames() => TutorialStages.All.Select(s => s.Voice)
            .Concat(TutorialAudioTimeline.Cues.Values.SelectMany(c => c).Select(c => c.Cue))
            .Concat(Enum.GetValues(typeof(TutorialAuxiliarySequence)).Cast<TutorialAuxiliarySequence>().Where(s => s != TutorialAuxiliarySequence.None)
                .Select(TutorialAuxiliaryTimeline.Get).SelectMany(t => t.Actions.Concat(t.Repeating)).Where(a => a.Command == TutorialAuxiliaryCommand.Voice).Select(a => a.Text))
            .Concat(new[] { "Computress_Tut26", "Computress_Tut41", "Computress_Tut42", "Computress_Tut43", "Computress_Tut48", "Computress_Tut49", "Computress_Tut52", "Computress_Tut54", "Computress_Tut57", "Computress_Tut58", "Computress_Tut61" })
            .Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase);
        static JToken AudioRow(JObject routes, string cue)
        {
            var rows = routes["m_pAudioData"]!.Where(r => string.Equals((string)r["trueName"], cue, StringComparison.OrdinalIgnoreCase)).ToArray();
            // These three duplicate true names have exact semantic routes in the native presenter.
            string preferred = cue == "Cyberus_Landing" ? "audio/sfx/world_events/cyberus_landing.ogg"
                : cue == "FusionButtercup_Stand1" ? "fusionbuttercup/stand1.ogg"
                : cue == "HologramOff" ? "audio/sfx/environment/hologramoff.ogg" : null;
            if (preferred != null) return rows.Single(r => ((string)r["path"]).EndsWith(preferred, StringComparison.OrdinalIgnoreCase));
            if (rows.Length != 1) throw new InvalidDataException($"Tutorial cue {cue} resolves to {rows.Length} native routes.");
            return rows[0];
        }
        static JToken Npc(JObject xdt, int id) => xdt["m_pNpcTable"]!["m_pNpcData"]!.Single(r => (int)r["m_iNpcNumber"]! == id);
        static string Model(JObject xdt, JObject routes, int id)
        {
            var row = Npc(xdt, id);
            string name = (string)xdt["m_pNpcTable"]!["m_pNpcMeshData"]![(int)row["m_iMesh"]!]["m_pstrMMeshModelString"];
            return (string)routes["m_pCharacterModelData"]!.Single(m => (string)m["logicalName"] == name)["glb"];
        }
        public static List<string> Plan(string source)
        {
            var scene = Read(source, ScenePath);
            var terrain = Read(source, TerrainPath);
            var xdt = Read(source, "data/tables/xdt.json");
            var routes = Routes(xdt);
            var files = new HashSet<string>(StringComparer.Ordinal) { ScenePath, TerrainPath,
                "audio/ambient/tutorialmain_wambient_loop.ogg",
                "characters/player/items/weapon/rifle_electricgun/models/rifle_electricgun/model.glb",
                "characters/player/items/weapon/rifle_electricgun/textures/rifle_electricgun.png" };
            foreach (var model in scene["models"]!) files.Add((string)model["path"]);
            foreach (int type in NpcTypes()) files.Add(Model(xdt, routes, type));
            foreach (string path in TutorialCinematicEffects.RequiredNativePaths()) files.Add(path);
            // Buttercup Nano uses the accepted native model route, not NPC numeric equality.
            foreach (var model in routes["m_pCharacterModelData"]!.Where(m => (string)m["category"] == "nano" && ((string)m["logicalName"]).Contains("buttercup", StringComparison.OrdinalIgnoreCase))) files.Add((string)model["glb"]);
            foreach (string cue in CueNames())
            {
                var row = AudioRow(routes, cue);
                files.Add(VoicePath(row, "en"));
                string russian = VoicePath(row, "ru");
                if (File.Exists(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, russian)))) files.Add(russian);
            }
            foreach (string folder in new[] { "ui/en/gameplay/intro", "ui/en/gameplay/tutorial" })
                foreach (string file in ContentPaths.GetFiles(NativeContentImport.ResolveSafe(source, folder), "*.png"))
                    files.Add(Path.GetRelativePath(source, file).Replace('\\', '/'));
            string terrainFolder = Path.GetDirectoryName(TerrainPath)!.Replace('\\', '/');
            files.Add(terrainFolder + "/" + (string)terrain["heightmap"]!["path"]);
            foreach (var layer in terrain["splat"]!["layers"]!) files.Add((string)layer["albedo"]!["path"]);
            foreach (var weights in terrain["splat"]!["weightMaps"]!) files.Add(terrainFolder + "/" + (string)weights["path"]);
            foreach (string glb in files.Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var document = NativeGlbDocument.Read(NativeContentImport.ResolveSafe(source, glb));
                foreach (string uri in document.Json.Descendants().OfType<JProperty>().Where(p => p.Name == "uri").Select(p => (string)p.Value).Distinct())
                {
                    if (string.IsNullOrEmpty(uri) || uri.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string path = NativeContentImport.ResolveSafe(source, Path.Combine(Path.GetDirectoryName(glb)!, uri));
                    files.Add(Path.GetRelativePath(source, path).Replace('\\', '/'));
                }
            }
            return files.OrderBy(f => f, StringComparer.Ordinal).ToList();
        }
        [MenuItem("FFOne/Import Tutorial Content")]
        public static void ImportDefault() => Import(NativeContentImport.DefaultSource);
        [MenuItem("FFOne/Rebuild Tutorial Presentation")]
        public static void RebuildDerivedContent() => RebuildDerivedContent(NativeContentImport.DefaultSource);
        public static void RebuildDerivedContent(string source)
        {
            // Rebuild only the two generated containers; imported editable source assets and
            // the geometry subassets retain their GUIDs and references.
            NativeContentImport.Preflight(source, Plan(source));
            BuildWorld(source); BuildContent(source); AssetDatabase.SaveAssets();
        }
        public static void Import(string source)
        {
            var files = Plan(source);
            string preflight = NativeContentImport.Preflight(source, files);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string relative in files)
                {
                    string to = NativeContentImport.ResolveSafe(NativeContentImport.Destination, relative);
                    if (File.Exists(ContentPaths.FilePath(to))) continue;
                    Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(to)!));
                    File.Copy(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, relative)), ContentPaths.FilePath(to), false);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(ContentPaths.FilePath(Output));
            AssetDatabase.Refresh();
            if (!File.Exists(ContentPaths.FilePath(Output + "/World.prefab"))) BuildWorld(source);
            if (!File.Exists(ContentPaths.FilePath(Output + "/Content.asset"))) BuildContent(source);
            else
            {
                var existing = AssetDatabase.LoadAssetAtPath<TutorialContent>(ContentPaths.Resolve(Output + "/Content.asset"))
                    ?? throw new InvalidDataException("Existing tutorial content could not be loaded; no authored content replaced.");
                var routes = Routes(Read(source, "data/tables/xdt.json"));
                Debug.Log("FFOne tutorial audio refresh: " + TutorialAudioRefresh.Apply(existing, AudioDefinitions(routes)));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("FFOne tutorial content: " + preflight);
        }
        static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(ContentPaths.Resolve(AbsoluteAsset(path)))
            ?? throw new InvalidDataException("Native dependency failed Unity import: " + path);
        static void BuildContent(string source)
        {
            var xdt = Read(source, "data/tables/xdt.json"); var routes = Routes(xdt);
            var content = ScriptableObject.CreateInstance<TutorialContent>();
            content.World = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(Output + "/World.prefab"));
            content.ButtercupNano = Asset<GameObject>("characters/nanos/nano_buttercup/nano_buttercup.glb");
            content.Actors = NpcTypes().Select(id => {
                var row = Npc(xdt, id);
                return new TutorialActorDefinition { NpcType = id, MaxHp = (int)row["m_iHP"]!, Style = (int)row["m_iNpcStyle"]!, Class = (int)row["m_iNpcType"]!,
                    Scale = (float)row["m_fScale"]!, AttackRange = (int)row["m_iAtkRange"]! / 100f, Height = (int)row["m_iHeight"]! / 100f, Radius = (int)row["m_iRadius"]! / 100f,
                    Name = (string)xdt["m_pNpcTable"]!["m_pNpcStringData"]![(int)row["m_iNpcName"]!]["m_strName"], Model = Asset<GameObject>(Model(xdt, routes, id)) };
            }).ToArray();
            content.Audio = AudioDefinitions(routes).ToArray();
            var existing = AssetDatabase.LoadAssetAtPath<TutorialContent>(ContentPaths.Resolve(Output + "/Content.asset"));
            if (existing) { EditorUtility.CopySerialized(content, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(content); }
            else AssetDatabase.CreateAsset(content, ContentPaths.Resolve(Output + "/Content.asset"));
        }
        static IEnumerable<TutorialAudioDefinition> AudioDefinitions(JObject routes) => CueNames().Select(cue => {
            var row = AudioRow(routes, cue);
            return new TutorialAudioDefinition { Cue = cue, Category = (string)row["category"], English = Asset<AudioClip>(VoicePath(row, "en")),
                Russian = AssetDatabase.LoadAssetAtPath<AudioClip>(ContentPaths.Resolve(AbsoluteAsset(VoicePath(row, "ru")))) };
        });
        static void BuildWorld(string source)
        {
            var scene = Read(source, ScenePath);
            var models = scene["models"]!.ToDictionary(m => (string)m["id"], m => (string)m["path"]);
            var root = new GameObject("Tutorial - Tech Square");
            var meshes = new Dictionary<string, Mesh>();
            Directory.CreateDirectory(ContentPaths.FilePath(Output + "/Geometry"));
            AssetDatabase.Refresh();
            try
            {
                foreach (var visual in scene["visuals"]!)
                {
                    if (!VisualHasPresentation(scene, visual)) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(models[(string)visual["model"]]), root.transform);
                    instance.name = (string)visual["name"];
                    ApplyNativeTransform(instance.transform, visual["transform"]!);
                }
                foreach (var collider in scene["colliders"]!)
                {
                    string model = models[(string)collider["model"]];
                    int meshIndex = (int)collider["mesh"]!, primitive = (int)collider["primitive"]!;
                    string key = (string)collider["model"] + "_" + meshIndex + "_" + primitive;
                    if (!meshes.TryGetValue(key, out var mesh))
                    {
                        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ContentPaths.Resolve(Output + "/Geometry/" + key + ".asset"));
                        if (mesh) meshes.Add(key, mesh);
                        else
                        {
                        var doc = NativeGlbDocument.Read(NativeContentImport.ResolveSafe(source, model));
                        var data = doc.Json["meshes"]![meshIndex]["primitives"]![primitive];
                        mesh = new Mesh { name = key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        mesh.vertices = doc.Values((int)data["attributes"]!["POSITION"]!).Select(v => new Vector3(-v[0], v[1], v[2])).ToArray();
                        var indices = doc.Values((int)data["indices"]!).Select(v => checked((int)v[0])).ToArray();
                        for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                        mesh.triangles = indices; mesh.RecalculateBounds();
                        AssetDatabase.CreateAsset(mesh, ContentPaths.Resolve(Output + "/Geometry/" + key + ".asset")); meshes.Add(key, mesh);
                        }
                    }
                    var go = new GameObject((string)collider["name"]); go.transform.SetParent(root.transform, false);
                    ApplyNativeTransform(go.transform, collider["transform"]!);
                    var component = go.AddComponent<MeshCollider>(); component.sharedMesh = mesh;
                    // Non-convex trigger meshes are invalid Unity physics; native trigger volumes
                    // are kept as authored solid geometry only when explicitly non-trigger.
                    if ((bool?)collider["isTrigger"] == true) { component.enabled = false; }
                }
                BuildTerrain(source, scene, root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(Output + "/World.prefab"));
            }
            finally { Object.DestroyImmediate(root); }
        }
        static bool VisualHasPresentation(JObject scene, JToken visual)
        {
            var model = scene["models"]!.Single(m => (string)m["id"] == (string)visual["model"]);
            string name = ((string)visual["name"]).ToLowerInvariant(), path = ((string)model["path"]).ToLowerInvariant();
            if (path.Contains("objects/collision/default_collision_alpha/") || name.Contains("collision_alpha") && (name.StartsWith("collision_", StringComparison.Ordinal) || name.Contains("-collision_"))) return false;
            // Preserve the accepted zero-contribution identity guard, never hide by name alone.
            return !((string)model["id"] == "map-geometry-a088b9c58c7a3d5e0ba6829c216b8b23817fa2e68c774045a3d78c15622a893f"
                && path == "objects/unclassified/no/models/no/visual.glb"
                && (string)model["blake3"] == "fbd430c396babfe818319ef1c45d61c022d065b864200333bbfb3a51af5f89ee");
        }
        public static void ApplyNativeTransform(Transform transform, JToken value)
        {
            var p = value["translation"]!; var q = value["rotation"]!; var s = value["scale"]!;
            transform.localPosition = new Vector3(-(float)p[0], (float)p[1], (float)p[2]);
            transform.localRotation = new Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]);
            transform.localScale = new Vector3((float)s[0], (float)s[1], (float)s[2]);
        }
        static void BuildTerrain(string source, JObject scene, Transform parent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TerrainData>(ContentPaths.Resolve(Output + "/Geometry/Terrain.asset"));
            if (existing) { CreateTerrainInstance(scene, parent, existing); return; }
            var definition = Read(source, TerrainPath);
            string folder = Path.GetDirectoryName(TerrainPath)!.Replace('\\', '/');
            int resolution = (int)definition["dimensions"]!["width"]!;
            var heights = TutorialHeightmap.Read(File.ReadAllBytes(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, folder + "/" + (string)definition["heightmap"]!["path"]))), resolution);
            var data = new TerrainData { heightmapResolution = resolution,
                size = new Vector3((float)definition["scale"]!["extentX"], (float)definition["scale"]!["heightScale"], (float)definition["scale"]!["extentZ"]),
                alphamapResolution = (int)definition["splat"]!["resolution"]!, baseMapResolution = (int)definition["splat"]!["baseMapResolution"]! };
            data.SetHeights(0, 0, heights);
            var layers = ((JArray)definition["splat"]!["layers"]!).ToArray();
            data.terrainLayers = layers.Select((layer, i) => {
                var result = new TerrainLayer { diffuseTexture = Asset<Texture2D>((string)layer["albedo"]!["path"]),
                    tileSize = new Vector2((float)layer["tileSize"]!["x"], (float)layer["tileSize"]!["y"]) };
                AssetDatabase.CreateAsset(result, ContentPaths.Resolve(Output + "/Geometry/TerrainLayer" + i + ".terrainlayer")); return result;
            }).ToArray();
            int a = data.alphamapResolution;
            var weights = new float[a, a, layers.Length];
            foreach (var map in definition["splat"]!["weightMaps"]!)
            {
                string path = (string)map["path"];
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(ContentPaths.FilePath(NativeContentImport.ResolveSafe(source, folder + "/" + path))))) throw new InvalidDataException("Invalid terrain weight PNG: " + path);
                    var pixels = texture.GetPixels();
                    for (int layer = 0; layer < layers.Length; layer++)
                    {
                        if ((string)layers[layer]["weight"]!["mapPath"] != path) continue;
                        int channel = (int)layers[layer]["weight"]!["channelIndex"]!;
                        for (int y = 0; y < a; y++) for (int x = 0; x < a; x++) weights[y, x, layer] = pixels[y * a + x][channel];
                    }
                }
                finally { Object.DestroyImmediate(texture); }
            }
            data.SetAlphamaps(0, 0, weights);
            AssetDatabase.CreateAsset(data, ContentPaths.Resolve(Output + "/Geometry/Terrain.asset"));
            CreateTerrainInstance(scene, parent, data);
        }
        static void CreateTerrainInstance(JObject scene, Transform parent, TerrainData data)
        {
            var go = Terrain.CreateTerrainGameObject(data); go.name = "Tutorial terrain"; go.transform.SetParent(parent, false);
            var instance = scene["nativeTerrain"]!;
            ApplyNativeTransform(go.transform, instance["transform"]!);
            // The terrain is a child of the tile root; static scene placements already include it.
            foreach (var node in instance["rootChain"]!["nodes"]!)
            {
                var p = node["nativeLocalTransform"]!["translation"]!;
                go.transform.localPosition += new Vector3(-(float)p[0], (float)p[1], (float)p[2]);
            }
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(ContentPaths.Resolve(Output + "/Geometry/TerrainMaterial.mat"));
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, ContentPaths.Resolve(Output + "/Geometry/TerrainMaterial.mat")); }
                go.GetComponent<Terrain>().materialTemplate = material;
            }
        }
    }
}
