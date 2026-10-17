using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.Tutorial;
using UnityEngine;

namespace FFOne.World
{
    public readonly struct ProjectileImpact
    {
        public readonly int Mode, Slot;
        public readonly Vector3 Position;
        public readonly int[] Targets;
        public ProjectileImpact(int mode, int slot, Vector3 position, int[] targets) { Mode = mode; Slot = slot; Position = position; Targets = targets; }
    }
    public sealed partial class NetworkWorldController
    {
        readonly Dictionary<(int player, int slot), NetworkProjectileMotor> projectiles = new();
        public event Action<ProjectileImpact> ProjectileImpacted;
        public int ProjectileCount => projectiles.Count;
        int projectilePlayerId;
        internal void ClearProjectiles()
        {
            foreach (var projectile in projectiles.Values) if (projectile) Destroy(projectile.gameObject);
            projectiles.Clear();
        }
        internal void ReceiveProjectile(ProjectileUpdate update)
        {
            if (update.Phase == ProjectilePhase.Ready) return;
            int owner = update.Own ? projectilePlayerId : update.PlayerId;
            var key = (owner, update.Slot);
            if (update.Phase == ProjectilePhase.Hit)
            {
                if (projectiles.Remove(key, out var old) && old) old.AcknowledgeImpact();
                return;
            }
            if (projectiles.ContainsKey(key)) return; // own ACK and interest broadcast describe one projectile
            var actor = owner == projectilePlayerId ? Player : Replicas.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == owner)?.transform;
            if (!actor) return;
            var weapon = WorldWeaponCatalog.Load().Find(update.WeaponId);
            if (weapon == null || !weapon.Projectile) { ReportCombatContentFailure("Unknown accepted projectile weapon " + update.WeaponId); return; }
            var bullet = WorldWeaponCatalog.Load().Bullet(update.Charged ? weapon.BoostedEffect : weapon.Effect);
            if (bullet == null) { ReportCombatContentFailure("Missing accepted bullet route for weapon " + weapon.Id); return; }
            var source = update.HasStart ? WorldCoordinates.FromServer(update.X, update.Y, update.Z) : actor.position + Vector3.up * .8f;
            var target = WorldCoordinates.FromServer(update.ToX, update.ToY, update.ToZ);
            var root = new GameObject("Native projectile " + owner + ":" + update.Slot); root.transform.SetParent(ownedRoot.transform, false);
            var motor = root.AddComponent<NetworkProjectileMotor>(); projectiles[key] = motor;
            try { motor.Initialize(source, target, update.Mode, weapon, bullet, View, point => {
                if (!this || !ownedRoot) return;
                if (owner == projectilePlayerId)
                {
                    var targets = Replicas.Actors.Where(a => a.Entity.Kind == WorldEntityKind.Npc && a.Hostile && a.Entity.Hp > 0 && !a.Entity.Hidden &&
                            Vector3.Distance(point, a.transform.position) <= weapon.Radius + Mathf.Max(a.Definition.Radius, a.Definition.Height * .5f))
                        .OrderBy(a => Vector3.Distance(point, a.transform.position)).Take(weapon.Targets).Select(a => a.Entity.Id).ToArray();
                    ProjectileImpacted?.Invoke(new ProjectileImpact(update.Mode, update.Slot, point, targets));
                }
                projectiles.Remove(key);
            }, ReportCombatContentFailure); }
            catch (Exception failure) { projectiles.Remove(key); Destroy(root); ReportCombatContentFailure(failure.Message); }
        }
        void ReportCombatContentFailure(string message) { error = "Native weapon effect: " + message; Debug.LogError(error); }
    }

    public sealed class NetworkProjectileMotor : MonoBehaviour
    {
        WorldWeaponDefinition weapon; WorldBulletDefinition bullet; Camera viewCamera;
        Vector3 target, velocity; float age; bool done; int mode; Action<Vector3> impact; Action<string> failure;
        public void Initialize(Vector3 source, Vector3 destination, int targetMode, WorldWeaponDefinition definition, WorldBulletDefinition projectile, Camera view, Action<Vector3> landed, Action<string> failed)
        {
            weapon = definition; bullet = projectile; viewCamera = view; mode = targetMode; target = destination; impact = landed; failure = failed;
            transform.position = source; velocity = (destination - source).normalized * weapon.Range;
            if (mode == 6) velocity.y = weapon.GrenadeVerticalSpeed;
            if (velocity.sqrMagnitude > 0) transform.rotation = Quaternion.LookRotation(velocity);
            NativeCombatEffect.Spawn(transform, bullet.Particle, bullet.Scale, true, viewCamera, -1);
        }
        void Update()
        {
            if (done) return;
            float delta = Time.deltaTime; age += delta;
            if (age >= weapon.Duration) { Detonate(); return; }
            Vector3 next = mode == 5 ? Vector3.MoveTowards(transform.position, target, weapon.Range * delta) : transform.position + velocity * delta;
            var step = next - transform.position;
            foreach (var hit in Physics.RaycastAll(transform.position, step.normalized, step.magnitude, ~(1 << 31), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var entity = hit.collider.GetComponentInParent<NetworkEntityView>();
                if (entity && entity.Entity.Kind == WorldEntityKind.Player) continue;
                if (hit.collider.GetComponentInParent<CharacterController>()) continue;
                if (entity && (!entity.Hostile || entity.Entity.Hidden)) continue;
                transform.position = hit.point;
                if (mode == 6 && !entity) { velocity = Vector3.Reflect(velocity, hit.normal) * .8f; transform.position += hit.normal * .02f; return; }
                Detonate(); return;
            }
            transform.position = next;
            if (mode == 6) velocity += Vector3.down * (9.8f * delta);
            else if ((next - target).sqrMagnitude < .0001f) Detonate();
        }
        void Detonate()
        {
            if (done) return; done = true;
            var point = transform.position;
            if (bullet.Impact > 0)
                try { NativeCombatEffect.Spawn(transform.parent, bullet.Impact, bullet.ImpactScale, false, viewCamera, Mathf.Max(1, bullet.Lifetime), point); }
                catch (Exception error) { failure?.Invoke(error.Message); }
            try { NativeCombatEffect.PlayImpactSound(transform.parent, bullet.ImpactSound, point); }
            catch (Exception error) { failure?.Invoke(error.Message); }
            impact?.Invoke(point); Destroy(gameObject);
        }
        public void AcknowledgeImpact() { impact = null; Detonate(); }
    }

    public sealed class NativeCombatEffect : MonoBehaviour
    {
        TutorialEffectAssets assets;
        internal static void PlayImpactSound(Transform parent, string cue, Vector3 position)
        {
            string name = (cue ?? "").Trim(' ', '\'', '"'); if (name.Length == 0) return;
            var clip = ContentAssets.Load<AudioClip>("audio/sfx/combat/" + name.Replace('-', '_').ToLowerInvariant());
            if (!clip) throw new InvalidOperationException("Missing original projectile impact sound " + cue);
            var root = new GameObject("Native weapon sound " + cue); root.transform.SetParent(parent, false); root.transform.position = position;
            var source = root.AddComponent<AudioSource>(); source.clip = clip; source.spatialBlend = 1; source.Play(); Destroy(root, clip.length);
        }
        internal static GameObject Spawn(Transform parent, int id, float scale, bool projectile, Camera view, float lifetime, Vector3? position = null)
        {
            var catalog = ContentAssets.Load<NativeModelCatalog>("NativeEditable/Index");
            string prefix = (projectile ? "map/shared/projectiles/effects/models/es" : "map/shared/effects/models/es") + id + "/";
            string model = catalog.Bindings.Where(b => b.OriginalResource.StartsWith(prefix, StringComparison.Ordinal)).Select(b => b.OriginalResource).SingleOrDefault();
            if (model == null && !projectile)
            {
                // These accepted status meshes have semantic identities from the
                // original Nano publication, rather than an ES-number directory.
                var statuses = FFOne.Content.NativeData.Object("effects/skills/catalog");
                model = (string)statuses["effects"]?.SingleOrDefault(e => (int)e["id"] == id)?["mesh"];
            }
            var assets = TutorialEffectAssets.Load(id, model, projectile, true);
            var root = new GameObject("Native combat ES" + id); root.transform.SetParent(parent, false);
            if (position.HasValue) root.transform.position = position.Value;
            root.AddComponent<NativeCombatEffect>().assets = assets;
            // Particle and trail dimensions already use effectScale.
            try { root.AddComponent<TutorialEffectPlayback>().Initialize(assets, scale, view, true); }
            catch { if (Application.isPlaying) Destroy(root); else DestroyImmediate(root); throw; }
            if (lifetime > 0) Destroy(root, lifetime);
            return root;
        }
        void OnDestroy() { assets?.Dispose(); assets = null; }
    }
}
