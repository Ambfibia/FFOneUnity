using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    /// <summary>Exact tutorial ES identity, attachment and lifetime adapter.</summary>
    public sealed class TutorialCinematicEffects : MonoBehaviour
    {
        static readonly int[] EffectIds = { 3, 4, 10, 21, 38, 50, 372, 503, 530, 566, 592, 593,
            547, 548, 668, 705, 734, 736, 737, 739, 740, 741, 742, 750, 751, 767, 768, 769, 771 };
        static readonly int[] AnimationEffectIds = { 3, 4, 21, 38, 50, 503, 530, 547, 548, 566, 592, 593, 734, 736, 737, 751, 768, 769 };
        public int RootAnimationEffectsPlayed { get; private set; }
        public int NativeProjectileTrailsSpawned { get; private set; }
        readonly Dictionary<int, TutorialEffectAssets> assets = new Dictionary<int, TutorialEffectAssets>();
        readonly List<GameObject> instances = new List<GameObject>();
        readonly List<GameObject> tracked = new List<GameObject>();
        readonly Dictionary<string, GameObject> named = new Dictionary<string, GameObject>();
        Func<int, Transform> npc;
        Func<JToken, Vector3> position;
        Camera view;
        bool russian;
        public bool UseRussianVoice { get => russian; set => russian = value; }

        public static System.Threading.Tasks.Task PreloadAnimationEffectsAsync(System.Threading.CancellationToken cancellation)
            => System.Threading.Tasks.Task.WhenAll(AnimationEffectIds.Select(id => TutorialEffectAssets.PreloadAsync(id, cancellation))
                .Concat(AnimationEffectIds.Select(ModelPath).Where(p => p != null).Select(p => FFOne.Content.NativeModelAssets.PreloadAsync(p, cancellation)))
                .Append(FFOne.Content.ContentAssets.GetAsync<Shader>("World/Particle", cancellation)));

        public static System.Threading.Tasks.Task PreloadAllAsync(System.Threading.CancellationToken cancellation)
            => System.Threading.Tasks.Task.WhenAll(EffectIds.Concat(new[] { 15, 391 })
                .Select(id => TutorialEffectAssets.PreloadAsync(id, cancellation))
                .Concat(EffectIds.Concat(new[] { 15, 391 }).Select(ModelPath).Where(p => p != null)
                    .Select(p => FFOne.Content.NativeModelAssets.PreloadAsync(p, cancellation)))
                .Append(FFOne.Content.ContentAssets.GetAsync<Shader>("World/Particle", cancellation)));

        public async System.Threading.Tasks.Task PrepareAllAsync(System.Threading.CancellationToken cancellation)
        {
            foreach (int id in EffectIds.Concat(new[] { 15, 391 }))
            {
                cancellation.ThrowIfCancellationRequested();
                Preload(id);
                await System.Threading.Tasks.Task.Yield();
            }
        }

        public void Initialize(Func<int, Transform> actor, Func<JToken, Vector3> resolvePosition, Camera camera, bool useRussian = false)
        { npc = actor; position = resolvePosition; view = camera; russian = useRussian; }

        public static IEnumerable<string> RequiredNativePaths()
        {
            foreach (int id in EffectIds)
            {
                yield return "map/shared/effects/es" + id + ".closure.json";
                string model = ModelPath(id);
                // ES768 is recovered directly into this Unity project's editable
                // publication; the accepted native tree has no model carrier.
                if (model != null && id != 768) yield return model + ".glb";
            }
            foreach (int id in new[] { 15, 391 })
            {
                yield return "map/shared/projectiles/effects/es" + id + ".closure.json";
                yield return ModelPath(id) + ".glb";
            }
        }
        internal static string ModelPath(int id) => id switch {
            15 => "map/shared/projectiles/effects/models/es15/tail1",
            391 => "map/shared/projectiles/effects/models/es391/tail2",
            668 => "map/shared/effects/models/es668/patrol_elena7",
            705 => "map/shared/effects/models/es705/nano_get",
            547 => "map/shared/effects/models/es547/f_buttercup_corruptak",
            548 => "map/shared/effects/models/es548/f_buttercup_megatak",
            734 => "map/shared/effects/models/es734/T_pistol",
            736 => "map/shared/effects/models/es736/t_bubbles",
            739 => "map/shared/effects/models/es739/Tutorial_dexterhologram",
            740 => "map/shared/effects/models/es740/m_inven",
            741 => "map/shared/effects/models/es741/Tutorial_dexterhologram_1",
            742 => "map/shared/effects/models/es742/T_electric gun",
            751 => "map/shared/effects/models/es751/npc_samuraijack_event_eff",
            768 => "map/shared/effects/models/es768/EP_DL_Scamper_tail",
            771 => "map/shared/effects/models/es771/tutorialstone",
            _ => null
        };

        public void Preload(int id)
        {
            bool animationEffect = AnimationEffectIds.Contains(id);
            if (!assets.ContainsKey(id)) assets.Add(id, TutorialEffectAssets.Load(id, ModelPath(id), id == 15 || id == 391, worldShader: animationEffect));
        }
        public GameObject Spawn(int id, Vector3 location, float scale = 1, bool track = false, string instanceName = null)
        {
            Preload(id);
            var root = new GameObject(instanceName ?? "Tutorial effect " + id);
            root.transform.SetParent(transform, false);
            root.transform.position = location;
            root.transform.localScale = Vector3.one * scale;
            try { root.AddComponent<TutorialEffectPlayback>().Initialize(assets[id], scale, view); }
            catch { TutorialEffectAssets.Release(root); throw; }
            instances.Add(root);
            if (track) tracked.Add(root);
            instanceName ??= id == 739 ? "Dexter hologram effect 739" : id == 741 ? "portal effect 741" : null;
            if (instanceName != null) { DestroyNamed(instanceName); named[instanceName] = root; root.name = instanceName; }
            return root;
        }
        public void Execute(JToken action)
        {
            string kind = (string)action["type"];
            var args = action["args"];
            switch (kind)
            {
                case "Preload": Preload((int)args[0]); break;
                case "Add": Add(args[0]); break;
                case "AddBatch": foreach (var item in args[0]) Add(item); break;
                case "AddAtNpcInclusive":
                    for (int id = (int)action["first"]; id <= (int)action["last"]; id++)
                    {
                        var actor = npc(id);
                        if (actor) Spawn((int)action["effect_id"], actor.position, (float)action["scale"], (bool)action["tracked"]);
                    }
                    break;
                case "AttachBone": Attach(args[0]); break;
                case "DestroyNamed": DestroyNamed((string)args[0]); break;
                case "ClearTracked": ClearTracked(); break;
                default: throw new InvalidOperationException("Unknown tutorial effect action: " + kind);
            }
        }
        void Add(JToken data) => Spawn((int)data["effect_id"], position(data["position"]),
            (float?)data["scale"] ?? 1, (bool?)data["tracked"] ?? false, (string)data["name"]);
        public void AttachAnimationEffect(Transform model, string parameter)
        {
            int separator = parameter?.IndexOf(':') ?? -1;
            if (separator <= 0 || !int.TryParse(parameter.Substring(0, separator), out int id) || !EffectIds.Contains(id))
                throw new InvalidOperationException("Unsupported tutorial animation effect: " + parameter);
            string node = parameter.Substring(separator + 1);
            var bone = model.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == node);
            if (!bone) throw new InvalidOperationException("Tutorial animation effect bone is absent: " + node);
            var instance = Spawn(id, bone.position, track: true);
            instance.transform.SetParent(bone, true);
            instance.transform.localPosition = Vector3.zero;
            // AnimationEventHandler.PlayCharacterEffect supplies this basis
            // whenever tag_p resolves a named attachment node.
            instance.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }
        public void AttachRootAnimationEffect(Transform model, int id)
        {
            if (!AnimationEffectIds.Contains(id)) throw new InvalidOperationException("Unsupported root animation effect: " + id);
            var instance = Spawn(id, model.position, track: true);
            instance.transform.SetParent(model, true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            RootAnimationEffectsPlayed++;
        }
        void Attach(JToken data)
        {
            var gate = data["locale_gate"];
            if ((string)gate?["type"] == "LegacyLocalEquals" && (int)gate["args"][0] != (russian ? 1 : 0)) return;
            var actor = npc((int)data["actor_id"]);
            if (!actor) throw new InvalidOperationException("Tutorial effect attachment actor is absent.");
            string exactName = (string)data["exact_node_name"];
            var bone = actor.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == exactName);
            if (!bone) throw new InvalidOperationException("Tutorial effect bone is absent: " + exactName);
            var effect = Spawn((int)data["effect_id"], bone.position, (float?)data["scale"] ?? 1,
                (bool?)data["tracked"] ?? false, (string)data["name"]);
            effect.transform.rotation = Quaternion.Euler(TutorialEffectAssets.Vector(data["spawn_world_rotation"]?["args"]?[0]));
            effect.transform.SetParent(bone, true);
            effect.transform.localRotation = Quaternion.Euler(TutorialEffectAssets.Vector(data["local_rotation_after_parenting"]?["args"]?[0]));
            if ((float?)data["destroy_after_seconds"] is float delay) Destroy(effect, delay);
        }
        public void DestroyNamed(string instanceName)
        {
            if (!named.TryGetValue(instanceName, out var instance)) return;
            TutorialEffectAssets.Release(instance);
            named.Remove(instanceName);
        }
        public void ClearTracked()
        {
            foreach (var instance in tracked) TutorialEffectAssets.Release(instance);
            tracked.Clear();
        }
        public void ClearAll()
        {
            foreach (var instance in instances) TutorialEffectAssets.Release(instance);
            instances.Clear(); tracked.Clear(); named.Clear();
        }
        public void ProjectilePair(Vector3 source, Func<Vector3> target, bool reverse)
        {
            foreach (int id in new[] { 15, 391 })
            {
                var instance = Spawn(id, source);
                var playback = instance.GetComponent<TutorialEffectPlayback>();
                if (playback.Trail != null && playback.Trail.Renderer) NativeProjectileTrailsSpawned++;
                instance.AddComponent<TutorialOniProjectile>().Initialize(target, reverse);
            }
        }
        void OnDestroy()
        {
            ClearAll();
            foreach (var asset in assets.Values) asset.Dispose();
            assets.Clear();
        }
    }

    // A direct transcription of the accepted OniMoveScript state transition.
    public sealed class TutorialOniProjectile : MonoBehaviour
    {
        Func<Vector3> target;
        bool reverse, stopped;
        Vector3 velocity;
        float speed = 1, age, stoppedTime;
        public void Initialize(Func<Vector3> destination, bool reversed)
        {
            target = destination; reverse = reversed;
            float x = UnityEngine.Random.Range(-10f, 10f), z = UnityEngine.Random.Range(-10f, 10f);
            velocity = new Vector3(x, reverse ? 0 : UnityEngine.Random.Range(5f, 10f), z);
        }
        void Update()
        {
            float dt = Time.deltaTime;
            if (stopped) { stoppedTime += dt; if (stoppedTime > .5f) Destroy(gameObject); return; }
            Vector3 destination = target(), delta = destination - transform.position;
            float distance = delta.magnitude;
            if (distance <= dt * (speed + dt * 5) || distance < .3f)
            { transform.position = destination; stopped = true; return; }
            if (distance > 50 || age > 5) { stopped = true; return; }
            Vector3 previous = transform.position;
            if (reverse)
            {
                velocity *= 1 - dt * 2; velocity += delta * dt * 4;
                transform.position += velocity * dt;
            }
            else
            {
                delta.y = 0; speed += dt * 5;
                velocity.x *= 1 - dt * 2; velocity.z *= 1 - dt * 2;
                velocity += delta * dt * 2; velocity.y -= 9.8f * dt;
                Vector3 planar = new Vector3(velocity.x, 0, velocity.z).normalized * speed;
                planar.y = velocity.y; transform.position += planar * dt;
                if (transform.position.y < destination.y)
                { var point = transform.position; point.y = destination.y; transform.position = point; velocity.y *= -.5f; }
            }
            Vector3 movement = transform.position - previous;
            if (movement.sqrMagnitude > .0000001f) transform.rotation = Quaternion.LookRotation(movement);
            age += dt;
        }
    }
}
