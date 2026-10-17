using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace FFOne.Tutorial
{
    public sealed class TutorialEffectPlayback : MonoBehaviour
    {
        sealed class Particle { internal Vector3 Position, Velocity; internal float Age; }
        sealed class Emitter
        {
            internal TutorialParticlePlan Plan;
            internal Mesh Mesh;
            internal Transform Transform;
            internal readonly List<Particle> Particles = new List<Particle>();
            internal float Timer = 2;
        }
        sealed class MaterialCurve { internal Material Material; internal string Property; internal AnimationCurve Curve; }
        TutorialEffectAssets assets;
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly List<MaterialCurve> curves = new List<MaterialCurve>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();
        Camera view;
        GameObject model;
        Mesh trail;
        Vector3[] trailPoints;
        float age, scale, materialDuration;

        internal void Initialize(TutorialEffectAssets definition, float effectScale, Camera camera, bool scaleMesh = false)
        {
            assets = definition; scale = effectScale; view = camera;
            if (assets.Model)
            {
                model = Instantiate(assets.Model, transform, false);
                if (scaleMesh) model.transform.localScale *= effectScale;
                foreach (var animation in model.GetComponentsInChildren<Animation>(true))
                {
                    animation.wrapMode = WrapMode.Loop;
                    foreach (AnimationState clip in animation) { clip.wrapMode = WrapMode.Loop; if (clip.name == "nif-default") animation.clip = clip.clip; }
                    if (animation.clip) animation.Play();
                }
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    // Explicit owned clones are valid in edit mode too. Accessing
                    // renderer.materials here implicitly instantiates scene materials.
                    var replacements = renderer.sharedMaterials.Select(source => new Material(source)).ToArray();
                    renderer.sharedMaterials = replacements;
                    foreach (var material in replacements)
                    {
                        materials.Add(material); assets.ConfigureMeshMaterial(material);
                        foreach (var source in assets.FloatCurves)
                        {
                            string path = (string)source["path"], node = path?.Split('/').Last();
                            bool matches = false;
                            for (var current = renderer.transform; current != null && current != transform; current = current.parent)
                                if (current.name == node) { matches = true; break; }
                            if (!matches) continue;
                            var curve = TutorialEffectAssets.Curve(source["curve"]);
                            if (curve.length == 0) continue;
                            curves.Add(new MaterialCurve { Material = material, Property = (string)source["attribute"], Curve = curve });
                            materialDuration = Mathf.Max(materialDuration, curve.keys[curve.length - 1].time);
                        }
                    }
                }
            }
            foreach (var plan in assets.Emitters)
            {
                var child = new GameObject("Authored " + (string)plan.Settings["particleName"]);
                child.transform.SetParent(transform, false);
                var mesh = CreateSurface(child, plan.Material);
                emitters.Add(new Emitter { Plan = plan, Mesh = mesh, Transform = child.transform });
            }
            if (assets.TrailMaterial != null)
            {
                var child = new GameObject("Authored projectile trail"); child.transform.SetParent(transform, false);
                trail = CreateSurface(child, assets.TrailMaterial);
                trailPoints = Enumerable.Repeat(transform.position, assets.TrailLength).ToArray();
            }
        }
        static Mesh CreateSurface(GameObject child, Material material)
        {
            var mesh = new Mesh { name = child.name }; mesh.MarkDynamic();
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return mesh;
        }
        void LateUpdate()
        {
            if (assets == null) return;
            float dt = Time.deltaTime; age += dt;
            if (!view) view = Camera.main;
            if (!assets.DisableUpdate && assets.MaximumTimer > 0)
            {
                if (model && age > assets.MaximumTimer) model.SetActive(false);
                if (age > assets.MaximumTimer + assets.LongestLifetime) { Destroy(gameObject); return; }
            }
            AnimateMaterials();
            foreach (var emitter in emitters) UpdateEmitter(emitter, dt);
            if (trail) UpdateTrail();
        }
        void AnimateMaterials()
        {
            float time = materialDuration > 0 ? age % materialDuration : 0;
            foreach (var entry in curves)
            {
                float value = entry.Curve.Evaluate(time);
                string property = entry.Property;
                if (property == "_MainTex.rotation") { entry.Material.SetFloat("_MainTexRotation", value); continue; }
                if (property.StartsWith("_MainTex.offset.", StringComparison.Ordinal))
                {
                    var offset = entry.Material.mainTextureOffset; if (property.EndsWith("x")) offset.x = value; else offset.y = value;
                    entry.Material.mainTextureOffset = offset; continue;
                }
                if (property.StartsWith("_MainTex.scale.", StringComparison.Ordinal))
                {
                    var size = entry.Material.mainTextureScale; if (property.EndsWith("x")) size.x = value; else size.y = value;
                    entry.Material.mainTextureScale = size; continue;
                }
                int separator = property.LastIndexOf('.');
                if (separator < 0) continue;
                string name = property.Substring(0, separator);
                if (!entry.Material.HasProperty(name)) continue;
                var color = entry.Material.GetColor(name);
                switch (property[property.Length - 1]) { case 'r': color.r = value; break; case 'g': color.g = value; break; case 'b': color.b = value; break; case 'a': color.a = value; break; }
                entry.Material.SetColor(name, color);
            }
        }
        void UpdateEmitter(Emitter emitter, float dt)
        {
            var plan = emitter.Plan; var settings = plan.Settings;
            bool emit = ScriptState(plan.Keys, 0, out Vector3 translation);
            if (!assets.DisableUpdate)
            {
                float duration = Mathf.Abs(assets.MaximumTimer);
                float normalized = duration > .00001f ? (assets.MaximumTimer < 0 ? age % duration : age) / duration : age;
                emit = ScriptState(plan.Keys, normalized, out translation);
                if (assets.MaximumTimer > 0 && age > assets.MaximumTimer) emit = false;
            }
            emitter.Transform.localPosition = translation;
            if (emit)
            {
                float rate = (float)settings["generationsPerSecond"];
                if (emitter.Timer + dt * rate >= 1)
                {
                    emitter.Timer = 0;
                    for (int i = 0, count = Mathf.CeilToInt((float)settings["numberPerGeneration"]); i < count; i++)
                    {
                        InitialState(settings, out var location, out var velocity);
                        emitter.Particles.Add(new Particle { Position = emitter.Transform.position + location, Velocity = emitter.Transform.rotation * velocity });
                    }
                }
                else emitter.Timer += dt * rate;
            }
            ClearGeometry();
            float lifetime = (float)settings["lifeTime"];
            for (int i = emitter.Particles.Count - 1; i >= 0; i--)
            {
                var particle = emitter.Particles[i]; particle.Age += dt;
                if (particle.Age > lifetime) { emitter.Particles.RemoveAt(i); continue; }
                particle.Velocity += TutorialEffectAssets.Vector(plan.Animator["force"]) * dt;
                particle.Position += particle.Velocity * dt;
                float fraction = Mathf.Clamp01(particle.Age / lifetime), initial = (float)settings["initialSize"];
                float width = plan.Width.length > 0 ? plan.Width.Evaluate(fraction) : plan.Height.length > 0 ? plan.Height.Evaluate(fraction) : initial;
                float height = plan.Height.length > 0 ? plan.Height.Evaluate(fraction) : width;
                Quaternion rotation = view ? view.transform.rotation : Quaternion.identity;
                if (plan.RenderMode == 4) rotation = Quaternion.Euler(-90, 0, 0);
                else if (plan.RenderMode == 5 && view)
                { var toCamera = view.transform.position - particle.Position; toCamera.y = 0; rotation = toCamera.sqrMagnitude > .00001f ? Quaternion.LookRotation(toCamera) : Quaternion.identity; }
                rotation *= Quaternion.Euler(0, 0, plan.Rotation.length > 0 ? plan.Rotation.Evaluate(fraction) : 0);
                Color tint = Color.white;
                if ((bool)plan.Animator["Does Animate Color?"])
                {
                    float sampled = fraction * 4; int key = Mathf.Min(3, Mathf.FloorToInt(sampled));
                    tint = Color.LerpUnclamped(PackedColor(plan.Animator["colorAnimation[" + key + "]"]["rgba"]),
                        PackedColor(plan.Animator["colorAnimation[" + (key + 1) + "]"]["rgba"]), sampled - key);
                }
                int frame = Mathf.FloorToInt(fraction * plan.Cycles * plan.XTiles * plan.YTiles) % (plan.XTiles * plan.YTiles);
                var uvScale = new Vector2(1f / plan.XTiles, 1f / plan.YTiles);
                var uvOffset = new Vector2(frame % plan.XTiles, frame / plan.XTiles) * uvScale;
                AddQuad(emitter.Transform, particle.Position, rotation, new Vector2(width, height) * scale, uvOffset, uvScale, tint);
            }
            ApplyGeometry(emitter.Mesh);
        }
        static Color PackedColor(JToken value)
        {
            uint packed = (uint)value;
            return new Color((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, (packed >> 24) / 255f);
        }
        static bool ScriptState(JArray keys, float time, out Vector3 translation)
        {
            var first = keys[0]; translation = TutorialEffectAssets.Vector(first["translate"]);
            if (time <= (float)first["time"]) return (int)first["genType"] != 0;
            for (int i = 1; i < keys.Count; i++)
            {
                if (time > (float)keys[i]["time"]) continue;
                float from = (float)keys[i - 1]["time"], span = (float)keys[i]["time"] - from;
                translation = Vector3.Lerp(TutorialEffectAssets.Vector(keys[i - 1]["translate"]), TutorialEffectAssets.Vector(keys[i]["translate"]), span <= .000001f ? 1 : (time - from) / span);
                return (int)keys[i - 1]["genType"] != 0;
            }
            translation = TutorialEffectAssets.Vector(keys[keys.Count - 1]["translate"]);
            return (int)keys[keys.Count - 1]["genType"] != 0;
        }
        static void InitialState(JObject settings, out Vector3 position, out Vector3 velocity)
        {
            int generation = (int)settings["m_iGenType"]; var plane = TutorialEffectAssets.Vector(settings["plane"]);
            float randomPosition = (float)settings["randomPosition"];
            if (generation == 2) position = new Vector3((Random.value - .5f) * plane.x, (Random.value - .5f) * plane.y, (Random.value - .5f) * plane.z);
            else if (generation <= 1) position = Random.onUnitSphere * (generation == 1 ? randomPosition : Random.value * randomPosition);
            else
            {
                int axis = (generation - 3) % 3;
                Vector2 direction; float radius;
                if (axis == 2) { direction = Random.insideUnitCircle.normalized; radius = Mathf.Lerp(plane.x, plane.z, Random.value); }
                else { radius = Mathf.Lerp(plane.x, plane.z, Random.value); direction = Random.insideUnitCircle.normalized; }
                position = (axis == 0 ? new Vector3(0, direction.x, direction.y) : axis == 1 ? new Vector3(direction.x, direction.y, 0) : new Vector3(direction.x, 0, direction.y)) * radius;
            }
            velocity = generation == 1 || generation >= 6 && generation <= 8 ? -position.normalized : generation >= 9 ? position.normalized : Vector3.zero;
            var initial = TutorialEffectAssets.Vector(settings["initVelocity"]);
            float angle = (float)settings["randomAngle"] * 180;
            var rotated = Quaternion.Euler(Random.Range(-angle, angle), Random.Range(-angle, angle), Random.Range(-angle, angle)) * initial;
            velocity += new Vector3(Mathf.Abs(initial.x) < .01f ? rotated.x * initial.x : rotated.x,
                Mathf.Abs(initial.y) < .01f ? rotated.y * initial.y : rotated.y, Mathf.Abs(initial.z) < .01f ? rotated.z * initial.z : rotated.z);
            velocity *= Random.value * 2 * (1 - (float)settings["randomVelocity"]);
        }
        void AddQuad(Transform renderer, Vector3 point, Quaternion rotation, Vector2 size, Vector2 uvOffset, Vector2 uvScale, Color color)
        {
            int first = vertices.Count;
            var right = rotation * Vector3.right * size.x * .5f; var up = rotation * Vector3.up * size.y * .5f;
            vertices.Add(renderer.InverseTransformPoint(point - right - up)); vertices.Add(renderer.InverseTransformPoint(point + right - up));
            vertices.Add(renderer.InverseTransformPoint(point + right + up)); vertices.Add(renderer.InverseTransformPoint(point - right + up));
            uvs.Add(uvOffset); uvs.Add(uvOffset + new Vector2(uvScale.x, 0)); uvs.Add(uvOffset + uvScale); uvs.Add(uvOffset + new Vector2(0, uvScale.y));
            for (int i = 0; i < 4; i++) colors.Add(color);
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        void UpdateTrail()
        {
            Vector3 previous = trailPoints[trailPoints.Length - 1], current = transform.position;
            for (float fraction = assets.TrailStep; fraction < 1; fraction += assets.TrailStep) PushTrail(Vector3.Lerp(previous, current, fraction));
            PushTrail(current); ClearGeometry();
            Vector3 direction = (current - trailPoints[trailPoints.Length - 2]).normalized;
            Vector3 right = view ? view.transform.right : Vector3.right;
            for (int i = 0; i < trailPoints.Length; i++)
            {
                float fraction = (float)i / (trailPoints.Length - 1);
                var axis = direction == Vector3.zero ? right : Quaternion.AngleAxis(fraction * 360 + age * 720, direction) * right;
                var half = axis * assets.TrailHeight * scale * .5f;
                vertices.Add(transform.InverseTransformPoint(trailPoints[i] + half));
                vertices.Add(transform.InverseTransformPoint(trailPoints[i] + (i + 1 == trailPoints.Length ? half : -half)));
                float u = i + 1 == trailPoints.Length ? 0 : 1 - (float)(i + 1) / trailPoints.Length;
                uvs.Add(new Vector2(u, 0)); uvs.Add(new Vector2(u, 1));
                float shade = (float)Mathf.Min(i, trailPoints.Length - 2) / (trailPoints.Length - 1);
                colors.Add(new Color(shade, shade, shade, shade)); colors.Add(new Color(shade, shade, shade, shade));
                if (i + 1 < trailPoints.Length) { int n = i * 2; indices.AddRange(new[] { n, n + 1, n + 2, n + 2, n + 1, n + 3 }); }
            }
            ApplyGeometry(trail);
        }
        void PushTrail(Vector3 point) { Array.Copy(trailPoints, 1, trailPoints, 0, trailPoints.Length - 1); trailPoints[trailPoints.Length - 1] = point; }
        void ClearGeometry() { vertices.Clear(); uvs.Clear(); colors.Clear(); indices.Clear(); }
        void ApplyGeometry(Mesh mesh) { mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); }
        void OnDestroy()
        {
            foreach (var emitter in emitters) TutorialEffectAssets.Release(emitter.Mesh);
            TutorialEffectAssets.Release(trail);
            foreach (var material in materials) TutorialEffectAssets.Release(material);
        }
    }
}
