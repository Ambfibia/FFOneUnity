#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Integration;
using FFOne.Network;
using FFOne.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        internal async Task LiveLoginInstance(int port, string account)
        {
            await Session.LoginAsync("127.0.0.1", port, account, "test-only-password", lifetime.Token);
            ShowSelection(); SelectRosterSlot(1); await EnterSelectedWorldAsync();
        }
        internal void LiveResetInstance()
        {
            DisposeWorldFlow(); Session.Dispose(); Session = new FusionClient(); SetWorldPresentation(false); ShowLogin();
        }
    }

    /// <summary>Independent map-by-map matrix. Missing authored portal routes remain explicitly scene-only.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed partial class LiveGameplayInstanceSmoke : MonoBehaviour
    {
        sealed class Point { public int x, y, z; }
        sealed class Edge
        {
            public int fromMap, map, warp, npcType, npcX, npcY, npcZ, x, y, z;
            public bool instance;
            public MissionAction[] beforeMission, afterMission;
        }
        sealed class Case
        {
            public int map; public string cell, account, disposition, reason, exitMode;
            public Edge[] entry, exit; public Point reconnectPosition, scenePoint;
            public object[] invalidDestinationWarps;
        }
        sealed class Plan { public string source; public Case[] cases; }
        readonly List<object> results = new();
        readonly List<string> checks = new();
        FrontendController frontend;
        RenderTexture image;
        string directory;
        int port;
        bool passed = true;
        Plan plan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if (DevelopmentConnection.Enabled && Environment.GetCommandLineArgs().Contains("--live-instance-smoke"))
                new GameObject("Independent instance acceptance").AddComponent<LiveGameplayInstanceSmoke>();
        }
        async void Start()
        {
            try
            {
                directory = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
                Directory.CreateDirectory(directory);
                plan = JsonConvert.DeserializeObject<Plan>(File.ReadAllText(Environment.GetEnvironmentVariable("FFONE_INSTANCE_PLAN")));
                var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable)).Endpoint;
                if (!endpoint.IsLoopback) throw new InvalidOperationException("Instance acceptance requires the isolated loopback server.");
                port = endpoint.Port;
                await Wait(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.Root?.panel != null, "frontend ready");
                image = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); image.Create();
                foreach (var item in plan.cases.Where(c => c.disposition == "eligible"))
                {
                    int firstCheck = checks.Count;
                    try
                    {
                        await Login(item);
                        await Route(item, item.entry);
                        var evidence = SceneEvidence(frontend.LiveWorld.Stream, item);
                        await Capture("instance-" + item.map);
                        var contentErrors = frontend.LiveWorld.Replicas.ContentFailures.ToArray();
                        Check(contentErrors.Length == 0, $"map {item.map}: admitted NPC content has no model or animation failures");
                        var point = item.reconnectPosition;
                        if (item.exit.Length > 0)
                        {
                            await Route(item, item.exit);
                            // The production motor may settle onto the floor after
                            // the exact authored warp position was acknowledged.
                            // Save a final real stop, then compare that saved pose.
                            frontend.LiveWorld.MovementEnabled = false;
                            var stop = WorldCoordinates.ToServer(frontend.LiveWorld.Player.position);
                            point = new Point { x = stop.x, y = stop.y, z = stop.z };
                            await frontend.LiveMoveTo(point.x, point.y, point.z);
                        }
                        await frontend.LiveExit(); await WaitForSceneTeardown(item);
                        await Login(item);
                        var entry = frontend.Session.World;
                        Check(entry.MapNumber == 0 && entry.X == point.x && entry.Y == point.y && entry.Z == point.z,
                            $"map {item.map}: saved reconnect restores the exact authoritative overworld return position");
                        await Route(item, item.entry);
                        var freshLoad = SceneEvidence(frontend.LiveWorld.Stream, item);
                        await frontend.LiveExit(); await WaitForSceneTeardown(item);
                        results.Add(new { item.map, item.cell, status = "passed", acceptance = "native-portals-loading-scene-colliders-exit-reconnect",
                            item.exitMode, evidence, freshLoad, contentErrors, checks = checks.Skip(firstCheck).ToArray() });
                    }
                    catch (Exception error)
                    {
                        passed = false;
                        results.Add(new { item.map, item.cell, status = "failed", acceptance = "native-portal-route", error = error.ToString(), checks = checks.Skip(firstCheck).ToArray() });
                        frontend.LiveResetInstance(); await Frames(4);
                        Debug.LogError($"INSTANCE ACCEPTANCE {item.map}: {error}");
                    }
                    Write();
                }
                // The server does not persist map IDs. A missing source portal cannot
                // honestly be tested by inventing a saved instance login. Exercise the
                // original map-to-cell route twice and label its narrower evidence.
                foreach (var item in plan.cases.Where(c => c.disposition != "eligible"))
                {
                    int firstCheck = checks.Count;
                    try
                    {
                        var first = await LoadSceneOnly(item, false);
                        var second = await LoadSceneOnly(item, true);
                        results.Add(new { item.map, item.cell, status = "passed", acceptance = "scene-load-colliders-unload-fresh-reload",
                            portalRoute = "unverified", item.reason, item.invalidDestinationWarps, first, second, checks = checks.Skip(firstCheck).ToArray() });
                    }
                    catch (Exception error)
                    {
                        passed = false;
                        results.Add(new { item.map, item.cell, status = "failed", acceptance = "scene-only", item.reason,
                            error = error.ToString(), checks = checks.Skip(firstCheck).ToArray() });
                    }
                    Write();
                }
                Write(); Debug.Log("INSTANCE MATRIX " + (passed ? "PASSED" : "FAILED")); Application.Quit(passed ? 0 : 1);
            }
            catch (Exception error)
            {
                passed = false; results.Add(new { status = "runner-failed", error = error.ToString() });
                Write(); Debug.LogError(error); Application.Quit(1);
            }
        }
        async Task Login(Case item)
        {
            await frontend.LiveLoginInstance(port, item.account);
            await Wait(() => frontend.LiveWorld && frontend.LiveWorld.IsReady && frontend.LiveWorld.MovementEnabled,
                $"map {item.map}: normal account login finishes scene and loading handshake");
            frontend.LiveWorld.View.targetTexture = image;
        }
        async Task Route(Case item, IEnumerable<Edge> route)
        {
            foreach (var edge in route)
            {
                Check(frontend.LiveWorld.MapNumber == edge.fromMap, $"map {item.map}: native warp {edge.warp} starts on authored source map {edge.fromMap}");
                await BeforeMissionWarp(item, edge);
                frontend.LiveWorld.MovementEnabled = false;
                await frontend.LiveMoveTo(edge.npcX, edge.npcY, edge.npcZ);
                await Wait(() => frontend.LiveWorld.IsReady, $"map {item.map}: source portal scenery finishes admission");
                NetworkEntityView npc = null;
                await Wait(() => (npc = frontend.LiveWorld.Replicas.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc &&
                    a.Entity.NpcType == edge.npcType && Math.Abs(a.Entity.X - edge.npcX) < 500 && Math.Abs(a.Entity.Y - edge.npcY) < 500)),
                    $"map {item.map}: original NPC {edge.npcType} enters native interest");
                Check(frontend.LiveUi.OpenNpcTravel(npc.Entity, npc.Definition), $"map {item.map}: native portal window opens");
                await Frames(2);
                Submit(frontend.LiveUi.Windows.View.Q<Button>("npc-warp-" + edge.warp));
                Submit(frontend.Root.Q<Button>("prompt-confirm"));
                await Wait(() => frontend.LiveWorld && frontend.LiveWorld.MapNumber == edge.map && frontend.LiveWorld.IsReady &&
                    frontend.LiveWorld.MovementEnabled && !frontend.LiveUi.Windows.IsOpen && !frontend.LiveUi.TravelBusy,
                    $"map {item.map}: native warp {edge.warp} completes authoritative map {edge.map}, scene and movement readiness");
                var entry = frontend.Session.World;
                Check(entry.X == edge.x && entry.Y == edge.y && entry.Z == edge.z,
                    $"map {item.map}: server preserves exact authored warp {edge.warp} destination");
                await AfterMissionWarp(item, edge);
            }
        }
        object SceneEvidence(WorldStreamer stream, Case item)
        {
            Check(stream.MapNumber == item.map && stream.LoadedSceneTileIds.SequenceEqual(new[] { item.cell }),
                $"map {item.map}: its own map identity resolves exactly the published scene {item.cell}");
            Check(stream.PendingTiles == 0 && stream.ResidentTiles == 1 && stream.ColliderCount > 0 && stream.VisualCount > 0,
                $"map {item.map}: preceding cells retire and authored visuals and colliders finish admission");
            return new { stream.MapNumber, scenes = stream.LoadedSceneTileIds, stream.ColliderCount, stream.VisualCount,
                stream.ResidentTiles, stream.PendingTiles, stream.LastError };
        }
        async Task<object> LoadSceneOnly(Case item, bool reload)
        {
            var owner = new GameObject("Scene-only acceptance map " + item.map);
            var cameraOwner = new GameObject("Instance evidence camera", typeof(Camera)); cameraOwner.transform.SetParent(owner.transform);
            var camera = cameraOwner.GetComponent<Camera>(); camera.targetTexture = image; camera.fieldOfView = 45; camera.farClipPlane = 340;
            var position = WorldCoordinates.FromServer(item.scenePoint.x, item.scenePoint.y, item.scenePoint.z);
            camera.transform.position = position + new Vector3(0, 4, -10); camera.transform.LookAt(position + Vector3.up);
            var stream = owner.AddComponent<WorldStreamer>(); stream.View = camera; stream.MapNumber = item.map; stream.Anchor = position;
            try
            {
                await Wait(() => stream.IsReady, $"map {item.map}: isolated authored scene {(reload ? "fresh reload" : "load")} finishes", () => stream.LastError);
                var evidence = SceneEvidence(stream, item);
                if (!reload) await Capture("instance-" + item.map + "-scene-only");
                return evidence;
            }
            finally { Destroy(owner); await WaitForSceneTeardown(item); }
        }
        Task WaitForSceneTeardown(Case item) => Wait(() => !FindObjectsByType<EnvironmentCellBehaviour>(FindObjectsSortMode.None).Any(),
            $"map {item.map}: all preceding Addressable cell roots unload before a fresh world is admitted");
        async Task Wait(Func<bool> condition, string description, Func<string> error = null)
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (!condition())
            {
                var issue = error?.Invoke() ?? (frontend && frontend.LiveWorld ? frontend.LiveWorld.LastError : null);
                if (!string.IsNullOrEmpty(issue)) throw new InvalidOperationException(description + ": " + issue);
                if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException(description);
                await Task.Yield();
            }
            Check(true, description);
        }
        static async Task Frames(int frames) { while (frames-- > 0) await Task.Yield(); }
        void Check(bool value, string description)
        { if (!value) throw new InvalidOperationException(description); checks.Add(description); Debug.Log("INSTANCE CHECK: " + description); }
        static void Submit(Button button)
        {
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Original portal action is absent or correctly blocked by prerequisites.");
            using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
        }
        async Task Capture(string name)
        {
            await Frames(4);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = image; pixels.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); pixels.Apply();
                Check(pixels.GetPixels32().Where((_, i) => i % 32 == 0).Select(p => (p.r, p.g, p.b)).Distinct().Count() > 20,
                    name + ": actual scene produces nontrivial rendered pixels");
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(pixels); }
        }
        void Write()
        {
            if (string.IsNullOrEmpty(directory)) return;
            File.WriteAllText(Path.Combine(directory, "instances-result.json"), JsonConvert.SerializeObject(new {
                passed, maps = plan?.cases.Length ?? 0, mapsReported = results.Count,
                routeCoverageComplete = passed && results.Count == plan?.cases.Length && plan.cases.All(c => c.disposition == "eligible"),
                source = plan?.source, results
            }, Formatting.Indented));
        }
        void LateUpdate()
        {
            if (!image) return;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!camera.enabled || camera.targetTexture != image) continue;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = image };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            }
        }
        void OnDestroy() { if (image) { image.Release(); Destroy(image); } }
    }
}
#endif
