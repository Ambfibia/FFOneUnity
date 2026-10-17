using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.UI;
using UnityEngine;
namespace FFOne.World
{
    /// <summary>Original Status buff slots and XDT effects, played through the accepted editable FX runtime.</summary>
    public sealed class NativeBuffPresentation : MonoBehaviour
    {
        readonly Dictionary<int, GameObject> loops = new();
        readonly HashSet<int> attemptedMissing = new();
        int desired, applied;
        Camera view;
        float effectScale = 1;
        public IReadOnlyCollection<int> MissingEffectIds => attemptedMissing;
        public IReadOnlyDictionary<int, GameObject> ActiveLoops => loops;
        public void SetCondition(int flags, Camera camera, float scale = 1)
        { desired = flags; view = camera; effectScale = scale; }
        void LateUpdate()
        {
            // Entity interest can precede asynchronous accepted model publication.
            var actor = GetComponent<NetworkEntityView>();
            if (actor && actor.Definition?.HiddenMarker != true && !(actor.Avatar && actor.Avatar.IsReady)
                && !actor.GetComponentInChildren<Renderer>()) return;
            var avatar = GetComponent<NativeAvatarPreview>();
            int immobilized = (1 << 9) | (1 << 10);
            if ((desired & immobilized) != 0 && (!actor || actor.Entity.Hp > 0))
            {
                if (avatar) avatar.PlayLocomotion((desired & (1 << 10)) != 0 ? "sleep" : "stun");
                else if (actor) actor.Animate("stand1");
            }
            else if (actor && (applied & immobilized) != 0)
            { actor.LastClip = null; actor.Animate(actor.Entity.Hp == 0 ? "death" : actor.Entity.Speed > 0 ? "run" : "stand1"); }
            var catalog = GameplayUiCatalog.Load();
            for (int id = 1; id <= 25; id++)
            {
                int bit = 1 << (id - 1); bool active = (desired & bit) != 0;
                if (!active)
                { if (loops.Remove(id, out var old) && old) Destroy(old); continue; }
                int loop = catalog.BuffEffect(id);
                if (loop > 0 && !attemptedMissing.Contains(loop) && (!loops.TryGetValue(id, out var current) || !current))
                    loops[id] = Spawn(id, loop, false, 0);
                int instant = catalog.BuffEffect(id, true);
                if ((applied & bit) == 0 && instant > 0) Spawn(id, instant, true, 0);
            }
            applied = desired;
        }
        GameObject Spawn(int buffId, int effect, bool instant, float lifetime)
        {
            string route = "map/shared/effects/es" + effect + ".closure";
            if (!NativeData.Load(route))
            {
                if (attemptedMissing.Add(effect)) Debug.LogWarning("Accepted buff effect closure is not published: ES" + effect);
                return null;
            }
            Transform anchor = Anchor(buffId, instant);
            try
            {
                var result = NativeCombatEffect.Spawn(anchor, effect, effectScale, false, view, lifetime);
                // Status and its instant coroutine apply a quarter-turn only
                // when they found a source attachment marker; root fallback is identity.
                result.transform.localRotation = anchor == transform ? Quaternion.identity : Quaternion.Euler(90, 0, 0);
                return result;
            }
            catch (Exception error)
            { attemptedMissing.Add(effect); Debug.LogError("Buff ES" + effect + ": " + error.Message); return null; }
        }
        Transform Anchor(int id, bool instant)
        {
            string name = id switch { 10 => "state", 11 when instant => "state", 12 => "state", 13 => "Bip01 Footsteps", 14 or 15 or 16 => "state", 19 => "center", _ => null };
            if (name == null) return transform;
            var nodes = GetComponentsInChildren<Transform>(true);
            var target = nodes.FirstOrDefault(t => t.name == name);
            if (!target && id == 19) target = nodes.FirstOrDefault(t => t.name == "Bip01 spine") ?? nodes.FirstOrDefault(t => t.name == "Bip01 spine1");
            return target ? target : transform;
        }
        void OnDestroy() { foreach (var value in loops.Values) if (value) Destroy(value); loops.Clear(); }
    }
}
