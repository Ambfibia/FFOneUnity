using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace FFOne.World
{
    /// <summary>Standalone animated world test. Timed intervals exclude screenshots and serialization.</summary>
    public sealed class WorldBenchmark : MonoBehaviour
    {
        public bool Running { get; private set; }
        WorldStreamer stream;
        string output;
        readonly Stopwatch clock = Stopwatch.StartNew();
        UniversalRenderPipelineAsset pipeline, previousPipeline;
        bool fixedPose;
        bool fixedSimulation;
        bool quitting;
        float poseElapsed;
        void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--world-benchmark") && (args.Contains("--fixed-simulation") || args.Contains("--pose-seconds")))
            { WorldSimulationClock.BeginControlled(); Time.timeScale = 0; }
        }
        static string Option(string[] args, string name, string fallback)
        { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback; }
        static Vector3 Vector(string text)
        { var values = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray(); if (values.Length != 3) throw new ArgumentException("Position needs X Y Z"); return new Vector3(-values[0], values[1], values[2]); }
        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Contains("--world-benchmark")) yield break;
            Running = true; stream = GetComponent<WorldStreamer>();
            string pose = Option(args, "--pose-seconds", "");
            fixedPose = pose != "";
            fixedSimulation = args.Contains("--fixed-simulation");
            if (fixedPose || fixedSimulation) Time.timeScale = 0;
            output = Option(args, "--output", Path.Combine(Application.persistentDataPath, "WorldBenchmark")); Directory.CreateDirectory(output);
            stream.Optimized = Option(args, "--mode", "shared") == "shared";
            stream.Anchor = Vector(Option(args, "--position", "-6374.3188 -56.4746 668.2039"));
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true;
            QualitySettings.antiAliasing = 0; QualitySettings.shadows = UnityEngine.ShadowQuality.Disable;
            previousPipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            var original = previousPipeline ? previousPipeline : GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (original)
            {
                pipeline = Instantiate(original); pipeline.msaaSampleCount = 1; pipeline.renderScale = 1;
                pipeline.useSRPBatcher = stream.Optimized; pipeline.supportsCameraDepthTexture = false;
                pipeline.supportsCameraOpaqueTexture = false; QualitySettings.renderPipeline = pipeline;
            }
            var camera = stream.View; camera.fieldOfView = 45; camera.nearClipPlane = .1f; camera.farClipPlane = 340;
            camera.allowHDR = false; camera.allowMSAA = false;
            PlaceCamera();
            double started = clock.Elapsed.TotalSeconds;
            while (!stream.IsReady)
            {
                if (Failed() || clock.Elapsed.TotalSeconds - started > 240) { Failure("World load failed or timed out"); yield break; }
                yield return null;
            }
            double initialReady = clock.Elapsed.TotalSeconds - started;
            double totalReady = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;
            if (fixedPose)
            {
                float target = float.Parse(pose, CultureInfo.InvariantCulture);
                if (target < 0 || target > 60) { Failure("Pose must be between 0 and 60 seconds"); yield break; }
                while (poseElapsed + .000001f < target)
                {
                    float stepSize = Mathf.Min(1f / 60, target - poseElapsed);
                    Time.captureDeltaTime = stepSize; Time.timeScale = 1;
                    yield return null;
                    poseElapsed += stepSize;
                    Time.timeScale = 0;
                }
                Time.captureDeltaTime = 0;
            }
            JObject transition = null;
            string step = Option(args, "--step", "");
            if (step != "")
            {
                double preTransitionReady = clock.Elapsed.TotalSeconds;
                while (clock.Elapsed.TotalSeconds - preTransitionReady < 1) yield return null;
                double transitionStart = clock.Elapsed.TotalSeconds; double previous = transitionStart;
                var intervals = new JArray(); stream.Anchor += Vector(step); PlaceCamera();
                yield return null;
                while (true)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    intervals.Add(new JObject { ["seconds"] = now - transitionStart, ["frameMs"] = (now - previous) * 1000,
                        ["residentTiles"] = stream.ResidentTiles, ["admitting"] = stream.PendingTiles }); previous = now;
                    if (Failed() || now - transitionStart > 240) { Failure("Transition failed or timed out"); yield break; }
                    if (stream.IsReady) break;
                    yield return null;
                }
                transition = new JObject { ["delta"] = JArray.FromObject(step.Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture))),
                    ["readySeconds"] = clock.Elapsed.TotalSeconds - transitionStart, ["frames"] = intervals };
                File.WriteAllText(Path.Combine(output, "transition.json"), transition.ToString());
            }
            double readyAt = clock.Elapsed.TotalSeconds;
            while (clock.Elapsed.TotalSeconds - readyAt < 3) { if (Failed()) { Failure(stream.LastError); yield break; } yield return null; }
            if (fixedSimulation) { Time.captureDeltaTime = 1f / 60; Time.timeScale = 1; }
            var samples = new List<double>(); double prior = clock.Elapsed.TotalSeconds;
            for (int frame = 0; frame < 360; frame++)
            {
                yield return null;
                double now = clock.Elapsed.TotalSeconds; samples.Add((now - prior) * 1000); prior = now;
                if (fixedSimulation) poseElapsed += 1f / 60;
                if (!stream.IsReady || Failed()) { Failure("Readiness changed during measurement: " + stream.LastError); yield break; }
            }
            if (fixedSimulation) { Time.timeScale = 0; Time.captureDeltaTime = 0; }
            var sorted = samples.OrderBy(v => v).ToArray();
            var behaviours = GetComponentsInChildren<WorldBehaviour>();
            var renderers = GetComponentsInChildren<Renderer>();
            var meshes = renderers.Select(r => r.GetComponent<MeshFilter>()?.sharedMesh).Where(m => m != null).Distinct().ToArray();
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            var process = Process.GetCurrentProcess();
            var report = new JObject {
                ["engine"] = "Unity " + Application.unityVersion, ["mode"] = stream.Optimized ? "shared" : "baseline",
                ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(), ["adapter"] = SystemInfo.graphicsDeviceName,
                ["cpu"] = SystemInfo.processorType, ["width"] = Screen.width, ["height"] = Screen.height,
                ["fov"] = camera.fieldOfView, ["far"] = camera.farClipPlane, ["near"] = camera.nearClipPlane,
                ["msaa"] = 0, ["vsync"] = 0, ["warmupSeconds"] = 3, ["animated"] = !fixedPose,
                ["fixedPoseSeconds"] = fixedPose ? new JValue(poseElapsed) : JValue.CreateNull(),
                ["fixedSimulationSeconds"] = fixedSimulation ? new JValue(WorldSimulationClock.ExactSeconds) : JValue.CreateNull(),
                ["simulationNominalStepSeconds"] = fixedSimulation || fixedPose ? new JValue(1.0 / 60) : JValue.CreateNull(),
                ["anchor"] = new JArray(-stream.Anchor.x, stream.Anchor.y, stream.Anchor.z),
                ["cameraPosition"] = new JArray(-camera.transform.position.x, camera.transform.position.y, camera.transform.position.z),
                ["cameraForward"] = new JArray(-camera.transform.forward.x, camera.transform.forward.y, camera.transform.forward.z),
                ["meanMs"] = samples.Average(), ["medianMs"] = sorted[180], ["p95Ms"] = sorted[(int)(sorted.Length * .95)],
                ["p99Ms"] = sorted[(int)(sorted.Length * .99)], ["maxMs"] = sorted.Last(), ["fps"] = 1000 / samples.Average(),
                ["frames"] = samples.Count, ["frameMs"] = JArray.FromObject(samples), ["readySeconds"] = initialReady,
                ["totalReadySeconds"] = totalReady,
                ["residentTileIds"] = JArray.FromObject(stream.LoadedTileIds), ["maximumRoots"] = stream.MaximumObservedRoots,
                ["visualPlacements"] = stream.Catalog.Tiles.Where(t => stream.LoadedTileIds.Contains(t.Id)).Sum(t => t.SourceVisuals) + stream.NeighborWaterVisuals,
                ["neighborWaterVisuals"] = stream.NeighborWaterVisuals,
                ["renderedVisualPlacements"] = stream.VisualCount + stream.NeighborWaterVisuals, ["colliders"] = stream.ColliderCount,
                ["placementCountPolicy"] = "visualPlacements counts authored source roots as in Rust; renderedVisualPlacements excludes collision-alpha and the accepted zero-contribution model",
                ["rendererCount"] = renderers.Length, ["meshAssets"] = meshes.Length, ["materialAssets"] = materials.Length,
                ["processWorkingSetBytes"] = process.WorkingSet64, ["processPrivateBytes"] = process.PrivateMemorySize64,
                ["unityAllocatedBytes"] = Profiler.GetTotalAllocatedMemoryLong(), ["sourceCatalogSha256"] = stream.Catalog.SourceCatalogSha256,
                ["behaviourErrors"] = JArray.FromObject(behaviours.SelectMany(b => b.Errors)),
                ["sourceCoverageNotes"] = JArray.FromObject(behaviours.SelectMany(b => b.SourceCoverageNotes)),
                ["behaviourCoverage"] = JArray.FromObject(behaviours.Select(b => new { tile = b.gameObject.name, counters = b.GetType().GetFields().Where(f => f.FieldType == typeof(int)).ToDictionary(f => f.Name, f => f.GetValue(b)) })),
                ["cacheCondition"] = "New process; filesystem cache not flushed; ready excludes process startup and initial catalog load"
            };
            File.WriteAllText(Path.Combine(output, "report.json"), report.ToString());
            Debug.Log("World benchmark: " + report["fps"] + " FPS; report " + output);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "frame.png"));
            yield return new WaitForEndOfFrame(); yield return null; yield return null;
            Application.Quit(0);
        }
        void PlaceCamera()
        { var rotation = Quaternion.Euler(0, 135, 0); stream.View.transform.SetPositionAndRotation(stream.Anchor + Vector3.up * 1.4f - rotation * Vector3.forward * 4, rotation); }
        bool Failed() => !string.IsNullOrEmpty(stream.LastError);
        void LateUpdate() { if (fixedPose || fixedSimulation) Shader.SetGlobalFloat("_FFWorldElapsedSeconds", WorldSimulationClock.Seconds); }
        void Failure(string error)
        {
            File.WriteAllText(Path.Combine(output, "failure.json"), new JObject { ["error"] = error, ["streamError"] = stream.LastError,
                ["residentTiles"] = stream.ResidentTiles, ["pendingTiles"] = stream.PendingTiles }.ToString());
            Debug.LogError(error + ": " + stream.LastError); Application.Quit(2);
        }
        void OnApplicationQuit() => quitting = true;
        void OnDestroy() { WorldSimulationClock.Controlled = false; Time.timeScale = 1; Time.captureDeltaTime = 0; if (!quitting && pipeline) { if (QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previousPipeline; Destroy(pipeline); } }
    }
}
