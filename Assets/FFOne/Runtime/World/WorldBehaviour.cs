using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.World
{
    // Consumes the accepted editable world documents; no legacy bundle is opened.
    public sealed class WorldBehaviour : MonoBehaviour
    {
        public int AnimationRecords, AnimationPlayers, TransformChannels, MaterialCurves;
        public int BillboardRecords, BillboardBindings, PlatformRecords, PlatformBindings;
        public int EffectRecords, ParticleEmitterBindings, NifAnimationBindings;
        public int ElementEffectRecords, ElementEffectBindings;
        public int MissingBindings, UnsupportedBehaviours, RetainedUnusedMaterialCurves;
        public int UnrenderedMaterialCurves;
        public int SourceCoverageGaps;
        public int ActiveParticles => particles == null ? 0 : particles.ParticleCount;
        public int ParticleBudgetDeferrals => particles == null ? 0 : particles.BudgetDeferrals;
        public int RetainedExpiredParticles => particles == null ? 0 : particles.RetainedExpiredParticles;
        public bool Ready { get; private set; }
        public static int SharedParticlePlans => WorldBehaviourEffectAssets.PlanCount;
        public static int SharedParticleMaterials => WorldBehaviourEffectAssets.MaterialCount;
        public static int SharedParticleTextures => WorldBehaviourEffectAssets.TextureCount;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> SourceCoverageNotes = new List<string>();
        readonly List<WorldBehaviourAnimation.Player> animations = new List<WorldBehaviourAnimation.Player>();
        readonly Dictionary<string, WorldBehaviourAnimation.Clip> clips = new Dictionary<string, WorldBehaviourAnimation.Clip>();
        readonly Dictionary<Renderer, Material[]> animatedMaterials = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<string, Transform> pivots = new Dictionary<string, Transform>();
        readonly List<Billboard> billboards = new List<Billboard>();
        readonly List<Platform> platforms = new List<Platform>();
        readonly List<Spin> spins = new List<Spin>();
        readonly List<Material> ownedMaterials = new List<Material>();
        WorldBehaviourParticles particles;
        WorldBehaviourEpEffects elementEffects;
        Dictionary<string, List<Transform>> legacy, source;
        Camera view;
        bool acceptedOlderTile;
        ISet<string> sourceModelOmissions;
        readonly HashSet<string> reportedSourceOmissions = new();

        sealed class Billboard { internal Transform Pivot; internal int Mode; internal bool Enabled; internal Renderer[] Renderers; }
        sealed class Spin { internal Transform Target; internal Quaternion Initial; }
        sealed class Platform
        {
            internal Transform Pivot; internal Vector3 Position, Scale, Delta; internal Quaternion Rotation;
            internal float Velocity, Spin; internal int Mode; internal Vector3[] Path;
        }

        public static WorldBehaviour Attach(GameObject root, string behaviourJson,
            Dictionary<string, List<Transform>> legacyModels, Dictionary<string, List<Transform>> sourceNodes, Camera camera,
            ISet<string> acceptedSourceOmissions = null)
        {
            var result = root.AddComponent<WorldBehaviour>();
            result.sourceModelOmissions = acceptedSourceOmissions;
            var iterator = result.Initialize(behaviourJson, legacyModels, sourceNodes, camera);
            while (iterator.MoveNext()) { }
            return result;
        }

        // A chunk loader can spread admitted records over frames and include this work in its load timing.
        public static IEnumerator AttachAsync(GameObject root, string behaviourJson,
            Dictionary<string, List<Transform>> legacyModels, Dictionary<string, List<Transform>> sourceNodes,
            Camera camera, Action<WorldBehaviour> completed, ISet<string> acceptedSourceOmissions = null)
        {
            var result = root.AddComponent<WorldBehaviour>();
            result.sourceModelOmissions = acceptedSourceOmissions;
            yield return result.Initialize(behaviourJson, legacyModels, sourceNodes, camera);
            completed?.Invoke(result);
        }

        IEnumerator Initialize(string json, Dictionary<string, List<Transform>> legacyModels,
            Dictionary<string, List<Transform>> sourceNodes, Camera camera)
        {
            legacy = legacyModels; source = sourceNodes; view = camera;
            JObject document;
            try { document = JObject.Parse(json); }
            catch (Exception error) { Problem("Invalid world behaviour: " + error.Message); yield break; }
            acceptedOlderTile = (string)document["id"] == "map_01_01";
            if (acceptedOlderTile)
                SourceCoverageNotes.Add("map_01_01 retains 105 legacy animation records without targets/models; the accepted Rust document has the same source binding gap. These records are counted as missing, while its published geometry and particle closures are admitted.");
            var timer = Stopwatch.StartNew();
            foreach (var record in document["billboards"] ?? new JArray())
            {
                BindBillboard(record); if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            foreach (var record in document["visibilitySwitches"] ?? new JArray())
            {
                if (pivots.TryGetValue((string)record["switches"] ?? "", out var pivot))
                {
                    var billboard = billboards.Find(b => b.Pivot == pivot);
                    if (billboard != null && (bool?)record["enabled"] == true)
                    { billboard.Enabled = true; billboard.Renderers = Renderers(Resolve(record)); }
                }
                else Problem("Missing VisibleSwitch target " + (string)record["switches"]);
            }
            foreach (var definition in document["animationClips"] ?? new JArray())
            {
                try { var clip = WorldBehaviourAnimation.Acquire(definition); clips.Add(clip.Id, clip); }
                catch (Exception error) { Problem("Invalid AnimationClip: " + error.Message, true); }
                if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            foreach (var record in document["animations"] ?? new JArray())
            {
                BindAnimation(record); if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            particles = new WorldBehaviourParticles(this, document["effectPrefabClosures"] as JArray, view);
            elementEffects = new WorldBehaviourEpEffects(this, particles, view);
            foreach (var record in document["effectEmitters"] ?? new JArray())
            {
                BindEffect(record); if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            particles.Complete();
            var waypoints = (document["waypoints"] ?? new JArray()).ToDictionary(w => (string)w["node"], w => w);
            foreach (var record in document["triggers"] ?? new JArray())
            {
                if ((string)record["kind"] == "platform") BindPlatform(record, waypoints);
                if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            foreach (var blocker in document["blockers"] ?? new JArray()) Problem("Published behaviour blocker: " + blocker.ToString(), true);
            Ready = true;
        }

        internal void Problem(string message, bool unsupported = false)
        {
            if (unsupported) UnsupportedBehaviours++; else MissingBindings++;
            if (Errors.Count < 128) Errors.Add(message);
        }
        internal List<Transform> Resolve(JToken record)
        {
            var result = new List<Transform>();
            // A source node can own several independently published visual/collider models.
            bool joinedSource = source != null && source.TryGetValue((string)record["node"] ?? "", out var joined);
            if (joinedSource) result.AddRange(source[(string)record["node"]]);
            foreach (var identity in record["models"] ?? new JArray())
            {
                if (legacy != null && legacy.TryGetValue((string)identity, out var models))
                { foreach (var model in models) if (model && !result.Contains(model)) result.Add(model); }
                else if (!joinedSource)
                {
                    string id = (string)identity;
                    if (sourceModelOmissions != null && sourceModelOmissions.Contains(id))
                    {
                        if (reportedSourceOmissions.Add(id))
                        {
                            SourceCoverageGaps++; MissingBindings++;
                            SourceCoverageNotes.Add("Accepted source has no published geometry for " + id + "; animation pivots retained without inventing geometry.");
                        }
                    }
                    else Problem("Missing model " + id + " for " + (string)record["node"]);
                }
            }
            return result;
        }
        internal static Renderer[] Renderers(IEnumerable<Transform> models) => models.Where(m => m)
            .SelectMany(m => m.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
        internal static bool AnyVisible(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] && renderers[i].isVisible) return true;
            return false;
        }
        internal Material[] Materials(Renderer renderer)
        {
            if (animatedMaterials.TryGetValue(renderer, out var result)) return result;
            result = renderer.sharedMaterials;
            for (int i = 0; i < result.Length; i++)
            { if (!result[i]) continue; result[i] = new Material(result[i]); ownedMaterials.Add(result[i]); }
            renderer.sharedMaterials = result; animatedMaterials.Add(renderer, result); return result;
        }
        internal Transform Pivot(string name, JToken worldMatrix)
        {
            var pivot = new GameObject(name).transform; pivot.SetParent(transform, false);
            SetMatrix(pivot, NativeMatrix(worldMatrix)); return pivot;
        }
        static void Reparent(IEnumerable<Transform> models, Transform pivot)
        {
            foreach (var model in models)
                if (model && model != pivot && !pivot.IsChildOf(model)) model.SetParent(pivot, true);
        }
        void BindBillboard(JToken record)
        {
            BillboardRecords++;
            var node = (string)record["node"];
            var pivot = Pivot("Billboard " + node, record["worldMatrix"]); pivots[node] = pivot;
            var models = Resolve(record); Reparent(models, pivot);
            WorldObjectRange.MarkDynamic(pivot);
            billboards.Add(new Billboard { Pivot = pivot, Mode = (int?)record["mode"] ?? 0,
                Enabled = (bool?)record["enabled"] ?? false, Renderers = Renderers(models) });
            BillboardBindings += models.Count;
        }
        void BindAnimation(JToken record)
        {
            AnimationRecords++;
            string id = (string)record["defaultClipId"] ?? (string)record["clipIds"]?.FirstOrDefault();
            if (id == null || !clips.TryGetValue(id, out var clip)) { Problem("Missing clip for " + (string)record["node"]); return; }
            if ((record["targets"] as JArray)?.Count is null or 0)
            {
                if (acceptedOlderTile)
                {
                    SourceCoverageGaps++;
                    MissingBindings += clip.Channels.Length + clip.Floats.Count(c => !c.Attribute.StartsWith("_SpecColor.", StringComparison.Ordinal));
                    return;
                }
                Problem("Animation record has no published targets: " + (string)record["node"]); return;
            }
            var targets = new Dictionary<string, Transform>();
            var rendererTargets = new Dictionary<string, Renderer[]>();
            Transform anchor = transform;
            var first = record["targets"]?.FirstOrDefault();
            if (first?["rootParentWorldMatrix"]?.Type == JTokenType.Array)
                anchor = Pivot("Animation anchor " + (string)record["node"], first["rootParentWorldMatrix"]);
            foreach (var target in record["targets"] ?? new JArray())
            {
                string path = (string)target["path"], node = (string)target["node"];
                var pivot = pivots.TryGetValue(node, out var integrated) ? integrated : new GameObject("Animation " + path).transform;
                pivot.SetParent(anchor, false); targets[path] = pivot;
            }
            foreach (var target in record["targets"] ?? new JArray())
            {
                string path = (string)target["path"], parent = (string)target["parentPath"];
                var pivot = targets[path];
                if (parent != null && targets.TryGetValue(parent, out var parentPivot)) pivot.SetParent(parentPivot, false);
                else if (parent != null) Problem("Missing animation parent " + parent);
                var trs = target["baseLocalTrs"];
                pivot.localPosition = NativeVector(trs["translation"]);
                pivot.localRotation = NativeRotation(trs["rotation"]); pivot.localScale = Vector(trs["scale"]);
                var models = Resolve(target); Reparent(models, pivot); rendererTargets[path] = Renderers(models);
            }
            var player = WorldBehaviourAnimation.Bind(this, clip, record, targets, rendererTargets, Renderers(Resolve(record)));
            animations.Add(player); AnimationPlayers++;
        }
        void BindEffect(JToken record)
        {
            EffectRecords++;
            if ((bool?)record["enabled"] == false) return;
            string name = (string)record["effectName"];
            var root = Pivot("Effect " + name, record["worldMatrix"]);
            particles.Bind(record, root);
            elementEffects.Bind(record, root);
            if (name == "neededjump_nano")
                foreach (var model in Resolve(record)) { WorldObjectRange.MarkDynamic(model); spins.Add(new Spin { Target = model, Initial = model.rotation }); }
            if (name != "blackhole") return;
            var contract = FFOne.Content.ContentAssets.Load<TextAsset>("map/shared/effects/blackhole.nif-animation");
            if (!contract && !FFOne.Content.NativeData.Load("map/shared/effects/blackhole.nif-animation")) { Problem("Missing accepted blackhole NIF animation contract"); return; }
            var doc = FFOne.Content.NativeData.Object("map/shared/effects/blackhole.nif-animation"); var definition = doc["clip"];
            string id = (string)definition["id"];
            if (!clips.TryGetValue(id, out var clip)) { clip = WorldBehaviourAnimation.Acquire(definition); clips.Add(id, clip); }
            var targets = new Dictionary<string, Renderer[]>();
            var models = record["models"] as JArray ?? new JArray();
            foreach (var binding in doc["modelBindings"] ?? new JArray())
            {
                int index = (int)binding["modelIndex"];
                if (index >= models.Count || !legacy.TryGetValue((string)models[index], out var joined))
                { Problem("Missing blackhole model index " + index); continue; }
                targets[(string)binding["targetPath"]] = Renderers(joined); NifAnimationBindings++;
            }
            animations.Add(WorldBehaviourAnimation.Bind(this, clip, record, new Dictionary<string, Transform>(), targets,
                Renderers(Resolve(record))));
        }
        void BindPlatform(JToken record, Dictionary<string, JToken> waypoints)
        {
            PlatformRecords++;
            if ((bool?)record["enabled"] == false) return;
            int mode = (int?)record["moveType"] ?? 0;
            if (mode < 0 || mode > 10) { Problem("Unsupported platform mode " + mode, true); return; }
            var models = Resolve(record); var pivot = Pivot("Platform " + (string)record["node"], record["worldMatrix"]);
            Reparent(models, pivot); WorldObjectRange.MarkDynamic(pivot); var path = new List<Vector3>(); string next = (string)record["head"];
            for (int i = 0, count = Math.Min(4096, ((int?)record["waypointCount"] ?? 0) + 1); i < count && next != null; i++)
            {
                if (!waypoints.TryGetValue(next, out var waypoint)) { Problem("Missing platform waypoint " + next); break; }
                path.Add(Vector(waypoint["point"])); next = (string)waypoint["next"];
            }
            platforms.Add(new Platform { Pivot = pivot, Position = pivot.position, Rotation = pivot.rotation, Scale = pivot.localScale,
                Mode = mode, Velocity = (float?)record["velocity"] ?? 0, Spin = (float?)record["spinVelocity"] ?? 0,
                Delta = Vector(record["to"]) - Vector(record["from"]), Path = path.ToArray() });
            PlatformBindings += models.Count;
        }
        void Update()
        {
            if (!Ready) return;
            foreach (var animation in animations) animation.Update(Time.deltaTime);
            float elapsed = WorldSimulationClock.Seconds;
            foreach (var spin in spins) if (spin.Target) spin.Target.rotation = Quaternion.AngleAxis(elapsed * 90, Vector3.up) * spin.Initial;
            foreach (var platform in platforms) UpdatePlatform(platform, elapsed);
        }
        static void UpdatePlatform(Platform motion, float elapsed)
        {
            if (!motion.Pivot || Mathf.Abs(motion.Velocity) < .000001f) return;
            bool pathMotion = motion.Mode >= 8; float phase = elapsed * Mathf.Abs(motion.Velocity);
            float cycle = phase % (pathMotion ? 1 : 2); bool returning = !pathMotion && cycle >= 1;
            float t = returning ? 2 - cycle : cycle;
            float eased = Mathf.Clamp01(t - Mathf.Sin(t * Mathf.PI * 2) * .15f);
            float angle = (returning ? Mathf.PI * 2 - t * Mathf.PI : t * Mathf.PI) + Mathf.PI / 2;
            Vector3 point = Vector3.zero; float radius = motion.Delta.magnitude * .5f;
            switch (motion.Mode)
            {
                case 0: case 6: case 7: point = motion.Delta * eased; break;
                case 1: point = motion.Delta * .5f + new Vector3(0, Mathf.Sin(angle), Mathf.Cos(angle)) * radius; break;
                case 2: point = motion.Delta * .5f + new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0) * radius; break;
                case 3: point = motion.Delta * .5f + new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius; break;
                case 8: case 9: case 10:
                    if (motion.Path.Length >= 2) { float p = t * (motion.Path.Length - 1); int i = Mathf.Min((int)p, motion.Path.Length - 2); point = Vector3.Lerp(motion.Path[i], motion.Path[i + 1], p - i); }
                    break;
            }
            float scale = motion.Mode >= 1 && motion.Mode <= 3 || pathMotion ? motion.Scale.x : 1;
            motion.Pivot.position = motion.Position + motion.Rotation * (point * scale);
            float degrees = (pathMotion ? t * 360 : (returning ? -1 : 1) * eased * 180) * motion.Spin;
            motion.Pivot.rotation = motion.Rotation * (motion.Mode is 4 or 6 or 9 ? Quaternion.AngleAxis(degrees, Vector3.right)
                : motion.Mode is 5 or 7 or 10 ? Quaternion.AngleAxis(degrees, Vector3.forward) : Quaternion.identity);
        }
        void LateUpdate()
        {
            if (!Ready) return; if (!view) view = Camera.main;
            if (view)
                foreach (var billboard in billboards)
                {
                    if (!billboard.Enabled || !billboard.Pivot || billboard.Renderers.Length > 0 && !AnyVisible(billboard.Renderers)) continue;
                    if (billboard.Mode == 0) billboard.Pivot.rotation = view.transform.rotation;
                    else if (billboard.Mode == 1) { var forward = view.transform.forward; forward.y = 0; if (forward.sqrMagnitude > .00001f) billboard.Pivot.rotation = Quaternion.LookRotation(forward); }
                    // Legacy modes 2/3/4 intentionally have no rotation in the accepted native runtime.
                }
            particles?.Update(Time.deltaTime, view);
        }
        internal void AddElementBillboard(Transform target, int mode) => billboards.Add(new Billboard {
            Pivot = target, Mode = mode, Enabled = true, Renderers = target.GetComponentsInChildren<Renderer>(true) });
        void OnDestroy()
        {
            particles?.Dispose(); elementEffects?.Dispose(); foreach (var clip in clips.Values) WorldBehaviourAnimation.Release(clip);
            foreach (var material in ownedMaterials) Release(material);
        }
        internal static Vector3 Vector(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return Vector3.zero;
            return token is JArray ? new Vector3((float)token[0], (float)token[1], (float)token[2])
                : new Vector3((float?)token["x"] ?? 0, (float?)token["y"] ?? 0, (float?)token["z"] ?? 0);
        }
        internal static Vector3 NativeVector(JToken token) { var value = Vector(token); value.x = -value.x; return value; }
        internal static Quaternion NativeRotation(JToken token)
        {
            var q = new Quaternion((float)token[0], -(float)token[1], -(float)token[2], (float)token[3]);
            return q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w > .00000001f ? q.normalized : Quaternion.identity;
        }
        internal static Matrix4x4 NativeMatrix(JToken token)
        {
            if (!(token is JArray)) return Matrix4x4.identity;
            var matrix = new Matrix4x4();
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
                matrix[row, col] = (float)token[row][col] * (row == 0 ? -1 : 1) * (col == 0 ? -1 : 1);
            return matrix;
        }
        internal static void SetMatrix(Transform target, Matrix4x4 matrix)
        {
            Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
            Vector3 scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
            if (matrix.determinant < 0) scale.x = -scale.x;
            target.localPosition = matrix.GetColumn(3); target.localScale = scale;
            target.localRotation = z.sqrMagnitude > .000000001f && y.sqrMagnitude > .000000001f ? Quaternion.LookRotation(z, y) : Quaternion.identity;
        }
        internal static void Release(Object item) { if (item) { if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item); } }
    }
}
