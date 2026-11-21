using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections;
using System.Linq;
using FFOne.Creation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

        [Test]
        public void PendingAppearanceRetainsTheSelectionMadeBeforeAwaitingAssets()
        {
            var source = new FFOne.Network.CharacterInfo();
            source.Style.Gender = 1; source.Style.Height = 2; source.Style.HairStyle = 3;
            source.Equipment[1] = new FFOne.Network.EquipmentItem { ItemType = 1, ItemId = 12 };
            var pending = FFOne.Content.NativeAvatarPreview.Snapshot(source);
            source.Style.Gender = 2; source.Style.Height = 4; source.Style.HairStyle = 8;
            source.Equipment[1] = new FFOne.Network.EquipmentItem { ItemType = 1, ItemId = 25 };
            Assert.That(pending.Style.Gender, Is.EqualTo(1));
            Assert.That(pending.Style.Height, Is.EqualTo(2));
            Assert.That(pending.Style.HairStyle, Is.EqualTo(3));
            Assert.That(pending.Equipment[1].ItemId, Is.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator ReservedRosterEntriesRenderBothGendersWithoutSavingAnAppearance()
        {
            foreach (int gender in new[] { 1, 2 })
            {
                var source = CreationCatalog.Preview(new FFOne.Network.CharacterCreation { Slot = 2, FirstName = "Pending", LastName = "Probe" });
                source.Style.Gender = gender;
                source.Style.FaceStyle = source.Style.HairStyle = source.Style.EyeColor = source.Style.SkinColor = source.Style.HairColor = 0;
                var request = CreationCatalog.RosterPreviewAsync(source);
                while (!request.IsCompleted) yield return null;
                Assert.That(request.IsCompletedSuccessfully, Is.True, request.Exception?.ToString());
                var preview = request.Result;
                var preload = FFOne.Content.NativeAvatarPreview.PreloadAsync(preview);
                while (!preload.IsCompleted) yield return null;
                Assert.That(preload.IsCompletedSuccessfully, Is.True, preload.Exception?.ToString());
                var host = new GameObject("reserved roster preview fixture");
                try
                {
                    var avatar = host.AddComponent<FFOne.Content.NativeAvatarPreview>();
                    avatar.Apply(preview);
                    Assert.That(avatar.LastError, Is.Null);
                    Assert.That(avatar.IsReady, Is.True);
                    Assert.That(preview.Style.Gender, Is.EqualTo(gender));
                    Assert.That(preview.Uid, Is.EqualTo(source.Uid));
                    Assert.That(preview.DisplayName, Is.EqualTo(source.DisplayName));
                    Assert.That(preview.Style.AppearanceComplete, Is.False);
                    Assert.That(source.Style.FaceStyle, Is.Zero);
                    Assert.That(source.Equipment[1].ItemId, Is.Zero, "Presentation must not write starter clothes into the server roster.");
                    Assert.That(source.Style.AppearanceComplete, Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
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
