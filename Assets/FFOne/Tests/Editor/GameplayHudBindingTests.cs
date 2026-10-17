using FFOne.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayHudBindingTests
    {
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
    }
}
