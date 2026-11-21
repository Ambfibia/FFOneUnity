using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialVoiceSubtitleTests
    {
        [TestCase("en")]
        [TestCase("ru")]
        public void EveryAcceptedVoiceIdentityResolvesASpeakerAndLocalizedDialogue(string language)
        {
            var state = new TutorialVoiceSubtitles();
            Assert.That(TutorialVoiceSubtitles.Cues.Count(), Is.EqualTo(87));
            foreach (string cue in TutorialVoiceSubtitles.Cues)
            {
                state.Play(cue.ToUpperInvariant(), language == "ru", key => LocalizationCatalog.Lookup(key, language));
                Assert.That(state.Text, Does.Contain(":"), cue);
                Assert.That(state.Text.StartsWith("content."), Is.False, cue);
                Assert.That(state.Remaining, Is.GreaterThan(0), cue);
                if (language == "ru") Assert.That(state.Text.Any(c => c >= 'А' && c <= 'я'), Is.True, cue);
            }
        }

        [Test]
        public void VoiceSubtitleExpiresReplacesAndRetainsLongLocalizedRecordings()
        {
            var state = new TutorialVoiceSubtitles();
            state.Play("Computress_Tut01", false, _ => "Speaker: first");
            state.Tick(4.9f); Assert.That(state.Text, Does.Contain("first"));
            state.Tick(.11f); Assert.That(state.Text, Is.Empty);
            state.Play("Btrcup_Tut01", true, _ => "ЛЮТИК: вторая", 4);
            state.Tick(2.5f); Assert.That(state.Text, Does.Contain("вторая"));
            state.Tick(1.5f); Assert.That(state.Text, Is.Empty);
            state.Play("Computress_Tut01", false, _ => "Speaker: first");
            state.Play("NumTwo_Tut01", false, _ => "must not resolve");
            Assert.That(state.Text, Is.Empty, "An audio-only VoiceOut clears the previous line.");
        }

        [Test]
        public void AuthoredAudioDispatchShowsDialogueWithoutReplacingLocationTitleAndVoiceOffClearsIt()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var host = new GameObject("subtitle integration");
            TutorialCinematics playback = null;
            try
            {
                var owner = host.AddComponent<TutorialController>();
                var type = typeof(TutorialController);
                type.GetProperty(nameof(TutorialController.ViewCamera)).SetValue(owner, host.AddComponent<Camera>());
                type.GetProperty(nameof(TutorialController.Russian)).SetValue(owner, true);
                type.GetField("content", flags).SetValue(owner, ContentAssets.Load<TutorialContent>("Tutorial/Content"));
                type.GetField("voice", flags).SetValue(owner, host.AddComponent<AudioSource>());
                playback = new TutorialCinematics(owner);
                var dispatch = typeof(TutorialCinematics).GetMethod("Dispatch", flags);
                owner.Subtitle("tutorial.cinematic.tech_square_future");
                string location = (string)type.GetField("subtitle", flags).GetValue(owner);
                dispatch.Invoke(playback, new object[] { JObject.Parse("{\"type\":\"AudioCue\",\"cue\":\"Computress_tut01\",\"kind\":0}") });
                Assert.That(owner.VoiceSubtitleText, Does.Contain(":"));
                Assert.That(type.GetField("subtitle", flags).GetValue(owner), Is.EqualTo(location));
                Assert.That((bool)type.GetField("locationCaption", flags).GetValue(owner), Is.True);
                dispatch.Invoke(playback, new object[] { new JObject { ["type"] = "VoiceOff" } });
                Assert.That(owner.VoiceSubtitleText, Is.Empty);
            }
            finally { playback?.Dispose(); Object.DestroyImmediate(host); }
        }
    }
}
