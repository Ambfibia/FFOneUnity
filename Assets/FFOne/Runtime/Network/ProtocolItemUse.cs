using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace FFOne.Network
{
    public sealed class ItemUseUpdate
    {
        public int PlayerId, Slot, SkillId, Type, Error;
        public bool Own, Failed;
        public GameplayUsedItem Remaining;
        public IReadOnlyList<NanoSkillEffect> Effects = Array.Empty<NanoSkillEffect>();
    }
    public readonly struct GameplayUsedItem
    {
        public readonly int Type, Id, Option, TimeLimit;
        public GameplayUsedItem(int type, int id, int option, int time) { Type = type; Id = id; Option = option; TimeLimit = time; }
    }
    public static class ProtocolItemUse
    {
        public const uint ChestOpen = 0x13000047, ChestOpened = 0x3100007d, ChestFailed = 0x3100007e;
        public const uint Request = 0x13000073, Success = 0x310000d3, Broadcast = 0x310000d4, Fail = 0x310000d2;
        public static byte[] Payload(int slot, int nanoSlot)
        {
            if (slot < 0 || slot >= 50 || nanoSlot < 0 || nanoSlot > 2) throw new ArgumentOutOfRangeException();
            var p = new byte[12]; ProtocolGameplay.I(p, 0, 1); ProtocolGameplay.I(p, 4, slot); Protocol0104.Write16(p, 8, nanoSlot); return p;
        }
        public static bool TryDecode(ProtocolFrame frame, out ItemUseUpdate value)
        {
            value = null; if (frame == null) return false; var p = frame.Payload;
            if (frame.PacketType == Fail)
            { Protocol0104.RequireSize(p, 4); value = new ItemUseUpdate { Failed = true, Error = ProtocolGameplay.I(p, 0) }; return true; }
            bool own = frame.PacketType == Success;
            if (!own && frame.PacketType != Broadcast) return false;
            int prefix = own ? 36 : 16;
            if (p == null || p.Length < prefix) throw new ProtocolException("Missing item skill header.");
            var result = new ItemUseUpdate { Own = own, PlayerId = ProtocolGameplay.I(p, 0), SkillId = Protocol0104.Read16(p, own ? 24 : 4),
                Type = ProtocolGameplay.I(p, own ? 28 : 8) };
            if (result.PlayerId <= 0 || result.SkillId <= 0) throw new ProtocolException("Invalid item skill identity.");
            result.Effects = ProtocolNanoSkill.ReadEffects(p, prefix, result.Type, ProtocolGameplay.I(p, prefix - 4));
            if (own)
            {
                result.Slot = ProtocolGameplay.I(p, 8);
                if (ProtocolGameplay.I(p, 4) != 1 || result.Slot < 0 || result.Slot >= 50) throw new ProtocolException("Invalid used inventory slot.");
                int type = Protocol0104.Read16(p, 12), id = Protocol0104.Read16(p, 14);
                if (type < 0 || id < 0 || (id > 0 && type != 7)) throw new ProtocolException("Invalid remaining general item.");
                result.Remaining = new GameplayUsedItem(type, id, ProtocolGameplay.I(p, 16), ProtocolGameplay.I(p, 20));
            }
            value = result; return true;
        }
    }
    public sealed partial class FusionClient
    {
        public Task OpenChestAsync(int slot, EquipmentItem chest, CancellationToken token = default)
        {
            if (slot < 0 || slot >= 50 || chest.ItemType != 9 || chest.ItemId <= 0)
                throw new ArgumentException("Chest must occupy a main inventory slot.");
            var request = new byte[20];
            ProtocolGameplay.I(request, 0, (int)ItemContainer.Inventory);
            ProtocolGameplay.I(request, 4, slot);
            ProtocolServices.WriteItem(request, 8, chest);
            return SendGameplayAsync(ProtocolItemUse.ChestOpen, request, token);
        }

        public Task UseItemAsync(int slot, int nanoSlot = 0, CancellationToken token = default)
            => SendGameplayAsync(ProtocolItemUse.Request, ProtocolItemUse.Payload(slot, nanoSlot), token);
    }
}
