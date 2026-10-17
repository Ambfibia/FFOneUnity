using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayNanoHudTests
    {
        [Test]
        public void SourceNanoBeltArtResolvesWithoutFabricatedButtons()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/FFOne/Content/UI/Views/Retrobution/GameplayNanoHud.uxml");
            Assert.That(asset, Is.Not.Null);
            var tree = asset.CloneTree();
            Assert.That(tree.Q<Button>(), Is.Null, "The source belt is display-only; gameplay keys are handled elsewhere.");
            foreach (string name in new[] { "nano-belt-background", "nano-battery-background", "weapon-battery-background" })
                Assert.That(RetrobutionImage.Bind(tree.Q<Image>(name)), Is.Not.Null, name);
            for (int i = 0; i < 3; i++)
            {
                foreach (string suffix in new[] { "back-0", "back-1", "back-2", "front-0", "front-1", "front-2", "style-0", "style-1", "style-2", "key", "skill-back", "stamina-back", "stamina" })
                    Assert.That(RetrobutionImage.Bind(tree.Q<Image>($"nano-{i}-{suffix}")), Is.Not.Null, suffix);
            }
        }

        [Test]
        public void AnimatedCameraRisesOnlyBelowTwentyPercentStamina()
        {
            Assert.That(GameplayNanoRenderStage.StaminaCameraOffset(0, false), Is.EqualTo(.48f));
            Assert.That(GameplayNanoRenderStage.StaminaCameraOffset(.1f, false), Is.EqualTo(.24f));
            Assert.That(GameplayNanoRenderStage.StaminaCameraOffset(.2f, false), Is.Zero);
            Assert.That(GameplayNanoRenderStage.StaminaCameraOffset(.1f, true), Is.Zero);
            Assert.That(GameplayNanoRenderStage.StaminaCameraOffset(null, false), Is.Zero);
        }

        [Test]
        public void HudCanDisposeNanoStageAfterUnityHasDestroyedIt()
        {
            var owner = new GameObject("Destroyed Nano HUD stage regression");
            var stage = owner.AddComponent<GameplayNanoRenderStage>();
            Object.DestroyImmediate(owner);
            Assert.That(stage == null, Is.True, "The Unity object must already be destroyed.");
            Assert.DoesNotThrow(() => stage.Dispose());
            Assert.DoesNotThrow(() => stage.Dispose());
        }

        [Test]
        public void NanoCatalogRetainsSourceStyleStaminaAndSkillIconLinks()
        {
            var catalog = GameplayUiCatalog.Load();
            var nano = catalog.Nano(1);
            Assert.That(nano.NanoStyle, Is.InRange(0, 2));
            Assert.That(nano.NanoMaxStamina, Is.GreaterThan(0));
            Assert.That(nano.NanoModelResource, Is.EqualTo("characters/nanos/nano_buttercup/nano_buttercup"));
            Assert.That(NativeModelAssets.Load(nano.NanoModelResource), Is.Not.Null);
            Assert.That(catalog.SkillIcon(1), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(catalog.SkillIcon(1)), Is.Not.Null);
            Assert.That(catalog.SkillIcon(int.MaxValue), Is.Null);
        }
    }
}
