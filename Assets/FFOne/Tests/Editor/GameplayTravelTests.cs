using System;
using System.Linq;
using FFOne.Network;
using FFOne.World;
using FFOne.UI;
using FFOne.Content;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class GameplayTravelTests
    {
        [Test] public void AcceptedLegacyAliasesResolveRealNpcModelsWithoutRenamingIds()
        {
            var npcs = WorldNpcCatalog.Load();
            Assert.AreEqual("characters/npcs/npc_iceking/npc_simon.glb", npcs.Find(2567).Model);
            Assert.AreEqual("characters/npcs/npc_father2/npc_daddy.glb", npcs.Find(709).Model);
            Assert.AreEqual("characters/npcs/npc_unstablenano/effect_unstablenanocapsule.glb", npcs.Find(3076).Model);
            Assert.AreEqual(WorldNpcCatalog.RecallPointModel, npcs.Find(1386).Model);
            Assert.AreEqual(446, npcs.Find(1386).Effect);
        }
        static GameplayUiSession Session()
        {
            var p = new byte[2700]; Protocol0104.Write32(p, 0, 51); Protocol0104.Write64(p, 8, 1234); Protocol0104.Write16(p, 1676, -1);
            var s = new GameplayUiSession(_ => null); s.SetWorld(Protocol0104.DecodeWorldEntry(p, 1234)); return s;
        }
        [Test] public void WarpCarriesConsumedInventoryPostStateAndWallet()
        {
            var s = Session(); var p = ProtocolTravel.Words(123, 234, -50, 1, 49, 0, 0, 0, 75);
            ProtocolServices.WriteItem(p, 20, new EquipmentItem { ItemType = 7, ItemId = 1, Option = 2 });
            Assert.IsTrue(s.ApplyFrame(new ProtocolFrame(ProtocolTravel.WarpOk, p)));
            Assert.AreEqual(75, s.Snapshot.Taros); Assert.AreEqual(1, s.Snapshot.Inventory[49].ItemId); Assert.AreEqual(2, s.Snapshot.Inventory[49].Quantity);
        }
        [Test] public void GroupPassengerDoesNotEraseInventoryFromUnusedWarpItem()
        {
            var s = Session(); s.ApplyFrame(new ProtocolFrame(ProtocolTravel.WarpOk, ProtocolTravel.Words(1, 2, 3, 4, 0, 0, 0, 0, 10)));
            s.Relocate(75, 128765, 217346, -15377);
            Assert.AreEqual(75, s.Snapshot.MapNumber); Assert.AreEqual(1287.65f, s.Snapshot.WorldPosition.x, .001);
        }
        [Test] public void NpcWarpClearsTheServerForcedMountEvenAfterReturningToOverworld()
        {
            var s = Session(); s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.OnSuccess, new byte[1]));
            Assert.IsTrue(s.Snapshot.VehicleMounted);
            s.ApplyFrame(new ProtocolFrame(ProtocolTravel.WarpOk, ProtocolTravel.Words(1, 2, 3, 4, 0, 0, 0, 0, 10)));
            s.Relocate(75, 128765, 217346, -15377); s.Relocate(0, 632032, 187177, -5500);
            Assert.IsFalse(s.Snapshot.VehicleMounted, "A scene return must not resurrect the mount removed by the authoritative warp.");
        }
        [Test] public void CrocIntentRejectsSameOrOutsideSlots()
        { Assert.Throws<ArgumentException>(() => ProtocolCroc.Combine(1, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolCroc.Combine(1, 50)); Assert.AreEqual(16, ProtocolCroc.Combine(0, 49).Length); }
        [TestCase(0)] [TestCase(1)] public void CrocConsumesStatsOnlyOnSuccessfulRoll(int success)
        {
            var s = Session(); var given = ProtocolTravel.Words(1, 3, 0, 0, 0); ProtocolServices.WriteItem(given, 8, new EquipmentItem { ItemType = 1, ItemId = 68, Option = 1 });
            s.ApplyFrame(new ProtocolFrame(0x31000061, given));
            var p = ProtocolTravel.Words(2, 0, 0, 0, 3, 0, 0, 77, success); ProtocolServices.WriteItem(p, 4, new EquipmentItem { ItemType = 1, ItemId = 67, Option = (68 << 16) | 1, TimeLimit = 1234 });
            s.ApplyFrame(new ProtocolFrame(ProtocolCroc.Ok, p));
            Assert.AreEqual(77, s.Snapshot.Taros); Assert.AreEqual(success == 1 ? 0 : 68, s.Snapshot.Inventory[3].ItemId);
            Assert.AreEqual(1234, s.Snapshot.Inventory[2].TimeLimit); Assert.AreEqual((68 << 16) | 1, s.Snapshot.Inventory[2].Option);
        }
        [Test] public void RejectedCrocIsAtomicAndAcceptsEchoOfInvalidSameSlot()
        {
            var s = Session(); var before = s.Snapshot;
            s.ApplyFrame(new ProtocolFrame(ProtocolCroc.Fail, ProtocolTravel.Words(1, 2, 2, 0, 0)));
            Assert.AreSame(before, s.Snapshot);
        }
        [Test] public void InvalidCrocReplyCannotPartlyCommitWallet()
        {
            var s = Session(); var before = s.Snapshot;
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolCroc.Ok, ProtocolTravel.Words(0, 0, 0, 0, 51, 0, 0, 1, 1))));
            Assert.AreSame(before, s.Snapshot);
        }
        [Test] public void InstanceAndSkywayPacketsUsePackFourAlignment()
        {
            var p = new byte[60]; Protocol0104.Write32(p, 0, 75); Protocol0104.Write64(p, 4, 1234567890123);
            Assert.IsTrue(ProtocolTravel.TryDecode(new ProtocolFrame(ProtocolTravel.MapInfo, p), out var map)); Assert.AreEqual(75, map.Map);
            p = new byte[28]; Protocol0104.Write64(p, 12, 1L << 60); Protocol0104.Write64(p, 20, 3);
            ProtocolTravel.TryDecode(new ProtocolFrame(ProtocolTravel.RegisterOk, p), out var flags); Assert.AreEqual(1L << 60, flags.SkywayFlags0); Assert.AreEqual(3, flags.SkywayFlags1);
            Assert.Throws<ProtocolException>(() => ProtocolTravel.TryDecode(new ProtocolFrame(ProtocolTravel.FlightMove, new byte[19]), out _));
        }
        [Test] public void CountedNpcRosterKeepsSeparateNpcAndPlayerIdentities()
        {
            var p = new byte[16 + 112 + 32]; Protocol0104.Write32(p, 8, 1); Protocol0104.Write32(p, 12, 1); Protocol0104.Write32(p, 16, 7);
            Protocol0104.Write32(p, 128, 7); Protocol0104.Write32(p, 132, 2567); Protocol0104.Write32(p, 136, 123); Protocol0104.Write32(p, 144, 75);
            ProtocolSocial.TryDecode(new ProtocolFrame(0x310000f1, p), out var u); var social = new SocialSession(); social.Apply(u);
            Assert.AreEqual(7, social.Npcs.Single().Id); Assert.AreEqual(75, social.Npcs.Single().Map); Assert.AreEqual(1, social.Members.Count);
            social.Apply(new SocialUpdate { Type = ProtocolSocial.Left, Members = Array.Empty<GroupMember>() }); Assert.IsEmpty(social.Npcs);
        }
        [Test] public void AuthoredInstancesUsePublishedCellInsteadOfMapIdAsTileNumber()
        {
            var data = WorldTravelCatalog.Load(); Assert.AreEqual((2, 4), data.Maps[75]);
            var world = ContentAssets.Load<WorldCatalog>("World/Catalog");
            Assert.IsTrue(NetworkWorldController.HasCoverage(world, 75, WorldCoordinates.FromServer(128765, 217346, -15377)));
            Assert.IsFalse(NetworkWorldController.HasCoverage(world, 1, WorldCoordinates.FromServer(128765, 217346, -15377)));
            Assert.IsTrue(data.Trips.Any(t => t.Type == 1 && t.Npc == 967)); Assert.IsTrue(data.Trips.Any(t => t.Type == 2));
            Assert.AreEqual(1, data.Croc[0].LooksCost); Assert.AreEqual(3, data.Croc[0].StatsCost); Assert.AreEqual(.95f, data.Croc[0].Rarity[0], .0001);
        }

        [TestCase(1, 5, "Peach Creek Commons", "Пич-Крик", "Будущее")]
        [TestCase(2, 8, "Genius Grove", "Роща Гениев", "Будущее")]
        [TestCase(1, 8, "Endsville", "Эндсвилль", "Пригород")]
        [TestCase(1, 6, "City Station", "Городская Станция", "Город")]
        [TestCase(2, 4, "Pimpleback Mountains", "Запрыщенные Горы", "Природа")]
        [TestCase(2, 26, "Forsaken Valley", "Забытая Долина", "Темные земли")]
        public void TransportStopsUseServiceSpecificNamesAndLocalizedRegions(
            int type, int id, string english, string russian, string region)
        {
            var stop = WorldTravelCatalog.Load().Locations.Single(l => l.Type == type && l.Id == id);
            Assert.AreEqual(english, GameplayUiController.TransportLocationName(stop, "en"));
            Assert.AreEqual(russian, GameplayUiController.TransportLocationName(stop, "ru"));
            Assert.AreEqual(region, GameplayUiController.TransportRegionName(stop, "ru"));
        }

        [Test]
        public void FutureRegionUsesTransportZoneWhenDistrictIsUnknown()
        {
            var stop = WorldTravelCatalog.Load().Locations.Single(l => l.Type == 1 && l.Id == 5);
            Assert.IsTrue(stop.IsFuture);
            var outsideNamedDistricts = new TravelLocation { X = 1, Y = 1, IsFuture = stop.IsFuture };
            Assert.AreEqual("Будущее", GameplayUiController.TransportRegionName(outsideNamedDistricts, "ru"));
            Assert.AreEqual("The Future", GameplayUiController.TransportRegionName(outsideNamedDistricts, "en"));
        }
    }
}
