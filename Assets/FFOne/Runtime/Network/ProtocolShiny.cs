using System;
using System.Collections.Generic;
namespace FFOne.Network
{
    public sealed class ShinyInterest
    {
        public int Id, Type, Map, X, Y, Z;
        public bool Removed;
    }
    /// <summary>Exact native 0104 shiny interest. Decode the complete page before publishing it.</summary>
    public static class ProtocolShiny
    {
        public const uint Enter = 0x310000ad, Leave = 0x310000ae, New = 0x310000af, Around = 0x310000b0, RemoveAround = 0x310000b1;
        public static bool TryDecode(ProtocolFrame frame, out IReadOnlyList<ShinyInterest> values)
        {
            values = null; if (frame == null) return false; var p = frame.Payload; var result = new List<ShinyInterest>();
            bool page = frame.PacketType == Around || frame.PacketType == RemoveAround;
            bool removed = frame.PacketType == Leave || frame.PacketType == RemoveAround;
            if (!page && frame.PacketType != Enter && frame.PacketType != New && frame.PacketType != Leave) return false;
            if (p == null || p.Length < (page ? 4 : 0)) throw new ProtocolException("Missing shiny interest page.");
            int width = removed ? 4 : 24, count = page ? ProtocolGameplay.I(p, 0) : 1, prefix = page ? 4 : 0;
            if (count < 0 || count > (Protocol0104.MaximumBodySize - 8) / width || p.Length != prefix + count * width)
                throw new ProtocolException("Invalid shiny interest count/length.");
            var seen = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int o = prefix + i * width, id = ProtocolGameplay.I(p, o);
                if (id <= 0 || !seen.Add(id)) throw new ProtocolException("Invalid/repeated shiny interest ID.");
                var value = new ShinyInterest { Id = id, Removed = removed };
                if (!removed)
                {
                    value.Type = ProtocolGameplay.I(p, o + 4); value.Map = ProtocolGameplay.I(p, o + 8);
                    value.X = ProtocolGameplay.I(p, o + 12); value.Y = ProtocolGameplay.I(p, o + 16); value.Z = ProtocolGameplay.I(p, o + 20);
                    if (value.Type <= 0 || value.Map < 0) throw new ProtocolException("Invalid shiny type/map.");
                }
                result.Add(value);
            }
            values = result.AsReadOnly(); return true;
        }
    }
}
