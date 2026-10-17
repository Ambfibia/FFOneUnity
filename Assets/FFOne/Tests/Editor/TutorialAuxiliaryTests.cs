using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialAuxiliaryTests
    {
        [Test]
        public void MinimapExplainsTheMapBeforeRepeatingTheTravelReminder()
        {
            var timeline = TutorialAuxiliaryTimeline.Get(TutorialAuxiliarySequence.MinimapEvent);
            var voices = timeline.Actions.Where(a => a.Command == TutorialAuxiliaryCommand.Voice).ToArray();
            Assert.That(voices.Select(a => a.Text), Is.EqualTo(new[] { "NumTwo_Tut01", "Computress_Tut19", "Computress_Tut20", "Computress_Tut21" }));
            Assert.That(voices.Select(a => a.Seconds), Is.EqualTo(new[] { 0f, 4f, 10f, 13f }));
            Assert.That(timeline.RepeatAt, Is.EqualTo(16));
            Assert.That(timeline.RepeatEvery, Is.EqualTo(25));
            Assert.That(timeline.Repeating.First().Text, Is.EqualTo("Computress_Tut22"));
            Assert.That(timeline.Actions.Single(a => a.Command == TutorialAuxiliaryCommand.NumbuhTwoMarker).Seconds, Is.EqualTo(1));
            Assert.That(timeline.Actions.Single(a => a.Command == TutorialAuxiliaryCommand.Waypoint).Seconds, Is.EqualTo(13));
            Assert.That(timeline.Actions.Select(a => a.Seconds), Is.Ordered);
        }

        [Test]
        public void MovementAndNanoInstructionsRetainTheirSecondVoiceAndSourceReminderTimes()
        {
            var movement = TutorialAuxiliaryTimeline.Get(TutorialAuxiliarySequence.BasicArrowKey);
            var voices = movement.Actions.Where(a => a.Command == TutorialAuxiliaryCommand.Voice).ToArray();
            Assert.That(voices.Select(a => a.Text), Is.EqualTo(new[] { "Computress_Tut07", "Computress_Tut08" }));
            Assert.That(voices.Select(a => a.Seconds), Is.EqualTo(new[] { 0f, 6f }));
            Assert.That(movement.Actions.Last().Text, Is.EqualTo("tut_move_w"));
            var nano = TutorialAuxiliaryTimeline.Get(TutorialAuxiliarySequence.NanoPowerTalk1);
            Assert.That(nano.RepeatAt, Is.EqualTo(8));
            Assert.That(nano.RepeatEvery, Is.EqualTo(25));
            Assert.That(nano.Repeating.First().Text, Is.EqualTo("Computress_Tut61"));
            var buttercup = TutorialAuxiliaryTimeline.Get(TutorialAuxiliarySequence.TalkButtercup1);
            Assert.That(buttercup.Actions.Single(a => a.Command == TutorialAuxiliaryCommand.FusionMatter).Seconds, Is.EqualTo(27));
        }

        [TestCase(TutorialPointerAnchor.Minimap, 990f, 50f)]
        [TestCase(TutorialPointerAnchor.MyPoint, 1070f, 50f)]
        [TestCase(TutorialPointerAnchor.Waypoint, 1130f, 0f)]
        [TestCase(TutorialPointerAnchor.Talk, 530f, 180f)]
        [TestCase(TutorialPointerAnchor.Mission, 704f, 319f)]
        public void PointerUsesOriginalScreenAnchorAndAnimatedOffset(TutorialPointerAnchor anchor, float x, float y)
        {
            var first = TutorialAuxiliaryGeometry.Rect(anchor, true, new Vector2(88, 69), 1280, 720, 0);
            Assert.That(first.position, Is.EqualTo(new Vector2(x, y)));
            var peak = TutorialAuxiliaryGeometry.Rect(anchor, true, new Vector2(88, 69), 1280, 720, .125f);
            Assert.That(peak.x, Is.EqualTo(x + 10).Within(.0001f));
            Assert.That(peak.y, Is.EqualTo(y));
            Assert.That(peak.size, Is.EqualTo(new Vector2(88, 69)));
        }

        [Test]
        public void PointerScaleUsesItsOwnPivotAndIntegerScreenFractions()
        {
            var talk = TutorialAuxiliaryGeometry.Rect(TutorialPointerAnchor.Talk, true, new Vector2(88, 69), 1280, 720, 0, 1.5f);
            Assert.That(talk.position, Is.EqualTo(new Vector2(475, 180)));
            Assert.That(talk.size, Is.EqualTo(new Vector2(132, 103.5f)));
            var fractional = TutorialAuxiliaryGeometry.Rect(TutorialPointerAnchor.Mission, true, new Vector2(88, 69), 1279, 719, 0);
            Assert.That(fractional.position, Is.EqualTo(new Vector2(703, 318)));
            var picture = TutorialAuxiliaryGeometry.Rect(TutorialPointerAnchor.MoveKeys, false, new Vector2(182, 131), 1280, 720, 0, 1.5f);
            Assert.That(picture.position, Is.EqualTo(new Vector2(120, 225)));
        }

        [Test]
        public void AllAuxiliaryVoicesHaveImportedClips()
        {
            var content = FFOne.Content.ContentAssets.Load<TutorialContent>("Tutorial/Content");
            Assert.That(content, Is.Not.Null);
            foreach (var sequence in Enum.GetValues(typeof(TutorialAuxiliarySequence)).Cast<TutorialAuxiliarySequence>().Where(s => s != TutorialAuxiliarySequence.None))
            {
                var timeline = TutorialAuxiliaryTimeline.Get(sequence);
                foreach (var action in timeline.Actions.Concat(timeline.Repeating).Where(a => a.Command == TutorialAuxiliaryCommand.Voice))
                {
                    var voice = content.Audio.SingleOrDefault(a => string.Equals(a.Cue, action.Text, StringComparison.OrdinalIgnoreCase));
                    Assert.That(voice?.English, Is.Not.Null, sequence + ": " + action.Text + "; import tutorial content after adding auxiliary cues.");
                }
            }
        }
    }
}
