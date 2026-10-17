using System;
using System.Linq;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        float nextWeaponAttack;
        bool characterAttackPermission;
        NetworkWorldController combatWorld;
        void ConfigureCombat(WorldEntry entry)
        {
            int level = Protocol0104.Read16(entry.RawLoadData, 0);
            characterAttackPermission = level >= 0 && level <= 30;
            nextWeaponAttack = 0; BindCombatWorld();
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
        }
        void ReportProjectileImpact(ProjectileImpact impact)
        {
            var position = WorldCoordinates.ToServer(impact.Position);
            var active = Session; var operation = worldLifetime;
            if (operation == null || !active.IsWorldLoaded) return;
            _ = SendGameplayCommand(() => active.HitProjectileAsync(impact.Mode, impact.Slot, position.x, position.y, position.z, impact.Targets, operation.Token));
        }
        bool CombatIncapacitated => gameplayState.Snapshot.HasBuff(10) || gameplayState.Snapshot.HasBuff(11) || gameplayState.Snapshot.HasBuff(12);
        bool TryFireWeapon(NetworkEntityView focus)
        {
            var snapshot = gameplayState.Snapshot;
            int item = snapshot.Equipment[0].ItemId;
            var weapon = WorldWeaponCatalog.Load().Find(item);
            bool projectile = weapon?.Projectile == true;
            bool character = focus && focus.Entity.Kind == WorldEntityKind.Player && characterAttackPermission;
            if (!projectile && (!focus || (!focus.Hostile && !character))) return false;
            if (Time.unscaledTime < nextWeaponAttack || CombatIncapacitated) return true;
            float range = weapon?.Range ?? 2;
            var start = networkWorld.Player.position + Vector3.up * .8f;
            if (projectile)
            {
                var ray = networkWorld.View.ViewportPointToRay(new Vector3(.5f, .5f));
                var point = ray.GetPoint(range + Vector3.Distance(networkWorld.View.transform.position, start));
                foreach (var hit in Physics.RaycastAll(ray, range + Vector3.Distance(networkWorld.View.transform.position, start), ~(1 << 31), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
                {
                    if (hit.collider.transform.IsChildOf(networkWorld.Player)) continue;
                    point = hit.point; break;
                }
                point = start + Vector3.ClampMagnitude(point - start, range);
                var from = WorldCoordinates.ToServer(start); var to = WorldCoordinates.ToServer(point);
                nextWeaponAttack = Time.unscaledTime + weapon.Delay;
                networkWorld.PlayAttack(weapon.Mode);
                _ = SendGameplayCommand(() => Session.FireProjectileAsync(weapon.Mode, from.x, from.y, from.z, to.x, to.y, to.z, worldLifetime.Token));
                return true;
            }
            float extent = Mathf.Max(focus.Definition?.Radius ?? .5f, (focus.Definition?.Height ?? 2) * .5f);
            if (Vector3.Distance(networkWorld.Player.position, focus.transform.position) > range + extent) return true;
            nextWeaponAttack = Time.unscaledTime + (weapon?.Delay ?? 1);
            networkWorld.PlayAttack(weapon?.Mode ?? 0);
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
    }
}
