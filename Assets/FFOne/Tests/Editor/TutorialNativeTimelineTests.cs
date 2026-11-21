using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class TutorialNativeTimelineTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<Object> assets = new();
        GameObject host, camera;
        TutorialController owner;
        TutorialTimelineScene scene;
        TutorialCinematics runtime;
        T Own<T>(T value) where T : Object { assets.Add(value); return value; }
        NativeDataDocument Data(string json)
        {
            var data = Own(ScriptableObject.CreateInstance<NativeDataDocument>()); data.Import(json); return data;
        }
        [SetUp] public void SetUp()
        {
            host = new GameObject("Timeline test owner"); owner = host.AddComponent<TutorialController>();
            camera = new GameObject("Timeline test camera");
            typeof(TutorialController).GetProperty(nameof(TutorialController.ViewCamera)).SetValue(owner, camera.AddComponent<Camera>());
            scene = Own(ScriptableObject.CreateInstance<TutorialTimelineScene>()); scene.Scene = TutorialScene.BasicMove;
            scene.Timeline = Own(ScriptableObject.CreateInstance<TimelineAsset>());
            scene.Timeline.durationMode = TimelineAsset.DurationMode.FixedLength; scene.Timeline.fixedDuration = 2;
            scene.Skip = Data("{\"scene_specific\":[],\"common_cleanup\":[],\"final_fade\":{\"type\":\"TransparentOverlay\"}}");
            scene.Loops = Data("[]");
            runtime = new TutorialCinematics(owner, new[] { scene });
        }
        [TearDown] public void TearDown()
        {
            runtime.Dispose(); Object.DestroyImmediate(host); Object.DestroyImmediate(camera);
            foreach (var asset in assets.AsEnumerable().Reverse()) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
        }
        TimelineClip Clip(string json, double start, double duration, int order, TrackAsset parent = null, bool muted = false)
        {
            var track = Own(scene.Timeline.CreateTrack<TutorialActionTrack>(parent, "action " + order)); track.muted = muted;
            var clip = track.CreateClip<TutorialActionClip>(); clip.start = start; clip.duration = duration;
            var action = Own((TutorialActionClip)clip.asset); action.Action = Data(json); action.Order = order; return clip;
        }
        static string Camera(string type, string argument) => "{\"type\":\"Camera\",\"args\":[{\"type\":\"" + type + "\",\"args\":[" + argument + "]}]}";
        static string Position(int x) => "{\"type\":\"Client\",\"args\":[[" + x + ",0,0]]}";
        float Fade => (float)typeof(TutorialController).GetField("fade", Private).GetValue(owner);

        [Test] public void DirectorGraphOwnsTimeAndRealPlayableClipsSampleResizedSequences()
        {
            Clip("{\"type\":\"Sequence\",\"args\":[{\"type\":\"Fade\",\"args\":[{\"channel\":{\"type\":\"Overlay\"},\"from\":0,\"to\":1,\"steps\":4,\"seconds_per_step\":0.25,\"divisor\":3}]}]}", 0, 2, 0);
            Clip("{\"type\":\"Sequence\",\"args\":[{\"type\":\"Fade\",\"args\":[{\"channel\":{\"type\":\"CinematicBars\"},\"from\":0,\"to\":1,\"steps\":4,\"seconds_per_step\":0.25,\"divisor\":3}]}]}", 0, 2, 1);
            runtime.Begin(scene.Scene); Assert.That(Fade, Is.Zero);
            Assert.That(runtime.Director.playableAsset, Is.SameAs(scene.Timeline));
            runtime.Tick(1);
            Assert.That(runtime.Elapsed, Is.EqualTo(1).Within(.00001));
            Assert.That(runtime.Director.time, Is.EqualTo(runtime.Elapsed).Within(.00001));
            Assert.That(Fade, Is.EqualTo(2f / 3).Within(.00001));
            runtime.Director.Pause(); runtime.Tick(.5f);
            Assert.That(runtime.Elapsed, Is.EqualTo(1).Within(.00001));
            runtime.Director.Resume();
            var graph = runtime.Director.playableGraph;
            Assert.That(graph.GetRootPlayable(0).GetPlayableType(), Is.EqualTo(typeof(TutorialSceneBehaviour)));
            var behaviours = new List<TutorialActionBehaviour>();
            var visited = new HashSet<Playable>();
            void Visit(Playable node)
            {
                if (!visited.Add(node)) return;
                if (node.GetPlayableType() == typeof(TutorialActionBehaviour)) behaviours.Add(((ScriptPlayable<TutorialActionBehaviour>)node).GetBehaviour());
                for (int i = 0; i < node.GetInputCount(); i++) if (node.GetInput(i).IsValid()) Visit(node.GetInput(i));
            }
            Visit(graph.GetRootPlayable(0));
            Assert.That(behaviours.Count, Is.EqualTo(2)); Assert.That(behaviours.All(b => b.EvaluatedFrames > 0), Is.True);
            runtime.Tick(1); Assert.That(runtime.Finished, Is.True); Assert.That(Fade, Is.EqualTo(1));
        }
        [Test] public void LongFramesPreserveCrossTrackOrderCapturesAndMutedGroups()
        {
            Clip(Camera("Mode", "{\"type\":\"FromToCamera\"}"), 0, .01, 0);
            Clip(Camera("ForwardInterpolation", "false"), 0, .01, 1);
            Clip(Camera("Start", Position(1)), .1, .01, 20);
            Clip(Camera("Start", Position(2)), .1, .01, 10);
            Clip(Camera("Start", Position(3)), .1, .01, 30, muted: true);
            var group = Own(scene.Timeline.CreateTrack<GroupTrack>(null, "muted parent")); group.muted = true;
            Clip(Camera("Start", Position(99)), .1, .01, 99, group);
            Clip(Camera("CaptureTransform", "{\"type\":\"Before\"}"), .2, .01, 40);
            Clip(Camera("Start", Position(4)), .3, .01, 50);
            runtime.Begin(scene.Scene); runtime.Tick(.4f);
            var captured = runtime.ResolvePosition(JObject.Parse("{\"type\":\"CapturedCamera\",\"args\":[{\"type\":\"Before\"}]}"));
            Assert.That(captured, Is.EqualTo(Vector3.right));
            Assert.That(camera.transform.position, Is.EqualTo(Vector3.right * 4));
        }
        [Test] public void SkipStopsTheGraphAndRestartHasFreshClipState()
        {
            Clip("{\"type\":\"Sequence\",\"args\":[{\"type\":\"Fade\",\"args\":[{\"channel\":{\"type\":\"Overlay\"},\"from\":0,\"to\":1,\"steps\":4,\"seconds_per_step\":0.25,\"divisor\":3}]}]}", 0, 2, 0);
            runtime.Begin(scene.Scene); runtime.Tick(1); Assert.That(Fade, Is.GreaterThan(0));
            runtime.Skip(); runtime.Tick(1); Assert.That(runtime.Finished, Is.True); Assert.That(Fade, Is.Zero);
            Assert.That(runtime.Director.state, Is.EqualTo(PlayState.Paused));
            runtime.Begin(scene.Scene); Assert.That(runtime.Elapsed, Is.Zero); Assert.That(Fade, Is.Zero);
            runtime.Tick(.5f); Assert.That(Fade, Is.EqualTo(1f / 3).Within(.00001));
        }
        [Test] public void CinematicWarpSettlesWithoutChangingTheScriptedHorizontalPosition()
        {
            var player = Own(new GameObject("cinematic player"));
            var motor = player.AddComponent<CharacterController>();
            NativePlayerMotor.Configure(motor);
            typeof(TutorialController).GetProperty(nameof(TutorialController.Player)).SetValue(owner, player.transform);
            typeof(TutorialController).GetField("motor", Private).SetValue(owner, motor);
            var floor = Own(new GameObject("cinematic floor"));
            floor.transform.position = new Vector3(11, -.5f, 13);
            floor.AddComponent<BoxCollider>().size = new Vector3(4, 1, 4);
            Physics.SyncTransforms();
            Clip("{\"type\":\"Player\",\"args\":[{\"type\":\"Warp\",\"args\":[{\"type\":\"Client\",\"args\":[[11,1,13]]}]}]}", 0, .01, 0);

            runtime.Begin(scene.Scene);
            Assert.That(player.transform.position, Is.EqualTo(new Vector3(11, 1, 13)));
            runtime.Director.Pause();
            runtime.Tick(.1f);
            Assert.That(player.transform.position.y, Is.EqualTo(1));
            runtime.Director.Resume();
            for (int frame = 0; frame < 60; frame++) runtime.Tick(1f / 60);

            Assert.That(player.transform.position.x, Is.EqualTo(11).Within(.001f));
            Assert.That(player.transform.position.z, Is.EqualTo(13).Within(.001f));
            Assert.That(player.transform.position.y, Is.EqualTo(0).Within(.08f));
            Assert.That(NativePlayerMotor.IsGrounded(motor, 0), Is.True);
        }
        [Test] public void FractionalDurationFinishesAndExecutesTheTerminalAction()
        {
            scene.Timeline.fixedDuration = 47.9;
            Clip(Camera("LookAt", Position(1)), scene.Timeline.duration, .01, 0);

            runtime.Begin(scene.Scene);
            runtime.Tick(47);
            runtime.Tick(.9f);
            runtime.Tick(.1f);

            Assert.That(runtime.Finished, Is.True);
            Assert.That(camera.transform.forward.x, Is.EqualTo(1).Within(.00001));
        }
        [Test] public void ActionAdaptersReadEveryAuthoredNodeWithoutSerializingOrParsingJson()
        {
            foreach (var authored in ContentAssets.Load<TutorialContent>("Tutorial/Content").Cinematics)
            {
                var documents = authored.Timeline.GetOutputTracks().SelectMany(t => t.GetClips())
                    .Select(c => ((TutorialActionClip)c.asset).Action).Concat(new[] { authored.Skip, authored.Loops });
                foreach (var data in documents) Assert.That(JToken.DeepEquals(TutorialActionData.Read(data), data.ToToken()), Is.True, data.name);
            }
        }
        [Test] public void PlayableCallbackFailuresReachTheControllersCleanupBoundary()
        {
            Clip("{\"type\":\"Npc\",\"args\":[{\"type\":\"SetPosition\",\"id\":999,\"target\":{\"type\":\"Client\",\"args\":[[0,0,0]]}}]}", .1, .01, 0);
            runtime.Begin(scene.Scene);
            var error = Assert.Throws<System.InvalidOperationException>(() => runtime.Tick(.2f));
            Assert.That(error.Message, Does.Contain("999"));
            runtime.Dispose(); Assert.DoesNotThrow(() => runtime.Tick(0));
        }
    }
}
