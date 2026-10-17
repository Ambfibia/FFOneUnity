using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class GameplayTradeTests
    {
        internal static ProtocolFrame Header(uint type, int requester = 51, int from = 51, int to = 52, int size = 12)
        {
            var bytes = new byte[size]; Protocol0104.Write32(bytes, 0, requester); Protocol0104.Write32(bytes, 4, from); Protocol0104.Write32(bytes, 8, to);
            return new ProtocolFrame(type, bytes);
        }
        internal static void Item(byte[] bytes, int offset, int type, int id, int quantity, int inventory, int offer)
        {
            Protocol0104.Write16(bytes, offset, type); Protocol0104.Write16(bytes, offset + 2, id); Protocol0104.Write32(bytes, offset + 4, quantity);
            Protocol0104.Write32(bytes, offset + 8, inventory); Protocol0104.Write32(bytes, offset + 12, offer);
        }
        static GameplayTradeSession Active()
        {
            var session = new GameplayTradeSession(51); session.BeginOutgoingOffer(52);
            session.ApplyFrame(Header(ProtocolTrade.Accepted, 52)); return session;
        }
        static ProtocolFrame Registered(int requester, int slot, int id, int inventory, int count = 1)
        {
            var frame = Header(ProtocolTrade.ItemRegistered, requester, size: 44);
            Item(frame.Payload, 12, 7, id, count, inventory, slot); Item(frame.Payload, 28, 7, id, 0, inventory, slot); return frame;
        }

        [Test]
        public void OfferAcceptAndEveryOfferChangeInvalidateBothConfirmations()
        {
            var session = Active(); var initial = session.Snapshot;
            session.ApplyFrame(Registered(51, 4, 77, 49, 3));
            Assert.That(session.Snapshot.OwnItems[4].ItemId, Is.EqualTo(77)); Assert.That(initial.OwnItems[4].IsEmpty, Is.True);
            Assert.Throws<NotSupportedException>(() => ((IList<TradeItem>)session.Snapshot.OwnItems)[4] = default);
            session.ApplyFrame(Header(ProtocolTrade.Confirmed, 51)); session.ApplyFrame(Header(ProtocolTrade.Confirmed, 52));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.AwaitingSettlement));
            var cash = Header(ProtocolTrade.CashRegistered, 52, size: 16); Protocol0104.Write32(cash.Payload, 12, 123);
            session.ApplyFrame(cash);
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.Active));
            Assert.That(session.Snapshot.OtherTaros, Is.EqualTo(123));
            Assert.That(session.Snapshot.OwnConfirmed, Is.False); Assert.That(session.Snapshot.OtherConfirmed, Is.False);
            Assert.That(session.Snapshot.OwnTaros, Is.Zero);
        }

        [Test]
        public void LocalRefusalAndCancellationArePendingUntilAuthoritativeReply()
        {
            var session = new GameplayTradeSession(52);
            Assert.That(session.ApplyFrame(Header(ProtocolTrade.Offer)), Is.True);
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.IncomingOffer));
            session.MarkRefusalSent(); Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.RefusalPending));
            session.Clear(); session.ApplyFrame(Header(ProtocolTrade.Offer)); session.ApplyFrame(Header(ProtocolTrade.Accepted, 52));
            session.ApplyFrame(Registered(51, 0, 10, 3)); session.MarkCancellationSent();
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.CancellationPending));
            Assert.That(session.Snapshot.OtherItems[0].ItemId, Is.EqualTo(10), "Sending does not fabricate released reservations.");
            session.ApplyFrame(Header(ProtocolTrade.Cancelled, 51));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.Cancelled));
        }

        [Test]
        public void CancellationAllowsNewNegotiationWithoutFabricatingOrDroppingLateSettlement()
        {
            var session = Active(); session.ApplyFrame(Registered(51, 0, 77, 3));
            session.MarkCancellationSent();
            Assert.That(session.Snapshot.IsTrading, Is.False);
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.CancellationPending));
            session.ApplyFrame(Registered(51, 1, 78, 4));
            var cash = Header(ProtocolTrade.CashRegistered, 52, size: 16); Protocol0104.Write32(cash.Payload, 12, 10);
            session.ApplyFrame(cash);
            session.ApplyFrame(Header(ProtocolTrade.Confirmed, 51)); session.ApplyFrame(Header(ProtocolTrade.Confirmed, 52));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.CancellationPending), "Late mutation/confirm acknowledgements cannot reopen cancelled UI.");
            Assert.That(session.Snapshot.OwnItems[1].ItemId, Is.EqualTo(78));
            Assert.That(session.Snapshot.OtherTaros, Is.EqualTo(10));
            GameplayTradeState completed = null; session.Completed += state => completed = state;
            session.BeginOutgoingOffer(53);
            var receipt = Header(ProtocolTrade.Completed, 52, size: 400);
            Assert.That(session.ApplyFrame(receipt), Is.True);
            Assert.That(completed.OwnItems[0].ItemId, Is.EqualTo(77));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.OutgoingOffer));
            Assert.That(session.Snapshot.PeerId, Is.EqualTo(53));
            Assert.That(session.ApplyFrame(receipt), Is.False);
            session.Clear(); session.BeginOutgoingOffer(52); session.ApplyFrame(Header(ProtocolTrade.Accepted, 52));
            session.MarkCancellationSent();
            Assert.That(session.ApplyFrame(Header(ProtocolTrade.Offer, 53, 53, 51)), Is.True);
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.IncomingOffer));
            Assert.That(session.Snapshot.PeerId, Is.EqualTo(53));
            Assert.That(session.Snapshot.OwnItems[0].IsEmpty, Is.True);
            Assert.That(session.ApplyFrame(receipt), Is.False);
        }

        [Test]
        public void ForeignStaleAndMalformedRepliesDoNotAlterTheTrade()
        {
            var session = Active(); var before = session.Snapshot;
            Assert.That(session.ApplyFrame(Header(ProtocolTrade.CashRegistered, 61, 61, 62, 16)), Is.False);
            Assert.That(session.ApplyFrame(Header(ProtocolTrade.CashRegistered, 51, 51, 99, 16)), Is.False);
            var invalid = Registered(51, 5, 77, 3);
            Assert.Throws<ProtocolException>(() => session.ApplyFrame(invalid));
            Assert.Throws<ProtocolException>(() => session.ApplyFrame(Header(ProtocolTrade.ItemRegistered, size: 43)));
            Assert.That(session.Snapshot, Is.SameAs(before));
            var failure = Header(ProtocolTrade.ItemRegistrationFailed, size: 16); Protocol0104.Write32(failure.Payload, 12, 7);
            session.ApplyFrame(failure);
            Assert.That(session.Snapshot.LastErrorCode, Is.EqualTo(7));
            Assert.That(session.Snapshot.Phase, Is.EqualTo(GameplayTradePhase.Active));
            Assert.That(session.Snapshot.OwnItems[0].IsEmpty, Is.True);
        }

        [Test]
        public void CompletionUsesTwelveWireRecordsAndAuthoritativeWalletOnlyOnce()
        {
            var session = Active(); int completions = 0; session.Completed += _ => completions++;
            session.ApplyFrame(Registered(51, 4, 77, 3)); session.ApplyFrame(Registered(52, 1, 88, 8));
            var completed = Header(ProtocolTrade.Completed, 52, size: 400);
            Item(completed.Payload, 12, 7, 88, 1, 3, 1);
            Protocol0104.Write32(completed.Payload, 204, 789);
            Item(completed.Payload, 208, 7, 77, 1, 8, 4);
            Assert.That(session.ApplyFrame(completed), Is.True);
            Assert.That(session.Snapshot.Receipt.Taros, Is.EqualTo(789));
            Assert.That(session.Snapshot.Receipt.ReceivedItems.Count, Is.EqualTo(12));
            Assert.That(session.Snapshot.Receipt.ReceivedItems[0].ItemId, Is.EqualTo(88));
            Assert.That(session.Snapshot.Receipt.ReceivedItems[0].InventorySlot, Is.EqualTo(3));
            Assert.That(session.Snapshot.InventoryMetadataRefreshRequired, Is.True);
            Assert.That(session.ApplyFrame(completed), Is.False); Assert.That(completions, Is.EqualTo(1));
            Assert.Throws<ProtocolException>(() => ProtocolTrade.TryDecode(Header(ProtocolTrade.Completed, size: 176), out _));
            Item(completed.Payload, 28, 7, 99, 1, 3, 2);
            Assert.Throws<ProtocolException>(() => ProtocolTrade.TryDecode(completed, out _));
        }

        [Test]
        public void RequestEncodingRetainsParticipantOrderAndRejectsInvalidSlotsAndMoney()
        {
            var item = new TradeItem(1, 500, 0, 49, 4);
            var payload = ProtocolTrade.ItemPayload(52, 51, 52, item);
            Assert.That(payload.Length, Is.EqualTo(28)); Assert.That(Protocol0104.Read32(payload, 0), Is.EqualTo(52));
            Assert.That(Protocol0104.Read32(payload, 4), Is.EqualTo(51)); Assert.That(Protocol0104.Read32(payload, 8), Is.EqualTo(52));
            Assert.That(Protocol0104.Read16(payload, 14), Is.EqualTo(500)); Assert.That(Protocol0104.Read32(payload, 16), Is.Zero);
            Assert.Throws<ArgumentException>(() => ProtocolTrade.Participants(99, 51, 52));
            Assert.Throws<ArgumentException>(() => ProtocolTrade.Participants(51, 51, 51));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolTrade.CashPayload(51, 51, 52, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolTrade.CashPayload(51, 51, 52, 1000000000));
            Assert.Throws<ArgumentException>(() => ProtocolTrade.ItemPayload(51, 51, 52, new TradeItem(1, 10, 1, 50, 0)));
            Assert.Throws<ArgumentException>(() => ProtocolTrade.ItemPayload(51, 51, 52, new TradeItem(1, 10, 1, 0, 5)));
        }

        static GameplayUiSession InventoryForTrade(int sourceQuantity)
        {
            var bytes = new byte[2700]; Protocol0104.Write32(bytes, 0, 51); Protocol0104.Write64(bytes, 8, 1234);
            Protocol0104.Write32(bytes, 4 + 104, 1000);
            int source = 4 + 244 + 3 * 12;
            Protocol0104.Write16(bytes, source, 7); Protocol0104.Write16(bytes, source + 2, 77);
            Protocol0104.Write32(bytes, source + 4, (18 << 16) | sourceQuantity); Protocol0104.Write32(bytes, source + 8, 987654);
            int unrelated = 4 + 244 + 41 * 12;
            Protocol0104.Write16(bytes, unrelated, 7); Protocol0104.Write16(bytes, unrelated + 2, 99); Protocol0104.Write32(bytes, unrelated + 4, 3);
            var session = new GameplayUiSession(_ => null); session.SetWorld(Protocol0104.DecodeWorldEntry(bytes, 1234)); return session;
        }

        [Test]
        public void SettlementUsesOwnOfferSourceAndPreservesOnlyKnownMetadata()
        {
            var session = InventoryForTrade(5); var previous = session.Snapshot; int changes = 0; session.Changed += _ => changes++;
            var offers = new TradeItem[5]; offers[4] = new TradeItem(7, 77, 2, 3, 4);
            var frame = Header(ProtocolTrade.Completed, 52, size: 400);
            Item(frame.Payload, 12, 7, 88, 4, 8, 1);
            Protocol0104.Write32(frame.Payload, 204, 321);
            Item(frame.Payload, 208, 7, 77, 2, 41, 4); //41 is the PEER destination, not our source3.
            Assert.That(ProtocolTrade.TryDecode(frame, out var receipt), Is.True);
            Assert.That(session.ApplyTradeReceipt(receipt, offers), Is.True);
            Assert.That(changes, Is.EqualTo(1)); Assert.That(session.Snapshot.Taros, Is.EqualTo(321));
            Assert.That(session.Snapshot.Inventory[3].Quantity, Is.EqualTo(3));
            Assert.That(session.Snapshot.Inventory[3].Option >> 16, Is.EqualTo(18));
            Assert.That(session.Snapshot.Inventory[3].KnownTimeLimit, Is.EqualTo(987654));
            Assert.That(session.Snapshot.Inventory[3].MetadataComplete, Is.True);
            Assert.That(session.Snapshot.Inventory[41].ItemId, Is.EqualTo(99));
            Assert.That(session.Snapshot.Inventory[8].ItemId, Is.EqualTo(88));
            Assert.That(session.Snapshot.Inventory[8].Quantity, Is.EqualTo(4));
            Assert.That(session.Snapshot.Inventory[8].MetadataComplete, Is.False);
            Assert.That(session.Snapshot.Inventory[8].KnownTimeLimit, Is.Null);
            Assert.That(previous.Inventory[3].Quantity, Is.EqualTo(5)); Assert.That(previous.Inventory[8].IsEmpty, Is.True);
            Assert.That(session.ApplyTradeReceipt(receipt, offers), Is.False); Assert.That(changes, Is.EqualTo(1));
            Assert.That(session.CanMoveItem(ItemContainer.Inventory, 8, ItemContainer.Inventory, 9), Is.True);
            // A subsequent complete item-move reply restores the previously missing metadata.
            var move = new byte[40]; Protocol0104.Write32(move, 0, 1); Protocol0104.Write32(move, 4, 8);
            Protocol0104.Write32(move, 20, 1); Protocol0104.Write32(move, 24, 9);
            Protocol0104.Write16(move, 28, 7); Protocol0104.Write16(move, 30, 88); Protocol0104.Write32(move, 32, 4); Protocol0104.Write32(move, 36, 112233);
            session.ApplyFrame(new ProtocolFrame(ProtocolInventory.MoveSuccess, move));
            Assert.That(session.Snapshot.Inventory[9].MetadataComplete, Is.True); Assert.That(session.Snapshot.Inventory[9].KnownTimeLimit, Is.EqualTo(112233));
        }

        [Test]
        public void SettlementClearsOutgoingBeforeReusedDestinationAndRejectsUnmatchedOffersAtomically()
        {
            var session = InventoryForTrade(1); var previous = session.Snapshot;
            var offers = new TradeItem[5]; offers[4] = new TradeItem(7, 77, 1, 3, 4);
            var frame = Header(ProtocolTrade.Completed, 52, size: 400);
            Item(frame.Payload, 12, 7, 88, 1, 3, 1); Item(frame.Payload, 208, 7, 77, 1, 41, 4);
            Protocol0104.Write32(frame.Payload, 204, 555); ProtocolTrade.TryDecode(frame, out var receipt);
            var wrongOffers = new TradeItem[5]; wrongOffers[4] = new TradeItem(7, 77, 2, 3, 4);
            Assert.Throws<ProtocolException>(() => session.ApplyTradeReceipt(receipt, wrongOffers));
            Assert.That(session.Snapshot, Is.SameAs(previous));
            Assert.That(session.ApplyTradeReceipt(receipt, offers), Is.True);
            Assert.That(session.Snapshot.Inventory[3].ItemId, Is.EqualTo(88));
            Assert.That(session.Snapshot.Inventory[3].MetadataComplete, Is.False);
            Assert.That(session.Snapshot.Inventory[3].KnownTimeLimit, Is.Null, "The previous owner's expiry must not leak into the received item.");
            Assert.That(session.Snapshot.Taros, Is.EqualTo(555));
        }
    }

    public sealed partial class NetworkSessionTests
    {
        [Test]
        public async Task TradeCommandsUseLoadedShardAndDoNotInventLocalAcknowledgements()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                Assert.Throws<InvalidOperationException>(() => client.SendTradeOfferAsync(52));
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234); await AssertSelection(loginStream, 1); await SendTicket(loginStream, shardListener.Port);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var stream = shardSocket.GetStream(); await AcceptShardEntry(stream, true); await entering;
                        var loading = client.CompleteLoadingAsync(); await ReadFrame(stream);
                        await WriteFrame(stream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loading;
                        await client.SendTradeOfferAsync(52);
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 2);
                        Assert.That(request.PacketType, Is.EqualTo(ProtocolTrade.OfferRequest));
                        CollectionAssert.AreEqual(ProtocolTrade.Participants(42, 42, 52), request.Payload);
                        Assert.That(client.TryDequeueEvent(out _), Is.False);
                        Assert.Throws<ArgumentException>(() => client.SendTradeAcceptAsync(42, 52));
                        await client.SendTradeRegisterCashAsync(42, 52, 100);
                        request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 3);
                        Assert.That(request.PacketType, Is.EqualTo(ProtocolTrade.RegisterCashRequest));
                        CollectionAssert.AreEqual(ProtocolTrade.CashPayload(42, 42, 52, 100), request.Payload);
                        await WriteFrame(stream, ProtocolTrade.Accepted, ProtocolTrade.Participants(52, 42, 52), Protocol0104.FrontendKey);
                        await WriteFrame(stream, 0x310000d0, new byte[4], Protocol0104.FrontendKey);
                        Assert.That(Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 4).PacketType, Is.EqualTo(0x13000075U));
                        Assert.That(client.TryDequeueEvent(out var received), Is.True);
                        Assert.That(ProtocolTrade.TryDecode(received.Frame, out var update), Is.True);
                        Assert.That(update.Kind, Is.EqualTo(TradeUpdateKind.Accepted));
                    }
                }
            }
        }
    }
}
