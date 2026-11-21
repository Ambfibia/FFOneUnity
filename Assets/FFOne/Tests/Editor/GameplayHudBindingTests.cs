using FFOne.UI;
using FFOne.Content;
using NUnit.Framework;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayHudBindingTests
    {
        [Test]
        public void PlayerFrameUsesLocalizedLevelArtworkAndRestoresTheAcceptedEnglishFrame()
        {
            using var hud = new GameplayHudController(new VisualElement());
            var frame = hud.View.Q("hud-player-background");
            var english = frame.style.backgroundImage;
            hud.SetLanguage("ru");
            Assert.That(frame.style.backgroundImage.value.texture,
                Is.EqualTo(ContentAssets.Load<UnityEngine.Texture2D>("ui/ru/gameplay/player/character_info")));
            hud.SetLanguage("en");
            Assert.That(frame.style.backgroundImage, Is.EqualTo(english));
        }

        [Test]
        public void ExperienceBindingBeforePanelLayoutKeepsFiniteGeometryAndRejectsUnknownRatios()
        {
            var parent = new VisualElement();
            using var hud = new GameplayHudController(parent);
            hud.SetExperience(.75f);
            var fill = hud.View.Q("hud-exp-fill");
            Assert.That(fill.style.width.value.value, Is.EqualTo(0), "An unattached parent has no usable viewport width.");
            Assert.That(fill.style.visibility.value, Is.EqualTo(Visibility.Visible));
            hud.SetExperience(float.NaN);
            Assert.That(fill.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(float.IsFinite(fill.style.width.value.value), Is.True);
            hud.SetExperience(float.PositiveInfinity);
            Assert.That(fill.style.visibility.value, Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void PlayerLevelRetainsOriginalTwoDigitFormatAndNamesArePlainText()
        {
            var parent = new VisualElement();
            using var hud = new GameplayHudController(parent);
            hud.Bind(GameplayUiState.Empty);
            Assert.That(hud.View.Q<Label>("hud-level").text, Is.EqualTo("00"));
            Assert.That(hud.View.Q<Label>("hud-name").enableRichText, Is.False);
        }

        [Test]
        public void PlayerNameAndLevelUseTheSourceChaletLabelFont()
        {
            var parent = new VisualElement();
            using var hud = new GameplayHudController(parent);
            var chalet = ContentAssets.Load<FontAsset>("fonts/chaletbook-regular-sdf");
            Assert.That(chalet, Is.Not.Null, "The accepted HUD replacement font must be available before rendering.");
            foreach (string id in new[] { "hud-name", "hud-level" })
                Assert.That(hud.View.Q<Label>(id).style.unityFontDefinition.value.fontAsset, Is.EqualTo(chalet), id);
        }
    }
}
