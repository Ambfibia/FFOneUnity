using System;
using System.Collections.Generic;
using System.IO;
using FFOne.Content;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.World
{
    internal sealed class WorldBehaviourParticles : IDisposable
    {
        const int MaximumStreamedParticles = 2048; // Same admitted budget as the native world runtime.
        const int ExpiredRemovalsPerFrame = 32;
        const float FarMargin = 32;
        static int budgetParticles;
        static int updatedFrame = -1;
        static readonly List<WorldBehaviourParticles> owners = new List<WorldBehaviourParticles>();
        static readonly List<Emission> ordered = new List<Emission>();
        internal int ParticleCount { get; private set; }
        internal int BudgetDeferrals { get; private set; }
        internal int RetainedExpiredParticles { get; private set; }
        sealed class RandomStream
        {
            uint state;
            internal RandomStream(uint seed) { state = seed == 0 ? 0x7e57372f : seed; }
            internal float Unit()
            {
                state ^= state<<13; state ^= state>>17; state ^= state<<5;
                return (float)((double)state/uint.MaxValue);
            }
        }
        struct Key { internal float Time; internal Vector3 Translation; internal bool Emit; }
        sealed class Emitter
        {
            internal NativeParticleSettings Plan; internal Transform Transform; internal NativeParticleEmitter Native;
            internal int Expired;
            internal Key[] Keys; internal float Timer = 2, Age, MaximumTimer, Scale; internal bool DisableUpdate;
            internal RandomStream Random;
        }
        struct Emission
        {
            internal WorldBehaviourParticles Owner; internal Emitter Emitter;
            internal Vector3 Position; internal Quaternion Rotation; internal float Priority; internal int Order;
        }
        readonly WorldBehaviour owner;
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly Dictionary<string, NativeParticleSettings> plans = new Dictionary<string, NativeParticleSettings>();
        readonly Dictionary<string, NativeParticleSettings> closures;
        Camera view;
        internal WorldBehaviourParticles(WorldBehaviour parent, NativeWorldParticleDefinition[] definitions, Camera camera)
        {
            owner = parent; view = camera;
            closures = (definitions ?? Array.Empty<NativeWorldParticleDefinition>()).ToDictionary(c => c.Settings.Id, c => c.Settings);
            owners.Add(this);
        }
        internal void Complete() => closures.Clear();
        internal void Bind(JToken record, Transform root)
        {
            var prefabs = record["resolvedParticlePrefabs"] as JArray ?? new JArray();
            var particles = record["particles"] as JArray ?? new JArray();
            for (int i = 0; i < prefabs.Count; i++)
            {
                string identity = (string)prefabs[i]; if (string.IsNullOrEmpty(identity)) continue; // Clean Start removes explicit null prefabs.
                try
                {
                    if (!plans.TryGetValue(identity, out var plan))
                    {
                        if (!closures.TryGetValue(identity, out var closure)) throw new InvalidDataException("Missing particle closure " + identity);
                        plan = WorldBehaviourEffectAssets.Acquire(closure); plans.Add(identity, plan);
                    }
                    if (i >= particles.Count || !(particles[i]["scriptKeys"] is JArray scriptKeys) || scriptKeys.Count == 0)
                        throw new InvalidDataException("Missing particle ScriptKeys for " + identity);
                    var child = new GameObject("Particle " + plan.Name); child.transform.SetParent(root, false);
                    var keys = scriptKeys.Select(k => new Key { Time = (float)k["time"], Translation = WorldBehaviour.Vector(k["translate"]), Emit = (int)k["genType"] != 0 }).ToArray();
                    child.transform.localPosition = keys[0].Translation;
                    float scale = (bool?)record["conformToScale"] == true ? Mathf.Abs(root.lossyScale.x) : 1;
                    var native = new NativeParticleEmitter(child, plan.Material, plan.Lifetime, plan.InitialSize,
                        plan.Width, plan.Height, plan.Rotation, plan.Force, scale, plan.RenderMode,
                        plan.XTiles, plan.YTiles, plan.Cycles, true, plan.AnimateColor, plan.Colors, MaximumStreamedParticles);
                    child.GetComponent<ParticleSystemRenderer>().enabled = plan.RendererEnabled;
                    emitters.Add(new Emitter { Plan = plan, Transform = child.transform, Native = native, Keys = keys,
                        MaximumTimer = (float?)record["maxTimer"] ?? 0, DisableUpdate = (bool?)record["disableUpdate"] ?? false,
                        Scale = scale, Random = new RandomStream(Seed((string)record["node"] + identity + i)) });
                    owner.ParticleEmitterBindings++;
                }
                catch (Exception error) { owner.Problem(error.Message, true); }
            }
            // Some named effects consist entirely of their separately published NIF meshes.
        }
        internal void BindElement(NativeEffectDefinition definition, Transform root, string node)
        {
            var refs=new JArray();var records=new JArray();
            foreach(var published in definition.ElementParticles)
            {
                var plan=published.Settings;closures.TryAdd(plan.Id,plan);refs.Add(plan.Id);
                records.Add(new JObject { ["scriptKeys"]=new JArray(plan.Keys.Select(k=>new JObject { ["time"]=k.Time,["translate"]=new JArray(k.Translation.x,k.Translation.y,k.Translation.z),["genType"]=k.Emit?1:0 })) });
            }
            Bind(new JObject { ["node"]=node,["resolvedParticlePrefabs"]=refs,["particles"]=records,
                ["maxTimer"]=definition.MaximumTimer,["disableUpdate"]=definition.DisableUpdate,["conformToScale"]=true },root);
        }
        static uint Seed(string value) { unchecked { uint hash = 2166136261; foreach (char c in value) hash = (hash ^ c) * 16777619; return hash; } }
        internal void Update(float delta, Camera camera)
        {
            if (camera) view = camera;
            if (updatedFrame == Time.frameCount) return;
            updatedFrame = Time.frameCount; delta = Mathf.Max(0,delta); ordered.Clear();
            // Bevy simulates/cleans existing cards in Update, then admits new cards in PostUpdate.
            // Process all resident roots together so the common budget favours nearby emitters.
            foreach (var current in owners)
            {
                if (!current.owner || !current.owner.Ready || !current.owner.isActiveAndEnabled) continue;
                if (camera) current.view = camera;
                foreach (var emitter in current.emitters)
                {
                    if (!emitter.Transform) continue;
                    current.Simulate(emitter,delta);
                    Vector3 position = emitter.Transform.position;
                    ordered.Add(new Emission { Owner = current, Emitter = emitter, Position = position, Rotation = emitter.Transform.rotation,
                        Priority = emitter.Plan.Persistent ? -.5f : current.view ? (position-current.view.transform.position).sqrMagnitude : 0,
                        Order = ordered.Count });
                }
            }
            // Expired native entities release budget at a bounded rate even though they stop drawing immediately.
            int removals = ExpiredRemovalsPerFrame;
            foreach (var item in ordered)
            {
                int count = Math.Min(item.Emitter.Expired, removals);
                item.Emitter.Expired -= count; item.Owner.RetainedExpiredParticles -= count;
                budgetParticles -= count; removals -= count;
            }
            ordered.Sort((a,b) => { int order = a.Priority.CompareTo(b.Priority); return order != 0 ? order : a.Order.CompareTo(b.Order); });
            foreach (var item in ordered) item.Owner.Admit(item,delta);
            foreach (var item in ordered) item.Emitter.Native.Face(item.Owner.view);
        }
        void Simulate(Emitter emitter, float delta)
        {
            int expired = emitter.Native.Step(delta);
            emitter.Expired += expired; ParticleCount -= expired; RetainedExpiredParticles += expired;
        }
        void Admit(Emission item,float delta)
        {
            if (delta <= 0) return; // Frozen captures render existing cards without advancing controllers or admitting births.
            var emitter = item.Emitter; var plan = emitter.Plan; emitter.Age += delta;
            bool emit = plan.InitialEmit;
            if (!emitter.DisableUpdate)
            {
                float duration = Mathf.Abs(emitter.MaximumTimer);
                float scriptTime = duration > .000001f ? (emitter.MaximumTimer < 0 ? emitter.Age % duration : emitter.Age) / duration : emitter.Age;
                emit = ScriptState(emitter.Keys, scriptTime, out var translation);
                emitter.Transform.localPosition = translation;
                if (emitter.MaximumTimer > 0 && emitter.Age > emitter.MaximumTimer) emit = false;
            }
            float radius = view ? view.farClipPlane + FarMargin : 0;
            bool near = view && (item.Position-view.transform.position).sqrMagnitude <= radius*radius;
            if (emit && emitter.Timer + delta * plan.Rate >= 1)
            {
                if (near)
                {
                    int wanted = Mathf.CeilToInt(plan.Count), count = plan.Persistent ? wanted : Math.Min(wanted, Math.Max(0, MaximumStreamedParticles-budgetParticles));
                    if (count > 0)
                    {
                        emitter.Timer = 0;
                        for (int i = 0; i < count; i++)
                        {
                            InitialState(plan, emitter.Random, out var position, out var velocity);
                            // Source Emit adds an unscaled, unrotated world offset; only velocity is rotated.
                            // Script-local translation is applied after native GlobalTransform propagation; this
                            // frame's births use the world placement captured before advancing the controller.
                            emitter.Native.Emit(item.Position + position, item.Rotation * velocity);
                            ParticleCount++; budgetParticles++;
                        }
                    }
                    else BudgetDeferrals++;
                }
                // Out-of-range admission keeps the due timer, just as the native streamed emitter does.
            }
            else if (emit) emitter.Timer += delta * plan.Rate;
        }
        static bool ScriptState(Key[] keys, float time, out Vector3 translation)
        {
            translation = keys[0].Translation;
            if (time <= keys[0].Time) return keys[0].Emit;
            for (int i = 1; i < keys.Length; i++)
            {
                if (time > keys[i].Time) continue;
                float span = keys[i].Time-keys[i-1].Time;
                translation = Vector3.Lerp(keys[i-1].Translation, keys[i].Translation, span <= .000001f ? 1 : (time-keys[i-1].Time)/span);
                return keys[i-1].Emit;
            }
            translation = keys[keys.Length-1].Translation; return keys[keys.Length-1].Emit;
        }
        static float Unit(RandomStream random) => random.Unit();
        static Vector3 Sphere(RandomStream random)
        {
            float z = Unit(random)*2-1, angle = Unit(random)*Mathf.PI*2, radius = Mathf.Sqrt(Mathf.Max(0, 1-z*z));
            return new Vector3(radius*Mathf.Cos(angle), radius*Mathf.Sin(angle), z);
        }
        static Vector2 Circle(RandomStream random)
        {
            while (true)
            {
                var candidate = new Vector2(Unit(random)*2-1,Unit(random)*2-1); float length = candidate.sqrMagnitude;
                if (length <= 1) return length > 0 ? candidate/Mathf.Sqrt(length) : Vector2.zero;
            }
        }
        static void InitialState(NativeParticleSettings plan, RandomStream random, out Vector3 position, out Vector3 velocity)
        {
            int generation = plan.Generation;
            if (generation == 2) position = new Vector3((Unit(random)-.5f)*plan.Plane.x, (Unit(random)-.5f)*plan.Plane.y, (Unit(random)-.5f)*plan.Plane.z);
            else if (generation <= 1) position = Sphere(random) * (generation == 1 ? plan.RandomPosition : Unit(random)*plan.RandomPosition);
            else
            {
                int axis = (generation-3)%3; float radius; Vector2 direction;
                // Legacy X/Z sample radius first; Y samples direction first.
                if (axis == 1) { direction = Circle(random); radius = Mathf.Lerp(plan.Plane.x, plan.Plane.z, Unit(random)); }
                else { radius = Mathf.Lerp(plan.Plane.x, plan.Plane.z, Unit(random)); direction = Circle(random); }
                position = (axis == 0 ? new Vector3(0,direction.x,direction.y) : axis == 1 ? new Vector3(direction.x,0,direction.y) : new Vector3(direction.x,direction.y,0))*radius;
            }
            velocity = generation == 1 || generation >= 6 && generation <= 8 ? -position.normalized : generation >= 9 ? position.normalized : Vector3.zero;
            float angle = plan.RandomAngle * 180;
            var rotated = Quaternion.Euler((Unit(random)*2-1)*angle, (Unit(random)*2-1)*angle, (Unit(random)*2-1)*angle) * plan.InitialVelocity;
            velocity += new Vector3(Mathf.Abs(plan.InitialVelocity.x) < .01f ? rotated.x*plan.InitialVelocity.x : rotated.x,
                Mathf.Abs(plan.InitialVelocity.y) < .01f ? rotated.y*plan.InitialVelocity.y : rotated.y, Mathf.Abs(plan.InitialVelocity.z) < .01f ? rotated.z*plan.InitialVelocity.z : rotated.z);
            velocity *= Unit(random)*2*(1-plan.RandomVelocity);
        }
        public void Dispose()
        {
            owners.Remove(this); if (owners.Count == 0) updatedFrame = -1;
            budgetParticles = Math.Max(0,budgetParticles-ParticleCount-RetainedExpiredParticles); ParticleCount = 0; RetainedExpiredParticles = 0;
            foreach (var emitter in emitters) emitter.Native.Dispose();
            foreach (var plan in plans.Values) WorldBehaviourEffectAssets.Release(plan);
            plans.Clear(); emitters.Clear(); closures.Clear();
        }
    }
}
