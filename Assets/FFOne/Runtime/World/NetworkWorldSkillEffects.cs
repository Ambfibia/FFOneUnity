using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    internal static class WorldCombatEffects
    {
        static WorldNanoEffectLibrary library;
        static bool attempted;
        internal static NativeEffectDefinition Find(int id)
        {
            if (!attempted) { attempted = true; library = Resources.Load<WorldNanoEffectLibrary>("WorldCombatEffects"); }
            if (library)
                foreach (var effect in library.Effects) if (effect && effect.Id == id) return effect;
            return null;
        }
    }
    public sealed partial class NetworkWorldController
    {
        sealed class SkillProjectile
        {
            internal int Particle, Impact;
            internal float Duration, Delay, Scale, ImpactScale;
            internal string SourceLink, TargetLink;
        }
        static Dictionary<int, SkillProjectile> skillProjectiles;
        static Dictionary<int, SkillProjectile> ReadSkillProjectiles()
        {
            var rows = NativeData.Object("effects/skill-hits/projectiles")["projectiles"];
            return rows.ToDictionary(r => (int)r["id"], r => new SkillProjectile {
                Particle = (int)r["particle"], Impact = (int)r["impact"], Duration = (float)r["duration_seconds"],
                Delay = (float)r["hide_seconds"], Scale = (float)r["scale"], ImpactScale = (float)r["impact_scale"],
                SourceLink = (string)r["source_link"], TargetLink = (string)r["target_link"] });
        }

        internal void PresentSkill(int skill, WorldEntityKind casterKind, int casterId, IReadOnlyList<NanoSkillEffect> results, int styleOffset = 0)
        {
            if (results.Count == 0 || !ownedRoot || !Player) return;
            foreach (var result in results)
            {
                if (result.Protected || (result.DrainedBoosts <= 0 && result.DrainedPotions <= 0)) continue;
                var victim = SkillTarget(result.Kind, result.Id, out _);
                if (!victim) continue;
                if (result.DrainedBoosts > 0) StartCoroutine(EjectBattery(victim, false));
                if (result.DrainedPotions > 0) StartCoroutine(EjectBattery(victim, true));
            }
            int bullet = WorldSkillCatalog.Load().Find(skill)?.TargetEffect ?? 0;
            if (bullet <= 0) return;
            skillProjectiles ??= ReadSkillProjectiles();
            if (!skillProjectiles.TryGetValue(bullet + styleOffset, out var row)) return;
            var caster = SkillTarget(casterKind, casterId, out _);
            if (casterKind == WorldEntityKind.Player)
            {
                var nano = caster == Player ? LocalNano : caster ? caster.GetComponent<NetworkEntityView>()?.Nano : null;
                if (nano && nano.Model) caster = nano.Model.transform;
            }
            foreach (var result in results)
            {
                if (result.Protected) continue;
                var target = SkillTarget(result.Kind, result.Id, out float height);
                if (!target) continue;
                var anchor = Link(target, row.TargetLink);
                Vector3 end = anchor ? anchor.position : target.position + Vector3.up * height;
                var fire = Link(caster, row.SourceLink);
                Vector3 start = fire ? fire.position : caster ? caster.position : end;
                StartCoroutine(SkillFlight(ownedRoot, row, start, end));
            }
        }

        static readonly Dictionary<string, Sprite> batterySprites = new();

        IEnumerator EjectBattery(Transform victim, bool potion)
        {
            string art = potion ? "potion" : "boost";
            if (!batterySprites.TryGetValue(art, out var sprite) || !sprite)
            {
                var image = ContentAssets.Load<Texture2D>("ui/en/gameplay/rewards/" + art);
                if (!image) yield break;
                sprite = Sprite.Create(image, new Rect(0, 0, image.width, image.height), new Vector2(.5f, .5f), image.height / .56f);
                batterySprites[art] = sprite;
            }
            var drop = new GameObject("Drained " + art);
            drop.transform.SetParent(ownedRoot.transform, false);
            var renderer = drop.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            var start = victim.position + Vector3.up * .95f;
            var right = View ? Vector3.ProjectOnPlane(View.transform.right, Vector3.up).normalized : Vector3.right;
            var side = right * (potion ? -1 : 1);
            float elapsed = 0;
            while (drop && elapsed < .9f)
            {
                float t = elapsed / .9f;
                drop.transform.position = start + side * (t * .85f) + Vector3.up * (2.2f * t - 3f * t * t);
                if (View) drop.transform.rotation = View.transform.rotation;
                renderer.color = new Color(1, 1, 1, 1 - Mathf.InverseLerp(.72f, 1, t));
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (drop) Destroy(drop);
        }

        Transform SkillTarget(WorldEntityKind kind, int id, out float height)
        {
            height = .8f;
            if (kind == WorldEntityKind.Player && id == projectilePlayerId) return Player;
            var view = Replicas?.Actors.FirstOrDefault(a => a.Entity.Kind == kind && a.Entity.Id == id);
            if (view?.Definition != null) height = view.Definition.Height * .5f;
            return view ? view.transform : null;
        }

        static Transform Link(Transform root, string name) => !root || string.IsNullOrEmpty(name) || name == "\"" ? null :
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(node => node.name == name);

        IEnumerator SkillFlight(GameObject owner, SkillProjectile row, Vector3 start, Vector3 end)
        {
            var carried = WorldCombatEffects.Find(row.Particle);
            var impact = WorldCombatEffects.Find(row.Impact);
            if (!carried && !impact) yield break;
            var flight = carried ? NativeCombatEffect.Spawn(owner.transform, carried, View, start, row.Scale) : null;
            float elapsed = 0;
            while (owner && owner == ownedRoot && elapsed < row.Duration)
            {
                yield return null; elapsed += Time.deltaTime;
                if (flight) flight.transform.position = Vector3.Lerp(start, end, Mathf.Clamp01((elapsed - row.Delay) / Mathf.Max(.001f, row.Duration - row.Delay)));
            }
            if (flight) Destroy(flight);
            if (owner && owner == ownedRoot && impact) NativeCombatEffect.Spawn(owner.transform, impact, View, end, row.ImpactScale);
        }

        internal void PresentHealingTick(IReadOnlyList<NanoSkillEffect> results)
        {
            foreach (var effect in results)
            {
                if (!effect.Hp.HasValue) continue;
                var target = SkillTarget(effect.Kind, effect.Id, out float height);
                if (!target) continue;
                float scale = target.GetComponent<NetworkEntityView>()?.Definition?.Radius * 2 ?? 1;
                NativeCombatEffect.Spawn(target, 54, scale, false, View, -1, target.position + Vector3.up * height);
            }
        }
    }
}
