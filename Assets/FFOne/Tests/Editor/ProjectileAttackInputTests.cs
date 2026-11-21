using FFOne.UI;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class ProjectileAttackInputTests
    {
        [Test]
        public void ShortPressFiresOnceOnReleaseWithoutShowingAim()
        {
            var input = new ProjectileAttackInput();
            Assert.That(input.Tick(true, true, false, 0), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(false, true, false, .25), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(false, false, true, .3), Is.EqualTo(ProjectileAttackPhase.BlindRelease));
            Assert.That(input.Tick(false, false, true, .3), Is.EqualTo(ProjectileAttackPhase.Idle));
        }

        [Test]
        public void HoldShowsAimAfterOneSecondAndFiresOnlyOnRelease()
        {
            var input = new ProjectileAttackInput();
            input.Tick(true, true, false, 0);
            Assert.That(input.Tick(false, true, false, .999), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(false, true, false, 1), Is.EqualTo(ProjectileAttackPhase.Aiming));
            Assert.That(input.Tick(false, true, false, 3), Is.EqualTo(ProjectileAttackPhase.Aiming));
            Assert.That(input.Tick(false, false, true, 3.1), Is.EqualTo(ProjectileAttackPhase.AimedRelease));
            Assert.That(input.Tick(false, false, false, 3.2), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(true, false, true, 3.3), Is.EqualTo(ProjectileAttackPhase.BlindRelease));
        }

        [Test]
        public void CanceledPressCannotFireWhenInputIsUnblocked()
        {
            var input = new ProjectileAttackInput();
            input.Tick(true, true, false, 0);
            input.Cancel();
            Assert.That(input.Tick(false, true, false, 2), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(false, false, true, 3), Is.EqualTo(ProjectileAttackPhase.Idle));
            Assert.That(input.Tick(true, false, true, 4), Is.EqualTo(ProjectileAttackPhase.BlindRelease));
        }

        [Test]
        public void GrenadeAimVerticalSpeedContributesToLaunch()
        {
            var weapon = new WorldWeaponDefinition { Range = 8, GrenadeVerticalSpeed = 6 };
            var level = NetworkProjectileMotor.InitialVelocity(Vector3.zero, Vector3.forward, 6, weapon);
            var down = NetworkProjectileMotor.InitialVelocity(Vector3.zero, Vector3.forward + Vector3.down, 6, weapon);
            Assert.That(down.y, Is.LessThan(level.y));
            Assert.That(NetworkProjectileMotor.GrenadePosition(Vector3.zero, down, .5f).y,
                Is.LessThan(NetworkProjectileMotor.GrenadePosition(Vector3.zero, level, .5f).y));
        }

        [Test]
        public void GrenadeSweepIgnoresWorldTriggerAndStopsAtSolidFloor()
        {
            var origin = new Vector3(732, 814, -432);
            var floor = new GameObject("Projectile test floor");
            var trigger = new GameObject("Projectile test trigger");
            try
            {
                floor.transform.position = origin;
                var solid = floor.AddComponent<BoxCollider>();
                solid.size = new Vector3(4, .1f, 4);
                trigger.transform.position = origin + Vector3.up;
                trigger.AddComponent<BoxCollider>().isTrigger = true;
                Physics.SyncTransforms();
                Assert.That(NetworkProjectileMotor.Trace(origin + Vector3.up * 2, origin + Vector3.down * 2, new RaycastHit[8],
                    out var hit, NetworkProjectileMotor.GrenadeCollisionRadius), Is.True);
                Assert.That(hit.collider, Is.EqualTo(solid));
                Assert.That(hit.distance, Is.EqualTo(1.75f).Within(.001f));
            }
            finally
            {
                Object.DestroyImmediate(trigger);
                Object.DestroyImmediate(floor);
            }
        }
    }
}
