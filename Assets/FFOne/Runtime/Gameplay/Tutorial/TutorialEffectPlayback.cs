using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FFOne.Tutorial
{
    public sealed class TutorialEffectPlayback : MonoBehaviour
    {
        sealed class Emitter
        {
            internal NativeParticleSettings Plan;
            internal FFOne.World.NativeParticleEmitter Native;
            internal Transform Transform;
            internal float Timer = 2;
        }
        sealed class MaterialCurve { internal Material Material; internal string Property; internal AnimationCurve Curve; }
        TutorialEffectAssets assets;
        readonly List<Emitter> emitters = new List<Emitter>();
        readonly List<MaterialCurve> curves = new List<MaterialCurve>();
        readonly List<Material> materials = new List<Material>();
        Camera view;
        GameObject model;
        NativeWoundTrail trail;
        internal NativeWoundTrail Trail => trail;
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
                    var replacements = renderer.sharedMaterials.Select(assets.MeshMaterial).ToArray();
                    for (int index=0;index<replacements.Length;index++)
                    {
                        var material=replacements[index]; if(!material)continue;
                        var applicable=assets.FloatCurves.Where(source=>{
                            string node=source.Path?.Split('/').Last();
                            for(var current=renderer.transform;current!=null&&current!=transform;current=current.parent)
                                if(current.name==node)return true;
                            return false;
                        }).Where(source=>source.Curve.length>0).ToArray();
                        if(applicable.Length==0)continue;
                        material=new Material(material);replacements[index]=material;materials.Add(material);
                        foreach(var source in applicable)
                        {
                            curves.Add(new MaterialCurve {Material=material,Property=source.Attribute,Curve=source.Curve});
                            materialDuration=Mathf.Max(materialDuration,source.Curve.keys[source.Curve.length-1].time);
                        }
                    }
                    renderer.sharedMaterials=replacements;
                }
            }
            foreach (var plan in assets.Emitters)
            {
                var child = new GameObject("Authored " + plan.Name);
                child.transform.SetParent(transform, false);
                int capacity=Mathf.Max(16,Mathf.CeilToInt(plan.Rate*plan.Lifetime+2)*Mathf.CeilToInt(plan.Count));
                var native=new FFOne.World.NativeParticleEmitter(child,assets.ParticleMaterial(plan),plan.Lifetime,plan.InitialSize,
                    plan.Width,plan.Height,plan.Rotation,plan.Force,scale,plan.RenderMode,plan.XTiles,plan.YTiles,plan.Cycles,false,plan.AnimateColor,plan.Colors,capacity);
                emitters.Add(new Emitter { Plan = plan, Native = native, Transform = child.transform });
            }
            if (assets.TrailMaterial != null)
            {
                trail = new NativeWoundTrail(transform, assets.TrailMaterial, assets.TrailLength,
                    assets.TrailHeight * scale, assets.TrailStep, transform.position);
            }
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
            trail?.Update(transform.position, age, view);
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
            var plan = emitter.Plan;
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
                float rate = plan.Rate;
                if (emitter.Timer + dt * rate >= 1)
                {
                    emitter.Timer = 0;
                    for (int i = 0, count = Mathf.CeilToInt(plan.Count); i < count; i++)
                    {
                        InitialState(plan, out var location, out var velocity);
                        emitter.Native.Emit(emitter.Transform.position + location, emitter.Transform.rotation * velocity);
                    }
                }
                else emitter.Timer += dt * rate;
            }
            emitter.Native.Step(dt);
            emitter.Native.Face(view);
        }
        static bool ScriptState(NativeEffectKey[] keys, float time, out Vector3 translation)
        {
            var first = keys[0]; translation = first.Translation;
            if (time <= first.Time) return first.Emit;
            for (int i = 1; i < keys.Length; i++)
            {
                if (time > keys[i].Time) continue;
                float from = keys[i - 1].Time, span = keys[i].Time - from;
                translation = Vector3.Lerp(keys[i - 1].Translation, keys[i].Translation, span <= .000001f ? 1 : (time - from) / span);
                return keys[i - 1].Emit;
            }
            translation = keys[keys.Length - 1].Translation;
            return keys[keys.Length - 1].Emit;
        }
        static void InitialState(NativeParticleSettings settings, out Vector3 position, out Vector3 velocity)
        {
            int generation = settings.Generation; var plane = settings.Plane;
            float randomPosition = settings.RandomPosition;
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
            var initial = settings.InitialVelocity;
            float angle = settings.RandomAngle * 180;
            var rotated = Quaternion.Euler(Random.Range(-angle, angle), Random.Range(-angle, angle), Random.Range(-angle, angle)) * initial;
            velocity += new Vector3(Mathf.Abs(initial.x) < .01f ? rotated.x * initial.x : rotated.x,
                Mathf.Abs(initial.y) < .01f ? rotated.y * initial.y : rotated.y, Mathf.Abs(initial.z) < .01f ? rotated.z * initial.z : rotated.z);
            velocity *= Random.value * 2 * (1 - settings.RandomVelocity);
        }
        void OnDestroy()
        {
            foreach (var emitter in emitters) emitter.Native.Dispose();
            trail?.Dispose();
            foreach (var material in materials) TutorialEffectAssets.Release(material);
        }
    }
}
