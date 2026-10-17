using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    /// <summary>Retrobution 0104 bus lifecycle and passenger movement, pack(4).</summary>
    public static class ProtocolSlider
    {
        public const uint Enter = 0x3100009b, Exit = 0x3100009c, Move = 0x3100009d,
            New = 0x3100009e, Around = 0x3100009f, LeaveAround = 0x310000a0,
            PassengerMove = 0x310000b4, WarpOk = 0x310000f5;
        public const string Model = "characters/transportation/downtown_bus/DT_ETC_Downtownbus_A_00.glb";
        public static bool TryDecode(ProtocolFrame frame, out IReadOnlyList<WorldUpdate> updates)
        {
            updates = null; if (frame == null) return false;
            var p = frame.Payload; var result = new List<WorldUpdate>();
            int I(int o) => Protocol0104.Read32(p, o);
            WorldEntity Appearance(int o)
            {
                if (I(o) != 3) throw new ProtocolException("Unsupported transportation kind.");
                return new WorldEntity { Kind = WorldEntityKind.Transportation, Id = Identity(I(o + 4)),
                    TransportationType = I(o + 8), X = I(o + 12), Y = I(o + 16), Z = I(o + 20) };
            }
            void Page(int header, int stride, int count, Action<int> read)
            {
                if (count < 0 || count > (Protocol0104.MaximumBodySize - header) / stride || p.Length != header + count * stride)
                    throw new ProtocolException("Invalid transportation page.");
                for (int i = 0; i < count; i++) read(header + i * stride);
            }
            switch (frame.PacketType)
            {
                case Enter: case New:
                    Protocol0104.RequireSize(p, 24); result.Add(new WorldUpdate { Kind = WorldUpdateKind.Enter, Entity = Appearance(0) }); break;
                case Around:
                    if (p == null || p.Length < 4) throw new ProtocolException("Missing transportation page.");
                    Page(4, 24, I(0), o => result.Add(new WorldUpdate { Kind = WorldUpdateKind.Enter, Entity = Appearance(o) })); break;
                case Exit:
                    Protocol0104.RequireSize(p, 8);
                    if (I(0) != 3) throw new ProtocolException("Unsupported transportation kind.");
                    result.Add(Leave(Identity(I(4)))); break;
                case LeaveAround:
                    if (p == null || p.Length < 8 || I(0) != 3) throw new ProtocolException("Invalid transportation removal page.");
                    Page(8, 4, I(4), o => result.Add(Leave(Identity(I(o))))); break;
                case Move:
                    Protocol0104.RequireSize(p, 28);
                    if (I(0) != 3 || I(20) < 0) throw new ProtocolException("Invalid transportation movement.");
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Move, Entity = new WorldEntity {
                        Kind = WorldEntityKind.Transportation, Id = Identity(I(4)), X = I(8), Y = I(12), Z = I(16), Speed = I(20) } }); break;
                case PassengerMove:
                    Protocol0104.RequireSize(p, 72);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Move, HasHeading = true, Entity = new WorldEntity {
                        Kind = WorldEntityKind.Player, Id = Identity(I(60)), TransportId = Identity(I(44)),
                        LocalX = I(8), LocalY = I(12), LocalZ = I(16), X = I(20), Y = I(24), Z = I(28),
                        Angle = I(48), Speed = Math.Max(0, I(56)), ServerTime = unchecked((ulong)Protocol0104.Read64(p, 64)) } }); break;
                default: return false;
            }
            updates = result.AsReadOnly(); return true;
        }
        static WorldUpdate Leave(int id) => new WorldUpdate { Kind = WorldUpdateKind.Leave,
            Entity = new WorldEntity { Kind = WorldEntityKind.Transportation, Id = id } };
        static int Identity(int id) { if (id <= 0) throw new ProtocolException("Invalid transportation identity."); return id; }
        public static byte[] PassengerPayload(int id, int lx, int ly, int lz, int x, int y, int z,
            int angle, float vx, float vy, float vz, int speed, byte keys)
        {
            Identity(id); if (speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
            var p = new byte[60]; Protocol0104.Write32(p, 8, lx); Protocol0104.Write32(p, 12, ly); Protocol0104.Write32(p, 16, lz);
            Protocol0104.Write32(p, 20, x); Protocol0104.Write32(p, 24, y); Protocol0104.Write32(p, 28, z);
            float[] velocity = { vx, vy, vz };
            for (int i = 0; i < 3; i++)
            {
                if (float.IsNaN(velocity[i]) || float.IsInfinity(velocity[i])) throw new ArgumentException("Nonfinite passenger velocity.");
                var bytes = BitConverter.GetBytes(velocity[i]); if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                Buffer.BlockCopy(bytes, 0, p, 32 + 4 * i, 4);
            }
            Protocol0104.Write32(p, 44, id); Protocol0104.Write32(p, 48, angle); p[52] = keys; Protocol0104.Write32(p, 56, speed);
            return p;
        }
    }
    public sealed partial class FusionClient
    {
        public Task SendSliderMovementAsync(int id, int lx, int ly, int lz, int x, int y, int z, int angle,
            float vx, float vy, float vz, int speed, byte keys, CancellationToken token = default)
        {
            if (!IsWorldLoaded) throw new InvalidOperationException("World is not loaded.");
            return SendGameplayAsync(0x13000062, ProtocolSlider.PassengerPayload(id, lx, ly, lz, x, y, z, angle, vx, vy, vz, speed, keys), token);
        }
    }
}
