using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed class VehicleUpdate
    {
        public uint Type;
        public bool Failed, Mounted;
        public int Error, PlayerId, StateFlags;
    }
    public readonly struct ExpiredItemSlot
    {
        public ItemContainer Container { get; }
        public int Slot { get; }
        public ExpiredItemSlot(ItemContainer container, int slot) { Container = container; Slot = slot; }
    }
    /// <summary>ffproto 0104: toggle ABI is one unused byte, state is i32 ID + i8 flags + padding.</summary>
    public static class ProtocolVehicle
    {
        public const uint OnRequest = 0x1300009f, OffRequest = 0x130000a0;
        public const uint OnSuccess = 0x31000122, OnFailure = 0x31000123, OffSuccess = 0x31000124, OffFailure = 0x31000125;
        public const uint StateChanged = 0x3100007a, ExpiredItems = 0x31000129;
        public const int MountedFlag = 8, EquipmentSlot = 8;
        public static byte[] TogglePayload() => new byte[1];
        public static bool TryDecode(ProtocolFrame f, out VehicleUpdate update)
        {
            update = null; if (f == null) return false;
            var v = new VehicleUpdate { Type = f.PacketType };
            switch (f.PacketType)
            {
                case OnSuccess: case OffSuccess:
                    Protocol0104.RequireSize(f.Payload, 1); v.Mounted = f.PacketType == OnSuccess; break;
                case OnFailure: case OffFailure:
                    Protocol0104.RequireSize(f.Payload, 4); v.Failed = true; v.Error = Protocol0104.Read32(f.Payload, 0); break;
                case StateChanged:
                    Protocol0104.RequireSize(f.Payload, 8); v.PlayerId = Protocol0104.Read32(f.Payload, 0);
                    if (v.PlayerId <= 0) throw new ProtocolException("Vehicle state has an invalid player identity.");
                    v.StateFlags = f.Payload[4]; v.Mounted = (v.StateFlags & MountedFlag) != 0; break;
                default: return false;
            }
            update = v; return true;
        }
        public static bool TryDecodeExpired(ProtocolFrame f, out IReadOnlyList<ExpiredItemSlot> slots)
        {
            slots = null; if (f == null || f.PacketType != ExpiredItems) return false;
            var p = f.Payload;
            if (p == null || p.Length < 4) throw new ProtocolException("Missing expired-item count.");
            int count = Protocol0104.Read32(p, 0);
            if (count < 0 || count > (Protocol0104.MaximumBodySize - 8) / 8 || p.Length != 4 + count * 8)
                throw new ProtocolException("Invalid expired-item count/length.");
            var result = new ExpiredItemSlot[count]; var unique = new HashSet<(ItemContainer, int)>();
            for (int i = 0; i < count; i++)
            {
                var container = (ItemContainer)Protocol0104.Read32(p, 4 + i * 8); int slot = Protocol0104.Read32(p, 8 + i * 8);
                try { ProtocolInventory.ValidateSlot(container, slot); }
                catch (ArgumentException) { throw new ProtocolException("Expired item refers to an invalid container/slot."); }
                if (!unique.Add((container, slot))) throw new ProtocolException("Repeated expired-item slot.");
                result[i] = new ExpiredItemSlot(container, slot);
            }
            slots = Array.AsReadOnly(result); return true;
        }
    }
    public sealed partial class FusionClient
    {
        public Task SetVehicleAsync(bool mounted, CancellationToken token = default)
            => SendGameplayAsync(mounted ? ProtocolVehicle.OnRequest : ProtocolVehicle.OffRequest, ProtocolVehicle.TogglePayload(), token);
    }
}
