using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FFOne.World
{
    [DefaultExecutionOrder(-10000)]
    public sealed class WorldStreamer : MonoBehaviour
    {
        sealed class Tile
        {
            internal WorldTileReference Reference;
            internal GameObject Root;
            internal WorldTileContent Content;
            internal FFOne.Content.ContentAssets.Request Lease;
            internal AsyncOperationHandle<SceneInstance> SceneLease;
            internal bool SceneRequested, SceneUnloading;
            internal bool Ready, Retiring;
            internal bool AdmissionFinished;
            internal int Visuals, Colliders;
            internal readonly List<Material> OwnedMaterials = new List<Material>();
        }
        public const float LoadDistance = 356, UnloadDistance = 420;
        public const int MaximumRoots = 9;
        public Camera View;
        public Vector3 Anchor;
        public bool Optimized = true;
        public WorldCatalog CatalogOverride;
        public string CatalogAddress = "World/Catalog";
        public WorldCatalog Catalog { get; private set; }
        public int MapNumber;
        public bool Includes(WorldTileReference tile) => MapNumber == 0 ||
            WorldTravelCatalog.Load().Maps.TryGetValue(MapNumber, out var cell) && tile.X == cell.X && tile.Z == cell.Z;
        public string LastError { get; private set; }
        public int ResidentTiles => tiles.Count;
        public int PendingTiles => tiles.Values.Count(t => !t.Ready || t.Retiring);
        public int PendingSceneLoads => tiles.Values.Count(t => t.SceneRequested && t.SceneLease.IsValid() && !t.SceneLease.IsDone);
        public int VisualCount { get; private set; }
        public int ColliderCount { get; private set; }
        public int MaximumObservedRoots { get; private set; }
        public string[] LoadedTileIds => tiles.Values.Where(t => t.Ready && !t.Retiring).Select(t => t.Reference.Id).OrderBy(id => id).ToArray();
        public string[] LoadedSceneTileIds => tiles.Values.Where(t => t.SceneRequested && t.Ready && !t.Retiring).Select(t => t.Reference.Id).OrderBy(id => id).ToArray();
        public bool IsReady => Catalog && string.IsNullOrEmpty(LastError) && PendingTiles == 0 &&
            Catalog.Tiles.Any(t => Includes(t) && t.DistanceSquared(Anchor) < LoadDistance * LoadDistance) &&
            (neighborWater == null || neighborWater.Ready) &&
            Catalog.Tiles.Where(t => Includes(t) && t.DistanceSquared(Anchor) < LoadDistance * LoadDistance).All(t => tiles.ContainsKey(t.Id));
        WorldNeighborWater neighborWater;
        public int NeighborWaterVisuals => neighborWater ? neighborWater.VisualCount : 0;
        public int PresentedVersion { get; private set; }
        public bool TilePresented(string id) => tiles.TryGetValue(id, out var tile) && tile.Ready && !tile.Retiring;
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        int loading, retired;
        AsyncOperation unloading;
        readonly Stopwatch budget = new Stopwatch();
        Transform ambienceTarget;
        RenderPipelineAsset previousPipeline, worldPipeline;

        IEnumerator Start()
        {
            previousPipeline = QualitySettings.renderPipeline;
            var pipelineRequest = FFOne.Content.ContentAssets.LoadAsync<RenderPipelineAsset>("World/RenderPipeline");
            yield return pipelineRequest;
            worldPipeline = pipelineRequest.asset as RenderPipelineAsset;
            if (worldPipeline) QualitySettings.renderPipeline = worldPipeline;
            if (CatalogOverride) Catalog = CatalogOverride;
            else
            {
                var catalogRequest = FFOne.Content.ContentAssets.LoadAsync<WorldCatalog>(CatalogAddress);
                yield return catalogRequest;
                Catalog = catalogRequest.asset as WorldCatalog;
            }
            if (!Catalog || Catalog.Tiles.Length == 0) LastError = "World catalog missing. Run FFOne → World → Import Entire Native World.";
            if (Catalog) { neighborWater = gameObject.AddComponent<WorldNeighborWater>(); neighborWater.Stream = this; }
            if (Catalog)
                foreach (var atmosphere in Catalog.Atmospheres)
                    FFOne.Runtime.World.WorldTerrainEnvironment.Register(atmosphere.Grid, atmosphere.FogDepth, atmosphere.Fog, atmosphere.Sky, atmosphere.Light);
            if (!View) View = Camera.main;
            if (View) View.clearFlags = CameraClearFlags.SolidColor;
            if (View && !View.GetComponent<WorldCameraProjection>()) View.gameObject.AddComponent<WorldCameraProjection>();
            ambienceTarget = new GameObject("World ambience anchor").transform; ambienceTarget.SetParent(transform, false);
            FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget = ambienceTarget;
        }
        void Update()
        {
            WorldSimulationClock.Advance();
            if (!Catalog || !string.IsNullOrEmpty(LastError)) return;
            neighborWater.Tick();
            if (!string.IsNullOrEmpty(neighborWater.Error)) { Fail(neighborWater.Error); return; }
            ambienceTarget.position = Anchor;
            Shader.SetGlobalFloat("_FFWorldElapsedSeconds", WorldSimulationClock.Seconds);
            foreach (var tile in tiles.Values.ToArray())
                if (!tile.Retiring && (!Includes(tile.Reference) || tile.Reference.DistanceSquared(Anchor) > UnloadDistance * UnloadDistance))
                { tile.Retiring = true; PresentedVersion++; tile.Root.SetActive(false); StartCoroutine(Retire(tile)); }
            if (unloading != null && !unloading.isDone) return;
            if (loading >= 1 || tiles.Values.Any(t => t.Retiring) || tiles.Count >= MaximumRoots) return;
            var candidate = Catalog.Tiles.Where(t => Includes(t) && !tiles.ContainsKey(t.Id) && t.DistanceSquared(Anchor) < LoadDistance * LoadDistance)
                .OrderBy(t => t.DistanceSquared(Anchor)).ThenBy(t => t.Z).ThenBy(t => t.X).FirstOrDefault();
            if (candidate == null) return;
            var pending = new Tile { Reference = candidate, Root = new GameObject(candidate.Id) };
            pending.Root.transform.SetParent(transform, false);
            tiles.Add(candidate.Id, pending); loading++;
            MaximumObservedRoots = Mathf.Max(MaximumObservedRoots, tiles.Count);
            StartCoroutine(Admit(pending));
        }
        IEnumerator Admit(Tile tile)
        {
            if (!string.IsNullOrEmpty(tile.Reference.SceneAddress))
            {
                yield return AdmitScene(tile);
                yield break;
            }
            var request = FFOne.Content.ContentAssets.AcquireAsync<WorldTileContent>(tile.Reference.Resource);
            tile.Lease = request;
            yield return request;
            tile.Content = request.asset as WorldTileContent;
            if (!tile.Content) { Fail("Missing world tile " + tile.Reference.Id); tile.AdmissionFinished = true; loading--; yield break; }
            var legacy = new Dictionary<string, List<Transform>>();
            var nodes = new Dictionary<string, List<Transform>>();
            if (tile.Content.Terrain && !tile.Retiring) Instantiate(tile.Content.Terrain, tile.Root.transform, false);
            yield return null;
            budget.Restart(); int count = 0;
            foreach (var placement in tile.Content.Visuals)
            {
                if (tile.Retiring) break;
                var instance = Instantiate(FFOne.Content.NativeModelAssets.Resolve(placement.Model), tile.Root.transform, false);
                WorldMaterialState.Prepare(instance);
                WorldModelBasis.ApplySource(instance);
                instance.name = placement.Name;
                Pose(instance.transform, placement.Position, placement.Rotation, placement.Scale);
                WorldObjectRange.Attach(instance, placement.CpuPresentationEnd);
                Register(legacy, placement.LegacyId, instance.transform); Register(nodes, placement.SourceNode, instance.transform);
                if (!Optimized)
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    { var materials = renderer.materials; foreach (var material in materials) { material.enableInstancing = false; tile.OwnedMaterials.Add(material); } }
                VisualCount++; tile.Visuals++; count++;
                if (budget.Elapsed.TotalMilliseconds > 2 || count >= 64) { yield return null; budget.Restart(); count = 0; }
            }
            foreach (var definition in tile.Content.Colliders)
            {
                if (tile.Retiring) break;
                var instance = new GameObject(definition.Name); instance.transform.SetParent(tile.Root.transform, false);
                Pose(instance.transform, definition.Position, definition.Rotation, definition.Scale);
                var collider = instance.AddComponent<MeshCollider>(); collider.sharedMesh = definition.Mesh;
                // Keep exact trigger mesh geometry, outside Unity nonconvex trigger physics constraints.
                collider.enabled = !definition.Trigger;
                Register(legacy, definition.LegacyId, instance.transform); Register(nodes, definition.SourceNode, instance.transform);
                ColliderCount++; tile.Colliders++; count++;
                if (budget.Elapsed.TotalMilliseconds > 2 || count >= 64) { yield return null; budget.Restart(); count = 0; }
            }
            if (!tile.Retiring)
            {
                WorldBehaviour behaviour = null;
                yield return WorldBehaviour.AttachAsync(tile.Root, FFOne.Content.NativeData.Read("map/tiles/" + tile.Reference.Id + "/behaviour"), legacy, nodes, View, value => behaviour = value);
                if (behaviour)
                {
                    if (behaviour.Errors.Count > 0) Fail(tile.Reference.Id + ": " + string.Join("; ", behaviour.Errors.Take(10)));
                    tile.Ready = behaviour.Ready;
                    PresentedVersion++;
                    WorldObjectRange.FinalizeTile(tile.Root);
                }
            }
            tile.AdmissionFinished = true;
            loading--;
        }
        IEnumerator AdmitScene(Tile tile)
        {
            TraceScene("request " + tile.Reference.Id);
            tile.SceneRequested = true;
            tile.SceneLease = Addressables.LoadSceneAsync(tile.Reference.SceneAddress, LoadSceneMode.Additive, true);
            yield return tile.SceneLease;
            TraceScene("loaded " + tile.Reference.Id + " " + tile.SceneLease.Status);
            if (tile.SceneLease.Status != AsyncOperationStatus.Succeeded)
            {
                Fail("Cannot load authored cell " + tile.Reference.Id + ": " + tile.SceneLease.OperationException);
                tile.AdmissionFinished = true; loading--; yield break;
            }
            var roots = tile.SceneLease.Result.Scene.GetRootGameObjects();
            var cell = roots.SelectMany(r => r.GetComponentsInChildren<EnvironmentCellBehaviour>(true)).SingleOrDefault();
            if (!cell || cell.TileId != tile.Reference.Id)
            {
                Fail("Invalid authored cell root: " + tile.Reference.Id);
                tile.AdmissionFinished = true; loading--; yield break;
            }
            Destroy(tile.Root); tile.Root = cell.gameObject;
            cell.View = View;
            if (!tile.Retiring)
            {
                yield return cell.Initialize();
                TraceScene("initialized " + tile.Reference.Id);
                if (!cell.Runtime || !cell.Runtime.Ready || cell.Runtime.Errors.Count > 0)
                    Fail("Authored cell behaviour failed: " + tile.Reference.Id + ": " + (cell.Runtime ? string.Join("; ",cell.Runtime.Errors.Take(10)) : "missing behaviour"));
                tile.Visuals = cell.Bindings.Count(b => b.Visual && b.Target);
                tile.Colliders = tile.Root.GetComponentsInChildren<Collider>(true).Length;
                VisualCount += tile.Visuals; ColliderCount += tile.Colliders;
                tile.Ready = string.IsNullOrEmpty(LastError); PresentedVersion++;
            }
            if (tile.Retiring) tile.Root.SetActive(false);
            tile.AdmissionFinished = true; loading--;
        }
        IEnumerator Retire(Tile tile)
        {
            // The request owns its root until admission observes cancellation.
            while (!tile.AdmissionFinished) yield return null;
            if (tile.SceneRequested)
            {
                if (tile.SceneLease.IsValid())
                {
                    if (tile.SceneLease.Status == AsyncOperationStatus.Succeeded)
                    { tile.SceneUnloading = true; yield return Addressables.UnloadSceneAsync(tile.SceneLease, true); }
                    else Addressables.Release(tile.SceneLease);
                }
                VisualCount -= tile.Visuals; ColliderCount -= tile.Colliders;
                tiles.Remove(tile.Reference.Id);
                yield break;
            }
            var descendants = tile.Root.GetComponentsInChildren<Transform>(true);
            budget.Restart(); int count = 0;
            for (int i = descendants.Length - 1; i >= 0; i--)
            {
                if (descendants[i] && descendants[i] != tile.Root.transform) Destroy(descendants[i].gameObject);
                if (++count >= 256 || budget.Elapsed.TotalMilliseconds > 1) { yield return null; budget.Restart(); count = 0; }
            }
            VisualCount -= tile.Visuals; ColliderCount -= tile.Colliders;
            foreach (var material in tile.OwnedMaterials) if (material) Destroy(material);
            Destroy(tile.Root); tile.Content = null;
            yield return null;
            tile.Lease?.Dispose(); tile.Lease = null;
            tiles.Remove(tile.Reference.Id); retired++;
            if (retired >= 4 && loading == 0) { retired = 0; unloading = Resources.UnloadUnusedAssets(); }
        }
        void Fail(string error) { LastError = error; Debug.LogError(error); }
        [Conditional("DEVELOPMENT_BUILD")]
        static void TraceScene(string message)
        {
            if(Environment.GetCommandLineArgs().Contains("--world-district-smoke"))Debug.Log("WORLD CELL " + message);
        }
        static void Register(Dictionary<string, List<Transform>> index, string key, Transform value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!index.TryGetValue(key, out var matches)) index[key] = matches = new List<Transform>(); matches.Add(value);
        }
        static void Pose(Transform target, Vector3 position, Quaternion rotation, Vector3 scale)
        { target.localPosition = position; target.localRotation = rotation; target.localScale = scale; }
        bool quitting;
        void OnApplicationQuit() => quitting = true;
        static void ReleaseScene(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid()) return;
            if (!handle.IsDone) { handle.Completed += ReleaseScene; return; }
            if (handle.Status == AsyncOperationStatus.Succeeded) Addressables.UnloadSceneAsync(handle, true);
            else Addressables.Release(handle);
        }
        void OnDestroy()
        {
            if (!quitting && worldPipeline && (QualitySettings.renderPipeline == worldPipeline || GetComponent<WorldBenchmark>()?.Running == true))
                QualitySettings.renderPipeline = previousPipeline;
            StopAllCoroutines();
            if (neighborWater) Destroy(neighborWater);
            foreach (var tile in tiles.Values) if (tile.SceneRequested && !tile.SceneUnloading && !quitting) ReleaseScene(tile.SceneLease);
            foreach (var tile in tiles.Values) tile.Lease?.Dispose();
            foreach (var tile in tiles.Values) foreach (var material in tile.OwnedMaterials) if (material) Destroy(material);
            if (FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget == ambienceTarget) FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget = null;
        }
    }
}
