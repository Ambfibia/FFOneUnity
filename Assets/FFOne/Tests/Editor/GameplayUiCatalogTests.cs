using FFOne.Content;
using FFOne.UI;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class GameplayUiCatalogTests
    {
        [Test]
        public void InstalledCatalogResolvesTypedIdentitiesLocalizedNamesAndActualIconAssets()
        {
            var catalog = GameplayUiCatalog.Load();
            var weapon = catalog.Item(0, 1, "en"); var shirt = catalog.Item(1, 1, "en");
            Assert.That(weapon.Name, Is.EqualTo("Disco Bomb"));
            Assert.That(shirt.Name, Is.EqualTo("Gangreen Gang Ace Jacket"));
            Assert.That(weapon.IconAddress, Is.EqualTo("icons/items/weapons/wpnicon_13"));
            Assert.That(shirt.IconAddress, Is.EqualTo("icons/items/cosmetics/cosicon_196"));
            Assert.That(ContentAssets.Load<Texture2D>(weapon.IconAddress), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(shirt.IconAddress), Is.Not.Null);
            Assert.That(catalog.Item(0, 32767), Is.Null);
            var mission = catalog.Mission(1, "en");
            Assert.That(mission.MissionId, Is.EqualTo(3)); Assert.That(mission.Title, Is.EqualTo("Dee Dee's Dilemmas (Part 1 of 4)"));
            Assert.That(mission.Objective, Is.Not.Empty);
            var russian = catalog.Mission(1, "ru"); Assert.That(russian.Title, Does.Not.StartWith("content."));
            Assert.That(catalog.Nano(1).Name, Is.EqualTo("Buttercup"));
            Assert.That(catalog.Nano(1).IconAddress, Is.EqualTo("icons/entities/nanos/nanoicon_buttercup"));
        }

        [Test]
        public void IconDispatchDoesNotConfuseNanoIdWithAtlasIndexOrInventMissingCategory()
        {
            Assert.That(GameplayUiCatalog.IconAddress(1, 20), Is.EqualTo("icons/entities/nanos/nanoicon_eddy"));
            Assert.That(GameplayUiCatalog.IconAddress(1, 0), Is.EqualTo("icons/entities/nanos/nanoicon_samurai-jack"));
            Assert.That(GameplayUiCatalog.IconAddress(6, 1), Is.Null);
            Assert.That(GameplayUiCatalog.IconAddress(1, 999), Is.Null);
            Assert.That(GameplayUiCatalog.IconAddress(5, 0), Is.EqualTo("icons/entities/nanos/ready/nanoready_samurai-jack"));
        }

        [TestCase(41, "holo-nano", "nano_holonano")]
        [TestCase(49, "rex", "nano_rex")]
        [TestCase(50, "alien-x", "nano_alienx")]
        [TestCase(53, "rigby", "nano_rigby")]
        [TestCase(54, "jake", "nano_jake")]
        [TestCase(56, "johnny-test", "nano_johnnytest")]
        [TestCase(59, "ice-king", "nano_iceking")]
        [TestCase(61, "ampfibian", "nano_ampfibian")]
        [TestCase(69, "ghostfreak", "nano_ghostfreak")]
        [TestCase(70, "upgrade", "nano_upgrade")]
        public void AcceptedNanoExtensionsUseTheirOwnAuthoredArtDespiteLegacyAtlasCollisions(int id, string slug, string model)
        {
            var catalog = GameplayUiCatalog.Load();
            var nano = catalog.Nano(id);
            Assert.That(nano, Is.Not.Null);
            Assert.That(nano.NanoModelResource, Is.EqualTo("characters/nanos/" + model + "/" + model));
            Assert.That(nano.IconAddress, Is.EqualTo("icons/entities/nanos/nanoicon_" + slug));
            Assert.That(nano.NanoReadyIconAddress, Is.EqualTo("icons/entities/nanos/ready/nanoready_" + slug));
            Assert.That(ContentAssets.Load<Texture2D>(nano.IconAddress), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(nano.NanoReadyIconAddress), Is.Not.Null);
            Assert.That(catalog.Nano(id, "ru").IconAddress, Is.EqualTo(nano.IconAddress));
        }

        [Test]
        public void InstalledNanoCollectionPreservesAcceptedExtendedIdsAndAuthoredReadyArt()
        {
            var catalog = GameplayUiCatalog.Load();
            var nanos = catalog.Nanos().ToArray();
            Assert.That(nanos.Length, Is.GreaterThanOrEqualTo(68));
            Assert.That(nanos.Select(nano => nano.ItemId), Is.EquivalentTo(Enumerable.Range(1, 68)));
            Assert.That(nanos.Count(n => n.Name == "Unstable Nano"), Is.EqualTo(1));
            Assert.That(catalog.Nano(62).Name, Is.EqualTo("Van Kleiss"));
            Assert.That(catalog.Nano(1).NanoSortOrder, Is.EqualTo(5));
            Assert.That(catalog.Nano(39).NanoSortOrder, Is.EqualTo(3));
            foreach (var nano in nanos)
            {
                Assert.That(ContentAssets.Load<Texture2D>(nano.IconAddress), Is.Not.Null, "Owned art for Nano " + nano.ItemId);
                Assert.That(ContentAssets.Load<Texture2D>(nano.NanoReadyIconAddress), Is.Not.Null, "Locked art for Nano " + nano.ItemId);
            }
            // These accepted model additions still have zeroed native icon rows; no
            // custom portraits have been authored for them in the installed catalog.
            Assert.That(catalog.Nano(67).IconAddress, Is.EqualTo("icons/entities/nanos/nanoicon_missing"));
            Assert.That(catalog.Nano(68).NanoReadyIconAddress, Is.EqualTo("icons/entities/nanos/ready/nanoready_missing"));
        }

        [Test]
        public void GuidePortraitResolvesNpcIconReferenceInsteadOfUsingNpcNumberAsAtlasNumber()
        {
            var catalog = GameplayUiCatalog.Load();
            Assert.That(catalog.NpcPortrait(730), Is.EqualTo("icons/entities/npc/npcicon_49"));
            Assert.That(catalog.NpcPortrait(1171), Is.EqualTo(catalog.NpcPortrait(730)));
            Assert.That(ContentAssets.Load<Texture2D>(catalog.NpcPortrait(730)), Is.Not.Null);
            Assert.That(catalog.NpcPortrait(32767), Is.Null);
        }

        [Test]
        public void SkillNamesResolveAcceptedSkillStringsAndBothLocales()
        {
            var catalog = GameplayUiCatalog.Load();
            Assert.That(catalog.SkillName(1), Is.EqualTo("Miss Fire"));
            Assert.That(catalog.SkillName(1, "ru"), Is.EqualTo("Мисс Огонь"));
            Assert.That(catalog.SkillName(122), Is.EqualTo("Infection"));
            Assert.That(catalog.SkillName(32767), Is.Empty);
        }
    }
}
