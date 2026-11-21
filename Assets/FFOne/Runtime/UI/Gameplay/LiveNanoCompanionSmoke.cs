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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal sealed partial class LiveNanoCompanionSmoke : MonoBehaviour
    {
        sealed class Warp { public int npcType, npcX, npcY, npcZ, warp, map; }
        FrontendController frontend;
        Keyboard keyboard;
        Transform subject;
        string role, peer, directory, sync;
        bool owner;
        int ownerId;
        readonly List<string> checks = new();
        readonly List<object> captures = new();
        WorldNanoCompanion Companion => subject ? subject.GetComponent<WorldNanoCompanion>() : null;
        GameObject Model => Companion ? Companion.Model : null;
        NetworkWorldController World => frontend.LiveWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            if (DevelopmentConnection.Enabled && (args.Contains("--live-nano-companion-owner") ||
                args.Contains("--live-nano-companion-observer")))
                new GameObject("Live Nano companion acceptance").AddComponent<LiveNanoCompanionSmoke>();
        }

        async void Start()
        {
            owner = Environment.GetCommandLineArgs().Contains("--live-nano-companion-owner");
            role = owner ? "owner" : "observer";
            peer = owner ? "observer" : "owner";
            directory = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
            sync = Environment.GetEnvironmentVariable("FFONE_NANO_SYNC");
            try
            {
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(sync);
                var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable)).Endpoint;
                if (!endpoint.IsLoopback) throw new InvalidOperationException("Expected isolated loopback fixture.");
                var warp = JsonConvert.DeserializeObject<Warp>(Environment.GetEnvironmentVariable("FFONE_NANO_WARP"));
                Application.runInBackground = true;
                await Until(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.LiveFrontendReady, "frontend ready");
                await frontend.LiveWeaponLogin(endpoint.Port, owner ? "unityweapa" : "unityweapb");
                await Until(() => World && World.IsReady && World.MovementEnabled, "world admission");
                int localPlayer = frontend.LiveState.Snapshot.PlayerId;
                Mark("id", localPlayer.ToString());
                await Until(() => File.Exists(Marker(peer, "id")), "other player entered");
                ownerId = owner ? localPlayer : int.Parse(File.ReadAllText(Marker(peer, "id")));
                int remoteId = int.Parse(File.ReadAllText(Marker(peer, "id")));
                NetworkEntityView remote = null;
                await Until(() => (remote = Remote(remoteId)), "other avatar entered interest");
                subject = owner ? World.Player : remote.transform;
                int first = owner ? frontend.LiveState.Snapshot.NanoSlots[0] : 0;
                int second = owner ? frontend.LiveState.Snapshot.NanoSlots[1] : 0;
                if (owner)
                {
                    Check(first > 0 && second > 0 && first != second, "two distinct equipped Nano identities");
                    Mark("loadout", first + "," + second);
                }
                else
                {
                    await Until(() => File.Exists(Marker(peer, "loadout")), "owner loadout");
                    var ids = File.ReadAllText(Marker(peer, "loadout")).Split(',');
                    first = int.Parse(ids[0]); second = int.Parse(ids[1]);
                }
                Check(!Model, "starts without a companion");
                BeginPresentationChecks();
                ArmPresentation("spawn", first);
                await Barrier("start");

                if (owner) { keyboard = InputSystem.AddDevice<Keyboard>(); await PressNano(Key.Digit1, 0); }
                var original = await Visible(first, "spawn");
                await VerifyPresentation();
                var startingAvatar = subject.position;
                var startingNano = original.transform.position;
                ArmPresentation("skill", first);
                await Barrier("spawn");

                if (owner) await UseButtercupSkill();
                await VerifyPresentation();
                await Barrier("skill");

                if (owner) await Walk(startingAvatar);
                await Until(() => subject && Model == original && Horizontal(subject.position - startingAvatar) > 6 &&
                    Horizontal(original.transform.position - startingNano) > 4 &&
                    Vector3.Distance(subject.position, original.transform.position) < 6, "same companion follows moving owner");
                await Visible(first, "follow");
                ArmPresentation("swap", second);
                await Barrier("follow");

                if (owner) await PressNano(Key.Digit2, 1);
                var replacement = await Visible(second, "swap");
                await Until(() => !original && replacement != original, "old model destroyed after swap");
                await VerifyPresentation();
                ArmPresentation("withdraw", second);
                await Barrier("swap");

                if (owner) await PressNano(Key.Digit2, -1);
                await Until(() => !replacement && !Model, "withdraw destroys active model");
                Check(!AnyVisibleCompanion(), "world has no remaining companion after withdraw");
                await VerifyPresentation();
                await Barrier("withdraw");

                if (owner) await PressNano(Key.Digit1, 0);
                var beforeWarp = await Visible(first, "reactivate");
                await Barrier("warp-ready");
                if (owner)
                {
                    await UseWarp(warp);
                    await Until(() => !beforeWarp, "old model destroyed on map change");
                    subject = World.Player;
                    var state = frontend.LiveState.Snapshot;
                    if (state.ActiveNanoSlot >= 0)
                        await Visible(state.NanoSlots[state.ActiveNanoSlot], "destination");
                    else
                        Check(!AnyVisibleCompanion(), "destination has no companion for an inactive slot");
                }
                else
                {
                    await Until(() => !Remote(ownerId) && !beforeWarp, "departed player and companion removed from original map");
                    Check(!AnyVisibleCompanion(), "source map contains no orphan companion after warp");
                }
                await Barrier("warp-complete");
                await frontend.LiveExit();
                Finish(true, null);
            }
            catch (Exception error)
            {
                Debug.LogError("NANO COMPANION " + role + ": " + error);
                if (Directory.Exists(sync)) Mark("failed", error.ToString());
                Finish(false, error.ToString());
            }
        }

        NetworkEntityView Remote(int id) => World.Replicas.Actors.FirstOrDefault(a =>
            a && a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == id);

        async Task PressNano(Key key, int expected)
        {
            await Until(() => Application.isFocused && World.MovementEnabled && !World.InputBlocked &&
                !frontend.LiveUi.InputBlocked, "foreground gameplay ready for " + key);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            await Task.Yield(); await Task.Yield();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Until(() => frontend.LiveState.Snapshot.ActiveNanoSlot == expected,
                key + " confirmed carry slot " + expected);
        }

        async Task Walk(Vector3 start)
        {
            // The four directions let the original CharacterController avoid the portal and incidental world geometry.
            foreach (var key in new[] { Key.W, Key.D, Key.S, Key.A })
            {
                float deadline = Time.realtimeSinceStartup + 4;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                try
                {
                    while (Time.realtimeSinceStartup < deadline && Horizontal(subject.position - start) < 8)
                        await Task.Yield();
                }
                finally { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); }
                await Task.Yield(); await Task.Yield();
                if (Horizontal(subject.position - start) >= 7)
                {
                    Check(true, "player walked with " + key + " using the world movement controller");
                    return;
                }
            }
            throw new InvalidOperationException("World movement keys could not walk seven metres from the initial position.");
        }

        static bool AnyVisibleCompanion() => FindObjectsByType<WorldNanoCompanion>(FindObjectsSortMode.None)
            .Any(c => c.Model && c.Model.activeInHierarchy);

        async Task UseWarp(Warp warp)
        {
            World.MovementEnabled = false;
            await frontend.LiveMoveTo(warp.npcX, warp.npcY, warp.npcZ);
            NetworkEntityView npc = null;
            await Until(() => World.IsReady && (npc = World.Replicas.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc &&
                a.Entity.NpcType == warp.npcType && Math.Abs(a.Entity.X - warp.npcX) < 500 && Math.Abs(a.Entity.Y - warp.npcY) < 500)), "portal NPC in interest");
            Check(frontend.LiveUi.OpenNpcTravel(npc.Entity, npc.Definition), "opened portal service");
            await Task.Yield();
            Submit(frontend.LiveUi.Windows.View.Q<Button>("npc-warp-" + warp.warp));
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Until(() => World && World.MapNumber == warp.map && World.IsReady && World.MovementEnabled &&
                !frontend.LiveUi.TravelBusy, "entered destination map " + warp.map);
        }

        static void Submit(Button button)
        {
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Expected available portal action.");
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button; button.SendEvent(submit);
        }

        async Task<GameObject> Visible(int id, string phase)
        {
            await Until(() => Companion && Companion.NanoId == id && Model && Model.activeInHierarchy &&
                Vector3.Distance(subject.position, Model.transform.position) < 6 &&
                Model.GetComponentsInChildren<Renderer>().Any(r => r.enabled && !r.forceRenderingOff), phase + " rendered model near owner");
            var model = Model;
            var animationPlayer = model.GetComponentInChildren<Animation>(true);
            await Until(() => model && (!animationPlayer || !animationPlayer.IsPlaying("call")),
                phase + " summon presentation settled before capture");
            Check(FindObjectsByType<WorldNanoCompanion>(FindObjectsSortMode.None).Count(c => c.Model && c.Model.activeInHierarchy) == 1,
                phase + " one active world companion across player replicas");
            Capture(phase, id, model);
            return model;
        }

        bool Capture(string phase, int id, GameObject model, bool vfx = false)
        {
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !r.forceRenderingOff).ToArray();
            if (renderers.Length == 0) return false;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var focus = (bounds.center + subject.position + Vector3.up) * .5f;
            var camera = World.View;
            var cameraPose = camera.transform;
            var position = cameraPose.position; var rotation = cameraPose.rotation;
            var previous = camera.targetTexture;
            var surface = RenderTexture.GetTemporary(800, 450, 24);
            try
            {
                camera.targetTexture = surface;
                // Frame avatar and companion together; identical camera and simulation frame for each shown/hidden pair.
                foreach (var angle in new[] { new Vector3(.65f,.35f,-1), new Vector3(-1,.5f,.65f), new Vector3(.2f,1,.4f) })
                {
                    cameraPose.position = focus + angle.normalized * Mathf.Max(4, Vector3.Distance(subject.position, bounds.center) * 2.1f);
                    cameraPose.LookAt(focus);
                    Texture2D shown = null, hidden = null;
                    try
                    {
                        shown = ReadFrame(camera, surface);
                        foreach (var r in renderers) r.forceRenderingOff = true;
                        hidden = ReadFrame(camera, surface);
                        var a = shown.GetPixelData<Color32>(0);
                        var b = hidden.GetPixelData<Color32>(0);
                        int changed = 0;
                        var lo = camera.WorldToViewportPoint(bounds.center);
                        if (lo.z <= 0 || lo.x < 0 || lo.x > 1 || lo.y < 0 || lo.y > 1) continue;
                        for (int i = 0; i < a.Length; i++)
                            if (Math.Abs(a[i].r-b[i].r) + Math.Abs(a[i].g-b[i].g) + Math.Abs(a[i].b-b[i].b) > 28) changed++;
                        if (changed < 32) continue;
                        File.WriteAllBytes(Path.Combine(directory, phase + ".png"), shown.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(directory, phase + "-hidden.png"), hidden.EncodeToPNG());
                        if (vfx) effectCaptures.Add(new { phase, effectId = id, visiblePixels = changed,
                            objectId = model.GetInstanceID(), root = model.name, position = Pos(model.transform.position) });
                        else captures.Add(new { phase, nano = id, visiblePixels = changed, objectId = model.GetInstanceID(),
                            model = model.name, avatarPosition = Pos(subject.position), nanoPosition = Pos(model.transform.position) });
                        return true;
                    }
                    finally
                    {
                        foreach (var r in renderers) r.forceRenderingOff = false;
                        if (shown) Destroy(shown);
                        if (hidden) Destroy(hidden);
                    }
                }
                if (vfx) return false;
                throw new InvalidOperationException(phase + ": companion yielded fewer than 32 visible pixel differences.");
            }
            finally
            {
                camera.targetTexture = previous;
                cameraPose.SetPositionAndRotation(position, rotation);
                RenderTexture.ReleaseTemporary(surface);
            }
        }

        static Texture2D ReadFrame(Camera camera, RenderTexture surface)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = surface };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Render request unsupported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            var old = RenderTexture.active;
            try
            {
                RenderTexture.active = surface;
                var texture = new Texture2D(surface.width, surface.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0,0,surface.width,surface.height),0,0); texture.Apply();
                return texture;
            }
            finally { RenderTexture.active = old; }
        }
        static float[] Pos(Vector3 p) => new[] { p.x,p.y,p.z };
        static float Horizontal(Vector3 p) => new Vector2(p.x, p.z).magnitude;
        string Marker(string writer, string phase) => Path.Combine(sync, writer + "-" + phase + ".txt");
        void Mark(string phase, string message)
        {
            string path = Marker(role, phase);
            File.WriteAllText(path + ".tmp", message);
            File.Move(path + ".tmp", path);
        }
        async Task Barrier(string phase)
        {
            Mark(phase,"ok");
            await Until(() => File.Exists(Marker(peer, phase)), phase + " verified in peer process");
        }
        async Task Until(Func<bool> test, string message)
        {
            float end = Time.realtimeSinceStartup + 120;
            while (!test())
            {
                if (File.Exists(Marker(peer,"failed"))) throw new InvalidOperationException("Peer failed during " + message);
                if (Time.realtimeSinceStartup >= end) throw new TimeoutException(message);
                await Task.Yield();
            }
            Check(true,message);
        }
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            checks.Add(message);
            Debug.Log("NANO COMPANION " + role + ": " + message);
        }
        void Finish(bool passed, string error)
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (Directory.Exists(directory))
                File.WriteAllText(Path.Combine(directory, "nano-companion-result.json"),
                    JsonConvert.SerializeObject(new { role, ownerId, passed, checks, captures, effectCaptures,
                        voiceLanguage = frontend ? frontend.VoiceLanguage : null, audio = AudioResults(), skillReceipts, error }, Formatting.Indented));
            Application.Quit(passed ? 0 : 1);
        }
    }
}
#endif
