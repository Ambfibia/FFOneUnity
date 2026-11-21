using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFOne.Tutorial;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class TutorialHudTests
    {
        [Test]
        public void TutorialHudUsesLocalHealthAndTemporaryButtercupWithoutChangingEntry()
        {
            var session = GameplayUiValidationData.Session();
            var entry = session.Snapshot;
            var local = TutorialController.ProjectTutorialHud(entry, 430, true, true, 150);
            Assert.That(local.Hp, Is.EqualTo(430));
            Assert.That(local.MaxHp, Is.EqualTo(1000));
            Assert.That(local.HealthOrigin, Is.EqualTo(GameplayHealthOrigin.LocalTutorial));
            Assert.That(local.NanoSlots, Is.EqualTo(new[] { 1, 0, 0 }));
            Assert.That(local.ActiveNanoSlot, Is.Zero);
            Assert.That(local.Nanos.Single(n => n.Id == 1).SkillId, Is.EqualTo(1));
            Assert.That(entry.Hp, Is.EqualTo(700));
            Assert.That(entry.NanoSlots, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(entry.ActiveNanoSlot, Is.EqualTo(-1));
            Assert.That(session.Snapshot, Is.SameAs(entry));
            Assert.That(local.Equipment, Is.SameAs(entry.Equipment));
            Assert.That(local.RunningQuests, Is.SameAs(entry.RunningQuests));
            var beforeAward = TutorialController.ProjectTutorialHud(entry, 1000, false, false, 150);
            Assert.That(beforeAward.NanoSlots, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(beforeAward.ActiveNanoSlot, Is.EqualTo(-1));
        }

        [Test]
        public void MissionControlsRespectVoicedDelayAndJournalCloseLesson()
        {
            var owner = new GameObject("Tutorial UI stage gates");
            var tutorial = owner.AddComponent<TutorialController>();
            try
            {
                Stage(3, 100); Assert.That(Flag("TutorialMissionAccept"), Is.False, "Do not accept during Computress's explanation.");
                Stage(3, 101); Assert.That(Flag("TutorialMissionAccept"), Is.True);
                Stage(3, 17); Assert.That(Flag("TutorialMissionReward"), Is.True);
                Stage(3, 8); Assert.That(Flag("TutorialWindowCanClose"), Is.False, "Keep the journal visible through its guided tour.");
                Stage(3, 11); Assert.That(Flag("TutorialWindowCanClose"), Is.False);
                Stage(3, 12); Assert.That(Flag("TutorialWindowCanClose"), Is.True);
                Stage(4, 2); Assert.That(Flag("TutorialMissionAccept"), Is.True);
                Stage(4, 101); Assert.That(Flag("TutorialMissionAccept"), Is.False);
                Stage(4, 109); Assert.That(Flag("TutorialMissionAccept"), Is.True);
            }
            finally { Object.DestroyImmediate(owner); }

            void Stage(int chapter, int step) => typeof(TutorialController).GetProperty("Stage").SetValue(tutorial, TutorialStages.Get(chapter, step));
            bool Flag(string name) => (bool)typeof(TutorialController).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tutorial);
        }

        [Test]
        public void TutorialMinimapSamplesTheCorrectOriginalTileAtStart()
        {
            var samples = new List<TutorialMinimapLayout.Sample>();
            TutorialMinimapLayout.Tiles(TutorialController.StartPosition, 8, samples);
            Assert.That(samples.Count, Is.EqualTo(1));
            Assert.That(samples[0].Tile, Is.EqualTo(13), "Original map rows run from the north, Unity Z increases northwards.");
            Assert.That(samples[0].Destination, Is.EqualTo(new Rect(0, 0, 148, 148)));
            Assert.That(samples[0].Uv.center.x, Is.EqualTo(547f / 2048).Within(.00001));
            Assert.That(samples[0].Uv.center.y, Is.EqualTo(655f / 2048).Within(.00001));
        }

        [Test]
        public void TargetReticlesKeepScreenPixelSizeAndCenterAcrossViewportChanges()
        {
            foreach (Vector2 center in new[] { new Vector2(512, 384), new Vector2(960, 540), new Vector2(1920, 1080) })
            {
                var hostile = TutorialTargetHudLayout.Reticle(center, true);
                var friendly = TutorialTargetHudLayout.Reticle(center, false);
                Assert.That(hostile.center, Is.EqualTo(center));
                Assert.That(friendly.center, Is.EqualTo(center));
                Assert.That(hostile.size, Is.EqualTo(new Vector2(100, 86)));
                Assert.That(friendly.size, Is.EqualTo(new Vector2(42, 41)));
            }
        }

        [Test]
        public void TargetAffinityRequiresActiveNanoAndPreservesAllThreeCyclicMatchups()
        {
            for (int style = 0; style < 3; style++)
            {
                Assert.That(TutorialTargetHudLayout.Matchup(style, style), Is.EqualTo(1));
                Assert.That(TutorialTargetHudLayout.Matchup((style + 1) % 3, style), Is.Zero);
                Assert.That(TutorialTargetHudLayout.Matchup((style + 2) % 3, style), Is.EqualTo(2));
                Assert.That(TutorialTargetHudLayout.Matchup(style, null), Is.EqualTo(-1));
            }
            Assert.That(TutorialTargetHudLayout.Matchup(-1, 0), Is.EqualTo(-1));
            Assert.That(TutorialTargetHudLayout.Matchup(3, 0), Is.EqualTo(-1));
            Assert.That(TutorialTargetHudLayout.Matchup(0, -1), Is.EqualTo(-1));
            Assert.That(TutorialTargetHudLayout.Matchup(0, 3), Is.EqualTo(-1));
        }

        [Test]
        public void TargetHudBindsBeforePanelLayoutAndHidesHostileFieldsForFriendlyActors()
        {
            var owner = new GameObject("Tutorial target HUD binding");
            var mob = new GameObject("Tutorial target");
            var friend = new GameObject("Tutorial friend");
            var tutorial = owner.AddComponent<TutorialController>();
            var root = new VisualElement();
            try
            {
                Set("tutorialHudRoot", root);
                typeof(TutorialController).GetProperty("Player").SetValue(tutorial, owner.transform);
                Invoke("CreateTutorialTargetHud");
                var actor = mob.AddComponent<TutorialActor>();
                actor.NpcType = 2674; actor.Hostile = true; actor.Style = 1; actor.Hp = 50; actor.MaxHp = 100;
                Set("target", actor); Invoke("TickTutorialTargetHud");
                Assert.That(root.Q("tutorial-target-info").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<Image>("tutorial-target-frame").image, Is.Not.Null);
                Assert.That(root.Q<Image>("tutorial-target-portrait").image, Is.Not.Null);
                Assert.That(root.Q("tutorial-target-hp").style.width.value.value, Is.EqualTo(99.5f));
                Assert.That(root.Q("tutorial-target-hp-damage").style.width.value.value, Is.EqualTo(99.5f));
                Assert.That(root.Q<Label>("tutorial-target-name").enableRichText, Is.False);
                Assert.That(root.Q<Label>("tutorial-target-level").text, Is.EqualTo(GameplayUiCatalog.Load().NpcLevel(2674).Value.ToString("00")));
                Assert.That(root.Q("tutorial-target-reticle").style.display.value, Is.EqualTo(DisplayStyle.None), "An unattached HUD has no viewport yet.");

                var friendly = friend.AddComponent<TutorialActor>();
                friendly.NpcType = 2663; friendly.Hp = friendly.MaxHp = 100;
                Set("target", friendly); Invoke("TickTutorialTargetHud");
                Assert.That(root.Q<Image>("tutorial-target-frame").image, Is.Not.Null);
                foreach (string name in new[] { "hp", "hp-damage", "level", "affinity-back", "affinity-front", "affinity-icon", "matchup" })
                    Assert.That(root.Q("tutorial-target-" + name).style.display.value, Is.EqualTo(DisplayStyle.None), name);
                friend.transform.position = Vector3.right * 20;
                Invoke("TickTutorialTargetHud");
                Assert.That(root.Q("tutorial-target-info").style.display.value, Is.EqualTo(DisplayStyle.None), "Friendly targeting requires the talk distance.");
            }
            finally { Object.DestroyImmediate(friend); Object.DestroyImmediate(mob); Object.DestroyImmediate(owner); }

            void Set(string name, object value) => typeof(TutorialController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tutorial, value);
            void Invoke(string name) => typeof(TutorialController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, null);
        }

        [Test]
        public void TutorialJournalIncludesRealRewardCopyAndDoesNotDuplicateMissingDescriptionFallback()
        {
            var content = GameplayUiCatalog.Load().Mission(2249);
            Assert.That(content, Is.Not.Null);
            string text = TutorialController.TutorialMissionDescription(content, null);
            Assert.That(text, Does.StartWith(content.Objective));
            if (!string.IsNullOrEmpty(content.RewardDescription)) Assert.That(text, Does.Contain(content.RewardDescription));
            Assert.That(TutorialController.TutorialMissionDescription(null, null), Is.Empty);
        }

        [Test]
        public void TutorialInstructionsKeepSourceQuarterScreenAnchorAndSeparateMouseIllustration()
        {
            Assert.That(TutorialInstructionLayout.Text(1280, 720, false, 1), Is.EqualTo(new Rect(0, 180, 1280, 60)));
            Assert.That(TutorialInstructionLayout.Text(1280, 720, true, 1), Is.EqualTo(new Rect(0, 0, 1280, 60)));
            var mouse = TutorialInstructionLayout.Illustration("tut_mouse", new Vector2(116, 154), 720, 1);
            Assert.That(mouse, Is.EqualTo(new Rect(80, 370, 116, 154)));
            var jump = TutorialInstructionLayout.Illustration("tut_jump", new Vector2(298, 91), 720, 1);
            Assert.That(jump, Is.EqualTo(new Rect(0, 430, 298, 91)));
            var arrow = TutorialInstructionLayout.Arrow(3, new Vector2(88, 69), 720, 0, 1);
            Assert.That(arrow, Is.EqualTo(new Rect(176, 451, 88, 69)));
            var scaled = TutorialInstructionLayout.Illustration("tut_mouse", new Vector2(116, 154), 1080, 1.5f);
            Assert.That(1080 - scaled.y, Is.EqualTo(350 * 1.5f));
            Assert.That(scaled.size, Is.EqualTo(mouse.size * 1.5f));
        }

        [Test]
        public void TutorialChromeShowsOnlyEquippedActiveNanoAndKeepsChatInputLocked()
        {
            var owner = new GameObject("Tutorial chrome binding");
            var tutorial = owner.AddComponent<TutorialController>();
            var root = new VisualElement();
            try
            {
                typeof(TutorialController).GetField("tutorialHudRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tutorial, root);
                typeof(TutorialController).GetMethod("CreateTutorialChrome", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, null);
                typeof(TutorialController).GetField("nanoActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tutorial, true);
                Tick(false);
                Assert.That(root.Q("tutorial-active-nano-info").style.display.value, Is.EqualTo(DisplayStyle.None));
                Tick(true);
                Assert.That(root.Q("tutorial-active-nano-info").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<Label>("tutorial-active-nano-name").text, Is.EqualTo("Buttercup"));
                Assert.That(root.Q<Image>("tutorial-active-nano-skill").image, Is.Not.Null);
                Assert.That(root.Q("chat-input").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q("chat-send").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q<Button>("chat-menu").enabledSelf, Is.False);
            }
            finally { Object.DestroyImmediate(owner); }
            void Tick(bool equipped) => typeof(TutorialController).GetMethod("TickTutorialChrome", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, new object[] { equipped });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CombatLeaseUsesFiveSecondsOfRealtimeAndSourceFrameSuppression(bool russian)
        {
            Assert.That(TutorialCombatHudPresentation.IsActive(float.NegativeInfinity, 0), Is.False);
            Assert.That(TutorialCombatHudPresentation.IsActive(10, 10), Is.True);
            Assert.That(TutorialCombatHudPresentation.IsActive(10, 15), Is.True, "EndCombatMode clears only when the source timeout is exceeded.");
            Assert.That(TutorialCombatHudPresentation.IsActive(10, 15.001f), Is.False);
            Assert.That(TutorialCombatHudPresentation.IsActive(10, 9), Is.False);
            Assert.That(TutorialCombatHudPresentation.FrameAlpha(0), Is.EqualTo(.5f).Within(.0001));
            Assert.That(TutorialCombatHudPresentation.FrameAlpha(.25f), Is.EqualTo(1).Within(.0001));
            Assert.That(TutorialCombatHudPresentation.FrameAlpha(.75f), Is.EqualTo(0).Within(.0001));

            var owner = new GameObject("Tutorial combat HUD lease");
            var tutorial = owner.AddComponent<TutorialController>();
            var root = new VisualElement();
            try
            {
                Set("tutorialHudRoot", root);
                typeof(TutorialController).GetProperty(nameof(TutorialController.Russian)).SetValue(tutorial, russian);
                typeof(TutorialController).GetMethod("CreateTutorialChrome", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, null);
                Assert.That(root.Q<Image>("tutorial-combat-danger").image, Is.EqualTo(FFOne.Content.ContentAssets.Load<Texture2D>(
                    "ui/" + (russian ? "ru" : "en") + "/gameplay/shared/danger")));
                typeof(TutorialController).GetMethod("RecordTutorialCombatResult", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, null);
                Tick();
                Assert.That(root.Q("tutorial-combat-frame").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q("tutorial-combat-danger").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.ElementAt(0).name, Is.EqualTo("tutorial-combat-frame"), "The screen frame is behind normal HUD chrome.");
                Set("eventScene", true); Tick();
                Assert.That(root.Q("tutorial-combat-frame").style.display.value, Is.EqualTo(DisplayStyle.None));
                Set("eventScene", false); Tick();
                Assert.That(root.Q("tutorial-combat-frame").style.display.value, Is.EqualTo(DisplayStyle.Flex), "Suppressing an event scene must not clear the live combat lease.");
                Set("tutorialLastCombatResult", Time.realtimeSinceStartup - 5.1f); Tick();
                Assert.That(root.Q("tutorial-combat-frame").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(root.Q("tutorial-combat-danger").style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally { Object.DestroyImmediate(owner); }
            void Set(string name, object value) => typeof(TutorialController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tutorial, value);
            void Tick() => typeof(TutorialController).GetMethod("TickTutorialChrome", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, new object[] { false });
        }

        [Test]
        public void MinimapSplitsAcrossFourTileCornersWithoutGapsOrOutOfRangeUv()
        {
            var samples = new List<TutorialMinimapLayout.Sample>();
            TutorialMinimapLayout.Tiles(new Vector3(2048, 0, 2048), 8, samples);
            Assert.That(samples.Select(s => s.Tile), Is.EquivalentTo(new[] { 9, 10, 13, 14 }));
            Assert.That(samples.Sum(s => s.Destination.width * s.Destination.height), Is.EqualTo(148 * 148).Within(.01));
            foreach (var sample in samples)
            {
                Assert.That(sample.Destination.width, Is.EqualTo(74).Within(.001));
                Assert.That(sample.Destination.height, Is.EqualTo(74).Within(.001));
                Assert.That(sample.Uv.xMin, Is.InRange(0, 1)); Assert.That(sample.Uv.xMax, Is.InRange(0, 1));
                Assert.That(sample.Uv.yMin, Is.InRange(0, 1)); Assert.That(sample.Uv.yMax, Is.InRange(0, 1));
            }
            TutorialMinimapLayout.Tiles(new Vector3(float.NaN, 0, 0), 8, samples);
            Assert.That(samples, Is.Empty);
        }
    }
}
