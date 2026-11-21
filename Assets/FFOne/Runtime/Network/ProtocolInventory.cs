using System;

namespace FFOne.Network
{
    // Quest counters are not movable slots. Bank access has a separate NPC/session contract.
    public enum ItemContainer { Equipment = 0, Inventory = 1, Bank = 3 }
    public enum InventoryUpdateKind { Move, EquipmentChanged, Delete }

    public sealed class InventoryUpdate
    {
        public InventoryUpdateKind Kind { get; internal set; }
        public int PlayerId { get; internal set; }
        public ItemContainer FromContainer { get; internal set; }
        public int FromSlot { get; internal set; }
        public EquipmentItem FromItem { get; internal set; }
        public ItemContainer ToContainer { get; internal set; }
        public int ToSlot { get; internal set; }
        public EquipmentItem ToItem { get; internal set; }
    }

    /// <summary>Accepted server 0104 item-move/equipment ABI. RustyFusion does not send an item-move failure packet.</summary>
    public static class ProtocolInventory
    {
        public const uint MoveRequest = 0x1300000a, MoveSuccess = 0x3100001a, EquipmentChanged = 0x3100001b;
        public const uint DeleteRequest = 0x13000019, DeleteSuccess = 0x31000039;

        public static byte[] DeletePayload(int slot)
        {
            ValidateSlot(ItemContainer.Inventory, slot);
            return ProtocolTravel.Words((int)ItemContainer.Inventory, slot);
        }

        public static byte[] MovePayload(ItemContainer from, int fromSlot, ItemContainer to, int toSlot)
        {
            ValidateSlot(from, fromSlot); ValidateSlot(to, toSlot);
            var bytes = new byte[16];
            Protocol0104.Write32(bytes, 0, (int)from); Protocol0104.Write32(bytes, 4, fromSlot);
            Protocol0104.Write32(bytes, 8, (int)to); Protocol0104.Write32(bytes, 12, toSlot);
            return bytes;
        }

        public static bool TryDecode(ProtocolFrame frame, out InventoryUpdate update)
        {
            update = null;
            if (frame?.PacketType == DeleteSuccess)
            {
                Protocol0104.RequireSize(frame.Payload, 8);
                int slot = Protocol0104.Read32(frame.Payload, 4);
                if (Protocol0104.Read32(frame.Payload, 0) != (int)ItemContainer.Inventory || slot < 0 || slot >= 50)
                    throw new ProtocolException("Invalid inventory item deletion reply.");
                update = new InventoryUpdate { Kind = InventoryUpdateKind.Delete, FromContainer = ItemContainer.Inventory, FromSlot = slot };
                return true;
            }
            if (frame == null || (frame.PacketType != MoveSuccess && frame.PacketType != EquipmentChanged)) return false;
            bool move = frame.PacketType == MoveSuccess;
            Protocol0104.RequireSize(frame.Payload, move ? 40 : 20);
            var p = frame.Payload;
            var from = move ? (ItemContainer)Protocol0104.Read32(p, 0) : ItemContainer.Equipment;
            int fromSlot = Protocol0104.Read32(p, 4);
            var to = move ? (ItemContainer)Protocol0104.Read32(p, 20) : from;
            int toSlot = move ? Protocol0104.Read32(p, 24) : fromSlot;
            try { ValidateSlot(from, fromSlot); ValidateSlot(to, toSlot); }
            catch (ArgumentException) { throw new ProtocolException("Item update refers to an unsupported container or invalid slot."); }
            var first = Item(p, 8); var second = move ? Item(p, 28) : first;
            if (from == to && fromSlot == toSlot && !first.Equals(second)) throw new ProtocolException("One slot has conflicting post-move values.");
            if (!Fits(from, fromSlot, first.ItemType, first.ItemId) || !Fits(to, toSlot, second.ItemType, second.ItemId))
                throw new ProtocolException("Equipment update contains an incompatible item type.");
            update = new InventoryUpdate { Kind = move ? InventoryUpdateKind.Move : InventoryUpdateKind.EquipmentChanged,
                PlayerId = move ? 0 : Protocol0104.Read32(p, 0), FromContainer = from, FromSlot = fromSlot, FromItem = first,
                ToContainer = to, ToSlot = toSlot, ToItem = second };
            return true;
        }

        public static EquipmentItem ReadItem(byte[] p, int offset) => Item(p, offset);
        static EquipmentItem Item(byte[] p, int offset)
        {
            var value = new EquipmentItem { ItemType = Protocol0104.Read16(p, offset), ItemId = Protocol0104.Read16(p, offset + 2),
                Option = Protocol0104.Read32(p, offset + 4), TimeLimit = Protocol0104.Read32(p, offset + 8) };
            if (value.ItemId < 0 || value.ItemType < 0) throw new ProtocolException("Item update contains a negative identity.");
            return value;
        }

        public static void ValidateSlot(ItemContainer container, int slot)
        {
            int count;
            switch (container)
            {
                case ItemContainer.Equipment: count = 9; break;
                case ItemContainer.Inventory: count = 50; break;
                case ItemContainer.Bank: count = ProtocolServices.BankCapacity; break;
                default: throw new ArgumentOutOfRangeException(nameof(container), "Unsupported item container.");
            }
            if (slot < 0 || slot >= count) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        public static bool Fits(ItemContainer container, int slot, int itemType, int itemId)
        {
            ValidateSlot(container, slot);
            if (itemId == 0 || container != ItemContainer.Equipment) return true;
            return itemType == (slot == 7 ? 0 : slot == 8 ? 10 : slot);
        }
    }
}
