using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public sealed class VendorListing
    {
        public int VendorId, SourceSlot, Sort;
        public float Price;
        public EquipmentItem Item;
    }
    public sealed class ServiceUpdate
    {
        public uint Type;
        public int NpcId, VendorId, BankNumber, Slot, Taros, WeaponBattery, NanoBattery, Error;
        public EquipmentItem Item, Remaining;
        public IReadOnlyList<VendorListing> Listings;
        public IReadOnlyList<EquipmentItem> Bank;
        public bool Failed;
    }
    /// <summary>Current RustyFusion 0104 service ABI, including its 200-slot bank extension.</summary>
    public static class ProtocolServices
    {
        public const uint Buy = 0x13000017, Sell = 0x13000018, BankOpen = 0x1300002e, BankClose = 0x1300002f,
            Start = 0x13000030, Table = 0x13000031, Restore = 0x13000032, Battery = 0x1300004a, Interaction = 0x13000078;
        public const uint Bought = 0x31000035, Sold = 0x31000037, BankOpened = 0x31000056, BankClosed = 0x31000058,
            Started = 0x3100005a, Listed = 0x3100005c, Restored = 0x3100005e, BatteryBought = 0x31000080;
        public const int BankCapacity = 200;
        public static byte[] Pair(int a, int b)
        { var p = new byte[8]; Protocol0104.Write32(p, 0, a); Protocol0104.Write32(p, 4, b); return p; }
        public static byte[] Purchase(int npc, int vendor, int list, EquipmentItem item, int slot, bool battery = false)
        {
            if (npc <= 0 || vendor <= 0 || list < 0 || list > 127 || item.ItemId <= 0 || item.ItemType < 0 || item.ItemType > 10 || item.Option <= 0)
                throw new ArgumentException("Invalid vendor purchase identity/quantity.");
            if (!battery) ProtocolInventory.ValidateSlot(ItemContainer.Inventory, slot);
            var p = new byte[battery ? 24 : 28]; Protocol0104.Write32(p, 0, npc); Protocol0104.Write32(p, 4, vendor); p[8] = (byte)list;
            WriteItem(p, 12, item); if (!battery) Protocol0104.Write32(p, 24, slot); return p;
        }
        public static EquipmentItem Item(byte[] p, int o)
        {
            var item = new EquipmentItem { ItemType = Protocol0104.Read16(p, o), ItemId = Protocol0104.Read16(p, o + 2),
                Option = Protocol0104.Read32(p, o + 4), TimeLimit = Protocol0104.Read32(p, o + 8) };
            if (item.ItemType < 0 || item.ItemType > 10 || item.ItemId < 0 || item.TimeLimit < 0) throw new ProtocolException("Invalid service item identity/expiry.");
            return item;
        }
        public static void WriteItem(byte[] p, int o, EquipmentItem item)
        { Protocol0104.Write16(p, o, item.ItemType); Protocol0104.Write16(p, o + 2, item.ItemId); Protocol0104.Write32(p, o + 4, item.Option); Protocol0104.Write32(p, o + 8, item.TimeLimit); }
        public static bool TryDecode(ProtocolFrame frame, out ServiceUpdate update)
        {
            update = null; if (frame == null) return false;
            var p = frame.Payload; var u = new ServiceUpdate { Type = frame.PacketType };
            switch (frame.PacketType)
            {
                case Bought + 1: case Sold + 1: case BankOpened + 1: case BankClosed + 1:
                case Started + 1: case Listed + 1: case Restored + 1: case BatteryBought + 1:
                    Protocol0104.RequireSize(p, 4); u.Failed = true; u.Error = I(p, 0); break;
                case Started:
                    Protocol0104.RequireSize(p, 8); u.NpcId = I(p, 0); u.VendorId = I(p, 4);
                    if (u.NpcId <= 0 || u.VendorId <= 0) throw new ProtocolException("Invalid vendor identity."); break;
                case Listed:
                    Protocol0104.RequireSize(p, 480); var rows = new List<VendorListing>();
                    for (int i = 0; i < 20; i++)
                    {
                        int o = i * 24; var item = Item(p, o + 8); if (item.ItemId == 0) continue;
                        int vendor = I(p, o); float price = BitConverter.ToSingle(p, o + 4);
                        if (vendor <= 0 || float.IsNaN(price) || float.IsInfinity(price) || price < 0) throw new ProtocolException("Invalid vendor listing price/identity.");
                        rows.Add(new VendorListing { SourceSlot = i, VendorId = vendor, Sort = I(p, o + 20), Price = price, Item = item });
                    }
                    u.Listings = rows.AsReadOnly(); break;
                case Bought: case Restored: case Sold:
                    Protocol0104.RequireSize(p, frame.PacketType == Sold ? 32 : 20);
                    u.Taros = I(p, 0); u.Slot = I(p, 4);
                    if (u.Taros < 0 || u.Slot < 0 || u.Slot >= 50) throw new ProtocolException("Invalid vendor inventory/currency post-state.");
                    u.Item = Item(p, 8); if (u.Item.ItemId == 0) throw new ProtocolException("Missing purchased/sold item.");
                    if (frame.PacketType == Sold) u.Remaining = Item(p, 20); break;
                case BatteryBought:
                    Protocol0104.RequireSize(p, 12); u.Taros = I(p, 0); u.WeaponBattery = I(p, 4); u.NanoBattery = I(p, 8);
                    if (u.Taros < 0 || u.WeaponBattery < 0 || u.NanoBattery < 0) throw new ProtocolException("Invalid battery purchase post-state."); break;
                case BankOpened:
                    Protocol0104.RequireSize(p, BankCapacity * 12 + 4); var bank = new EquipmentItem[BankCapacity];
                    for (int i = 0; i < bank.Length; i++) bank[i] = Item(p, i * 12);
                    u.BankNumber = I(p, BankCapacity * 12);
                    if (u.BankNumber < 0 || u.BankNumber > 4) throw new ProtocolException("Invalid RustyFusion bank number.");
                    u.Bank = Array.AsReadOnly(bank); break;
                case BankClosed: Protocol0104.RequireSize(p, 4); break;
                default: return false;
            }
            update = u; return true;
        }
        static int I(byte[] p, int o) => Protocol0104.Read32(p, o);
    }
}
