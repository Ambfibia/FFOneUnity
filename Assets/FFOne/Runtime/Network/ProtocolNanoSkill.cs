using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public sealed class NanoSkillEffect
    {
        public WorldEntityKind Kind { get; internal set; }
        public int Id { get; internal set; }
        public int? Hp { get; internal set; }
        public int? Condition { get; internal set; }
        public int? Map { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        public int? NanoId { get; internal set; }
        public int? NanoStamina { get; internal set; }
        public bool NanoDeactivated { get; internal set; }
        public bool Resurrected { get; internal set; }
        internal bool Protected { get; set; }
        public int DrainedBoosts { get; internal set; }
        public int DrainedPotions { get; internal set; }
        public int? WeaponBattery { get; internal set; }
        public int? NanoBattery { get; internal set; }
    }
    public sealed class NanoSkillUpdate
    {
        public int CasterId, SkillId, NanoId, Stamina, Type;
        public bool Deactivated;
        public IReadOnlyList<NanoSkillEffect> Effects;
    }
    public static class ProtocolNanoSkill
    {
        public const uint Request = 0x13000011, Success = 0x3100002b, Broadcast = 0x3100002c;
        public static byte[] Payload(IReadOnlyList<int> targets, int focus = 0)
        {
            if (targets == null || targets.Count > 3) throw new ArgumentException("A Nano skill supports at most three targets.");
            var p = new byte[20 + targets.Count * 4]; ProtocolGameplay.I(p, 4, focus); ProtocolGameplay.I(p, 16, targets.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            { if (targets[i] <= 0 || !seen.Add(targets[i])) throw new ArgumentException("Invalid Nano skill targets."); ProtocolGameplay.I(p, 20 + i * 4, targets[i]); }
            return p;
        }
        public static bool TryDecode(ProtocolFrame frame, out NanoSkillUpdate result)
        {
            result = null;
            if (frame == null || (frame.PacketType != Success && frame.PacketType != Broadcast)) return false;
            var p = frame.Payload; if (p == null || p.Length < 36) throw new ProtocolException("Missing Nano skill result header.");
            int type = ProtocolGameplay.I(p, 28), count = ProtocolGameplay.I(p, 32);
            var effects = ReadEffects(p, 36, type, count);
            result = new NanoSkillUpdate { CasterId = ProtocolGameplay.I(p, 0), SkillId = Protocol0104.Read16(p, 6),
                NanoId = Protocol0104.Read16(p, 24), Stamina = Protocol0104.Read16(p, 26), Deactivated = ProtocolGameplay.I(p, 20) != 0,
                Type = type, Effects = effects };
            if (result.CasterId <= 0 || result.SkillId <= 0 || result.NanoId <= 0 || result.Stamina < 0) throw new ProtocolException("Invalid Nano caster post-state.");
            return true;
        }
        internal static IReadOnlyList<NanoSkillEffect> ReadEffects(byte[] p, int prefix, int type, int count, bool allowEmptyNpcHit = false)
        {
            int stride = Width(type);
            if (count < (allowEmptyNpcHit ? 0 : 1) || count > (Protocol0104.MaximumBodySize - 4 - prefix) / stride || p.Length != prefix + count * stride)
                throw new ProtocolException($"Invalid skill result length/count (type {type}, count {count}, prefix {prefix}, length {p.Length}).");
            var effects = new List<NanoSkillEffect>();
            for (int i = 0; i < count; i++)
            {
                int o = prefix + i * stride;
                if (type == 30) { effects.Add(Effect(p, o, 12)); effects.Add(Effect(p, o + 16, 16)); continue; }
                int hp = type == 26 ? 8 : type == 2 || type == 34 ? 12 :
                    type == 1 || type == 22 || type == 38 || type == 39 || type == 3 || type == 4 || type == 5 || type == 8 ? 16 : -1;
                if (type == 6 || type == 7)
                {
                    var stamina = Effect(p, o, type == 7 ? 12 : -1);
                    int n = o + (type == 7 ? 18 : 10);
                    stamina.NanoId = Protocol0104.Read16(p, n); stamina.NanoStamina = Protocol0104.Read16(p, n + 4);
                    if (stamina.NanoId < 0 || stamina.NanoStamina < 0) throw new ProtocolException("Invalid stamina result.");
                    effects.Add(stamina); continue;
                }
                var effect = Effect(p, o, hp);
                if (type is 1 or 3 or 4 or 5 or 8 or 21 or 22 or 38 or 39 ||
                    hp < 0 && type is not (27 or 28))
                    effect.Protected = ProtocolGameplay.I(p, o + 8) > 0;
                effect.Resurrected = type == 26;
                if (type == 27 || type == 28)
                { effect.Map = ProtocolGameplay.I(p, o + 8); effect.X = ProtocolGameplay.I(p, o + 12); effect.Y = ProtocolGameplay.I(p, o + 16); effect.Z = ProtocolGameplay.I(p, o + 20); }
                if (type == 3 || type == 4 || type == 5 || type == 8)
                { effect.Condition = ProtocolGameplay.I(p, o + 28); effect.NanoStamina = Protocol0104.Read16(p, o + 20); effect.NanoDeactivated = ProtocolGameplay.I(p, o + 24) != 0; }
                else if (type == 21)
                {
                    effect.DrainedBoosts = ProtocolGameplay.I(p, o + 12);
                    effect.DrainedPotions = ProtocolGameplay.I(p, o + 20);
                    effect.WeaponBattery = ProtocolGameplay.I(p, o + 16); effect.NanoBattery = ProtocolGameplay.I(p, o + 24);
                    effect.NanoStamina = Protocol0104.Read16(p, o + 28); effect.NanoDeactivated = ProtocolGameplay.I(p, o + 32) != 0;
                    effect.Condition = ProtocolGameplay.I(p, o + 36);
                    if (effect.DrainedBoosts < 0 || effect.DrainedPotions < 0 || effect.WeaponBattery < 0 || effect.NanoBattery < 0 || effect.NanoStamina < 0) throw new ProtocolException("Invalid drained battery/stamina post-state.");
                }
                else if (hp < 0 && type != 21 && type != 27 && type != 28) effect.Condition = ProtocolGameplay.I(p, o + 12);
                effects.Add(effect);
            }
            return effects.AsReadOnly();
        }
        internal static NanoSkillEffect Effect(byte[] p, int o, int hpOffset)
        {
            int kind = ProtocolGameplay.I(p, o), id = ProtocolGameplay.I(p, o + 4);
            if ((kind != 1 && kind != 2 && kind != 4) || id <= 0) throw new ProtocolException("Invalid Nano effect target.");
            int? hp = hpOffset >= 0 ? ProtocolGameplay.I(p, o + hpOffset) : (int?)null;
            if (hp < 0) throw new ProtocolException("Negative Nano target HP.");
            return new NanoSkillEffect { Kind = kind == 1 ? WorldEntityKind.Player : WorldEntityKind.Npc, Id = id, Hp = hp };
        }
        // The accepted OpenFusion/FFOne result families have different trailer widths.
        static int Width(int type) => type switch {
            26 => 12,
            2 or 6 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 23 or 25 or 31 or 32 or 33 or 34 or 35 or 36 => 16,
            1 or 22 or 38 or 39 => 20, 7 or 27 or 28 => 24, 3 or 4 or 5 or 8 => 32, 30 => 36, 21 => 40,
            _ => throw new ProtocolException("Unsupported Nano result ABI: " + type) };
    }
}
