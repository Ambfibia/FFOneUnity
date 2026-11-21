using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace FFOne.Tests
{
    public sealed class TutorialCinematicPlaybackTests
    {
        GameObject host, cameraRoot, actorRoot;
        TutorialController owner;
        TutorialCinematics cinematic;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void SetUp()
        {
            host = new GameObject("cinematic regression"); owner = host.AddComponent<TutorialController>();
            cameraRoot = new GameObject("camera");
            typeof(TutorialController).GetProperty(nameof(TutorialController.ViewCamera)).SetValue(owner, cameraRoot.AddComponent<Camera>());
            cameraRoot.transform.position = new Vector3(10, 20, 30);
            cinematic = new TutorialCinematics(owner); Call("ResetCamera");
        }
        [TearDown] public void TearDown()
        {
            cinematic.Dispose(); Object.DestroyImmediate(host); Object.DestroyImmediate(cameraRoot);
            if (actorRoot) Object.DestroyImmediate(actorRoot);
        }
        void Call(string method, params object[] args) => typeof(TutorialCinematics).GetMethod(method, Private).Invoke(cinematic, args);
        void Camera(string type, JToken value) => Call("Camera", new JObject { ["type"] = type, ["args"] = new JArray(value) });
        static JObject Position(float x, float y, float z) => new() { ["type"] = "Client", ["args"] = new JArray { new JArray(x, y, z) } };

        [Test]
        public void AuthoredLocationTypewriterUsesTheLocationLabelAndClearsItsMode()
        {
            var actions = ContentAssets.Load<TutorialContent>("Tutorial/Content").Cinematics.Single(s => s.Scene == TutorialScene.BasicMove).Definition()["actions"];
            var title = actions.Single(a => (string)a["action"]["type"] == "Sequence" &&
                (string)a["action"]["args"][0]["type"] == "SubtitleTypewriter")["action"];
            Call("Dispatch", title);
            Assert.That((bool)typeof(TutorialController).GetField("locationCaption", Private).GetValue(owner), Is.True,
                "The authored location key must not select the centered dialogue renderer.");
            Call("Dispatch", new JObject { ["type"] = "SubtitleClear" });
            Assert.That((bool)typeof(TutorialController).GetField("locationCaption", Private).GetValue(owner), Is.False);
        }

        [Test] public void EventCameraReverseInterpolationUsesStoredTargetRatherThanCameraPosition()
        {
            Camera("Mode", new JObject { ["type"] = "EventCameraControl" });
            Camera("Target", Position(100, 50, 200));
            Camera("TargetRotation", new JObject { ["type"] = "Euler", ["args"] = new JArray { new JArray(0, 0, 0) } });
            Camera("Distance", 5); Camera("StoredStart", Position(90, 40, 180)); Camera("ForwardInterpolation", false);
            Call("Advance", .5f);
            Assert.That(Vector3.Distance(cameraRoot.transform.position, new Vector3(95, 45, 185)), Is.LessThan(.0001f));
            Assert.That(Vector3.Angle(cameraRoot.transform.forward, Vector3.forward), Is.LessThan(.001f));
        }

        [Test] public void FromToCameraReseedsItsIndependentStoredPosition()
        {
            Camera("Mode", new JObject { ["type"] = "FromToCamera" });
            Camera("Start", Position(100, 0, 0)); Camera("CurrentTarget", Position(100, 0, 10)); Camera("Target", Position(100, 0, 10));
            Camera("StoredStart", Position(80, 0, 0)); Call("Advance", .5f);
            Assert.That(cameraRoot.transform.position, Is.EqualTo(new Vector3(90, 0, 0)));
        }

        [Test] public void NpcMovementPreservesGroundHeightAndForceUpdateStopsControllerMotion()
        {
            actorRoot = new GameObject("grounded actor"); actorRoot.transform.position = new Vector3(0, 5, 0);
            var actor = actorRoot.AddComponent<TutorialActor>(); actor.RuntimeId = 7;
            ((Dictionary<int, TutorialActor>)typeof(TutorialController).GetField("actors", Private).GetValue(owner)).Add(7, actor);
            Call("Npc", JObject.Parse("{\"type\":\"Move\",\"id\":7,\"target\":{\"type\":\"Client\",\"args\":[[2,-100,0]]},\"speed\":100}"));
            Call("Advance", 1f);
            Assert.That(actorRoot.transform.position, Is.EqualTo(new Vector3(1, 5, 0)), "A server destination height cannot pull a walking actor underground.");
            Call("Npc", JObject.Parse("{\"type\":\"Command\",\"id\":7,\"command\":{\"type\":\"ForceUpdate\"}}"));
            Call("Advance", 1f);
            Assert.That(actor.ForceUpdate, Is.True);
            Assert.That(actorRoot.transform.position, Is.EqualTo(new Vector3(1, 5, 0)));
        }

        [Test] public void PublishedTimelinesAreRuntimeDefinitionsAndPreserveAllOriginalActions()
        {
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            Assert.That(content.Cinematics.Length, Is.EqualTo(10), "Publish editable timelines before shipping.");
            var source = NativeData.Object("data/tutorial/cinematics");
            foreach (var scene in content.Cinematics)
            {
                var definition = scene.Definition();
                var original = System.Linq.Enumerable.Single(source["scenes"], s => (string)s["scene"]["type"] == scene.Scene.ToString());
                Assert.That((float)definition["deterministic_duration_seconds"], Is.EqualTo((float)original["deterministic_duration_seconds"]).Within(.001), scene.name);
                var actions = System.Linq.Enumerable.Where(definition["actions"], a => (string)a["action"]["type"] != "AudioCue");
                Assert.That(System.Linq.Enumerable.Count(actions), Is.EqualTo(original["actions"].Count()), scene.name);
                Assert.That(JToken.DeepEquals(definition["skip"], original["skip"]), Is.True, scene.name + " skip");
            }
        }

        [TestCase(2675, false)]
        [TestCase(2675, true)]
        [TestCase(2677, false)]
        [TestCase(2677, true)]
        public void CyberusAndTechWingIdleCommandsSelectTheirRealLoopingStandClips(int npcType, bool once)
        {
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            var definition = content.Actors.Single(a => a.NpcType == npcType);
            actorRoot = Object.Instantiate(NativeModelAssets.Resolve(definition.Model));
            actorRoot.AddComponent<TutorialActor>().NpcType = npcType;
            var animation = actorRoot.GetComponentInChildren<NativeNpcAnimator>(true);
            Assert.That(animation, Is.Not.Null);
            animation.gameObject.AddComponent<TutorialAnimationEffectReceiver>().Initialize(owner, definition, null);
            Assert.That(animation.GetClip("idle", true), Is.Null, "The source idle request is a command, not a named clip.");
            foreach (string stand in new[] { "stand1", "stand2", "stand3", "stand4" })
                Assert.That(animation.GetClip(stand, true), Is.Not.Null, definition.Name + "/" + stand);
            var randomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(463);
                string expected = TutorialCinematics.SelectIdleStand(UnityEngine.Random.Range(0, 100), null);
                UnityEngine.Random.InitState(463);
                Assert.That(TutorialCinematics.Animate(actorRoot, "idle", once), Is.True);
                Assert.That(animation.CurrentMotion.isLooping, Is.True, "ForceStandMotion also loops an AnimationNpcOnce idle command.");
                Assert.That(animation.CurrentClip, Is.EqualTo(expected), "The selected accepted stand clip must actually play.");
            }
            finally { UnityEngine.Random.state = randomState; }
        }

        [Test] public void IdleStandSelectionPreservesPrimaryWeightsAndAvoidsAlternateRepetition()
        {
            Assert.That(Enumerable.Range(0, 100).Select(roll => TutorialCinematics.SelectIdleStand(roll, null))
                .GroupBy(clip => clip).ToDictionary(group => group.Key, group => group.Count()),
                Is.EquivalentTo(new Dictionary<string, int> { ["stand1"] = 40, ["stand2"] = 30, ["stand3"] = 20, ["stand4"] = 10 }));
            Assert.That(TutorialCinematics.SelectIdleStand(45, "stand2"), Is.EqualTo("stand1"));
            Assert.That(TutorialCinematics.SelectIdleStand(75, "stand3"), Is.EqualTo("stand1"));
            Assert.That(TutorialCinematics.SelectIdleStand(95, "stand4"), Is.EqualTo("stand1"));
            Assert.That(TutorialCinematics.SelectIdleStand(5, "stand1"), Is.EqualTo("stand1"));
        }

        [Test] public void DeathHoldsAndAnAuthoredEndRetriggersTheActiveCinematicMotion()
        {
            actorRoot = new GameObject("forced cinematic NPC");
            actorRoot.AddComponent<TutorialActor>();
            using var rig = new NpcAnimatorTestRig(actorRoot, ("death", false, 0), ("skill0", false, .75f),
                ("melee1", false, .9f), ("ready", true, 0));
            var receiver = actorRoot.AddComponent<TutorialAnimationEffectReceiver>();
            receiver.Initialize(owner, new TutorialActorDefinition(), null);
            Assert.That(TutorialCinematics.Animate(actorRoot, "death", false), Is.True);
            rig.Animator.Update(1.1f);
            receiver.LateUpdate();
            Assert.That(rig.Animation.CurrentClip, Is.EqualTo("death"));
            Assert.That(rig.Animation.CurrentMotion.isLooping, Is.False);

            Assert.That(TutorialCinematics.Animate(actorRoot, "skill0", true), Is.True);
            rig.Advance(.8f);
            float beforePause = rig.Animation.MotionTime;
            Assert.That(beforePause, Is.GreaterThan(.75f));
            receiver.PauseNpc();
            receiver.LateUpdate();
            Assert.That(rig.Animation.MotionTime, Is.EqualTo(beforePause).Within(.001f));
            Assert.That(rig.Animator.speed, Is.Zero);

            Assert.That(TutorialCinematics.Animate(actorRoot, "skill0", true), Is.True);
            Assert.That(rig.Animator.speed, Is.EqualTo(1));
            rig.Advance(.8f);
            receiver.LateUpdate();
            Assert.That(rig.Animation.MotionTime, Is.Zero.Within(.001f), "Actual authored Animator event restarts a clip before its length.");

            receiver.PlayNpc("melee1", TutorialAnimationEffectReceiver.Completion.Ready, 0);
            rig.Animator.Update(.8f);
            receiver.LateUpdate();
            Assert.That(rig.Animation.CurrentClip, Is.EqualTo("melee1"));
            rig.Animator.Update(.15f);
            receiver.LateUpdate();
            Assert.That(rig.Animation.CurrentClip, Is.EqualTo("ready"));
            Assert.That(rig.Animation.CurrentMotion.isLooping, Is.True);
        }

        [Test]
        public void NativeAnimatorMovesTheBoundNodeAndRestoresAnInterruptedFaceCue()
        {
            actorRoot = new GameObject("animated face owner");
            var face = new GameObject("face");
            face.transform.SetParent(actorRoot.transform, false);
            var renderer = face.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Unlit/Texture")) { name = "face_sub" };
            var normal = new Texture2D(1, 1);
            var expression = new Texture2D(1, 1);
            try
            {
                renderer.sharedMaterial = material;
                material.mainTexture = normal;
                using var rig = new NpcAnimatorTestRig(actorRoot, ("talk", false, 0), ("stand1", true, 0));
                rig.Animation.Faces = new[] { new TutorialFaceTexture { Name = "blink", Texture = expression } };
                var talk = rig.Animation.GetClip("talk", false);
                talk.SetCurve("face", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
                UnityEditor.AnimationUtility.SetAnimationEvents(talk, new[]
                {
                    new AnimationEvent { time = .25f, functionName = "FFOneFaceOn", stringParameter = "blink" },
                    new AnimationEvent { time = .75f, functionName = "FFOneFaceOff" }
                });
                rig.Animator.Rebind();

                Assert.That(rig.Animation.Play("talk", false, 0), Is.True);
                rig.Animator.Update(.5f);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Assert.That(face.transform.localPosition.y, Is.EqualTo(.5f).Within(.01f));
                Assert.That(block.GetTexture("_MainTex"), Is.SameAs(expression));

                Assert.That(rig.Animation.Play("stand1", true, 0), Is.True);
                renderer.GetPropertyBlock(block, 0);
                Assert.That(block.GetTexture("_MainTex"), Is.SameAs(normal));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(normal);
                Object.DestroyImmediate(expression);
            }
        }

        [Test] public void StaticLocationActorsKeepTheirAuthoredHeightWithoutGravity()
        {
            actorRoot = new GameObject("static location actor"); actorRoot.transform.position = new Vector3(2, 30, 4);
            var actor = actorRoot.AddComponent<TutorialActor>(); actor.RuntimeId = 9; actor.Class = 100; actor.Hp = 1;
            ((Dictionary<int, TutorialActor>)typeof(TutorialController).GetField("actors", Private).GetValue(owner)).Add(9, actor);
            typeof(TutorialController).GetMethod("GroundActors", Private).Invoke(owner, new object[] { 1f });
            Assert.That(actorRoot.transform.position, Is.EqualTo(new Vector3(2, 30, 4)));
        }

        [Test]
        public void MovingAndArrivingPreserveAnExplicitCinematicPose()
        {
            actorRoot = new GameObject("cutscene actor with forced pose");
            var actor = actorRoot.AddComponent<TutorialActor>();
            actor.RuntimeId = 7;
            ((Dictionary<int, TutorialActor>)typeof(TutorialController).GetField("actors", Private)
                .GetValue(owner)).Add(7, actor);
            using var rig = new NpcAnimatorTestRig(actorRoot, ("stand1", true, 0), ("walk", true, 0),
                ("melee1", true, 0), ("death", false, 0));
            actorRoot.AddComponent<TutorialAnimationEffectReceiver>().Initialize(owner, new TutorialActorDefinition(), null);
            var move = JObject.Parse("{\"type\":\"Move\",\"id\":7,\"target\":{\"type\":\"Client\",\"args\":[[2,0,0]]},\"speed\":100}");
            Call("Npc", move);
            Assert.That(TutorialCinematics.IsPlayingLocomotion(actorRoot), Is.True);
            TutorialCinematics.Animate(actorRoot, "melee1", false);
            Call("Npc", move);
            Assert.That(TutorialCinematics.IsPlayingLocomotion(actorRoot), Is.False);
            Call("Advance", 2f);
            Assert.That(actorRoot.transform.position.x, Is.EqualTo(2f));
            Assert.That(rig.Animation.CurrentClip, Is.EqualTo("melee1"));

            TutorialCinematics.Animate(actorRoot, "death", true);
            rig.Advance(.75f);
            float time = rig.Animation.MotionTime;
            Assert.That(time, Is.GreaterThan(.7f));
            Call("Npc", JObject.Parse("{\"type\":\"Command\",\"id\":7,\"command\":{\"type\":\"Dead\"}}"));
            Assert.That(rig.Animation.MotionTime, Is.EqualTo(time));
        }

        [Test] public void FacingAnActorOnAnotherFloorDoesNotTiltThePlayerOrNpcRoot()
        {
            typeof(TutorialController).GetProperty(nameof(TutorialController.Player)).SetValue(owner, host.transform);
            host.transform.position = new Vector3(10, 30, 0);
            actorRoot = new GameObject("actor on another floor");
            var actor = actorRoot.AddComponent<TutorialActor>(); actor.RuntimeId = 7;
            ((Dictionary<int, TutorialActor>)typeof(TutorialController).GetField("actors", Private).GetValue(owner)).Add(7, actor);
            Call("Player", JObject.Parse("{\"type\":\"Face\",\"args\":[{\"type\":\"Npc\",\"args\":[7]}]}"));
            Assert.That(Vector3.Angle(host.transform.up, Vector3.up), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(host.transform.forward, Vector3.left), Is.LessThan(.001f));
            Call("Npc", JObject.Parse("{\"type\":\"Command\",\"id\":7,\"command\":{\"type\":\"SetAngleTo\",\"args\":[{\"type\":\"Player\"}]}}"));
            Assert.That(Vector3.Angle(actorRoot.transform.up, Vector3.up), Is.LessThan(.001f));
            Assert.That(Vector3.Angle(actorRoot.transform.forward, Vector3.right), Is.LessThan(.001f));
        }

        [Test] public void PlayerGroundContactSurvivesTheControllerSkinGapButNotJumpAscentOrLeavingTheFloor()
        {
            typeof(TutorialController).GetProperty(nameof(TutorialController.Player)).SetValue(owner, host.transform);
            var motor = host.AddComponent<CharacterController>(); motor.height = 1.8f; motor.radius = .45f; motor.center = Vector3.up * .9f;
            typeof(TutorialController).GetField("motor", Private).SetValue(owner, motor);
            actorRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            actorRoot.name = "floor under controller skin gap";
            actorRoot.transform.position = new Vector3(0, -.55f, 0); actorRoot.transform.localScale = new Vector3(10, 1, 10);
            Physics.SyncTransforms();
            Assert.That(motor.isGrounded, Is.False, "The fixture must exercise contact without an existing Below flag.");
            Assert.That(Grounded(), Is.True, "The 5cm skin gap must not alternate idle and falling animation or swallow a jump.");
            typeof(TutorialController).GetField("vertical", Private).SetValue(owner, 5.68f);
            Assert.That(Grounded(), Is.False, "Ascending must leave the ground even while the sphere is still close to the floor.");
            typeof(TutorialController).GetField("vertical", Private).SetValue(owner, 0f);
            host.transform.position = Vector3.up; Physics.SyncTransforms();
            Assert.That(Grounded(), Is.False, "A distant floor must not permit a mid-air jump.");
            bool Grounded() => (bool)typeof(TutorialController).GetMethod("IsPlayerGrounded", Private).Invoke(owner, null);
        }
    }
}
