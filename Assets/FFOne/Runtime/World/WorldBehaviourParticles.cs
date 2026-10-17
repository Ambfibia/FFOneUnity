using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

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
        struct Particle { internal Vector3 Position, Velocity; internal float Age; internal bool Alive; }
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
            internal WorldBehaviourEffectAssets.Plan Plan; internal Transform Transform; internal Mesh Mesh; internal MeshRenderer Renderer;
            internal readonly List<Particle> Particles = new List<Particle>();
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
        readonly Dictionary<string, WorldBehaviourEffectAssets.Plan> plans = new Dictionary<string, WorldBehaviourEffectAssets.Plan>();
        readonly Dictionary<string, JToken> closures;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();
        Camera view;
        internal WorldBehaviourParticles(WorldBehaviour parent, JArray definitions, Camera camera)
        {
            owner = parent; view = camera;
            closures = (definitions ?? new JArray()).ToDictionary(c => (string)c["id"], c => c);
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
                    var mesh = new Mesh { name = "Streamed " + plan.Name }; mesh.MarkDynamic();
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = plan.Material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.enabled = plan.RendererEnabled;
                    emitters.Add(new Emitter { Plan = plan, Transform = child.transform, Mesh = mesh, Renderer = renderer, Keys = keys,
                        MaximumTimer = (float?)record["maxTimer"] ?? 0, DisableUpdate = (bool?)record["disableUpdate"] ?? false,
                        Scale = (bool?)record["conformToScale"] == true ? Mathf.Abs(root.lossyScale.x) : 1,
                        Random = new RandomStream(Seed((string)record["node"] + identity + i)) });
                    owner.ParticleEmitterBindings++;
                }
                catch (Exception error) { owner.Problem(error.Message, true); }
            }
            // Some named effects consist entirely of their separately published NIF meshes.
        }
        internal void BindElement(JObject document, Transform root, string node)
        {
            var objects = (JArray)document["objects"];
            var controller = objects.Select(o => o["value"]).First(v => v["particles"] != null && v["maxTimer"] != null);
            var refs = new JArray();
            foreach (var particle in controller["particles"])
            {
                long actor = (long)particle["particlePrefab"]["pathId"];
                var gameObject = objects.Single(o => (string)o["objectType"] == "GameObject" && (long)o["pathId"] == actor);
                string id = "world-ep:" + (string)gameObject["asset"] + "#" + actor;
                if (!closures.ContainsKey(id)) closures.Add(id, new JObject { ["id"] = id, ["root"] = new JObject { ["pathId"] = actor }, ["objects"] = objects.DeepClone() });
                refs.Add(id);
            }
            var record = new JObject { ["node"] = node, ["resolvedParticlePrefabs"] = refs, ["particles"] = controller["particles"].DeepClone(),
                ["maxTimer"] = controller["maxTimer"].DeepClone(), ["disableUpdate"] = (int?)controller["disableUpdate"] != 0,
                ["conformToScale"] = true };
            Bind(record, root);
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
                for (int i = item.Emitter.Particles.Count-1; i >= 0 && removals > 0; i--)
                    if (!item.Emitter.Particles[i].Alive)
                    {
                        item.Emitter.Particles.RemoveAt(i); item.Owner.RetainedExpiredParticles--; budgetParticles--; removals--;
                    }
            ordered.Sort((a,b) => { int order = a.Priority.CompareTo(b.Priority); return order != 0 ? order : a.Order.CompareTo(b.Order); });
            foreach (var item in ordered) item.Owner.Admit(item,delta);
            foreach (var item in ordered) item.Owner.Render(item.Emitter);
        }
        void Simulate(Emitter emitter, float delta)
        {
            if (delta <= 0) return;
            for (int i = emitter.Particles.Count-1; i >= 0; i--)
            {
                var particle = emitter.Particles[i]; particle.Age += delta;
                if (!particle.Alive) continue;
                if (particle.Age > emitter.Plan.Lifetime)
                { particle.Alive = false; emitter.Particles[i] = particle; ParticleCount--; RetainedExpiredParticles++; continue; }
                particle.Velocity += emitter.Plan.Force*delta; particle.Position += particle.Velocity*delta; emitter.Particles[i] = particle;
            }
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
                            emitter.Particles.Add(new Particle { Position = item.Position + position, Velocity = item.Rotation * velocity, Alive = true });
                            ParticleCount++; budgetParticles++;
                        }
                    }
                    else BudgetDeferrals++;
                }
                // Out-of-range admission keeps the due timer, just as the native streamed emitter does.
            }
            else if (emit) emitter.Timer += delta * plan.Rate;
        }
        void Render(Emitter emitter)
        {
            var plan = emitter.Plan;
            vertices.Clear(); uvs.Clear(); colors.Clear(); indices.Clear();
            for (int i = emitter.Particles.Count - 1; i >= 0; i--)
            {
                var particle = emitter.Particles[i];
                if (!particle.Alive || !plan.RendererEnabled) continue;
                float fraction = Mathf.Clamp01(particle.Age / plan.Lifetime);
                float width = plan.Width.length > 0 ? plan.Width.Evaluate(fraction) : plan.Height.length > 0 ? plan.Height.Evaluate(fraction) : plan.InitialSize;
                float height = plan.Height.length > 0 ? plan.Height.Evaluate(fraction) : width;
                Vector3 right = view ? view.transform.right : Vector3.right, up = view ? view.transform.up : Vector3.up;
                // Native procedural quads are reflected by H along with their world placement.
                // The camera-facing mode already supplies that basis; fixed/up modes need reflected X explicitly.
                if (plan.RenderMode == 4) { right = Vector3.left; up = Vector3.back; }
                else if (plan.RenderMode == 5 && view)
                {
                    var toCamera = view.transform.position-particle.Position; toCamera.y = 0;
                    right = toCamera.sqrMagnitude > .000001f ? -(Quaternion.LookRotation(toCamera)*Vector3.right) : Vector3.left;
                    up = Vector3.up;
                }
                float roll = (plan.Rotation.length > 0 ? plan.Rotation.Evaluate(fraction) : 0)*Mathf.Deg2Rad;
                float sine = Mathf.Sin(roll), cosine = Mathf.Cos(roll);
                Vector3 rolledRight = right*cosine+up*sine, rolledUp = up*cosine-right*sine;
                Color tint = Color.white;
                if (plan.AnimateColor)
                {
                    // Native shared particle materials retain 16 colour samples; atlas/size/rotation stay continuous.
                    float sample = Mathf.Floor(fraction*15+.5f)/15*4; int key = Math.Min(3,(int)sample);
                    tint = Color.LerpUnclamped(plan.Colors[key],plan.Colors[key+1],sample-key);
                }
                int frame = Mathf.FloorToInt(fraction * plan.Cycles * plan.XTiles * plan.YTiles) % (plan.XTiles*plan.YTiles);
                var uvScale = new Vector2(1f / plan.XTiles, 1f / plan.YTiles);
                // Native rows are flipped and particle_quad has bottom V=1. In Unity's raw
                // texture the equivalent atlas cell uses the reflected row, retaining cell-local UV direction.
                var uvOffset = new Vector2(frame % plan.XTiles,plan.YTiles-1-frame/plan.XTiles)*uvScale;
                AddQuad(emitter.Transform,particle.Position,rolledRight,rolledUp,new Vector2(width,height)*emitter.Scale,uvOffset,uvScale,tint);
            }
            // Avoid uploading an unchanged empty mesh for thousands of distant emitter plans.
            if (vertices.Count == 0 && emitter.Mesh.vertexCount == 0) return;
            emitter.Mesh.Clear(); emitter.Mesh.SetVertices(vertices); emitter.Mesh.SetUVs(0, uvs); emitter.Mesh.SetColors(colors);
            emitter.Mesh.SetTriangles(indices, 0); emitter.Mesh.RecalculateBounds();
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
        static void InitialState(WorldBehaviourEffectAssets.Plan plan, RandomStream random, out Vector3 position, out Vector3 velocity)
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
        void AddQuad(Transform target, Vector3 point, Vector3 rightAxis, Vector3 upAxis, Vector2 size, Vector2 uvOffset, Vector2 uvScale, Color tint)
        {
            int first = vertices.Count; var right = rightAxis*size.x*.5f; var up = upAxis*size.y*.5f;
            vertices.Add(target.InverseTransformPoint(point-right-up)); vertices.Add(target.InverseTransformPoint(point+right-up));
            vertices.Add(target.InverseTransformPoint(point+right+up)); vertices.Add(target.InverseTransformPoint(point-right+up));
            uvs.Add(uvOffset); uvs.Add(uvOffset+new Vector2(uvScale.x,0)); uvs.Add(uvOffset+uvScale); uvs.Add(uvOffset+new Vector2(0,uvScale.y));
            for (int i = 0; i < 4; i++) colors.Add(tint);
            indices.Add(first); indices.Add(first+1); indices.Add(first+2); indices.Add(first); indices.Add(first+2); indices.Add(first+3);
        }
        public void Dispose()
        {
            owners.Remove(this); if (owners.Count == 0) updatedFrame = -1;
            budgetParticles = Math.Max(0,budgetParticles-ParticleCount-RetainedExpiredParticles); ParticleCount = 0; RetainedExpiredParticles = 0;
            foreach (var emitter in emitters) WorldBehaviour.Release(emitter.Mesh);
            foreach (var plan in plans.Values) WorldBehaviourEffectAssets.Release(plan);
            plans.Clear(); emitters.Clear(); closures.Clear();
        }
    }
}
