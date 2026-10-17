using System;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class GameplayInventoryTests
    {
        static GameplayUiSession Session()
        {
            var bytes = new byte[2700]; Protocol0104.Write64(bytes, 8, 1234); Protocol0104.Write32(bytes, 0, 51);
            Item(bytes, 4 + 136 + 12, 1, 10, 51); Item(bytes, 4 + 244 + 3 * 12, 1, 11, 52);
            var session = new GameplayUiSession(_ => null); session.SetWorld(Protocol0104.DecodeWorldEntry(bytes, 1234)); return session;
        }
        internal static void Item(byte[] bytes, int offset, int type, int id, int option)
        {
            Protocol0104.Write16(bytes, offset, type); Protocol0104.Write16(bytes, offset + 2, id);
            Protocol0104.Write32(bytes, offset + 4, option); Protocol0104.Write32(bytes, offset + 8, 1234567);
        }
        internal static byte[] MoveReply()
        {
            var bytes = new byte[40];
            Protocol0104.Write32(bytes, 0, 1); Protocol0104.Write32(bytes, 4, 3); Item(bytes, 8, 1, 10, 900);
            Protocol0104.Write32(bytes, 20, 0); Protocol0104.Write32(bytes, 24, 1); Item(bytes, 28, 1, 11, 901);
            return bytes;
        }

        [Test]
        public void MoveAppliesBothServerPostStatesAtomicallyWithoutSimulatingTheRequest()
        {
            var session = Session(); var previous = session.Snapshot; int changes = 0; session.Changed += _ => changes++;
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, 3, ItemContainer.Equipment, 1), Is.True);
            Assert.That(session.Snapshot.Inventory[3].ItemId, Is.EqualTo(11));
            Assert.That(session.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, MoveReply())), Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(session.Snapshot.Equipment[1].ItemId, Is.EqualTo(11));
            Assert.That(session.Snapshot.Inventory[3].ItemId, Is.EqualTo(10));
            Assert.That(session.Snapshot.Equipment[1].Option, Is.EqualTo(901));
            Assert.That(session.Snapshot.Inventory[3].Option, Is.EqualTo(900));
            Assert.That(previous.Inventory[3].ItemId, Is.EqualTo(11));
            Assert.That(previous.Equipment[1].ItemId, Is.EqualTo(10));
        }

        [Test]
        public void MalformedSecondRecordCannotPartlyApplyAndForeignEquipIsIgnored()
        {
            var session = Session(); var previous = session.Snapshot;
            var bytes = MoveReply(); Protocol0104.Write32(bytes, 24, 9);
            Assert.Throws<ProtocolException>(() => session.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, bytes)));
            Assert.That(session.Snapshot, Is.SameAs(previous));
            bytes = MoveReply(); Protocol0104.Write16(bytes, 28, 2);
            Assert.Throws<ProtocolException>(() => session.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, bytes)));
            bytes = new byte[20]; Protocol0104.Write32(bytes, 0, 999); Protocol0104.Write32(bytes, 4, 1); Item(bytes, 8, 1, 12, 12);
            Assert.That(session.ApplyFrame(new ProtocolFrame(ProtocolInventory.EquipmentChanged, bytes)), Is.False);
            Assert.That(session.Snapshot, Is.SameAs(previous));
            Protocol0104.Write32(bytes, 0, 51);
            Assert.That(session.ApplyFrame(new ProtocolFrame(ProtocolInventory.EquipmentChanged, bytes)), Is.True);
            Assert.That(session.Snapshot.Equipment[1].ItemId, Is.EqualTo(12));
            session.Clear(); Assert.That(session.ApplyFrame(new ProtocolFrame(ProtocolInventory.EquipmentChanged, bytes)), Is.False);
        }

        [Test]
        public void AcceptedEquipmentRefreshPreservesRosterIdentityAndIgnoresForeignUpdates()
        {
            var session = Session();
            var character = new CharacterInfo();
            typeof(CharacterInfo).GetProperty(nameof(CharacterInfo.Uid)).SetValue(character, 1234L);
            var style = character.Style;
            Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.True);
            var previous = character.Equipment;
            Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.False);
            var foreign = new byte[20]; Protocol0104.Write32(foreign, 0, 99); Protocol0104.Write32(foreign, 4, 1); Item(foreign, 8, 1, 12, 12);
            Assert.That(session.ApplyFrame(new ProtocolFrame(ProtocolInventory.EquipmentChanged, foreign)), Is.False);
            Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.False);
            session.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, MoveReply()));
            Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.True);
            Assert.That(character.Equipment[1].ItemId, Is.EqualTo(11));
            Assert.That(character.Equipment[1].Option, Is.EqualTo(901));
            Assert.That(character.Equipment[1].TimeLimit, Is.EqualTo(1234567));
            Assert.That(previous[1].ItemId, Is.EqualTo(10));
            Assert.That(character.Uid, Is.EqualTo(1234)); Assert.That(character.Style, Is.SameAs(style));
            typeof(CharacterInfo).GetProperty(nameof(CharacterInfo.Uid)).SetValue(character, 999L);
            Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.False);
            session.Clear(); Assert.That(GameplayEquipmentAppearance.Apply(character, session.Snapshot), Is.False);
        }

        [Test]
        public void OnlySupportedBoundedSlotsAndCompatibleEquipmentCanBeMoved()
        {
            var session = Session();
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, 3, ItemContainer.Equipment, 0), Is.False);
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, 2, ItemContainer.Equipment, 1), Is.False);
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, 3, ItemContainer.Inventory, 3), Is.False);
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, -1, ItemContainer.Inventory, 2), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolInventory.MovePayload((ItemContainer)2, 0, ItemContainer.Inventory, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolInventory.MovePayload(ItemContainer.Inventory, 50, ItemContainer.Inventory, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolInventory.MovePayload(ItemContainer.Equipment, 9, ItemContainer.Inventory, 1));
            Assert.That(ProtocolInventory.Fits(ItemContainer.Equipment, 7, 0, 100), Is.True);
            Assert.That(ProtocolInventory.Fits(ItemContainer.Equipment, 8, 10, 100), Is.True);
            Assert.That(ProtocolInventory.Fits(ItemContainer.Equipment, 8, 8, 100), Is.False);
            var sameSlot = MoveReply(); Protocol0104.Write32(sameSlot, 20, 1); Protocol0104.Write32(sameSlot, 24, 3);
            Assert.Throws<ProtocolException>(() => ProtocolInventory.TryDecode(new ProtocolFrame(ProtocolInventory.MoveSuccess, sameSlot), out _));
            Assert.Throws<ProtocolException>(() => ProtocolInventory.TryDecode(new ProtocolFrame(ProtocolInventory.MoveSuccess, new byte[39]), out _));
        }
    }

    public sealed partial class NetworkSessionTests
    {
        [Test]
        public async Task ItemMoveSendsExactSlotsWithoutInventingAnAcknowledgement()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendItemMoveAsync(ItemContainer.Inventory, 3, ItemContainer.Equipment, 1));
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234);
                    await AssertSelection(loginStream, 1); await SendTicket(loginStream, shardListener.Port);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var stream = shardSocket.GetStream(); await AcceptShardEntry(stream, true); await entering;
                        var loading = client.CompleteLoadingAsync();
                        await ReadFrame(stream); await WriteFrame(stream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loading;
                        await client.SendItemMoveAsync(ItemContainer.Inventory, 3, ItemContainer.Equipment, 1);
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 2);
                        Assert.That(request.PacketType, Is.EqualTo(ProtocolInventory.MoveRequest));
                        CollectionAssert.AreEqual(ProtocolInventory.MovePayload(ItemContainer.Inventory, 3, ItemContainer.Equipment, 1), request.Payload);
                        Assert.That(client.TryDequeueEvent(out _), Is.False);
                        await WriteFrame(stream, ProtocolInventory.MoveSuccess, GameplayInventoryTests.MoveReply(), Protocol0104.FrontendKey);
                        await WriteFrame(stream, 0x310000d0, new byte[4], Protocol0104.FrontendKey);
                        Assert.That(Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 3).PacketType, Is.EqualTo(0x13000075U));
                        Assert.That(client.TryDequeueEvent(out var received), Is.True);
                        Assert.That(ProtocolInventory.TryDecode(received.Frame, out var update), Is.True);
                        Assert.That(update.ToContainer, Is.EqualTo(ItemContainer.Equipment)); Assert.That(update.ToItem.ItemId, Is.EqualTo(11));
                    }
                }
            }
        }
    }
}
