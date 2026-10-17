using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public enum TradeUpdateKind
    {
        Offer, OfferCancelled, Accepted, Refused, OfferAborted, Confirmed, Cancelled, Aborted,
        Completed, CompletionFailed, ItemRegistered, ItemRegistrationFailed, ItemUnregistered,
        ItemUnregistrationFailed, CashRegistered, CashRegistrationFailed
    }

    /// <summary>sItemTrade omits item expiry; Option is the trade quantity, not full sItemBase metadata.</summary>
    public readonly struct TradeItem
    {
        public int ItemType { get; }
        public int ItemId { get; }
        public int Option { get; }
        public int InventorySlot { get; }
        public int TradeSlot { get; }
        public bool IsEmpty => ItemId == 0;
        public int Quantity => IsEmpty ? 0 : Option > 0 ? Option : 1;
        public TradeItem(int itemType, int itemId, int option, int inventorySlot, int tradeSlot)
        { ItemType = itemType; ItemId = itemId; Option = option; InventorySlot = inventorySlot; TradeSlot = tradeSlot; }
    }

    public sealed class TradeUpdate
    {
        public TradeUpdateKind Kind { get; internal set; }
        public int RequesterId { get; internal set; }
        public int FromId { get; internal set; }
        public int ToId { get; internal set; }
        public int ErrorCode { get; internal set; }
        public int Taros { get; internal set; }
        public TradeItem Item { get; internal set; }
        public TradeItem InventoryItem { get; internal set; }
        public IReadOnlyList<TradeItem> ReceivedItems { get; internal set; } = Array.Empty<TradeItem>();
        public IReadOnlyList<TradeItem> PeerReceivedItems { get; internal set; } = Array.Empty<TradeItem>();
    }

    /// <summary>
    /// RustyFusion/ffproto3cdf273 build104 trade ABI. Five operational offer slots; completion
    /// retains twelve wire records per side (400 bytes), with zero-filled unused records.
    /// </summary>
    public static class ProtocolTrade
    {
        public const int OfferSlots = 5, ReceiptSlots = 12;
        public const uint OfferRequest = 0x13000022, CancelOfferRequest = 0x13000023, AcceptRequest = 0x13000024,
            RefuseRequest = 0x13000025, ConfirmRequest = 0x13000027, CancelRequest = 0x13000028,
            RegisterItemRequest = 0x1300002a, UnregisterItemRequest = 0x1300002b, RegisterCashRequest = 0x1300002c;
        public const uint Offer = 0x31000042, OfferCancelled = 0x31000043, Accepted = 0x31000044,
            Refused = 0x31000045, OfferAborted = 0x31000046, Confirmed = 0x31000047,
            Cancelled = 0x31000048, Aborted = 0x31000049, Completed = 0x3100004a, CompletionFailed = 0x3100004b,
            ItemRegistered = 0x3100004c, ItemRegistrationFailed = 0x3100004d,
            ItemUnregistered = 0x3100004e, ItemUnregistrationFailed = 0x3100004f,
            CashRegistered = 0x31000050, CashRegistrationFailed = 0x31000051;

        public static byte[] Participants(int requesterId, int fromId, int toId)
        {
            ValidateParticipants(requesterId, fromId, toId);
            var bytes = new byte[12]; WriteParticipants(bytes, requesterId, fromId, toId); return bytes;
        }

        public static byte[] CashPayload(int requesterId, int fromId, int toId, int taros)
        {
            ValidateParticipants(requesterId, fromId, toId);
            if (taros < 0 || taros > 999999999) throw new ArgumentOutOfRangeException(nameof(taros));
            var bytes = new byte[16]; WriteParticipants(bytes, requesterId, fromId, toId); Protocol0104.Write32(bytes, 12, taros); return bytes;
        }

        public static byte[] ItemPayload(int requesterId, int fromId, int toId, TradeItem item)
        {
            ValidateParticipants(requesterId, fromId, toId); ValidateItem(item, false);
            var bytes = new byte[28]; WriteParticipants(bytes, requesterId, fromId, toId);
            Protocol0104.Write16(bytes, 12, item.ItemType); Protocol0104.Write16(bytes, 14, item.ItemId);
            Protocol0104.Write32(bytes, 16, item.Option); Protocol0104.Write32(bytes, 20, item.InventorySlot); Protocol0104.Write32(bytes, 24, item.TradeSlot);
            return bytes;
        }

        public static bool TryDecode(ProtocolFrame frame, out TradeUpdate update)
        {
            update = null;
            if (frame == null || frame.PacketType < Offer || frame.PacketType > CashRegistrationFailed) return false;
            uint type = frame.PacketType;
            int size = type == Completed ? 400 : type == ItemRegistered || type == ItemUnregistered ? 44 :
                type == OfferAborted || type == CompletionFailed || type == ItemRegistrationFailed || type == ItemUnregistrationFailed ||
                type == CashRegistered || type == CashRegistrationFailed ? 16 : 12;
            Protocol0104.RequireSize(frame.Payload, size);
            var p = frame.Payload;
            int requester = Protocol0104.Read32(p, 0), from = Protocol0104.Read32(p, 4), to = Protocol0104.Read32(p, 8);
            try { ValidateParticipants(requester, from, to); }
            catch (ArgumentException) { throw new ProtocolException("Trade reply has invalid participants."); }
            var result = new TradeUpdate { Kind = (TradeUpdateKind)(type - Offer), RequesterId = requester, FromId = from, ToId = to };
            if (type == OfferAborted) result.ErrorCode = Protocol0104.Read16(p, 12);
            else if (type == CompletionFailed || type == ItemRegistrationFailed || type == ItemUnregistrationFailed || type == CashRegistrationFailed)
                result.ErrorCode = Protocol0104.Read32(p, 12);
            else if (type == CashRegistered || type == Completed)
            {
                result.Taros = Protocol0104.Read32(p, type == Completed ? 204 : 12);
                if (result.Taros < 0 || result.Taros > 999999999) throw new ProtocolException("Trade reply has an invalid Taros amount.");
            }
            if (type == ItemRegistered || type == ItemUnregistered)
            {
                result.Item = ReadItem(p, 12, false); result.InventoryItem = ReadItem(p, 28, false);
                if (result.Item.TradeSlot != result.InventoryItem.TradeSlot || result.Item.InventorySlot != result.InventoryItem.InventorySlot)
                    throw new ProtocolException("Trade reply has inconsistent item slots.");
            }
            if (type == Completed)
            {
                result.ReceivedItems = ReadReceipt(p, 12); result.PeerReceivedItems = ReadReceipt(p, 208);
            }
            update = result; return true;
        }

        static IReadOnlyList<TradeItem> ReadReceipt(byte[] bytes, int offset)
        {
            var items = new TradeItem[ReceiptSlots]; var destinations = new HashSet<int>(); var offers = new HashSet<int>();
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = ReadItem(bytes, offset + i * 16, true);
                if (!items[i].IsEmpty && (!destinations.Add(items[i].InventorySlot) || !offers.Add(items[i].TradeSlot)))
                    throw new ProtocolException("Trade receipt repeats an inventory or offer slot.");
            }
            return Array.AsReadOnly(items);
        }
        static TradeItem ReadItem(byte[] bytes, int offset, bool allowEmpty)
        {
            var item = new TradeItem(Protocol0104.Read16(bytes, offset), Protocol0104.Read16(bytes, offset + 2),
                Protocol0104.Read32(bytes, offset + 4), Protocol0104.Read32(bytes, offset + 8), Protocol0104.Read32(bytes, offset + 12));
            try { ValidateItem(item, allowEmpty); }
            catch (ArgumentException) { throw new ProtocolException("Trade reply has an invalid item record."); }
            return item;
        }
        static void ValidateItem(TradeItem item, bool allowEmpty)
        {
            if (allowEmpty && item.IsEmpty)
            {
                if (item.ItemType != 0 || item.Option != 0 || item.InventorySlot != 0 || item.TradeSlot != 0)
                    throw new ArgumentException("Unused trade receipt records must be zero-filled.");
                return;
            }
            if (item.ItemId <= 0 || item.ItemId > short.MaxValue || item.ItemType < 0 || item.ItemType > short.MaxValue ||
                item.Option < 0 || item.Option > ushort.MaxValue || item.InventorySlot < 0 || item.InventorySlot >= 50 || item.TradeSlot < 0 || item.TradeSlot >= OfferSlots)
                throw new ArgumentException("Invalid trade item identity, quantity or slot.");
        }
        static void ValidateParticipants(int requester, int from, int to)
        {
            if (from <= 0 || to <= 0 || from == to || (requester != from && requester != to)) throw new ArgumentException("Trade requires two distinct players and a participant requester.");
        }
        static void WriteParticipants(byte[] bytes, int requester, int from, int to)
        { Protocol0104.Write32(bytes, 0, requester); Protocol0104.Write32(bytes, 4, from); Protocol0104.Write32(bytes, 8, to); }
    }
}
