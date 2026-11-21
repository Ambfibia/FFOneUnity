using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public sealed class BuffUpdate
    {
        public int Id, Operation, Source, Condition;
        public ulong RemainingMilliseconds, DurationMilliseconds;
        public int Repeat, Value, Confirm;
    }
    /// <summary>One source-native buff value. Deadlines affect display, never invent a server removal.</summary>
    public sealed class BuffState
    {
        public int Condition { get; private set; }
        readonly Dictionary<int, BuffUpdate> values = new();
        readonly Dictionary<int, double> received = new();
        public bool Has(int id) => id >= 1 && id <= 25 && (Condition & (1 << (id - 1))) != 0;
        public int Value(int id) => Has(id) && values.TryGetValue(id, out var value) ? value.Value : 0;
        public double? RemainingSeconds(int id, double now) => values.TryGetValue(id, out var value) && value.RemainingMilliseconds > 0
            ? Math.Max(0, value.RemainingMilliseconds / 1000.0 - (now - received[id])) : null;
        public void SetCondition(int condition)
        {
            Condition = condition;
            foreach (int id in new List<int>(values.Keys))
                if (!Has(id)) { values.Remove(id); received.Remove(id); }
        }
        public void Apply(BuffUpdate value, double now)
        {
            SetCondition(value.Condition);
            if (value.Operation == 2) { values.Remove(value.Id); received.Remove(value.Id); }
            else { values[value.Id] = value; received[value.Id] = now; }
        }
    }
    /// <summary>Retrobution 0104 pack(4): sTimeBuff28, PC_BUFF_UPDATE44, timeout12, tick12+typed trailer.</summary>
    public static class ProtocolBuff
    {
        public const uint Update = 0x310000df, Timeout = 0x31000060, Tick = 0x3100007f;
        public static BuffState FromEntry(WorldEntry entry, double now)
        {
            Protocol0104.RequireSize(entry.RawLoadData, 2688);
            var result = new BuffState(); var p = entry.RawLoadData;
            int condition = ProtocolGameplay.I(p, 1676);
            result.SetCondition(condition);
            // eCSTB___Add is an add indicator, not a buff-slot ID. Rusty's
            // load snapshot does not identify a timed source here. Preserve
            // flags and wait for its exact PC_BUFF_UPDATE before assigning a value.
            return result;
        }
        public static bool TryUpdate(ProtocolFrame frame, out BuffUpdate result)
        {
            result = null; if (frame == null || frame.PacketType != Update) return false;
            var p = frame.Payload; Protocol0104.RequireSize(p, 44);
            int id = ProtocolGameplay.I(p, 0), operation = ProtocolGameplay.I(p, 4), source = ProtocolGameplay.I(p, 8);
            if (operation < 1 || operation > 3 || (operation != 2 && (source < 1 || source > 6)))
                throw new ProtocolException("Invalid buff update operation/source.");
            result = ReadTime(p, 12, id, operation, source, ProtocolGameplay.I(p, 40)); return true;
        }
        public static bool TryNanoCondition(ProtocolFrame frame, int playerId, out int condition)
        {
            condition = 0;
            if (frame == null || frame.PacketType != ProtocolGameplay.NanoActiveBroadcast) return false;
            var p = frame.Payload;
            Protocol0104.RequireSize(p, 20);
            if (ProtocolGameplay.I(p, 0) != playerId) return false;
            condition = ProtocolGameplay.I(p, 12);
            return true;
        }
        static BuffUpdate ReadTime(byte[] p, int offset, int id, int operation, int source, int condition)
        {
            if (id < 1 || id > 25) throw new ProtocolException("Invalid native buff slot.");
            ulong remaining = unchecked((ulong)Protocol0104.Read64(p, offset)), duration = unchecked((ulong)Protocol0104.Read64(p, offset + 8));
            if (operation != 2 && (remaining > long.MaxValue || duration > long.MaxValue)) throw new ProtocolException("Invalid buff timer.");
            return new BuffUpdate { Id = id, Operation = operation, Source = source, Condition = condition,
                RemainingMilliseconds = remaining, DurationMilliseconds = duration,
                Repeat = ProtocolGameplay.I(p, offset + 16), Value = ProtocolGameplay.I(p, offset + 20), Confirm = ProtocolGameplay.I(p, offset + 24) };
        }
        public static bool TryEffects(ProtocolFrame frame, out IReadOnlyList<NanoSkillEffect> effects)
        {
            effects = null; if (frame == null) return false; var p = frame.Payload;
            if (frame.PacketType == Timeout)
            {
                Protocol0104.RequireSize(p, 12); var effect = ProtocolNanoSkill.Effect(p, 0, -1);
                effect.Condition = ProtocolGameplay.I(p, 8); effects = new[] { effect }; return true;
            }
            if (frame.PacketType != Tick) return false;
            if (p == null || p.Length < 12) throw new ProtocolException("Missing buff tick header.");
            int id = Protocol0104.Read16(p, 8);
            NanoSkillEffect target;
            switch (id)
            {
                case 17: // Infection: sSkillResult_DotDamage32 (negative -2 protected damage is valid)
                    Protocol0104.RequireSize(p, 44); target = ProtocolNanoSkill.Effect(p, 12, 16);
                    target.NanoStamina = Protocol0104.Read16(p, 32); target.NanoDeactivated = ProtocolGameplay.I(p, 36) != 0;
                    target.Condition = ProtocolGameplay.I(p, 40); break;
                case 19: // Bounding Ball: sSkillResult_Damage20
                    Protocol0104.RequireSize(p, 32); target = ProtocolNanoSkill.Effect(p, 12, 16); break;
                case 24: // Heal: sSkillResult_Heal_HP16 (when emitted by the server)
                    Protocol0104.RequireSize(p, 28); target = ProtocolNanoSkill.Effect(p, 12, 12); break;
                default: throw new ProtocolException("Unsupported native periodic buff trailer: " + id);
            }
            int kind = ProtocolGameplay.I(p, 0), raw = ProtocolGameplay.I(p, 4);
            if (raw != target.Id || (kind == 1) != (target.Kind == WorldEntityKind.Player)) throw new ProtocolException("Buff tick target disagrees with trailer.");
            effects = new[] { target }; return true;
        }
    }
}
