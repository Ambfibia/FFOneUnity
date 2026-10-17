using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    /// <summary>Read-only geometry anchors for the existing scene-only player acceptance.</summary>
    public static class InstanceSceneViewpoints
    {
        public static void Export()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this diagnostic in batch mode.");
            var catalog = ContentAssets.Load<WorldCatalog>("World/Catalog");
            if (!catalog) throw new InvalidDataException("Published world catalog is missing.");
            var travel = WorldTravelCatalog.Load();
            var maps = travel.Maps;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings) throw new InvalidDataException("Addressable settings are missing.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var active = SceneManager.GetActiveScene();
            var results = new JArray();
            var cells = new Dictionary<string, JObject>();
            foreach (int map in new[] { 146, 147, 148, 149, 151 })
            {
                if (!maps.TryGetValue(map, out var grid)) throw new InvalidDataException("Missing original map " + map);
                var tile = catalog.Tiles.Single(t => t.X == grid.X && t.Z == grid.Z);
                if (cells.TryGetValue(tile.SceneAddress, out var cached))
                {
                    var copy = (JObject)cached.DeepClone(); copy["map"] = map; results.Add(copy); continue;
                }
                var authoredAnchors = travel.Warps.Where(w => maps.TryGetValue(w.Map, out var location) && location.X == grid.X && location.Z == grid.Z)
                    .OrderBy(w => w.Id).ToArray();
                if (authoredAnchors.Length == 0) throw new InvalidDataException("No original warp destination supplies a height reference for " + tile.Id);
                var anchorPositions = authoredAnchors.Select(w => WorldCoordinates.FromServer(w.X, w.Y, w.Z)).ToArray();
                float minimumHeight = anchorPositions.Min(p => p.y) - 60, maximumHeight = anchorPositions.Max(p => p.y) + 60;
                float HeightDistance(Vector3 p) => anchorPositions.Min(a => Mathf.Abs(a.y - p.y));
                string path = AssetDatabase.GUIDToAssetPath(tile.SceneAddress);
                if (string.IsNullOrEmpty(path))
                {
                    var entry = settings.groups.Where(g => g).SelectMany(g => g.entries)
                        .SingleOrDefault(e => e.address == tile.SceneAddress);
                    path = entry?.AssetPath;
                }
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Cannot resolve published scene " + tile.SceneAddress);
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    var surfaces = new List<Surface>();
                    foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)))
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !renderer.GetComponent<MeshFilter>()?.sharedMesh) continue;
                        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                        surfaces.Add(new Surface { Renderer = renderer, Bounds = renderer.bounds, Name = Hierarchy(renderer.transform),
                            Vertices = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray(), Indices = mesh.triangles });
                    }
                    var seeds = new List<(Surface Surface, Vector3 Point, int Triangle, float Area)>();
                    int excludedTerrain = 0, upwardBeforeHeightBand = 0;
                    foreach (var surface in surfaces)
                    {
                        string name = surface.Name.ToLowerInvariant();
                        if (surface.Renderer.name == "Terrain" || surface.Renderer.GetComponentInParent<FFOne.Runtime.World.WorldTerrainParameters>())
                        { excludedTerrain++; continue; }
                        if (new[] { "animated", "water", "particle", "foliage", "leaf", "leaves", "grass", "effect" }.Any(name.Contains) ||
                            surface.Renderer.GetComponentInParent<Animation>() || surface.Renderer.GetComponentInParent<Animator>()) continue;
                        // Individual authored floor tiles can be narrower than six metres;
                        // the rear-floor probes validate the joined surface instead.
                        for (int i = 0; i < surface.Indices.Length; i += 3)
                        {
                            var a = surface.Vertices[surface.Indices[i]]; var b = surface.Vertices[surface.Indices[i + 1]]; var c = surface.Vertices[surface.Indices[i + 2]];
                            var cross = Vector3.Cross(b - a, c - a); float area = cross.magnitude * .5f;
                            if (area < 1 || cross.normalized.y < .97f) continue;
                            upwardBeforeHeightBand++;
                            var point = (a + b + c) / 3 + Vector3.up * .05f;
                            if (point.y >= minimumHeight && point.y <= maximumHeight)
                                seeds.Add((surface, point, i / 3, area));
                        }
                    }
                    var candidates = new List<(Surface Surface, Vector3 Point, int Triangle, float Area, float Score, float Clearance, int Visible)>();
                    var visited = new HashSet<Vector3Int>();
                    // The authored cell has many repeated floor triangles. Bound the
                    // expensive visual-ray stage before testing any of them.
                    bool NamedFloor(Surface s) => new[] { "floor", "ground", "platform", "road", "bridge", "walk", "terrain", "deck" }
                        .Any(s.Name.ToLowerInvariant().Contains);
                    var examinedSeeds = seeds.OrderBy(s => HeightDistance(s.Point)).ThenByDescending(s => NamedFloor(s.Surface)).ThenByDescending(s => Mathf.Min(s.Area, 100))
                        .ThenBy(s => s.Surface.Name, StringComparer.Ordinal).ThenBy(s => s.Triangle)
                        .Where(s => visited.Add(new Vector3Int(Mathf.FloorToInt(s.Point.x / 4), Mathf.FloorToInt(s.Point.y / 2), Mathf.FloorToInt(s.Point.z / 4))))
                        .Take(256).ToArray();
                    Debug.Log($"INSTANCE VIEWPOINTS {tile.Id}: {surfaces.Count} renderers, {seeds.Count} upward triangles; examining at most {examinedSeeds.Length} spatially distinct seeds");
                    int supported = 0, examined = 0;
                    foreach (var seed in examinedSeeds)
                    {
                        examined++;
                        if (candidates.Any(c => Vector3.Distance(c.Point, seed.Point) < 8)) continue;
                        bool floorBehindCamera = true;
                        foreach (float distance in new[] { 0f, 3f, 6f, 10f })
                        {
                            var start = seed.Point + Vector3.back * distance + Vector3.up;
                            if (!Cast(surfaces, new Ray(start, Vector3.down), 3, out var floor) || floor.Normal.y < .9f || Mathf.Abs(start.y - floor.Distance - seed.Point.y) > 1)
                            { floorBehindCamera = false; break; }
                        }
                        if (!floorBehindCamera) continue;
                        supported++;
                        var camera = seed.Point + new Vector3(0, 4, -10);
                        var focus = seed.Point + Vector3.up;
                        if (Cast(surfaces, new Ray(camera, (focus - camera).normalized), (focus - camera).magnitude - .1f, out _)) continue;
                        var rotation = Quaternion.LookRotation(focus - camera);
                        float tangent = Mathf.Tan(22.5f * Mathf.Deg2Rad), clearance = 80, farthest = 0;
                        var visible = new HashSet<Surface>();
                        foreach (float x in new[] { -.8f, -.4f, 0, .4f, .8f }) foreach (float y in new[] { -.7f, 0, .7f })
                        {
                            var ray = new Ray(camera, rotation * new Vector3(x * tangent * 1280f / 720, y * tangent, 1).normalized);
                            if (!Cast(surfaces, ray, 80, out var hit)) continue;
                            clearance = Mathf.Min(clearance, hit.Distance); farthest = Mathf.Max(farthest, hit.Distance); visible.Add(hit.Surface);
                        }
                        // A clear central ray alone admitted cameras almost inside visual walls.
                        // Check the entire sampled field of view against render meshes, including
                        // meshes without authored colliders, and require ten metres of floor behind.
                        if (clearance < 6 || visible.Count < 2) continue;
                        float score = Mathf.Min(clearance, 30) * 3 + visible.Count * 8 + Mathf.Min(farthest - clearance, 50) + Mathf.Min(seed.Area, 50) * .1f - HeightDistance(seed.Point) * 3;
                        candidates.Add((seed.Surface, seed.Point, seed.Triangle, seed.Area, score, clearance, visible.Count));
                        if (candidates.Count == 32) break;
                    }
                    var selected = new List<Vector3>();
                    var points = new JArray();
                    foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Surface.Name, StringComparer.Ordinal))
                    {
                        if (selected.Any(p => Vector3.Distance(p, candidate.Point) < 8)) continue;
                        selected.Add(candidate.Point);
                        var server = WorldCoordinates.ToServer(candidate.Point);
                        var bounds = candidate.Surface.Bounds;
                        points.Add(JObject.FromObject(new { renderer = candidate.Surface.Name,
                            sourceMesh = AssetDatabase.GetAssetPath(candidate.Surface.Renderer.GetComponent<MeshFilter>().sharedMesh),
                            sourceTriangle = candidate.Triangle, triangleArea = candidate.Area,
                            authoredHeightDistance = HeightDistance(candidate.Point),
                            point = new { x = server.x, y = server.y, z = server.z },
                            unityPoint = new[] { candidate.Point.x, candidate.Point.y, candidate.Point.z },
                            boundsCenter = new[] { bounds.center.x, bounds.center.y, bounds.center.z },
                            boundsSize = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                            viewBlockers = 0, viewClearance = candidate.Clearance, visibleRenderers = candidate.Visible, score = candidate.Score }));
                        if (points.Count == 10) break;
                    }
                    var report = new JObject { ["map"] = map, ["cell"] = tile.Id, ["sceneAddress"] = tile.SceneAddress,
                        ["scenePath"] = path, ["authoredAnchors"] = JArray.FromObject(authoredAnchors.Select(w => new { warp = w.Id, map = w.Map, point = new { x = w.X, y = w.Y, z = w.Z } })),
                        ["heightBand"] = JObject.FromObject(new { minimumUnityY = minimumHeight, maximumUnityY = maximumHeight, marginMetres = 60 }),
                        ["excludedTerrainRenderers"] = excludedTerrain, ["upwardTrianglesBeforeHeightBand"] = upwardBeforeHeightBand,
                        ["upwardTriangles"] = seeds.Count, ["floorBehindCamera"] = supported,
                        ["examinedSeeds"] = examined, ["maximumExaminedSeeds"] = 256, ["maximumValidCandidates"] = 32,
                        ["candidateCount"] = candidates.Count, ["candidates"] = points,
                        ["diagnosis"] = points.Count < 5 ? "Insufficient static floor anchors satisfy source height, rear floor 10m, clear focus and view clearance 6m; player acceptance must remain unresolved." : "Source triangle candidates require actual player visual review." };
                    results.Add(report); cells.Add(tile.SceneAddress, report);
                    Debug.Log($"INSTANCE VIEWPOINTS map {map}, {tile.Id}: {points.Count} actual floor triangle anchors ({seeds.Count} upward triangles, {supported} with rear floor)");
                }
                finally
                {
                    if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                    if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/InstanceSceneViewpoints.json", new JObject { ["schema"] = 1,
                ["cameraRule"] = "point + (0,4,-10), looking at point + up; static upward triangle centroids with 10m rear floor, clear focus and minimum 6m sampled render-mesh view clearance; verify actual player rendering",
                ["maps"] = results }.ToString());
        }
        sealed class Surface
        {
            public MeshRenderer Renderer; public Bounds Bounds; public string Name;
            public Vector3[] Vertices; public int[] Indices;
        }
        struct Hit { public Surface Surface; public float Distance; public Vector3 Normal; }
        // Two-sided visual geometry intersection avoids the gaps and back-face
        // blindness of collider-only queries without modifying authored scenes.
        static bool Cast(List<Surface> surfaces, Ray ray, float maximum, out Hit hit)
        {
            hit = default; float nearest = maximum;
            foreach (var surface in surfaces)
            {
                if (!surface.Bounds.IntersectRay(ray, out float enter) || enter > nearest) continue;
                for (int i = 0; i < surface.Indices.Length; i += 3)
                {
                    var a = surface.Vertices[surface.Indices[i]]; var ab = surface.Vertices[surface.Indices[i + 1]] - a;
                    var ac = surface.Vertices[surface.Indices[i + 2]] - a;
                    var p = Vector3.Cross(ray.direction, ac); float determinant = Vector3.Dot(ab, p);
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    float inverse = 1 / determinant; var offset = ray.origin - a;
                    float u = Vector3.Dot(offset, p) * inverse; if (u < 0 || u > 1) continue;
                    var q = Vector3.Cross(offset, ab); float v = Vector3.Dot(ray.direction, q) * inverse;
                    if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(ac, q) * inverse;
                    if (distance <= .01f || distance >= nearest) continue;
                    nearest = distance; hit = new Hit { Surface = surface, Distance = distance, Normal = Vector3.Cross(ab, ac).normalized };
                }
            }
            return hit.Surface != null;
        }
        static string Hierarchy(Transform node) => node.parent ? Hierarchy(node.parent) + "/" + node.name : node.name;
    }
}
