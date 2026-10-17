using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    /// <summary>Imports the accepted map registry; source files and Unity edits are preflighted before writes.</summary>
    [InitializeOnLoad]
    public static class WorldContentImport
    {
        const string Output = "Assets/Resources/World";
        const string Job = "FFOne.WorldImport.";
        public static string LastStatus { get => SessionState.GetString(Job + "Status", "Not imported"); private set => SessionState.SetString(Job + "Status", value); }
        static readonly Queue<JObject> Pending = new Queue<JObject>();
        static WorldCatalog catalog;
        static readonly List<WorldTileReference> references = new List<WorldTileReference>();
        static readonly List<WorldAtmosphere> atmospheres = new List<WorldAtmosphere>();
        static string source;
        static WorldContentImport() { if (SessionState.GetString(Job + "Source", "") != "") EditorApplication.update += Resume; }
        static JObject Read(string root, string path) => JObject.Parse(File.ReadAllText(ContentPaths.FilePath(NativeContentImport.ResolveSafe(root, path))));
        static string Digest(string path) { using var sha = SHA256.Create(); using var file = File.OpenRead(ContentPaths.FilePath(path)); return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        static string AssetPath(string relative) => NativeContentImport.Destination + "/" + relative;
        public static List<string> Plan(string root)
        {
            var registry = Read(root, "map/catalog.json");
            var files = new HashSet<string>(StringComparer.Ordinal) { "map/catalog.json" };
            foreach (var property in registry.Descendants().OfType<JProperty>().Where(p => p.Name == "path"))
                files.Add((string)property.Value);
            foreach (var tile in registry["tiles"]!)
            {
                var manifest = Read(root, (string)tile["manifest"]!["path"]);
                foreach (var file in manifest["files"]!) files.Add((string)file["path"]);
            }
            foreach (string glb in files.Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var document = NativeGlbDocument.Read(NativeContentImport.ResolveSafe(root, glb));
                foreach (string uri in document.Json.Descendants().OfType<JProperty>().Where(p => p.Name == "uri").Select(p => (string)p.Value).Distinct())
                {
                    if (string.IsNullOrEmpty(uri) || uri.StartsWith("data:", StringComparison.Ordinal)) continue;
                    files.Add(Path.GetRelativePath(root, NativeContentImport.ResolveSafe(root, Path.Combine(Path.GetDirectoryName(glb)!, uri))).Replace('\\', '/'));
                }
            }
            return files.OrderBy(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(p => p, StringComparer.Ordinal).ToList();
        }
        [MenuItem("FFOne/World/Import Entire Native World")]
        public static void ImportDefault() => Import(NativeContentImport.DefaultSource);
        [MenuItem("FFOne/World/Retry Incomplete Import")]
        public static void RetryIncompleteImport()
        {
            if (SessionState.GetString(Job + "Source", "") != "") throw new InvalidOperationException("World import is already running: " + LastStatus);
            string root = NativeContentImport.DefaultSource;
            var existing = AssetDatabase.LoadAssetAtPath<WorldCatalog>(ContentPaths.Resolve(Output + "/Catalog.asset"));
            if(existing && existing.Tiles.Any(t=>!string.IsNullOrEmpty(t.SceneAddress)))
                throw new InvalidOperationException("Authored scene cells are published. Retry source import in a staging project to protect their dependencies.");
            if (!existing || existing.SourceCatalogSha256 != Digest(NativeContentImport.ResolveSafe(root, "map/catalog.json")))
                throw new InvalidOperationException("No matching partial world import exists. Import Entire Native World first.");
            SessionState.SetString(Job + "Source", root);
            SessionState.SetString(Job + "Hash", existing.SourceCatalogSha256);
            SessionState.SetInt(Job + "Files", existing.SourceFiles);
            SessionState.SetString(Job + "Bytes", existing.SourceBytes.ToString());
            SessionState.SetString(Job + "Stage", "samplers");
            SessionState.SetBool(Job + "Fresh", false);
            LastStatus = "Resuming incomplete world import after dependency preparation.";
            EditorApplication.update -= Resume; EditorApplication.update += Resume;
        }
        public static void Import(string root)
        {
            EnsureWholeWorldImportAllowed();
            if (SessionState.GetString(Job + "Source", "") != "") throw new InvalidOperationException("World import is already running: " + LastStatus);
            source = root;
            var files = Plan(root);
            LastStatus = NativeContentImport.Preflight(root, files);
            Debug.Log("World preflight: " + LastStatus);
            SessionState.SetString(Job + "Source", root);
            SessionState.SetString(Job + "Stage", "samplers");
            SessionState.SetString(Job + "Hash", Digest(NativeContentImport.ResolveSafe(root, "map/catalog.json")));
            SessionState.SetInt(Job + "Files", files.Count);
            SessionState.SetString(Job + "Bytes", files.Sum(p => new FileInfo(ContentPaths.FilePath(NativeContentImport.ResolveSafe(root, p))).Length).ToString());
            SessionState.SetBool(Job + "Fresh", true);
            EditorApplication.update -= Resume; EditorApplication.update += Resume;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string file in files)
                {
                    string destination = NativeContentImport.ResolveSafe(NativeContentImport.Destination, file);
                    if (File.Exists(ContentPaths.FilePath(destination))) continue;
                    Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(destination)!));
                    File.Copy(ContentPaths.FilePath(NativeContentImport.ResolveSafe(root, file)), ContentPaths.FilePath(destination), false);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        public static void EnsureWholeWorldImportAllowed()
        {
            var accepted = AssetDatabase.LoadAssetAtPath<WorldCatalog>(ContentPaths.Resolve(Output + "/Catalog.asset"));
            if (accepted && accepted.Tiles.Length > 0)
                throw new InvalidOperationException("Accepted world is protected. Import changed source in a staging project; the live catalog and authored cell scenes must not be reset.");
        }
        static void Resume()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                source = SessionState.GetString(Job + "Source", "");
                if (source == "") { EditorApplication.update -= Resume; return; }
                if (Digest(NativeContentImport.ResolveSafe(source, "map/catalog.json")) != SessionState.GetString(Job + "Hash", ""))
                    throw new InvalidDataException("Source catalog changed during the import; restart after reviewing the source edits.");
                if (SessionState.GetString(Job + "Stage", "") == "samplers")
                {
                    LastStatus = "Applying accepted image samplers; the job resumes after Unity reloads.";
                    // Persist the next stage before a synchronous import can reload this assembly.
                    SessionState.SetString(Job + "Stage", "building");
                    ConfigureModelSamplers(source, Plan(source));
                    return;
                }
                EditorApplication.update -= Resume;
                BeginBuild();
            }
            catch (Exception error) { Failed(error); }
        }
        static void BeginBuild()
        {
            // Dependencies precede models. Refresh retries previously absent textures without changing authored GLBs.
            EnsureFolder(Output + "/Tiles");
            catalog = AssetDatabase.LoadAssetAtPath<WorldCatalog>(ContentPaths.Resolve(Output + "/Catalog.asset"));
            if (!catalog) { catalog = ScriptableObject.CreateInstance<WorldCatalog>(); AssetDatabase.CreateAsset(catalog, ContentPaths.Resolve(Output + "/Catalog.asset")); }
            catalog.SourceCatalogSha256 = SessionState.GetString(Job + "Hash", "");
            catalog.SourceFiles = SessionState.GetInt(Job + "Files", 0);
            catalog.SourceBytes = long.Parse(SessionState.GetString(Job + "Bytes", "0"));
            references.Clear(); atmospheres.Clear();
            if (!SessionState.GetBool(Job + "Fresh", true)) { references.AddRange(catalog.Tiles); atmospheres.AddRange(catalog.Atmospheres); }
            else { catalog.Tiles = Array.Empty<WorldTileReference>(); catalog.Atmospheres = Array.Empty<WorldAtmosphere>(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); SessionState.SetBool(Job + "Fresh", false); }
            Pending.Clear();
            foreach (var reference in Read(source, "map/catalog.json")["tiles"]!)
            {
                if (!references.Any(r => r.Id == (string)reference["tileId"])) Pending.Enqueue(Read(source, (string)reference["manifest"]!["path"]));
            }
            EditorApplication.update -= Advance;
            EditorApplication.update += Advance;
            LastStatus = $"Prepared {catalog.SourceFiles} source files; building {Pending.Count} world tiles.";
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (Pending.Count > 0)
                {
                    var manifest = Pending.Dequeue(); BuildTile(manifest);
                    catalog.Tiles = references.ToArray(); catalog.Atmospheres = atmospheres.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
                    LastStatus = $"World tiles built {references.Count}/170; remaining {Pending.Count}.";
                    if (references.Count % 10 == 0) { AssetDatabase.SaveAssets(); Debug.Log(LastStatus); }
                    return;
                }
                catalog.Tiles = references.ToArray(); catalog.Atmospheres = atmospheres.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
                LastStatus = $"Complete: {references.Count} world tiles, {catalog.SourceFiles} accepted source files, {catalog.SourceBytes} bytes.";
                SessionState.EraseString(Job + "Source"); SessionState.EraseString(Job + "Stage");
                Debug.Log(LastStatus); EditorApplication.update -= Advance;
            }
            catch (Exception error) { Failed(error); }
        }
        static void Failed(Exception error)
        {
            LastStatus = "FAILED: " + error; Pending.Clear(); SessionState.EraseString(Job + "Source");
            EditorApplication.update -= Advance; EditorApplication.update -= Resume; Debug.LogException(error);
        }
        static void ConfigureModelSamplers(string root, List<string> files)
        {
            var models = files.Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string model in models)
                WorldModelTextureAssets.Prepare(AssetPath(model), NativeGlbDocument.Read(NativeContentImport.ResolveSafe(root, model)).Json);
            AssetDatabase.SaveAssets();
            LastStatus = $"Prepared {WorldModelTextureAssets.PreparedResources} exact texture resources; importing {models.Length} models.";
            Debug.Log(LastStatus);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string model in models) AssetDatabase.ImportAsset(ContentPaths.Resolve(AssetPath(model)), ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
        }
        static T Asset<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(ContentPaths.Resolve(AssetPath(path)))
            ?? throw new InvalidDataException("World dependency failed to import: " + path);
        static void BuildTile(JObject manifest)
        {
            string id = (string)manifest["id"], folder = Output + "/Tiles/" + id;
            if (File.Exists(ContentPaths.FilePath(folder + "/Content.asset")))
                throw new IOException("Accepted world cell is protected from reimport: " + id + ". Import changed source into a staging project and review the differences first.");
            EnsureFolder(folder);
            var scene = Read(source, (string)manifest["scene"]!["path"]);
            var behaviours = Read(source, (string)manifest["behaviour"]!["path"]);
            var collisionSources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var owner in Read(source, "map/tiles/" + id + "/objects.json")["objects"]!)
                foreach (var geometry in ((JObject)owner["sourceGeometry"]!).Properties())
                    collisionSources[(string)owner["sourceNode"] + "|" + geometry.Name] = (string)geometry.Value;
            var modelDefinitions = scene["models"]!.ToDictionary(m => (string)m["id"], m => m);
            var modelPaths = modelDefinitions.ToDictionary(m => m.Key, m => (string)m.Value["path"]);
            var models = new Dictionary<string, GameObject>();
            GameObject Model(string key)
            {
                if (!models.TryGetValue(key, out var model)) { model = FFOne.Content.NativeModelAssets.Resolve(Asset<GameObject>(modelPaths[key])); models.Add(key, model); }
                return model;
            }
            var tile = ScriptableObject.CreateInstance<WorldTileContent>(); tile.Id = id;
            tile.Behaviour = Asset<TextAsset>((string)manifest["behaviour"]!["path"]);
            var placements = new List<WorldPlacement>();
            foreach (var visual in scene["visuals"]!)
            {
                string path = modelPaths[(string)visual["model"]], name = (string)visual["name"];
                string lowerName = name.ToLowerInvariant();
                if (path.Contains("objects/collision/default_collision_alpha/", StringComparison.Ordinal) ||
                    lowerName.Contains("collision_alpha", StringComparison.Ordinal) && (lowerName.StartsWith("collision_", StringComparison.Ordinal) || lowerName.Contains("-collision_", StringComparison.Ordinal))) continue;
                var modelDefinition = modelDefinitions[(string)visual["model"]];
                if ((string)modelDefinition["id"] == "map-geometry-a088b9c58c7a3d5e0ba6829c216b8b23817fa2e68c774045a3d78c15622a893f"
                    && path == "objects/unclassified/no/models/no/visual.glb" && (string)modelDefinition["blake3"] == "fbd430c396babfe818319ef1c45d61c022d065b864200333bbfb3a51af5f89ee") continue;
                var placement = new WorldPlacement { Name = name, Model = Model((string)visual["model"]), LegacyId = Legacy(id, (string)visual["sourceModelPath"]), SourceNode = Node(name),
                    CpuPresentationEnd = (int?)visual["legacyLayer"] == 12 && AuditedSmallStuff(path) ? 130 : 0 };
                Pose(visual["transform"]!, out placement.Position, out placement.Rotation, out placement.Scale); placements.Add(placement);
            }
            tile.Visuals = placements.ToArray();
            var colliders = new List<WorldCollider>();
            var collisionMeshes = new Dictionary<string, Mesh[]>();
            foreach (var definition in scene["colliders"]!)
            {
                string key = (string)definition["model"];
                int vertices = (int)definition["expectedVertexCount"], indices = (int)definition["expectedIndexCount"];
                if (!collisionMeshes.TryGetValue(key, out var availableMeshes))
                    collisionMeshes.Add(key, availableMeshes = AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(AssetPath(modelPaths[key]))).OfType<Mesh>().ToArray());
                var mesh = availableMeshes
                    .FirstOrDefault(m => m.vertexCount == vertices && m.GetIndexCount(0) == indices)
                    ?? throw new InvalidDataException("Missing exact collider mesh: " + id + " " + key);
                string name = (string)definition["name"];
                var collider = new WorldCollider { Name = name, Mesh = EnvironmentSharedAssets.PublishedMesh(mesh), Trigger = (bool?)definition["isTrigger"] ?? false, SourceNode = Node(name) };
                if (collisionSources.TryGetValue(collider.SourceNode + "|" + key, out var originalRoute)) collider.LegacyId = Legacy(id, originalRoute);
                Pose(definition["transform"]!, out collider.Position, out collider.Rotation, out collider.Scale); colliders.Add(collider);
            }
            tile.Colliders = colliders.ToArray();
            if (scene["nativeTerrain"] != null)
            {
                var terrain = WorldTerrainImport.Build(source, scene, folder + "/Terrain");
                try
                {
                    var environment = terrain.GetComponent<FFOne.Runtime.World.WorldTerrainEnvironment>();
                    if (environment) atmospheres.Add(new WorldAtmosphere { Grid = environment.Grid, FogDepth = environment.FogDepth,
                        Fog = environment.FogColor, Sky = environment.SkyColor, Light = environment.LightColor });
                    tile.Terrain = PrefabUtility.SaveAsPrefabAsset(terrain, ContentPaths.Resolve(folder + "/Terrain.prefab"));
                }
                finally { Object.DestroyImmediate(terrain); }
                tile.Environment = Asset<TextAsset>((string)scene["nativeTerrain"]!["environment"]!["path"]);
            }
            string contentPath = folder + "/Content.asset";
            var existing = AssetDatabase.LoadAssetAtPath<WorldTileContent>(ContentPaths.Resolve(contentPath));
            if (existing) { EditorUtility.CopySerialized(tile, existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(tile); }
            else AssetDatabase.CreateAsset(tile, ContentPaths.Resolve(contentPath));
            int x = (int)manifest["grid"]![0], z = (int)manifest["grid"]![1];
            references.Add(new WorldTileReference { Id = id, Resource = "World/Tiles/" + id + "/Content", X = x, Z = z,
                Minimum = new Vector3(x * 512, -10000, z * 512), Maximum = new Vector3((x + 1) * 512, 10000, (z + 1) * 512),
                Visuals = tile ? tile.Visuals.Length : placements.Count, Colliders = colliders.Count,
                OverflowCenters = placements.Where(p => p.Position.x < x * 512 || p.Position.x > (x + 1) * 512 || p.Position.z < z * 512 || p.Position.z > (z + 1) * 512)
                    .Select(p => new Vector2(p.Position.x, p.Position.z)).ToArray(),
                Animations = ((JArray)behaviours["animations"]).Count, Effects = ((JArray)behaviours["effectEmitters"]).Count });
        }
        static string Legacy(string id, string path)
        {
            var match = Regex.Match(path ?? "", @"/[vc]-(\d{5})-");
            return match.Success ? "static-" + id + "-" + match.Groups[1].Value : "";
        }
        static bool AuditedSmallStuff(string path) =>
            path == "objects/nature/etc_tree_07_variant_0002/models/etc_tree_07_variant_0005/visual.glb" ||
            path == "objects/nature/etc_tree_10/models/etc_tree_07_variant_0004/visual.glb" ||
            Regex.IsMatch(path, @"^objects/nature/15_default_wd_dldt_darktree_01_14/models/wd_dldt_darktree_bridge_01_15_default(?:_variant_\d{4})?/visual\.glb$") ||
            Regex.IsMatch(path, @"^objects/nature/16_default_wd_dldt_darktree_01_15/models/wd_dldt_darktree_bridge_01_16_default(?:_variant_\d{4})?/visual\.glb$");
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path))) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/'); EnsureFolder(parent);
            ContentPaths.CreateFolder(parent, Path.GetFileName(path));
        }
        static string Node(string name) { var match = Regex.Match(name ?? "", @"\[([^\]]+#\d+) (?:visual|collision)\]"); return match.Success ? match.Groups[1].Value : ""; }
        static void Pose(JToken value, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var p = value["translation"]!; var q = value["rotation"]!; var s = value["scale"]!;
            position = new Vector3(-(float)p[0], (float)p[1], (float)p[2]);
            rotation = new Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]);
            scale = new Vector3((float)s[0], (float)s[1], (float)s[2]);
        }
    }
}
