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
            internal AsyncOperationHandle<SceneInstance> SceneLease;
            internal bool SceneRequested, SceneUnloading;
            internal bool Ready, Retiring;
            internal bool AdmissionFinished;
            internal int Visuals, Colliders;
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
        public bool IsReady => initialized && Catalog && string.IsNullOrEmpty(LastError) && PendingTiles == 0 &&
            Catalog.Tiles.Any(t => Includes(t) && t.DistanceSquared(Anchor) < LoadDistance * LoadDistance) &&
            (neighborWater == null || neighborWater.Ready) &&
            Catalog.Tiles.Where(t => Includes(t) && t.DistanceSquared(Anchor) < LoadDistance * LoadDistance).All(t => tiles.ContainsKey(t.Id));
        WorldNeighborWater neighborWater;
        public int NeighborWaterVisuals => neighborWater ? neighborWater.VisualCount : 0;
        public int PresentedVersion { get; private set; }
        public bool TilePresented(string id) => tiles.TryGetValue(id, out var tile) && tile.Ready && !tile.Retiring;
        public bool Owns(Collider collider) => collider && tiles.Values.Any(tile => tile.Ready && !tile.Retiring && tile.Root && collider.transform.IsChildOf(tile.Root.transform));
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        int loading;
        Transform ambienceTarget;
        bool initialized;
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
            if (View) View.clearFlags = View.TryGetComponent<Skybox>(out var skybox) && skybox.material
                ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (View && !View.GetComponent<WorldCameraProjection>()) View.gameObject.AddComponent<WorldCameraProjection>();
            ambienceTarget = new GameObject("World ambience anchor").transform; ambienceTarget.SetParent(transform, false);
            FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget = ambienceTarget;
            initialized = true;
        }
        void Update()
        {
            WorldSimulationClock.Advance();
            if (!initialized || !Catalog || !string.IsNullOrEmpty(LastError)) return;
            neighborWater.Tick();
            if (!string.IsNullOrEmpty(neighborWater.Error)) { Fail(neighborWater.Error); return; }
            ambienceTarget.position = Anchor;
            Shader.SetGlobalFloat("_FFWorldElapsedSeconds", WorldSimulationClock.Seconds);
            foreach (var tile in tiles.Values.ToArray())
                if (!tile.Retiring && (!Includes(tile.Reference) || tile.Reference.DistanceSquared(Anchor) > UnloadDistance * UnloadDistance))
                { tile.Retiring = true; PresentedVersion++; tile.Root.SetActive(false); StartCoroutine(Retire(tile)); }
            // Prefetch one adjacent scene while the closest scene binds its editable content.
            if (loading >= 2 || tiles.Values.Any(t => t.Retiring) || tiles.Count >= MaximumRoots) return;
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
            // Physics belongs to the authored Unity scene. Reconstructing an
            // absent cell from legacy meshes silently restores broken winding.
            if (string.IsNullOrEmpty(tile.Reference.SceneAddress))
            {
                Fail("Missing authored Unity scene for world tile " + tile.Reference.Id);
                tile.AdmissionFinished = true; loading--; yield break;
            }
            yield return AdmitScene(tile);
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
                if (!string.IsNullOrEmpty(cell.LastError) || !cell.Runtime || !cell.Runtime.Ready || cell.Runtime.Errors.Count > 0)
                    Fail("Authored cell behaviour failed: " + tile.Reference.Id + ": " + (cell.LastError ?? (cell.Runtime ? string.Join("; ",cell.Runtime.Errors.Take(10)) : "missing behaviour")));
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
            if (tile.Root) Destroy(tile.Root);
            tiles.Remove(tile.Reference.Id);
        }
        void Fail(string error) { LastError = error; Debug.LogError(error); }
        [Conditional("DEVELOPMENT_BUILD")]
        static void TraceScene(string message)
        {
            if(Environment.GetCommandLineArgs().Contains("--world-district-smoke"))Debug.Log("WORLD CELL " + message);
        }
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
            if (FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget == ambienceTarget) FFOne.Runtime.World.WorldTerrainEnvironment.SamplingTarget = null;
        }
    }
}
