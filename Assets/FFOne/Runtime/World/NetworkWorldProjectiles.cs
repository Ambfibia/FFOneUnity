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
        internal void PlayMeleeImpact(WorldWeaponDefinition weapon, int battery, NetworkEntityView target)
        {
            if (weapon?.Mode != 1 || !target) return;
            int row = battery > 0 && weapon.BoostedEffect > 0 ? weapon.BoostedEffect : weapon.Effect;
            var bullet = WorldWeaponCatalog.Load().Bullet(row);
            if (bullet == null || bullet.Impact <= 0) return;
            var link = target.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bullet.ImpactLink);
            Vector3 point = link ? link.position : target.transform.position + Vector3.up * (target.Definition?.Height * .5f ?? .8f);
            try { NativeCombatEffect.Spawn(ownedRoot.transform, bullet.Impact, bullet.ImpactScale, false, View, -1, point); }
            catch (Exception error) { Replicas?.ContentFailure("Melee impact ES" + bullet.Impact + ": " + error.Message); }
        }
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
            if (owner != projectilePlayerId) actor.GetComponent<NetworkEntityView>()?.Animate("attack1");
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
        const float GrenadeGravity = 9.8f;
        internal const float GrenadeCollisionRadius = .2f;
        readonly RaycastHit[] traceHits = new RaycastHit[16];
        WorldWeaponDefinition weapon; WorldBulletDefinition bullet; Camera viewCamera;
        Vector3 target, velocity; float age; bool done; int mode; Action<Vector3> impact; Action<string> failure;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal event Action<RaycastHit, Vector3> Bounced;
#endif
        public void Initialize(Vector3 source, Vector3 destination, int targetMode, WorldWeaponDefinition definition, WorldBulletDefinition projectile, Camera view, Action<Vector3> landed, Action<string> failed)
        {
            weapon = definition; bullet = projectile; viewCamera = view; mode = targetMode; target = destination; impact = landed; failure = failed;
            transform.position = source; velocity = InitialVelocity(source, destination, mode, weapon);
            if (velocity.sqrMagnitude > 0) transform.rotation = Quaternion.LookRotation(velocity);
            NativeCombatEffect.Spawn(transform, bullet.Particle, bullet.Scale, true, viewCamera, -1);
        }
        void Update()
        {
            if (done) return;
            float delta = Mathf.Min(Time.deltaTime, weapon.Duration - age);
            age += delta;
            if (mode == 6)
                while (delta > 0 && !done)
                {
                    float step = Mathf.Min(delta, 1f / 60);
                    AdvanceGrenade(step);
                    delta -= step;
                }
            else AdvanceRocket(delta);
            if (age >= weapon.Duration) Detonate();
        }

        void AdvanceRocket(float delta)
        {
            var next = Vector3.MoveTowards(transform.position, target, weapon.Range * delta);
            if (Trace(transform.position, next, traceHits, out var hit))
            {
                transform.position = hit.point;
                Detonate();
                return;
            }
            transform.position = next;
            if ((next - target).sqrMagnitude < .0001f) Detonate();
        }

        void AdvanceGrenade(float delta)
        {
            var start = transform.position;
            var next = GrenadePosition(start, velocity, delta);
            if (!Trace(start, next, traceHits, out var hit, GrenadeCollisionRadius))
            {
                transform.position = next;
                velocity += Vector3.down * (GrenadeGravity * delta);
                return;
            }
            transform.position = Vector3.MoveTowards(start, next, hit.distance);
            if (hit.collider.GetComponentInParent<NetworkEntityView>())
            {
                Detonate();
                return;
            }
            float timeToHit = delta * hit.distance / Vector3.Distance(start, next);
            velocity += Vector3.down * (GrenadeGravity * timeToHit);
            var horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            var deflected = (horizontal.normalized + Vector3.ProjectOnPlane(hit.normal, Vector3.up)).normalized;
            velocity = deflected * horizontal.magnitude + Vector3.up * (hit.normal.y * Mathf.Abs(velocity.y) * .8f);
            transform.position += hit.normal * .001f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Bounced?.Invoke(hit, velocity);
#endif
        }

        internal static Vector3 GrenadePosition(Vector3 start, Vector3 velocity, float elapsed) =>
            start + elapsed * velocity + Vector3.down * (.5f * GrenadeGravity * elapsed * elapsed);

        internal static Vector3 InitialVelocity(Vector3 start, Vector3 target, int mode, WorldWeaponDefinition weapon)
        {
            var initial = (target - start).normalized * weapon.Range;
            if (mode == 6) initial.y += weapon.GrenadeVerticalSpeed;
            return initial;
        }

        internal static bool Trace(Vector3 start, Vector3 end, RaycastHit[] buffer, out RaycastHit nearest, float radius = 0)
        {
            nearest = default;
            var step = end - start;
            float distance = step.magnitude;
            if (distance < .00001f) return false;
            var ray = new Ray(start, step / distance);
            const int layers = ~(1 << 31);
            int count = radius > 0
                ? Physics.SphereCastNonAlloc(ray, radius, buffer, distance, layers, QueryTriggerInteraction.Collide)
                : Physics.RaycastNonAlloc(ray, buffer, distance, layers, QueryTriggerInteraction.Collide);
            if (count == buffer.Length)
            {
                buffer = radius > 0
                    ? Physics.SphereCastAll(ray, radius, distance, layers, QueryTriggerInteraction.Collide)
                    : Physics.RaycastAll(ray, distance, layers, QueryTriggerInteraction.Collide);
                count = buffer.Length;
            }
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = buffer[i];
                if (hit.distance >= closest || hit.collider.GetComponentInParent<CharacterController>()) continue;
                var entity = hit.collider.GetComponentInParent<NetworkEntityView>();
                if (entity && (entity.Entity.Kind == WorldEntityKind.Player || !entity.Hostile || entity.Entity.Hidden || entity.Entity.Hp <= 0)) continue;
                if (!entity && hit.collider.isTrigger) continue;
                nearest = hit;
                closest = hit.distance;
            }
            return nearest.collider;
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
            var assets = TutorialEffectAssets.Load(id, null, projectile, true, defaultModel:true);
            return Spawn(parent, assets, id, scale, view, lifetime, position);
        }
        internal static GameObject Spawn(Transform parent, NativeEffectDefinition definition, Camera view, Vector3? position, float scale = 1)
        {
            var assets = TutorialEffectAssets.FromDefinition(definition, worldShader: true, defaultModel: true);
            return Spawn(parent, assets, definition.Id, scale, view, -1, position);
        }
        static GameObject Spawn(Transform parent, TutorialEffectAssets assets, int id, float scale, Camera view, float lifetime, Vector3? position)
        {
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
