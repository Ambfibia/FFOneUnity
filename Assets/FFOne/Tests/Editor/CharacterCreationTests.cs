using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using FFOne.Creation;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class CharacterCreationTests
    {
        [TestCase("Able", "Abnormal", "Ableabnormal")]
        [TestCase(" ", "Abnormal", "Abnormal")]
        [TestCase("Able", " ", "Able")]
        [TestCase(" ", " ", "")]
        [TestCase("The ", "Abnormal", "The Abnormal")]
        public void GeneratedNamePreservesLegacyCompoundAndBlankRules(string middle, string last, string expected)
            => Assert.That(CharacterCreationRules.ComposeLastName(middle, last), Is.EqualTo(expected));

        [Test]
        public void NameWheelWrapsThroughSelectableBlankWithoutSelectingSentinel()
        {
            var catalog = new CreationCatalog();
            foreach (int part in new[] { 0, 1, 2 })
            {
                int count = catalog.Names(part).Count;
                Assert.That(CharacterCreationRules.CycleName(1, -1, count), Is.EqualTo(count - 1));
                Assert.That(CharacterCreationRules.CycleName(count - 1, 1, count), Is.EqualTo(1));
                for (int index = 1; index < count; index++)
                    Assert.That(CharacterCreationRules.CycleName(CharacterCreationRules.CycleName(index, 2, count), -2, count), Is.EqualTo(index));
            }
            Assert.That((string)catalog.Names(1)[1]["value"], Is.EqualTo(" "));
            Assert.That((string)catalog.Names(2)[1]["value"], Is.EqualTo(" "));
            Assert.Throws<InvalidOperationException>(() => CharacterCreationRules.CycleName(0, 1, 1));
        }

        [TestCase("  Alice   Space Ranger  ", "Alice", "Space Ranger", true)]
        [TestCase("Оля Космос", "Оля", "Космос", true)]
        [TestCase("Alice", "Alice", "", false)]
        [TestCase("123456789 Surname", "123456789", "Surname", false)]
        [TestCase("Alice 12345678901234567", "Alice", "12345678901234567", false)]
        public void CustomNamesKeepTwoWirePartsAndEnforceLimits(string text, string first, string last, bool valid)
        {
            CharacterCreationRules.SplitCustomName(text, out string actualFirst, out string actualLast);
            Assert.That(actualFirst, Is.EqualTo(first));
            Assert.That(actualLast, Is.EqualTo(last));
            Assert.That(CharacterCreationRules.ValidName(actualFirst, actualLast), Is.EqualTo(valid));
        }

        [Test]
        public void CreatorCatalogKeepsAcceptedIdsAndImportedAudio()
        {
            var catalog = new CreationCatalog();
            foreach (int gender in new[] { 1, 2 })
                foreach (string category in new[] { "face", "hair", "shirt", "pants", "shoes" })
                {
                    var choices = catalog.Choices(category, gender);
                    Assert.That(choices, Is.Not.Empty, category);
                    Assert.That(choices.Select(c => (int)c["creationIndex"]).Distinct().Count(), Is.EqualTo(choices.Count));
                    Assert.That(choices.All(c => (int)c["creationIndex"] >= 2 && (int)c["value"] > 0), Is.True);
                }
            foreach (string cue in new[] { "continue_01", "randomize", "tab_click01", "select_color", "height_up", "height_down",
                "girth_wide", "girth_narrow", "mouse_click01", "mouse_click02", "mouse_click03", "mouse_click04", "mouse_click05" })
                Assert.That(FFOne.Content.ContentAssets.Load<AudioClip>("audio/sfx/ui/" + cue), Is.Not.Null, cue);
            Assert.That(FFOne.Content.ContentAssets.Load<AudioClip>("audio/music/charactercreation_loop"), Is.Not.Null);
        }
    }
}
