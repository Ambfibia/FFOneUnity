using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed class TravelUpdate
    {
        public uint Type;
        public bool Failed;
        public int Error, Transport, Location, WarpFlags, X, Y, Z, Map, Taros, PlayerId, Ride, Slot, Container;
        public long SkywayFlags0, SkywayFlags1;
        public EquipmentItem Item;
    }
    public static class ProtocolTravel
    {
        public const uint WarpOk = 0x31000090, WarpFail = 0x31000091, MapInfo = 0x3100009a,
            RegisterOk = 0x310000bf, RegisterFail = 0x310000be, TripOk = 0x310000c1, TripFail = 0x310000c0,
            Riding = 0x310000d8, PeerRiding = 0x310000d9, FlightMove = 0x310000da;
        public static byte[] Words(params int[] words)
        { var p = new byte[words.Length * 4]; for (int i = 0; i < words.Length; i++) Protocol0104.Write32(p, i * 4, words[i]); return p; }
        public static bool TryDecode(ProtocolFrame f, out TravelUpdate u)
        {
            u = null; if (f == null) return false; var p = f.Payload; var v = new TravelUpdate { Type = f.PacketType };
            int I(int o) => Protocol0104.Read32(p, o);
            switch (f.PacketType)
            {
                case WarpOk:
                    Protocol0104.RequireSize(p, 36); v.X = I(0); v.Y = I(4); v.Z = I(8); v.Container = I(12); v.Slot = I(16);
                    v.Item = ProtocolInventory.ReadItem(p, 20); v.Taros = I(32); break;
                case MapInfo: Protocol0104.RequireSize(p, 60); v.Map = I(0); if (v.Map < 0) throw new ProtocolException("Invalid instance map."); break;
                case RegisterOk:
                    Protocol0104.RequireSize(p, 28); v.Transport = I(0); v.Location = I(4); v.WarpFlags = I(8);
                    v.SkywayFlags0 = Protocol0104.Read64(p, 12); v.SkywayFlags1 = Protocol0104.Read64(p, 20); break;
                case TripOk: Protocol0104.RequireSize(p, 20); v.Transport = I(0); v.X = I(4); v.Y = I(8); v.Z = I(12); v.Taros = I(16); break;
                case Riding: case PeerRiding: Protocol0104.RequireSize(p, 8); v.PlayerId = I(0); v.Ride = I(4); break;
                case FlightMove: Protocol0104.RequireSize(p, 20); v.PlayerId = I(0); v.X = I(4); v.Y = I(8); v.Z = I(12); break;
                case WarpFail: Protocol0104.RequireSize(p, 4); v.Failed = true; v.Error = I(0); break;
                case RegisterFail: Protocol0104.RequireSize(p, 12); v.Failed = true; v.Transport = I(0); v.Location = I(4); v.Error = I(8); break;
                case TripFail: Protocol0104.RequireSize(p, 8); v.Failed = true; v.Error = I(4); break;
                default: return false;
            }
            if ((v.Type == WarpOk || v.Type == TripOk) && v.Taros < 0) throw new ProtocolException("Negative travel balance.");
            u = v; return true;
        }
    }
    public sealed class CrocUpdate
    {
        public bool Failed, Combined;
        public int LooksSlot, StatsSlot, Taros, Error;
        public EquipmentItem Item;
    }
    public static class ProtocolCroc
    {
        public const uint Ok = 0x31000116, Fail = 0x31000117;
        public static byte[] Combine(int looks, int stats)
        {
            ProtocolInventory.ValidateSlot(ItemContainer.Inventory, looks); ProtocolInventory.ValidateSlot(ItemContainer.Inventory, stats);
            if (looks == stats) throw new ArgumentException("Croc Pot needs two different slots.");
            return ProtocolTravel.Words(looks, stats, 0, 0);
        }
        public static bool TryDecode(ProtocolFrame f, out CrocUpdate u)
        {
            u = null; if (f == null || (f.PacketType != Ok && f.PacketType != Fail)) return false;
            var p = f.Payload; bool failed = f.PacketType == Fail; Protocol0104.RequireSize(p, failed ? 20 : 36);
            var v = new CrocUpdate { Failed = failed, Error = failed ? Protocol0104.Read32(p, 0) : 0,
                LooksSlot = Protocol0104.Read32(p, failed ? 4 : 0), StatsSlot = Protocol0104.Read32(p, failed ? 8 : 16) };
            if (!failed)
            {
                if (v.LooksSlot < 0 || v.LooksSlot >= 50 || v.StatsSlot < 0 || v.StatsSlot >= 50) throw new ProtocolException("Invalid Croc Pot inventory slot.");
                if (v.LooksSlot == v.StatsSlot) throw new ProtocolException("Repeated Croc Pot slot.");
                v.Item = ProtocolInventory.ReadItem(p, 4); v.Taros = Protocol0104.Read32(p, 28); int success = Protocol0104.Read32(p, 32);
                if (v.Taros < 0 || success < 0 || success > 1 || Protocol0104.Read32(p, 20) != 0 || Protocol0104.Read32(p, 24) != 0)
                    throw new ProtocolException("Invalid Croc Pot post-state.");
                v.Combined = success == 1;
            }
            u = v; return true;
        }
    }
    public sealed partial class FusionClient
    {
        public Task RegisterTransportAsync(int npc, int type, int location, CancellationToken token = default)
            => SendGameplayAsync(0x13000068, ProtocolTravel.Words(type, npc, location), token);
        public Task TravelAsync(int npc, int trip, CancellationToken token = default)
            => SendGameplayAsync(0x13000069, ProtocolTravel.Words(npc, trip, 4, 0), token);
        internal Task RaceStartAsync(int npc, bool record, int ticket, CancellationToken token)
            => SendGameplayAsync(0x1300005b, ProtocolTravel.Words(npc, record ? 1 : 0, ticket), token);
        internal Task RaceEndAsync(int npc, int ticket, CancellationToken token)
            => SendGameplayAsync(0x1300005c, ProtocolTravel.Words(npc,ticket), token);
        internal Task RaceCancelAsync(int npc, CancellationToken token)
            => SendGameplayAsync(0x1300005d, ProtocolTravel.Words(npc), token);
        internal Task RaceRingAsync(int id, CancellationToken token)
            => SendGameplayAsync(0x1300005e, ProtocolTravel.Words(id), token);
        public Task WarpNpcAsync(int npc, int warp, int requiredLocation = 4, int requiredSlot = 0, int consumedSlot = -1, CancellationToken token = default)
            => SendGameplayAsync(0x1300004b, ProtocolTravel.Words(npc, warp, requiredLocation, requiredSlot, consumedSlot < 0 ? 4 : 1, Math.Max(0, consumedSlot)), token);
        public Task ChangeMentorAsync(int mentor, CancellationToken token = default)
        {
            if (mentor < 1 || mentor > 4) throw new ArgumentOutOfRangeException(nameof(mentor));
            var payload = new byte[2];
            Protocol0104.Write16(payload, 0, mentor);
            return SendGameplayAsync(0x13000054, payload, token);
        }
        public Task RequestMapPresenceAsync(ulong lastSync, CancellationToken token)
        {
            var payload = new byte[8]; Protocol0104.Write64(payload, 0, unchecked((long)lastSync));
            return SendGameplayAsync(0x130000a7, payload, token);
        }
        public Task CombineItemsAsync(int looks, int stats, CancellationToken token = default)
            => SendGameplayAsync(0x13000098, ProtocolCroc.Combine(looks, stats), token);
        public Task InviteNpcAsync(int npc, CancellationToken token = default) => SendGameplayAsync(0x13000084, ProtocolSocial.Id(npc), token);
        public Task KickNpcAsync(int npc, CancellationToken token = default) => SendGameplayAsync(0x13000085, ProtocolSocial.Id(npc), token);

        // NPC warp removes the player from the server's interest grid. This handshake is also
        // required for group passengers. Instance info follows LOADING_COMPLETE, without map IDs
        // in WARP_SUCC itself. Wait for that info before choosing the scene for an instance.
        TaskCompletionSource<int> warpMap;
        public async Task<WorldEntry> CompleteWarpAsync(TravelUpdate destination, bool instance, CancellationToken token = default)
        {
            var mapReply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            linked.CancelAfter(ReplyTimeout);
            await operations.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (!IsWorldLoaded) throw new InvalidOperationException("No active world to warp.");
                lock (bootstrapLock)
                {
                    warpMap = mapReply; world.MapNumber = 0; world.X = destination.X; world.Y = destination.Y; world.Z = destination.Z;
                    loadingPackets.Clear(); IsWorldLoaded = false;
                }
                await shard.RequestAsync(PacketId.LoadingComplete, new byte[4], new[] { PacketId.LoadingSuccess }, ReplyTimeout, linked.Token).ConfigureAwait(false);
                if (instance)
                    using (linked.Token.Register(() => mapReply.TrySetCanceled()))
                        await mapReply.Task.ConfigureAwait(false);
                lock (bootstrapLock) { world.BootstrapPackets = loadingPackets.ToArray(); loadingPackets.Clear(); IsWorldLoaded = true; warpMap = null; }
                return world;
            }
            catch { DisconnectSockets(); throw; }
            finally { operations.Release(); }
        }
    }
}
