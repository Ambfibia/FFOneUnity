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
    [DefaultExecutionOrder(-120)]
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
        readonly Dictionary<string, WorldMotionBinding> nativeMotion = new();
        Transform motionSample;
        AnimationClip spinClip;
        internal FFOne.Content.NativeWorldEffectLibrary NativeEffects;
        WorldBehaviourParticles particles;
        WorldBehaviourEpEffects elementEffects;
        Dictionary<string, List<Transform>> legacy, source;
        Camera view;
        bool acceptedOlderTile;
        ISet<string> sourceModelOmissions;
        readonly HashSet<string> reportedSourceOmissions = new();
        internal sealed class RaceRing
        {
            internal string Key;
            internal Vector3 Center;
            internal float Radius;
            internal Renderer[] Models;
            bool? visible;
            internal void Show(bool value)
            {
                if (visible == value) return;
                visible = value;
                // WorldObjectRange owns forceRenderingOff. Keep race visibility independent of distance.
                foreach (var renderer in Models) if (renderer) renderer.enabled = value;
            }
        }
        internal static readonly List<RaceRing> RaceRings = new();
        readonly List<RaceRing> cellRings = new();
        readonly List<WorldZipline> cellZiplines = new();
        readonly List<WorldLauncher> cellLaunchers = new();

        internal void AddAuthoredLaunchers(WorldLauncherPlacement[] placements)
        {
            foreach (var placement in placements)
            {
                var launcher = new WorldLauncher(this, view, placement);
                cellLaunchers.Add(launcher);
                WorldLauncher.Loaded.Add(launcher);
            }
        }

        sealed class Billboard { internal Transform Pivot; internal int Mode; internal bool Enabled; internal Renderer[] Renderers; }
        sealed class Spin { internal Transform Target; internal Quaternion Initial; }
        sealed class Platform
        {
            internal Transform Pivot; internal Vector3 Position, Scale, Delta; internal Quaternion Rotation;
            internal float Velocity; internal int Mode; internal AnimationClip Clip;
        }

        public static WorldBehaviour Attach(GameObject root, string behaviourJson,
            Dictionary<string, List<Transform>> legacyModels, Dictionary<string, List<Transform>> sourceNodes, Camera camera,
            ISet<string> acceptedSourceOmissions = null, WorldMotionBinding[] motion = null, FFOne.Content.NativeWorldEffectLibrary effects = null)
        {
            var result = root.AddComponent<WorldBehaviour>();
            result.sourceModelOmissions = acceptedSourceOmissions;
            result.NativeEffects = effects;
            if (motion != null) foreach (var binding in motion) result.nativeMotion.Add(binding.SourceId, binding);
            var iterator = result.Initialize(behaviourJson, legacyModels, sourceNodes, camera);
            while (iterator.MoveNext()) { }
            return result;
        }

        // A chunk loader can spread admitted records over frames and include this work in its load timing.
        public static IEnumerator AttachAsync(GameObject root, string behaviourJson,
            Dictionary<string, List<Transform>> legacyModels, Dictionary<string, List<Transform>> sourceNodes,
            Camera camera, Action<WorldBehaviour> completed, ISet<string> acceptedSourceOmissions = null, WorldMotionBinding[] motion = null, FFOne.Content.NativeWorldEffectLibrary effects = null)
        {
            var result = root.AddComponent<WorldBehaviour>();
            result.sourceModelOmissions = acceptedSourceOmissions;
            result.NativeEffects = effects;
            if (motion != null) foreach (var binding in motion) result.nativeMotion.Add(binding.SourceId, binding);
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
            motionSample = new GameObject("Native motion sample").transform; motionSample.SetParent(transform, false);
            if (nativeMotion.TryGetValue("world-spin", out var spin)) spinClip = spin.Channels[0];
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
                try { nativeMotion.TryGetValue((string)definition["id"], out var native); var clip = WorldBehaviourAnimation.Acquire(definition, native); clips.Add(clip.Id, clip); }
                catch (Exception error) { Problem("Invalid AnimationClip: " + error.Message, true); }
                if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            foreach (var record in document["animations"] ?? new JArray())
            {
                BindAnimation(record); if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            particles = new WorldBehaviourParticles(this, NativeEffects ? NativeEffects.Particles : null, view);
            elementEffects = new WorldBehaviourEpEffects(this, particles, view);
            foreach (var record in document["effectEmitters"] ?? new JArray())
            {
                BindEffect(record); if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            particles.Complete();
            var ziplineTriggers = new Dictionary<string, JToken>();
            foreach (var record in document["triggers"] ?? new JArray())
            {
                if ((string)record["kind"] == "platform") BindPlatform(record);
                if ((string)record["kind"] == "launcher" && (bool?)record["enabled"] != false)
                {
                    var launcher = new WorldLauncher(this, view, record);
                    cellLaunchers.Add(launcher);
                    WorldLauncher.Loaded.Add(launcher);
                }
                if ((string)record["kind"] == "zipline" && (bool?)record["enabled"] != false &&
                    (string)record["node"] is string ziplineNode)
                    ziplineTriggers[ziplineNode] = record;
                if ((string)record["kind"] == "ring")
                {
                    var matrix = NativeMatrix(record["worldMatrix"]);
                    var ring = new RaceRing {
                        Key = (string)document["id"] + "/" + (string)record["node"],
                        Center = transform.TransformPoint(matrix.MultiplyPoint3x4(NativeVector(record["startPosition"]))),
                        Radius = (float?)record["radius"] ?? 0, Models = Renderers(Resolve(record)).Where(r => r.enabled).ToArray()
                    };
                    ring.Show(false);
                    if ((bool?)record["enabled"] != false) { cellRings.Add(ring); RaceRings.Add(ring); }
                }
                if (timer.ElapsedMilliseconds >= 3) { yield return null; timer.Restart(); }
            }
            if (ziplineTriggers.Count != 0)
                foreach (var volume in document["triggerVolumes"] ?? new JArray())
                {
                    if ((bool?)volume["enabled"] == false || (bool?)volume["isTrigger"] != true ||
                        (string)volume["node"] is not string node || !ziplineTriggers.TryGetValue(node, out var record)) continue;
                    var route = new WorldZipline(this, view, record, volume);
                    float length = Vector3.Distance(route.Start, route.End);
                    if (!float.IsFinite(length) || length <= .01f || !float.IsFinite(route.Speed) || route.Speed <= 0) continue;
                    cellZiplines.Add(route);
                    WorldZipline.Loaded.Add(route);
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
        public static bool IsRotatingNanoSign(string effectName) => effectName is "neededjump_nano" or "roket_symbol";

        void BindEffect(JToken record)
        {
            EffectRecords++;
            if ((bool?)record["enabled"] == false) return;
            string name = (string)record["effectName"];
            var root = Pivot("Effect " + name, record["worldMatrix"]);
            particles.Bind(record, root);
            elementEffects.Bind(record, root);
            if (IsRotatingNanoSign(name))
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
        void BindPlatform(JToken record)
        {
            PlatformRecords++;
            if ((bool?)record["enabled"] == false) return;
            int mode = (int?)record["moveType"] ?? 0;
            if (mode < 0 || mode > 10) { Problem("Unsupported platform mode " + mode, true); return; }
            var models = Resolve(record); var pivot = Pivot("Platform " + (string)record["node"], record["worldMatrix"]);
            Reparent(models, pivot); WorldObjectRange.MarkDynamic(pivot);
            if (!nativeMotion.TryGetValue("platform:" + (string)record["node"], out var native)) { Problem("Republish platform motion " + (string)record["node"], true); return; }
            pivot.gameObject.AddComponent<MovingWorldPlatform>();
            platforms.Add(new Platform { Pivot = pivot, Position = pivot.position, Rotation = pivot.rotation, Scale = pivot.localScale,
                Mode = mode, Velocity = (float?)record["velocity"] ?? 0, Clip = native.Channels[0] });
            PlatformBindings += models.Count;
        }
        void Update()
        {
            if (!Ready) return;
            foreach (var animation in animations) animation.Update(Time.deltaTime);
            float elapsed = WorldSimulationClock.Seconds;
            if (spinClip)
            {
                motionSample.localRotation = Quaternion.identity;
                spinClip.SampleAnimation(motionSample.gameObject, Mathf.Repeat(elapsed, 4));
                foreach (var spin in spins) if (spin.Target) spin.Target.rotation = motionSample.localRotation * spin.Initial;
            }
            foreach (var platform in platforms) UpdatePlatform(platform, elapsed);
        }
        void UpdatePlatform(Platform motion, float elapsed)
        {
            if (!motion.Pivot || !motion.Clip || Mathf.Abs(motion.Velocity) < .000001f) return;
            bool followsPath = motion.Mode >= 8;
            motionSample.localPosition = Vector3.zero; motionSample.localRotation = Quaternion.identity;
            motion.Clip.SampleAnimation(motionSample.gameObject, Mathf.Repeat(elapsed * Mathf.Abs(motion.Velocity), followsPath ? 1 : 2));
            float scale = motion.Mode >= 1 && motion.Mode <= 3 || followsPath ? motion.Scale.x : 1;
            motion.Pivot.position = motion.Position + motion.Rotation * (motionSample.localPosition * scale);
            motion.Pivot.rotation = motion.Rotation * motionSample.localRotation;
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
            foreach (var ring in cellRings) RaceRings.Remove(ring);
            foreach (var route in cellZiplines) WorldZipline.Loaded.Remove(route);
            foreach (var launcher in cellLaunchers) WorldLauncher.Loaded.Remove(launcher);
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
