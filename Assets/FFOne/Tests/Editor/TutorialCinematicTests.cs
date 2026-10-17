using ContentPaths = FFOne.Content.ContentPaths;
using System.Collections.Generic;
using System.Linq;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialCinematicTests
    {
        // These are the actors required by the next playable step, including the
        // source's explicit 3000 replacement from Numbuh Two to the combat rig.
        static IEnumerable<TestCaseData> CastCases()
        {
            yield return Cast("BasicMove", new int[0], new[] { 100, 2669, 101, 2670 });
            yield return Cast("BasicCombatA", new[] { 100, 2669, 101, 2670 }, new[] { 100, 2669, 101, 2670, 1001, 2674, 1002, 2674, 1003, 2674, 1004, 2897 });
            yield return Cast("BasicCombatB", new[] { 100, 2669, 101, 2670, 1004, 2897 }, new[] { 100, 2669, 101, 2670, 1005, 2675 });
            yield return Cast("BasicCombatC", new[] { 100, 2669, 101, 2670, 1005, 2675 }, new int[0]);
            yield return Cast("InfectionA", new int[0], new int[0]);
            yield return Cast("InfectionB", new[] { 3000, 2673 }, new[] { 3000, 2902, 4000, 2678 });
            yield return Cast("InfectionC", new[] { 3000, 2902, 4000, 2678 }, new[] { 4001, 2696 });
            yield return Cast("NanoPowerA2", new int[0], new[] { 5000, 2677 });
        }

        static TestCaseData Cast(string scene, int[] seed, int[] expected) =>
            new TestCaseData(scene, seed, expected).SetName("Cinematic_cast_survives_every_skip_checkpoint_" + scene);

        [TestCaseSource(nameof(CastCases))]
        public void SkipAtEveryAuthoredPointLeavesTheCastRequiredByTheNextStep(string name, int[] seed, int[] expected)
        {
            var scene = Scene(name);
            var actions = scene["actions"].ToArray();
            var natural = Roster(seed);
            foreach (var item in actions) ApplyRoster(natural, item["action"]);
            Assert.That(natural, Is.EquivalentTo(Roster(expected)), name + " natural ending");

            // Includes skipping before the coroutine has spawned anything, and
            // immediately after each intermediate replacement/deletion point.
            foreach (float checkpoint in new[] { -1f }.Concat(actions.Select(a => (float)a["time"]).Distinct()))
            {
                var roster = Roster(seed);
                foreach (var item in actions.Where(a => (float)a["time"] <= checkpoint)) ApplyRoster(roster, item["action"]);
                foreach (var action in scene["skip"]["scene_specific"]) ApplyRoster(roster, action);
                Assert.That(roster, Is.EquivalentTo(Roster(expected)), name + " skipped at " + checkpoint);
            }
        }

        [Test]
        public void ComicPanHasSourceTimingAndBothExitPathsReleaseAllFrames()
        {
            var scene = Scene("BasicCombatC");
            var pan = scene["actions"].Where(a => Kind(a["action"]) == "Pan").ToArray();
            Assert.That(pan.Select(a => Kind(Arg(a["action"]))), Is.EqualTo(new[] { "LoadEightTextures", "Start", "Stop", "ReleaseEightTextures" }));
            Assert.That(pan.Select(a => (float)a["time"]), Is.EqualTo(new[] { 0f, 17.5f, 39f, 45f }));
            Assert.That(scene["skip"]["scene_specific"].Where(a => Kind(a) == "Pan").Select(a => Kind(Arg(a))), Is.EqualTo(new[] { "Stop", "ReleaseEightTextures" }));
        }

        [Test]
        public void QuakeSurvivesInfectionEndingAndPortalEndingRemainsOpaque()
        {
            var quake = Scene("InfectionC")["loops"].Single();
            Assert.That((string)quake["cue"], Is.EqualTo("LairCollapse_Quake_LOOP"));
            Assert.That((bool)quake["persists_after_scene"], Is.True);
            Assert.That(Kind(quake["skip_action"]), Is.EqualTo("StartIfAbsent"));
            var nextSceneStop = Scene("NanoPowerA")["actions"].Single(a => Kind(a["action"]) == "Loop" && Kind(Arg(a["action"])) == "StopIfPresent");
            Assert.That((float)nextSceneStop["time"], Is.EqualTo(13.5f));
            Assert.That(Kind(Arg(nextSceneStop["action"])), Is.EqualTo("StopIfPresent"));
            Assert.That(Kind(Scene("NanoPowerB")["skip"]["final_fade"]), Is.EqualTo("OpaqueOverlay"));
        }

        [Test]
        public void SerializedStartAnchorDoesNotFollowThePlayersLiveTransform()
        {
            var host = new GameObject("cinematic anchor fixture");
            var player = new GameObject("moved player");
            try
            {
                var owner = host.AddComponent<TutorialController>();
                typeof(TutorialController).GetProperty(nameof(TutorialController.Player)).SetValue(owner, player.transform);
                player.transform.position = new Vector3(907, 11, 687);
                var expressions = new TutorialCinematicExpressions(owner);
                var start = Entity("StartPosition");
                Assert.That(expressions.Position(new JObject { ["type"] = "Entity", ["args"] = new JArray(start) }), Is.EqualTo(TutorialController.StartPosition));
                var offset = new JObject { ["type"] = "EntityOffset", ["entity"] = start.DeepClone(), ["offset"] = new JArray(0, 1, 0) };
                Assert.That(expressions.Position(offset), Is.EqualTo(TutorialController.StartPosition + Vector3.up));
                var midpoint = new JObject { ["type"] = "Midpoint", ["left"] = start.DeepClone(), ["right"] = Entity("Player") };
                Assert.That(expressions.Position(midpoint), Is.EqualTo((TutorialController.StartPosition + player.transform.position) / 2));
                player.transform.position += Vector3.one * 100;
                Assert.That(expressions.Position(offset), Is.EqualTo(TutorialController.StartPosition + Vector3.up));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(player); }
        }

        [Test]
        public void InfectionCameraCaptureKeepsItsPoseAfterTheLiveCameraMoves()
        {
            var host = new GameObject("cinematic camera fixture");
            var cameraRoot = new GameObject("captured camera");
            try
            {
                var owner = host.AddComponent<TutorialController>();
                var camera = cameraRoot.AddComponent<Camera>();
                typeof(TutorialController).GetProperty(nameof(TutorialController.ViewCamera)).SetValue(owner, camera);
                var expressions = new TutorialCinematicExpressions(owner);
                var position = new Vector3(574, -128, 969);
                camera.transform.SetPositionAndRotation(position, Quaternion.Euler(25, 45, 0));
                expressions.Capture("InfectionEntry");
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var captured = new JObject { ["type"] = "CapturedCamera", ["args"] = new JArray(Entity("InfectionEntry")) };
                Assert.That(expressions.Position(captured), Is.EqualTo(position));
                var offset = new JObject {
                    ["type"] = "OrientedOffset", ["origin"] = captured,
                    ["world_offset"] = new JArray(0, 2, 0), ["local_offset"] = new JArray(0, 0, -6),
                    ["orientation"] = new JObject { ["type"] = "CapturedCameraYaw", ["args"] = new JArray(Entity("InfectionEntry")) }
                };
                var expected = position + Vector3.up * 2 + Quaternion.Euler(0, 45, 0) * new Vector3(0, 0, -6);
                Assert.That(Vector3.Distance(expressions.Position(offset), expected), Is.LessThan(.0001f));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(cameraRoot); }
        }

        static JObject Entity(string kind) => new JObject { ["type"] = kind };
        static string Kind(JToken token) => (string)token?["type"];
        static JToken Arg(JToken token) => token?["args"]?[0];
        static JObject Scene(string name)
        {
            var text = FFOne.Content.ContentAssets.Load<TextAsset>("data/tutorial/cinematics");
            Assert.That(text, Is.Not.Null, "Tutorial choreography must be imported.");
            return JObject.Parse(text.text)["scenes"].OfType<JObject>().Single(s => Kind(s["scene"]) == name);
        }
        static Dictionary<int, int> Roster(int[] pairs)
        {
            var roster = new Dictionary<int, int>();
            for (int i = 0; i < pairs.Length; i += 2) roster.Add(pairs[i], pairs[i + 1]);
            return roster;
        }
        static void ApplyRoster(Dictionary<int, int> roster, JToken action)
        {
            if (Kind(action) != "Npc") return;
            var npc = Arg(action);
            switch (Kind(npc))
            {
                case "Spawn": roster[(int)Arg(npc)["id"]] = (int)Arg(npc)["npc_type"]; break;
                case "SpawnBatch": foreach (var spawn in Arg(npc)) roster[(int)spawn["id"]] = (int)spawn["npc_type"]; break;
                case "Delete": roster.Remove((int)Arg(npc)); break;
                case "DeleteInclusive": for (int id = (int)npc["first"]; id <= (int)npc["last"]; id++) roster.Remove(id); break;
            }
        }
    }
}
