using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    internal static class ProtocolZipline
    {
        internal const uint Request = 0x1300003f, Broadcast = 0x31000077;
        internal const int PayloadSize = 76, BroadcastSize = 88;

        internal static byte[] Movement((int x, int y, int z) start, (int x, int y, int z) position,
            (float x, float y, float z) direction, float moved, float length, int angle, int speed, bool detached)
        {
            var p = new byte[PayloadSize];
            Protocol0104.Write32(p, 8, start.x);
            Protocol0104.Write32(p, 12, start.y);
            Protocol0104.Write32(p, 16, start.z);
            WriteFloat(p, 20, moved);
            WriteFloat(p, 24, length);
            Protocol0104.Write32(p, 32, position.x);
            Protocol0104.Write32(p, 36, position.y);
            Protocol0104.Write32(p, 40, position.z);
            WriteFloat(p, 44, direction.x);
            WriteFloat(p, 48, direction.y);
            WriteFloat(p, 52, direction.z);
            Protocol0104.Write32(p, 56, detached ? 1 : 0);
            Protocol0104.Write32(p, 60, 30);
            Protocol0104.Write32(p, 68, angle);
            Protocol0104.Write32(p, 72, speed);
            return p;
        }

        internal static WorldUpdate ReadBroadcast(byte[] p)
        {
            Protocol0104.RequireSize(p, BroadcastSize);
            int id = Protocol0104.Read32(p, 76);
            int detach = Protocol0104.Read32(p, 56);
            float moved = BitConverter.Int32BitsToSingle(Protocol0104.Read32(p, 20));
            float length = BitConverter.Int32BitsToSingle(Protocol0104.Read32(p, 24));
            if (id <= 0 || detach is < 0 or > 1 || !float.IsFinite(moved) || !float.IsFinite(length) ||
                moved < 0 || length <= 0 || moved > length) throw new ProtocolException("Invalid zipline movement.");
            bool attached = detach == 0 && moved < length;
            return new WorldUpdate
            {
                Kind = WorldUpdateKind.Move, HasHeading = true,
                Entity = new WorldEntity
                {
                    Kind = WorldEntityKind.Player, Id = id, Ziplining = attached, Jumping = !attached,
                    X = Protocol0104.Read32(p, 32), Y = Protocol0104.Read32(p, 36), Z = Protocol0104.Read32(p, 40),
                    Angle = Protocol0104.Read32(p, 68), Speed = Math.Max(0, Protocol0104.Read32(p, 72)),
                    ServerTime = unchecked((ulong)Protocol0104.Read64(p, 80))
                }
            };
        }

        static void WriteFloat(byte[] bytes, int offset, float value)
            => Protocol0104.Write32(bytes, offset, BitConverter.SingleToInt32Bits(value));
    }

    public sealed partial class FusionClient
    {
        internal Task SendZiplineAsync(byte[] movement, CancellationToken cancellationToken)
            => SendGameplayAsync(ProtocolZipline.Request, movement, cancellationToken);
    }
}
