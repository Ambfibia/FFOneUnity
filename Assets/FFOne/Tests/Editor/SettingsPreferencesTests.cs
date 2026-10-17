using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using FFOne.UI;

namespace FFOne.Tests
{
    public sealed class SettingsPreferencesTests
    {
        [TestCase(1920, 1080, 1080)]
        [TestCase(2560, 1440, 1080)]
        [TestCase(1920, 1200, 1200)]
        [TestCase(2560, 1600, 1200)]
        [TestCase(0, 0, 1080)]
        public void FullHdDefaultsAndGraphicsResetRespectTheDisplayAspect(int width, int height, int expectedHeight)
        {
            var prefs = new SettingsPreferences(null, width, height);
            Assert.That(prefs.Get<int>("graphics.width"), Is.EqualTo(1920));
            Assert.That(prefs.Get<int>("graphics.height"), Is.EqualTo(expectedHeight));
            prefs.Set("graphics.width", 1280); prefs.Set("graphics.height", 720);
            prefs.Reset("graphics");
            Assert.That(prefs.Get<int>("graphics.width"), Is.EqualTo(1920));
            Assert.That(prefs.Get<int>("graphics.height"), Is.EqualTo(expectedHeight));
        }

        [Test]
        public void ExistingSavedResolutionIsPreservedOnA16By10Display()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                var prefs = new SettingsPreferences(file);
                prefs.Set("graphics.width", 1600); prefs.Set("graphics.height", 900); prefs.Apply();
                var restored = new SettingsPreferences(file, 1920, 1200);
                Assert.That(restored.Get<int>("graphics.width"), Is.EqualTo(1600));
                Assert.That(restored.Get<int>("graphics.height"), Is.EqualTo(900));
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }

        [Test] public void DirtyCancelAfterApplyRestoresOpeningOptionsButRetainsAppliedInput()
        {
            var prefs = new SettingsPreferences();
            prefs.Set("graphics.glow", false); prefs.Set("input.sensitivity", 9); prefs.Apply();
            prefs.Set("graphics.toon", false); prefs.Set("input.sensitivity", 2); prefs.Cancel();
            Assert.That((bool)prefs.Committed["graphics"]["glow"], Is.True);
            Assert.That((bool)prefs.Committed["graphics"]["toon"], Is.True);
            Assert.That((int)prefs.Committed["input"]["sensitivity"], Is.EqualTo(9));
        }
        [Test] public void SoundIsEagerAndDirtyCancelRestoresOpeningSound()
        {
            var prefs = new SettingsPreferences(); prefs.Set("sound.master", 2);
            Assert.That((int)prefs.Committed["sound"]["master"], Is.EqualTo(2));
            prefs.Cancel(); Assert.That((int)prefs.Committed["sound"]["master"], Is.EqualTo(5));
        }
        [Test] public void IndependentLanguagesSurviveCancelAndDoNotCommitOtherDraftChanges()
        {
            var prefs = new SettingsPreferences(); prefs.Set("graphics.glow", false);
            prefs.SetLocale("text_locale", "ru"); prefs.SetLocale("voice_locale", "en"); prefs.Cancel();
            Assert.That(prefs.Get<string>("text_locale"), Is.EqualTo("ru"));
            Assert.That(prefs.Get<string>("voice_locale"), Is.EqualTo("en"));
            Assert.That(prefs.Get<bool>("graphics.glow"), Is.True);
        }
        [TestCase("graphics", "broken")]
        [TestCase("sound.master", "very loud")]
        [TestCase("text_locale", "unknown")]
        [TestCase("graphics.detail", "8")]
        public void MalformedFileFallsBackWithoutOverwriting(string key, string invalid)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ffone-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ContentPaths.FilePath(directory)); string path = Path.Combine(directory, "settings.json");
            try
            {
                var json = SettingsPreferences.Defaults();
                json.SelectToken(key).Replace(key == "graphics.detail" ? new JValue(8) : new JValue(invalid));
                File.WriteAllText(ContentPaths.FilePath(path),json.ToString()); string original = File.ReadAllText(ContentPaths.FilePath(path));
                var prefs = new SettingsPreferences(path);
                Assert.That(prefs.LoadError, Is.Not.Null); Assert.That(prefs.Get<int>("graphics.detail"), Is.EqualTo(2));
                Assert.Throws<IOException>(() => prefs.Apply()); Assert.That(File.ReadAllText(ContentPaths.FilePath(path)), Is.EqualTo(original));
            }
            finally { File.Delete(ContentPaths.FilePath(path)); Directory.Delete(ContentPaths.FilePath(directory)); }
        }
        [Test] public void BalancedPresetDiffersFromConstructorAndInputDefaultsRetainMappings()
        {
            var prefs = new SettingsPreferences(); Assert.That(prefs.Get<bool>("graphics.vegetation"), Is.True);
            prefs.Preset(2); Assert.That(prefs.Get<bool>("graphics.vegetation"), Is.False);
            prefs.Set("input.bindings.Up.primary", "T"); prefs.Reset("input");
            Assert.That(prefs.Get<string>("input.bindings.Up.primary"), Is.EqualTo("T"));
        }
    }
}
