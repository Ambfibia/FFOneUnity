using System;
using System.Linq;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        const int HandSlot = 0, ReserveWeaponSlot = 7;
        float nextWeaponAttack, nextWeaponChange;
        bool characterAttackPermission;
        NetworkWorldController combatWorld;
        readonly ProjectileAttackInput projectileInput = new();
        readonly Vector3[] grenadeAimPoints = new Vector3[33];
        readonly RaycastHit[] projectileAimHits = new RaycastHit[16];
        WorldWeaponDefinition projectileInputWeapon;
        Vector3? queuedProjectileDirection;
        void ConfigureCombat(WorldEntry entry)
        {
            int level = Protocol0104.Read16(entry.RawLoadData, 0);
            characterAttackPermission = level >= 0 && level <= 30;
            nextWeaponAttack = nextWeaponChange = 0; BindCombatWorld();
        }
        void BindCombatWorld()
        {
            if (combatWorld == networkWorld) return;
            if (combatWorld) combatWorld.ProjectileImpacted -= ReportProjectileImpact;
            combatWorld = networkWorld;
            if (combatWorld) combatWorld.ProjectileImpacted += ReportProjectileImpact;
        }
        void StopCombat()
        {
            if (combatWorld) combatWorld.ProjectileImpacted -= ReportProjectileImpact;
            combatWorld = null; characterAttackPermission = false; nextWeaponAttack = 0;
            CancelProjectileInput();
        }
        void ReportProjectileImpact(ProjectileImpact impact)
        {
            var position = WorldCoordinates.ToServer(impact.Position);
            var active = Session; var operation = worldLifetime;
            if (operation == null || !active.IsWorldLoaded) return;
            _ = SendGameplayCommand(() => active.HitProjectileAsync(impact.Mode, impact.Slot, position.x, position.y, position.z, impact.Targets, operation.Token));
        }
        bool CombatIncapacitated => gameplayState.Snapshot.HasBuff(10) || gameplayState.Snapshot.HasBuff(11) || gameplayState.Snapshot.HasBuff(12);
        bool CombatUnavailable => !networkWorld || !networkWorld.IsReady || worldTransition || networkWorld.Riding || networkWorld.Ziplining ||
            gameplayState.Snapshot.Hp <= 0 || gameplayState.Snapshot.VehicleMounted || CombatIncapacitated;
        void ChangeEquippedWeapon()
        {
            if (Time.time < nextWeaponChange || gameplayUi.WeaponChangePending) return;
            // GameCondition.IsCallCoolTime(2) consumes one second even if neither slot can move.
            nextWeaponChange = Time.time + 1f;
            var equipment = gameplayState.Snapshot.Equipment;
            if (equipment.Count <= ReserveWeaponSlot) return;
            var hand = equipment[HandSlot];
            var reserve = equipment[ReserveWeaponSlot];
            if (!reserve.IsEmpty && reserve.ItemType == 0 && WorldWeaponCatalog.Load().Find(reserve.ItemId) != null)
                _ = gameplayUi.RequestItemMove(ItemContainer.Equipment, ReserveWeaponSlot, ItemContainer.Equipment, HandSlot);
            else if (reserve.IsEmpty && !hand.IsEmpty && hand.ItemType == 0)
                _ = gameplayUi.RequestItemMove(ItemContainer.Equipment, HandSlot, ItemContainer.Equipment, ReserveWeaponSlot);
        }

        bool TryFireWeapon(NetworkEntityView focus, bool directProjectile = false)
        {
            var snapshot = gameplayState.Snapshot;
            int item = snapshot.Equipment[HandSlot].ItemId;
            var weapon = WorldWeaponCatalog.Load().Find(item);
            bool projectile = weapon?.Projectile == true;
            bool character = focus && focus.Entity.Kind == WorldEntityKind.Player && characterAttackPermission;
            if (!projectile && (!focus || (!focus.Hostile && !character))) return false;
            if (Time.unscaledTime < nextWeaponAttack || CombatUnavailable) return true;
            float range = weapon?.Range ?? 2;
            var start = networkWorld.Player.position + Vector3.up * .8f;
            if (projectile)
            {
                if (directProjectile) FireProjectile(weapon, ProjectileDirection(weapon, start, true));
                return true;
            }
            float extent = Mathf.Max(focus.Definition?.Radius ?? .5f, (focus.Definition?.Height ?? 2) * .5f);
            if (Vector3.Distance(networkWorld.Player.position, focus.transform.position) > range + extent) return true;
            nextWeaponAttack = Time.unscaledTime + (weapon?.Delay ?? 1);
            networkWorld.PlayAttack(weapon?.EquipType ?? 0);
            networkWorld.PlayMeleeImpact(weapon, gameplayState.Snapshot.WeaponBattery, focus);
            if (character)
                _ = SendGameplayCommand(() => Session.AttackCharactersAsync(new[] { (WorldEntityKind.Player, focus.Entity.Id) }, worldLifetime.Token));
            else
            {
                int count = weapon?.Targets ?? 1;
                var origin = networkWorld.Player.position;
                var direction = (focus.transform.position - origin).normalized;
                var targets = new[] { focus }.Concat(networkWorld.Replicas.Actors.Where(actor => actor != focus && actor.Hostile && actor.Entity.Hp > 0 && !actor.Entity.Hidden &&
                        Vector3.Distance(origin, actor.transform.position) <= range + Mathf.Max(actor.Definition.Radius, actor.Definition.Height * .5f) &&
                        Vector3.Angle(direction, actor.transform.position - origin) <= (weapon?.Angle ?? 0) * .5f &&
                        !Physics.Linecast(start, actor.transform.position + Vector3.up * actor.Definition.Height * .5f, ~(1 << 31), QueryTriggerInteraction.Ignore)))
                    .Take(count).Select(actor => actor.Entity.Id).ToArray();
                _ = SendGameplayCommand(() => Session.AttackNpcsAsync(targets, worldLifetime.Token));
            }
            return true;
        }

        void CancelProjectileInput()
        {
            projectileInput.Cancel();
            projectileInputWeapon = null;
            queuedProjectileDirection = null;
            gameplayUi?.Hud.SetProjectileAim(false);
        }

        bool TickProjectileInput()
        {
            var weapon = WorldWeaponCatalog.Load().Find(gameplayState.Snapshot.Equipment[HandSlot].ItemId);
            if (weapon?.Projectile != true)
            {
                CancelProjectileInput();
                return false;
            }
            if (CombatUnavailable || !networkWorld.View || !networkWorld.Player)
            {
                CancelProjectileInput();
                return true;
            }
            if (projectileInputWeapon != weapon)
            {
                CancelProjectileInput();
                projectileInputWeapon = weapon;
            }

            var action = GameplayAction.Fire1;
            var phase = projectileInput.Tick(GameplayInput.Pressed(action), GameplayInput.Held(action),
                GameplayInput.Released(action), Time.unscaledTimeAsDouble);
            var start = networkWorld.Player.position + Vector3.up * .8f;
            if (phase == ProjectileAttackPhase.Aiming)
            {
                if (weapon.Mode == 6)
                {
                    var direction = ProjectileDirection(weapon, start, true);
                    int count = PreviewGrenade(start, direction, weapon);
                    gameplayUi.Hud.SetProjectileAim(false, networkWorld.View, grenadeAimPoints, count);
                }
                else gameplayUi.Hud.SetProjectileAim(true);
            }
            else gameplayUi.Hud.SetProjectileAim(false);

            if (phase == ProjectileAttackPhase.BlindRelease || phase == ProjectileAttackPhase.AimedRelease)
                queuedProjectileDirection = ProjectileDirection(weapon, start, phase == ProjectileAttackPhase.AimedRelease);
            if (queuedProjectileDirection is Vector3 directionToFire && !gameplayCommandSending && Time.unscaledTime >= nextWeaponAttack)
            {
                queuedProjectileDirection = null;
                FireProjectile(weapon, directionToFire);
            }
            return true;
        }

        Vector3 ProjectileDirection(WorldWeaponDefinition weapon, Vector3 start, bool aimed)
        {
            var view = networkWorld.View;
            if (!aimed)
            {
                var forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up);
                return forward.sqrMagnitude > .0001f ? forward.normalized : networkWorld.Player.forward;
            }
            var ray = view.ViewportPointToRay(new Vector3(.5f, .5f));
            float distance = weapon.Range + Vector3.Distance(view.transform.position, start);
            var point = ray.GetPoint(distance);
            if (NetworkProjectileMotor.Trace(ray.origin, point, projectileAimHits, out var hit)) point = hit.point;
            return Vector3.ClampMagnitude((point - start) / weapon.Range, 1);
        }

        int PreviewGrenade(Vector3 start, Vector3 direction, WorldWeaponDefinition weapon)
        {
            grenadeAimPoints[0] = start;
            var target = start + direction * weapon.Range;
            var velocity = NetworkProjectileMotor.InitialVelocity(start, target, weapon.Mode, weapon);
            for (int i = 1; i < grenadeAimPoints.Length; i++)
            {
                float elapsed = weapon.Duration * i / (grenadeAimPoints.Length - 1);
                var next = NetworkProjectileMotor.GrenadePosition(start, velocity, elapsed);
                if (NetworkProjectileMotor.Trace(grenadeAimPoints[i - 1], next, projectileAimHits, out var hit,
                    NetworkProjectileMotor.GrenadeCollisionRadius))
                {
                    grenadeAimPoints[i] = Vector3.MoveTowards(grenadeAimPoints[i - 1], next, hit.distance);
                    return i + 1;
                }
                grenadeAimPoints[i] = next;
            }
            return grenadeAimPoints.Length;
        }

        void FireProjectile(WorldWeaponDefinition weapon, Vector3 direction)
        {
            var start = networkWorld.Player.position + Vector3.up * .8f;
            var from = WorldCoordinates.ToServer(start);
            var to = WorldCoordinates.ToServer(start + direction * weapon.Range);
            nextWeaponAttack = Time.unscaledTime + weapon.Delay;
            networkWorld.PlayAttack(weapon.EquipType);
            _ = SendGameplayCommand(() => Session.FireProjectileAsync(weapon.Mode, from.x, from.y, from.z, to.x, to.y, to.z, worldLifetime.Token));
        }
    }

    internal enum ProjectileAttackPhase { Idle, Aiming, BlindRelease, AimedRelease }

    internal sealed class ProjectileAttackInput
    {
        const double AimHoldSeconds = 1;
        double? pressedAt;

        internal ProjectileAttackPhase Tick(bool pressed, bool held, bool released, double now)
        {
            if (pressed && !pressedAt.HasValue) pressedAt = now;
            if (!pressedAt.HasValue) return ProjectileAttackPhase.Idle;
            bool aimed = now - pressedAt.Value >= AimHoldSeconds;
            if (released && !held)
            {
                Cancel();
                return aimed ? ProjectileAttackPhase.AimedRelease : ProjectileAttackPhase.BlindRelease;
            }
            if (!held) Cancel();
            return held && aimed ? ProjectileAttackPhase.Aiming : ProjectileAttackPhase.Idle;
        }

        internal void Cancel() => pressedAt = null;
    }
}
