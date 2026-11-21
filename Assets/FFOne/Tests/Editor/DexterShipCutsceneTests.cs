using System.Collections;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FFOne.Tests
{
    public sealed class DexterShipCutsceneTests
    {
        [TestCase("TECH SQUARE.%sTHE FUTURE.", "TECH SQUARE.\nTHE FUTURE.")]
        [TestCase("A%sB%sC", "A\nB\nC")]
        [TestCase("СКВЕР ТЕХНОЛОГИЙ.%sБУДУЩЕЕ.", "СКВЕР ТЕХНОЛОГИЙ.\nБУДУЩЕЕ.")]
        public void LegacyCinematicBreaksBecomeRealLines(string source, string expected)
            => Assert.That(DexterShipTimeline.NormalizeSubtitle(source), Is.EqualTo(expected));

        [UnityTest]
        public IEnumerator WarmupLoadsBothSetsWithoutCreatingPlaybackObjects()
        {
            var host = new GameObject("cutscene asset warmup fixture");
            try
            {
                var cutscene = host.AddComponent<DexterShipCutscene>();
                var first = cutscene.PreloadBeforeCreationAsync("en");
                while (!first.IsCompleted) yield return null;
                Assert.That(first.IsCompletedSuccessfully, Is.True, first.Exception?.ToString());
                var tutorial = cutscene.PreloadBeforeTutorialAsync("en");
                while (!tutorial.IsCompleted) yield return null;
                Assert.That(tutorial.IsCompletedSuccessfully, Is.True, tutorial.Exception?.ToString());
                Assert.That(host.transform.childCount, Is.Zero);
                Assert.That(cutscene.IsPlaying, Is.False);
                foreach (string field in new[] { "overlay", "sceneRoot", "computerAssets", "hologramMaterial", "sceneTexture", "outputTexture" })
                    Assert.That(typeof(DexterShipCutscene).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cutscene), Is.Null, field);
                Assert.That(cutscene.PreloadBeforeCreationAsync("en").IsCompletedSuccessfully, Is.True, "The prepared set should be reused immediately.");
                Assert.That(cutscene.PreloadBeforeTutorialAsync("en").IsCompletedSuccessfully, Is.True, "The prepared set should be reused immediately.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ExactNativeActorsContainEveryAuthoredPerformance()
        {
            string[][] required = {
                new[] { "stand1", "observe", "cutscene_stand_1", "excellent", "deedeeno", "busyrun" },
                new[] { "stand1", "walk" }, new[] { "stand1", "event1", "event2", "stand2" }
            };
            for (int i = 0; i < required.Length; i++)
            {
                var model = NativeModelAssets.Load(DexterShipTimeline.ActorModels[i]);
                Assert.That(model, Is.Not.Null, DexterShipTimeline.ActorModels[i]);
                foreach (string clip in required[i])
                    Assert.That(model.GetComponentsInChildren<NativeNpcAnimator>(true).Any(a => a.GetClip(clip, false) || a.GetClip(clip, true)), Is.True, model.name + ":" + clip);
            }
            var computer = NativeModelAssets.Load(DexterShipTimeline.ComputerModel);
            Assert.That(computer, Is.Not.Null);
            Assert.That(computer.GetComponentsInChildren<Animation>(true).Any(a => a["nif-default"] != null), Is.True);
        }

        [Test]
        public void BothScenesResolveTheirExactAudioAndHologramAssets()
        {
            foreach (var cue in DexterShipTimeline.Creation.Concat(DexterShipTimeline.Tutorial).Where(c => c.Action != 3))
                Assert.That(ContentAssets.Load<AudioClip>(cue.Resource("en")), Is.Not.Null, cue.Resource("en"));
            Assert.That(ContentAssets.Load<Texture2D>(DexterShipTimeline.Background), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(DexterShipTimeline.Scanline), Is.Not.Null);
            Assert.That(ContentAssets.Load<AudioClip>("audio/music/charactercreation_loop"), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(DexterShipTimeline.DexterBodyTexture), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(DexterShipTimeline.DexterFaceTexture), Is.Not.Null);
            Assert.That(ContentAssets.Load<UnityEngine.TextCore.Text.FontAsset>("fonts/jeffe-sdf"), Is.Not.Null);
            Assert.That(ContentAssets.Load<UnityEngine.TextCore.Text.FontAsset>("fonts/chaletbook-regular-sdf"), Is.Not.Null);
            Assert.That(ContentAssets.Load<Shader>("Tutorial/DexterShipHologram"), Is.Not.Null);
        }

        [Test]
        public void CapsuleBreakdownFinishesItsFadeBeforeTheTutorialTransition()
        {
            Assert.That(DexterShipTimeline.Tutorial.Select(c => c.Time), Is.Ordered);
            Assert.That(DexterShipTimeline.DexterClip(false, 27.4f), Is.EqualTo("deedeeno"));
            Assert.That(DexterShipTimeline.DexterClip(false, 28.9f), Is.EqualTo("busyrun"));
            Assert.That(DexterShipTimeline.DexterPosition(false, 30), Is.EqualTo(new Vector3(-2, 0, 0)));
            Assert.That(DexterShipTimeline.Fade(false, 32.5f), Is.EqualTo(1));
            Assert.That(DexterShipTimeline.Tutorial.Last().Time, Is.LessThan(DexterShipTimeline.TutorialDuration));
            // TutorialEvent explicitly clears the engine-start speech, while
            // Dee Dee's final interruption remains until the scene ends.
            Assert.That(DexterShipTimeline.SubtitleLine(false, 16), Is.Zero);
            Assert.That(DexterShipTimeline.SubtitleLine(false, 32), Is.EqualTo(10));
        }

        [Test]
        public void DexterBodyBindsPerInstanceWithoutRepaintingHisGlassesOrSword()
        {
            var prefab = NativeModelAssets.Load(DexterShipTimeline.ActorModels[0]);
            var first = Object.Instantiate(prefab); var second = Object.Instantiate(prefab);
            var texture = ContentAssets.Load<Texture2D>(DexterShipTimeline.DexterBodyTexture);
            try
            {
                Assert.That(DexterShipAppearance.BindDexterBody(first, texture), Is.GreaterThan(0));
                var block = new MaterialPropertyBlock();
                foreach (var renderer in first.GetComponentsInChildren<Renderer>(true))
                {
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var material = renderer.sharedMaterials[i]; renderer.GetPropertyBlock(block, i);
                        Assert.That(block.GetTexture("_MainTex"), material.name == DexterShipAppearance.DexterBodyMaterial
                            ? Is.SameAs(texture) : Is.Null, material.name);
                        block.Clear();
                    }
                }
                foreach (var renderer in second.GetComponentsInChildren<Renderer>(true))
                    Assert.That(renderer.HasPropertyBlock(), Is.False, "Appearance must not leak into another NPC using shared materials.");
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [TestCase(56, 3.75f, 7.25f, 17.25f, 18.25f)]
        [TestCase(54, 3.65f, 7.15f, 17.15f, 18.15f)]
        public void NameEventWaitsForTheLocalizedTypewriterBeforeStartingSpeech(int characters, float hello, float input, float clear, float end)
        {
            Assert.That(DexterShipTimeline.CreationReveal(characters), Is.EqualTo(hello).Within(.0001f));
            Assert.That(DexterShipTimeline.CreationInput(characters), Is.EqualTo(input).Within(.0001f));
            Assert.That(DexterShipTimeline.CreationVoiceClear(characters), Is.EqualTo(clear).Within(.0001f));
            Assert.That(DexterShipTimeline.CreationDuration(characters), Is.EqualTo(end).Within(.0001f));
            Assert.That(DexterShipTimeline.SubtitleLine(true, clear + .01f, characters), Is.Zero);
            Assert.That(DexterShipTimeline.DexterClip(true, clear + .01f, characters), Is.EqualTo("cutscene_stand_1"));
            Assert.That(DexterShipTimeline.Fade(true, hello + .9f, characters), Is.Zero);
        }

        [TestCase("DEXTER: Hello? It is I, Dexter.", "DEXTER:", " Hello? It is I, Dexter.")]
        [TestCase("ДЕКСТЕР: Привет? Это я, Декстер.", "ДЕКСТЕР:", " Привет? Это я, Декстер.")]
        public void SpeakerAndDialogueRemainSeparateSourceLabels(string full, string expectedSpeaker, string expectedDialogue)
        {
            DexterShipTimeline.SplitSubtitle(full, out string speaker, out string dialogue);
            Assert.That(speaker, Is.EqualTo(expectedSpeaker));
            Assert.That(dialogue, Is.EqualTo(expectedDialogue));
        }
    }
}
