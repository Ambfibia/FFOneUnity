using System;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class WeaponCombatProtocolTests
    {
        static void I(byte[] p, int offset, int value) => Protocol0104.Write32(p, offset, value);
        static ProtocolFrame Fire(bool rocket, bool own, bool charged = true)
        {
            var p = new byte[rocket ? (own ? 56 : 48) : (own ? 44 : 36)];
            I(p, 0, own ? 0 : 51); int slot = rocket ? 28 : 16, to = rocket ? 16 : 4;
            if (rocket) { I(p, 4, 100); I(p, 8, 200); I(p, 12, 300); }
            I(p, to, 400); I(p, to + 4, 500); I(p, to + 8, 600);
            p[slot] = 2; I(p, slot + 8, rocket ? 781 : 1); I(p, slot + 12, charged ? 1 : 0);
            if (own) I(p, slot + 16, 750);
            return new ProtocolFrame(rocket ? (own ? ProtocolProjectiles.RocketSuccess : ProtocolProjectiles.RocketPeer) : (own ? ProtocolProjectiles.GrenadeSuccess : ProtocolProjectiles.GrenadePeer), p);
        }
        [Test] public void RocketGrenadeAcknowledgementsAndInterestBroadcastsKeepNativeOffsetsAndCharge()
        {
            foreach (bool rocket in new[] { true, false }) foreach (bool own in new[] { true, false }) foreach (bool charged in new[] { true, false })
            {
                Assert.IsTrue(ProtocolProjectiles.TryDecode(Fire(rocket, own, charged), out var p));
                Assert.AreEqual(ProjectilePhase.Fire, p.Phase); Assert.AreEqual(rocket ? 5 : 6, p.Mode); Assert.AreEqual(2, p.Slot);
                Assert.AreEqual(own, p.Own); Assert.AreEqual(charged, p.Charged); Assert.AreEqual(own ? 0 : 51, p.PlayerId);
                Assert.AreEqual(400, p.ToX); Assert.AreEqual(500, p.ToY); Assert.AreEqual(600, p.ToZ);
                Assert.AreEqual(own ? 750 : (int?)null, p.WeaponBattery); Assert.AreEqual(rocket, p.HasStart);
            }
        }
        [TestCase(true, 1)]
        [TestCase(false, 22)]
        public void FireRejectsAnItemIdInPlaceOfTheNativeFamilyMarker(bool rocket, int invalidMarker)
        {
            var frame = Fire(rocket, true);
            I(frame.Payload, (rocket ? 28 : 16) + 8, invalidMarker);
            Assert.Throws<ProtocolException>(() => ProtocolProjectiles.TryDecode(frame, out _));
        }
        [TestCase(ProtocolProjectiles.RocketDetonate, 781, 5)]
        [TestCase(ProtocolProjectiles.GrenadeDetonate, 781, 5)]
        [TestCase(ProtocolProjectiles.GrenadeDetonate, 1, 6)]
        public void SharedNativeImpactPreservesTheBulletFamily(uint packet, int marker, int mode)
        {
            var payload = new byte[24]; I(payload, 0, 51); I(payload, 12, marker);
            Assert.IsTrue(ProtocolProjectiles.TryDecode(new ProtocolFrame(packet, payload), out var result));
            Assert.AreEqual(ProjectilePhase.Hit, result.Phase);
            Assert.AreEqual(marker, result.WeaponId); Assert.AreEqual(mode, result.Mode);
        }
        [Test] public void NativeImpactHasBulletPrefixAndAuthoritativeTwentyFourByteResults()
        {
            var p = new byte[48]; I(p, 0, 51); p[4] = 2; I(p, 12, 781); I(p, 16, 1); I(p, 20, 1);
            I(p, 24, 4); I(p, 28, 12); I(p, 36, 75); I(p, 40, 275);
            var frame = new ProtocolFrame(ProtocolProjectiles.RocketDetonate, p);
            Assert.IsTrue(ProtocolGameplay.TryCombat(frame, out var combat)); Assert.AreEqual(51, combat.AttackerId);
            Assert.AreEqual(1, combat.Hits.Count); Assert.AreEqual(WorldEntityKind.Npc, combat.Hits[0].Kind);
            Assert.AreEqual(12, combat.Hits[0].Id); Assert.AreEqual(75, combat.Hits[0].Damage); Assert.AreEqual(275, combat.Hits[0].Hp);
            Assert.IsNull(combat.WeaponBattery);
        }
        [Test] public void RequestsPreserveEightByteTargetsAndRejectMalformedCountsIdentitiesAndFrames()
        {
            var chars = ProtocolGameplay.CharacterAttackPayload(new[] { (WorldEntityKind.Player, 51), (WorldEntityKind.Npc, 51) });
            Assert.AreEqual(20, chars.Length); Assert.AreEqual(51, Protocol0104.Read32(chars, 4)); Assert.AreEqual(1, Protocol0104.Read32(chars, 8));
            Assert.AreEqual(51, Protocol0104.Read32(chars, 12)); Assert.AreEqual(4, Protocol0104.Read32(chars, 16));
            var hit = ProtocolProjectiles.Hit(2, 100, 200, 300, new[] { 51, 52 });
            Assert.AreEqual(36, hit.Length); Assert.AreEqual(51, Protocol0104.Read32(hit, 20)); Assert.AreEqual(0, Protocol0104.Read32(hit, 24));
            Assert.Throws<ArgumentException>(() => ProtocolProjectiles.Hit(3, 0, 0, 0, Array.Empty<int>()));
            Assert.Throws<ArgumentException>(() => ProtocolProjectiles.Hit(0, 0, 0, 0, new[] { 51, 51 }));
            Assert.Throws<ArgumentException>(() => ProtocolGameplay.CharacterAttackPayload(new[] { (WorldEntityKind.Player, 51), (WorldEntityKind.Player, 51) }));
            Assert.AreEqual(28, ProtocolProjectiles.Fire(5, 1, 2, 3, 4, 5, 6).Length); Assert.AreEqual(16, ProtocolProjectiles.Fire(6, 1, 2, 3, 4, 5, 6).Length);
            foreach (bool rocket in new[] { true, false }) foreach (bool own in new[] { true, false })
            {
                var original = Fire(rocket, own);
                Assert.Throws<ProtocolException>(() => ProtocolProjectiles.TryDecode(new ProtocolFrame(original.PacketType, original.Payload.Take(original.Payload.Length - 1).ToArray()), out _));
                Assert.Throws<ProtocolException>(() => ProtocolProjectiles.TryDecode(new ProtocolFrame(original.PacketType, original.Payload.Concat(new byte[4]).ToArray()), out _));
                var invalid = (byte[])original.Payload.Clone(); invalid[rocket ? 28 : 16] = 255;
                Assert.Throws<ProtocolException>(() => ProtocolProjectiles.TryDecode(new ProtocolFrame(original.PacketType, invalid), out _));
            }
        }
        [Test] public void AllAcceptedWeaponFamiliesAndProjectileEffectsResolveWithoutSubstitution()
        {
            var catalog = WorldWeaponCatalog.Load(); var weapons = catalog.Weapons.ToArray();
            Assert.AreEqual(806, weapons.Length); CollectionAssert.AreEqual(new[] { 202, 178, 226, 73, 74, 53 }, Enumerable.Range(1, 6).Select(mode => weapons.Count(w => w.Mode == mode)).ToArray());
            var projectiles = weapons.Where(w => w.Projectile).ToArray(); Assert.AreEqual(127, projectiles.Length);
            var ids = projectiles.SelectMany(w => new[] { w.Effect, w.BoostedEffect }).Distinct().OrderBy(id => id).ToArray();
            CollectionAssert.AreEqual(new[] { 68, 69, 72, 135, 149, 150, 160, 161, 162, 163, 164 }, ids);
            foreach (var weapon in projectiles) { Assert.Greater(weapon.Range, 0); Assert.Greater(weapon.Radius, 0); Assert.Greater(weapon.Duration, 0); Assert.IsNotNull(catalog.Bullet(weapon.Effect)); Assert.IsNotNull(catalog.Bullet(weapon.BoostedEffect)); }
            Assert.AreEqual(1.4f, catalog.Find(781).Duration, .00001f); Assert.AreEqual(2.5f, catalog.Find(1).Duration, .00001f); Assert.AreEqual(6, catalog.Find(1).GrenadeVerticalSpeed);
            foreach (int id in ids) foreach (bool impact in new[] { false, true })
            {
                var bullet = catalog.Bullet(id); int effect = impact ? bullet.Impact : bullet.Particle;
                string cue = bullet.ImpactSound.Trim(' ', '\'', '"');
                if (cue.Length > 0) Assert.IsTrue(ContentAssets.Load<AudioClip>("audio/sfx/combat/" + cue.Replace('-', '_').ToLowerInvariant()), "Original impact cue " + cue);
                var parent = new GameObject("Weapon effect contract " + id); GameObject spawned = null;
                try {
                    spawned = (GameObject)typeof(NativeCombatEffect).GetMethod("Spawn", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { parent.transform, effect, 1f, !impact, null, -1f, null });
                    Assert.IsTrue(spawned); Assert.Greater(spawned.GetComponentsInChildren<Renderer>(true).Length, 0, "Original bullet " + id + " effect " + effect);
                }
                finally { UnityEngine.Object.DestroyImmediate(parent); }
            }
        }
        [Test] public void NativeGrenadeMarkerOneKeepsEveryAcceptedGrenadeVisualAndFlightParameter()
        {
            // OpenFusion Combat.cpp grenadeFire and addBullet preserve native
            // sPCBullet.iID=1, independently of the equipped grenade item ID.
            var catalog = WorldWeaponCatalog.Load(); var native = catalog.Find(1);
            var grenades = catalog.Weapons.Where(w => w.Mode == 6).ToArray();
            Assert.AreEqual(53, grenades.Length); Assert.AreEqual(72, native.Effect); Assert.AreEqual(163, native.BoostedEffect);
            foreach (var grenade in grenades)
            {
                Assert.AreEqual(native.Effect, grenade.Effect, "Original grenade item " + grenade.Id);
                Assert.AreEqual(native.BoostedEffect, grenade.BoostedEffect);
                Assert.AreEqual(native.Range, grenade.Range); Assert.AreEqual(native.Radius, grenade.Radius);
                Assert.AreEqual(native.Duration, grenade.Duration); Assert.AreEqual(native.GrenadeVerticalSpeed, grenade.GrenadeVerticalSpeed);
                Assert.AreEqual(native.Targets, grenade.Targets);
            }
            foreach (bool own in new[] { true, false })
            {
                Assert.IsTrue(ProtocolProjectiles.TryDecode(Fire(false, own), out var frame));
                Assert.AreEqual(6, frame.Mode); Assert.AreEqual(1, frame.WeaponId);
            }
        }
    }
}
