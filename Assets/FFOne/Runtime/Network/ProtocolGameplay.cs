using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public readonly struct CombatHit
    {
        public WorldEntityKind Kind { get; }
        public int Id { get; }
        public int Damage { get; }
        public int Hp { get; }
        public CombatHit(WorldEntityKind kind, int id, int damage, int hp) { Kind = kind; Id = id; Damage = damage; Hp = hp; }
    }
    public sealed class CombatUpdate
    {
        public int? WeaponBattery { get; internal set; }
        public int AttackerId { get; internal set; }
        public WorldEntityKind AttackerKind { get; internal set; }
        public IReadOnlyList<CombatHit> Hits { get; internal set; }
    }

    /// <summary>Gameplay intents and validated server results. No intent changes authoritative state.</summary>
    public static class ProtocolGameplay
    {
        public const uint AttackRequest = 0x13000006, RegenRequest = 0x13000009;
        public const uint NanoEquipRequest = 0x1300000d, NanoUnequipRequest = 0x1300000e, NanoActiveRequest = 0x1300000f;
        public const uint AttackSuccess = 0x31000014, AttackBroadcast = 0x31000015, NpcAttack = 0x31000016;
        public const uint RegenSuccess = 0x31000017, NanoEquipSuccess = 0x31000026, NanoUnequipSuccess = 0x31000027;
        public const uint NanoActiveSuccess = 0x31000028, NanoActiveBroadcast = 0x3100002a, ChargeNano = 0x31000032;
        public const uint Tick = 0x31000033, NanoCreate = 0x31000053, Reward = 0x3100007c;

        public static byte[] AttackPayload(IReadOnlyList<int> npcIds)
        {
            if (npcIds == null || npcIds.Count < 1 || npcIds.Count > 3) throw new ArgumentException("Invalid NPC target count.");
            var seen = new HashSet<int>(); var p = new byte[4 + npcIds.Count * 4]; I(p, 0, npcIds.Count);
            for (int i = 0; i < npcIds.Count; i++)
            { if (npcIds[i] <= 0 || !seen.Add(npcIds[i])) throw new ArgumentException("Attack targets must be distinct positive IDs."); I(p, 4 + 4 * i, npcIds[i]); }
            return p;
        }
        public static byte[] CharacterAttackPayload(IReadOnlyList<(WorldEntityKind kind, int id)> targets)
        {
            if (targets == null || targets.Count < 1 || targets.Count > 32) throw new ArgumentException("Invalid character target count.");
            var payload = new byte[4 + targets.Count * 8]; I(payload, 0, targets.Count);
            var seen = new HashSet<(WorldEntityKind, int)>();
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.id <= 0 || (target.kind != WorldEntityKind.Player && target.kind != WorldEntityKind.Npc) || !seen.Add(target)) throw new ArgumentException("Invalid character target.");
                I(payload, 4 + i * 8, target.id); I(payload, 8 + i * 8, target.kind == WorldEntityKind.Player ? 1 : 4);
            }
            return payload;
        }
        public static byte[] NanoSlotPayload(int slot, bool deactivate = false)
        {
            if (slot < (deactivate ? -1 : 0) || slot > 2) throw new ArgumentOutOfRangeException(nameof(slot));
            var p = new byte[2]; Protocol0104.Write16(p, 0, slot); return p;
        }
        public static byte[] EquipNanoPayload(int nanoId, int slot)
        {
            NanoSlotPayload(slot);
            if (nanoId <= 0 || nanoId > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(nanoId));
            var p = new byte[4]; Protocol0104.Write16(p, 0, nanoId); Protocol0104.Write16(p, 2, slot); return p;
        }
        public static byte[] RegenPayload(bool unstick)
        { var p = new byte[12]; I(p, 0, unstick ? 5 : 1); return p; }

        public static byte[] PhoenixRegenPayload(bool group)
        { var p = new byte[12]; I(p, 0, group ? 4 : 3); return p; }

        public static bool TryCombat(ProtocolFrame frame, out CombatUpdate update)
        {
            update = null; if (frame == null) return false;
            uint type = frame.PacketType;
            if (type == ProtocolProjectiles.RocketDetonate || type == ProtocolProjectiles.GrenadeDetonate)
            {
                ProtocolProjectiles.TryDecode(frame, out var projectile);
                update = new CombatUpdate { AttackerKind = WorldEntityKind.Player, AttackerId = projectile.PlayerId, Hits = projectile.Hits }; return true;
            }
            bool own = type == AttackSuccess || type == 0x31000102;
            bool npc = type == NpcAttack || type == 0x31000104;
            if (!own && !npc && type != AttackBroadcast && type != 0x31000103) return false;
            var p = frame.Payload;
            if (p == null || p.Length < 8) throw new ProtocolException("Missing combat result header.");
            int count = I(p, 4);
            var hits = ReadCombatHits(p, 8, count);
            update = new CombatUpdate { WeaponBattery = own ? I(p, 0) : (int?)null,
                AttackerId = own ? 0 : I(p, 0), AttackerKind = npc ? WorldEntityKind.Npc : WorldEntityKind.Player,
                Hits = hits };
            if (update.WeaponBattery < 0) throw new ProtocolException("Negative weapon battery.");
            return true;
        }
        internal static IReadOnlyList<CombatHit> ReadCombatHits(byte[] p, int prefix, int count)
        {
            if (count < 0 || count > (Protocol0104.MaximumBodySize - 4 - prefix) / 24 || p.Length != prefix + count * 24) throw new ProtocolException("Invalid combat result count/length.");
            var hits = new CombatHit[count];
            for (int i = 0; i < count; i++)
            {
                int o = prefix + i * 24, kind = I(p, o), id = I(p, o + 4), damage = I(p, o + 12), hp = I(p, o + 16);
                // eCharType: PC=1, NPC=2, Mob=4. IDs can overlap across player/NPC namespaces.
                if ((kind != 1 && kind != 2 && kind != 4) || id <= 0 || damage < 0 || hp < 0) throw new ProtocolException("Invalid combat target post-state.");
                hits[i] = new CombatHit(kind == 1 ? WorldEntityKind.Player : WorldEntityKind.Npc, id, damage, hp);
            }
            return Array.AsReadOnly(hits);
        }
        public static int I(byte[] p, int o) => Protocol0104.Read32(p, o);
        public static void I(byte[] p, int o, int value) => Protocol0104.Write32(p, o, value);
    }
}
