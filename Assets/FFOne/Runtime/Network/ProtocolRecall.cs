using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed class RecallRegistration
    {
        public bool Failed;
        public int NpcId, X, Y, Z, Error;
    }

    public static class ProtocolRecall
    {
        public const uint Register = 0x13000071, Success = 0x310000cd, Fail = 0x310000ce;
        public static byte[] Registration(int npc)
        {
            if (npc <= 0) throw new ArgumentOutOfRangeException(nameof(npc));
            return ProtocolTravel.Words(npc);
        }
        public static bool TryDecode(ProtocolFrame frame, out RecallRegistration result)
        {
            result = null;
            if (frame == null || (frame.PacketType != Success && frame.PacketType != Fail)) return false;
            bool failed = frame.PacketType == Fail;
            Protocol0104.RequireSize(frame.Payload, failed ? 4 : 16);
            var next = new RecallRegistration { Failed = failed };
            if (failed) next.Error = Protocol0104.Read32(frame.Payload, 0);
            else
            {
                // Historic wire field iMapNum is Retrobution's selected NPC identity.
                next.NpcId = Protocol0104.Read32(frame.Payload, 0);
                next.X = Protocol0104.Read32(frame.Payload, 4);
                next.Y = Protocol0104.Read32(frame.Payload, 8);
                next.Z = Protocol0104.Read32(frame.Payload, 12);
                if (next.NpcId <= 0) throw new ProtocolException("Invalid registered Recall Point identity.");
            }
            result = next; return true;
        }
    }

    public sealed partial class FusionClient
    {
        public Task RegisterRecallPointAsync(int npc, CancellationToken token = default)
            => SendGameplayAsync(ProtocolRecall.Register, ProtocolRecall.Registration(npc), token);
    }
}
