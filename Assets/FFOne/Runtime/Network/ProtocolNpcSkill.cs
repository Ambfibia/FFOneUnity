using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public static class ProtocolNpcSkill
    {
        public const uint Hit = 0x31000022, Corruption = 0x31000024;
        public static bool TryDecode(ProtocolFrame frame, out IReadOnlyList<NanoSkillEffect> effects)
        {
            effects = null;
            if (frame == null || (frame.PacketType != Hit && frame.PacketType != Corruption)) return false;
            var p = frame.Payload;
            int prefix = frame.PacketType == Hit ? 28 : 24;
            if (p == null || p.Length < prefix) throw new ProtocolException("Missing NPC skill header.");
            int count = ProtocolGameplay.I(p, prefix - 4);
            if (frame.PacketType == Hit)
            {
                int type = ProtocolGameplay.I(p, 20);
                // RETROROCKET_SELF is the accepted server no-op, with no trailers.
                if (type == 29 && count == 0) { Protocol0104.RequireSize(p, 28); effects = Array.Empty<NanoSkillEffect>(); return true; }
                if (count == 0 && (ProtocolGameplay.I(p, 0) <= 0 || Protocol0104.Read16(p, 4) <= 0))
                    throw new ProtocolException("Invalid empty NPC skill identity.");
                // A ground Mega still resolves when everyone leaves its pinned
                // blast area: native NPC_SKILL_HIT then has a header and zero
                // trailers (OpenFusion Abilities::useNPCSkill). Nano-use SUCC
                // and corruption HIT keep their stricter nonempty contracts.
                effects = ProtocolNanoSkill.ReadEffects(p, prefix, type, count, allowEmptyNpcHit: true); return true;
            }
            if (count < 1 || count > 101 || p.Length != prefix + count * 40) throw new ProtocolException("Invalid corruption result length/count.");
            var result = new NanoSkillEffect[count];
            for (int i = 0; i < count; i++)
            {
                int o = prefix + i * 40;
                // Legacy corruption can serialize a negative lethal HP. Keep the frame intact;
                // the living/dead gameplay projection clamps that signed post-state at zero.
                var effect = ProtocolNanoSkill.Effect(p, o, -1); effect.Hp = Math.Max(0, ProtocolGameplay.I(p, o + 16));
                effect.NanoId = Protocol0104.Read16(p, o + 28); effect.NanoStamina = Protocol0104.Read16(p, o + 30);
                effect.NanoDeactivated = ProtocolGameplay.I(p, o + 24) != 0; effect.Condition = ProtocolGameplay.I(p, o + 32);
                if (effect.NanoId < 0 || effect.NanoStamina < 0) throw new ProtocolException("Invalid corruption Nano post-state.");
                result[i] = effect;
            }
            effects = Array.AsReadOnly(result); return true;
        }
    }
}
