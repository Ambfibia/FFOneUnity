using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public enum ProjectilePhase { Ready, Fire, Hit }
    public sealed class ProjectileUpdate
    {
        public ProjectilePhase Phase;
        public int PlayerId, Slot, WeaponId, Mode, X, Y, Z, ToX, ToY, ToZ;
        public bool Own, Charged, HasStart;
        public int? WeaponBattery;
        public IReadOnlyList<CombatHit> Hits = Array.Empty<CombatHit>();
    }
    public static class ProtocolProjectiles
    {
        public const uint RocketReady = 0x1300001b, RocketFire = 0x1300001c, RocketHit = 0x1300001d;
        public const uint GrenadeReady = 0x1300001e, GrenadeFire = 0x1300001f, GrenadeHit = 0x13000020;
        public const uint RocketReadyPeer = 0x3100003a, RocketSuccess = 0x3100003b, RocketPeer = 0x3100003c, RocketDetonate = 0x3100003d;
        public const uint GrenadeReadyPeer = 0x3100003e, GrenadeSuccess = 0x3100003f, GrenadePeer = 0x31000040, GrenadeDetonate = 0x31000041;
        public static byte[] Fire(int mode, int x, int y, int z, int toX, int toY, int toZ)
        {
            if (mode != 5 && mode != 6) throw new ArgumentOutOfRangeException(nameof(mode));
            return mode == 5 ? ProtocolTravel.Words(0, x, y, z, toX, toY, toZ) : ProtocolTravel.Words(0, toX, toY, toZ);
        }
        public static byte[] Hit(int slot, int x, int y, int z, IReadOnlyList<int> targets)
        {
            if (slot < 0 || slot >= 3 || targets == null || targets.Count > 32) throw new ArgumentException("Invalid projectile slot or target count.");
            var payload = new byte[20 + targets.Count * 8]; payload[0] = (byte)slot;
            Protocol0104.Write32(payload, 4, x); Protocol0104.Write32(payload, 8, y); Protocol0104.Write32(payload, 12, z); Protocol0104.Write32(payload, 16, targets.Count);
            var seen = new HashSet<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] <= 0 || !seen.Add(targets[i])) throw new ArgumentException("Invalid projectile target identity.");
                Protocol0104.Write32(payload, 20 + i * 8, targets[i]); // native int64 low-word ID
            }
            return payload;
        }
        public static bool TryDecode(ProtocolFrame frame, out ProjectileUpdate update)
        {
            update = null; if (frame == null || frame.PacketType < RocketReadyPeer || frame.PacketType > GrenadeDetonate) return false;
            uint type = frame.PacketType; byte[] p = frame.Payload;
            int I(int offset) => Protocol0104.Read32(p, offset);
            var next = new ProjectileUpdate { Mode = type <= RocketDetonate ? 5 : 6 };
            if (type == RocketReadyPeer || type == GrenadeReadyPeer)
            {
                Protocol0104.RequireSize(p, 8); next.Phase = ProjectilePhase.Ready; next.PlayerId = I(0);
                if (next.PlayerId <= 0 || I(4) != 0) throw new ProtocolException("Invalid weapon ready identity.");
            }
            else if (type == RocketDetonate || type == GrenadeDetonate)
            {
                if (p == null || p.Length < 24) throw new ProtocolException("Missing projectile impact header.");
                next.Phase = ProjectilePhase.Hit; next.PlayerId = I(0); next.Slot = p[4];
                next.WeaponId = I(12); next.Charged = I(16) == 1;
                if (next.PlayerId <= 0 || next.Slot >= 3 || next.WeaponId <= 0 || I(16) < 0 || I(16) > 1) throw new ProtocolException("Invalid projectile impact identity.");
                // Native grenade HIT also carries rocket impacts. iID=1 is
                // the grenade marker; other values identify rocket weapons.
                next.Mode = next.WeaponId == 1 ? 6 : 5;
                next.Hits = ProtocolGameplay.ReadCombatHits(p, 24, I(20));
            }
            else
            {
                next.Phase = ProjectilePhase.Fire; next.Own = type == RocketSuccess || type == GrenadeSuccess;
                bool rocket = type == RocketSuccess || type == RocketPeer;
                Protocol0104.RequireSize(p, rocket ? (next.Own ? 56 : 48) : (next.Own ? 44 : 36));
                next.PlayerId = next.Own ? 0 : I(0); next.HasStart = rocket;
                int destination = rocket ? 16 : 4, slot = rocket ? 28 : 16;
                if (rocket) { next.X = I(4); next.Y = I(8); next.Z = I(12); }
                next.ToX = I(destination); next.ToY = I(destination + 4); next.ToZ = I(destination + 8);
                next.Slot = p[slot]; next.WeaponId = I(slot + 8); next.Charged = I(slot + 12) == 1;
                if (next.Slot >= 3 || next.WeaponId <= 0 || I(slot + 12) < 0 || I(slot + 12) > 1 || (!next.Own && next.PlayerId <= 0)) throw new ProtocolException("Invalid fired projectile identity.");
                if (rocket == (next.WeaponId == 1)) throw new ProtocolException("Projectile marker does not match the native fire family.");
                if (next.Own)
                {
                    next.WeaponBattery = I(slot + 16);
                    if (next.WeaponBattery < 0 || I(0) != 0) throw new ProtocolException("Invalid projectile weapon post-state.");
                }
                // FIRE packet IDs identify the family. Native grenade iID=1
                // is a protocol marker shared by all 53 accepted grenade items.
            }
            update = next; return true;
        }
    }
    public sealed partial class FusionClient
    {
        public Task FireProjectileAsync(int mode, int x, int y, int z, int toX, int toY, int toZ, CancellationToken token = default)
            => SendGameplayAsync(mode == 5 ? ProtocolProjectiles.RocketFire : ProtocolProjectiles.GrenadeFire, ProtocolProjectiles.Fire(mode, x, y, z, toX, toY, toZ), token);
        public Task HitProjectileAsync(int mode, int slot, int x, int y, int z, IReadOnlyList<int> targets, CancellationToken token = default)
        {
            if (mode != 5 && mode != 6) throw new ArgumentOutOfRangeException(nameof(mode));
            return SendGameplayAsync(mode == 5 ? ProtocolProjectiles.RocketHit : ProtocolProjectiles.GrenadeHit, ProtocolProjectiles.Hit(slot, x, y, z, targets), token);
        }
        public Task AttackCharactersAsync(IReadOnlyList<(WorldEntityKind kind, int id)> targets, CancellationToken token = default)
            => SendGameplayAsync(0x13000090, ProtocolGameplay.CharacterAttackPayload(targets), token);
    }
}
