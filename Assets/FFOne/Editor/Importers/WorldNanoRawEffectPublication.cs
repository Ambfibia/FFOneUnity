using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    // These ES owners include built-in Unity 2.x ellipsoid emitters.
    public static class WorldNanoRawEffectPublication
    {
        const string Folder = "Assets/FFOne/Content/VFX/Published/NanoRaw";
        const string Sources = "Assets/FFOne/Editor/ImportMetadata/NanoEffects";

        public static NativeEffectDefinition[] Publish()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/FFOne/Content/VFX/Published", "NanoRaw");
            return new[] { Publish(843), Publish(844), Publish(845), Publish(846) };
        }

        internal static NativeEffectDefinition Publish(int id)
        {
            var closure = JObject.Parse(File.ReadAllText($"{Sources}/es{id}.closure.json"));
            var objects = (JArray)closure["objects"];
            var names = objects.Where(o => (string)o["objectType"] == "GameObject")
                .ToDictionary(o => (long)o["pathId"], o => (string)o["name"]);
            var actors = new Dictionary<long, GameObject>();
            var transforms = objects.Where(o => (string)o["objectType"] == "Transform")
                .ToDictionary(o => (long)o["pathId"], o => o["value"]);
            var controller = objects.Single(o => (string)o["objectType"] == "MonoBehaviour")["value"];
            long nifRoot = (long)controller["nifObject"]["pathId"];
            long Actor(JToken obj) => (long)obj["m_GameObject"]["pathId"];
            long rootId = (long)closure["rootPathId"];
            var root = actors[rootId] = new GameObject(names[rootId]);
            try
            {
                if (id == 843 || id == 846)
                {
                    string path = Directory.GetFiles($"Assets/FFOne/Content/VFX/NanoModels/ES{id}", "*.glb").Single().Replace('\\', '/');
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!model) throw new InvalidDataException($"ES{id} mesh was not imported.");
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    var nodes = instance.GetComponentsInChildren<Transform>(true);
                    foreach (var emitter in objects.Where(o => (string)o["objectType"] == "EllipsoidParticleEmitter"))
                    {
                        long key = Actor(emitter["value"]);
                        actors[key] = nodes.Single(t => t.name == names[key]).gameObject;
                    }
                }
                else
                {
                    foreach (var pair in names)
                        if (pair.Key != rootId) actors[pair.Key] = new GameObject(pair.Value);
                    foreach (var trs in transforms.Values)
                    {
                        var actor = actors[Actor(trs)].transform;
                        if (actor == root.transform) continue; // World event supplies position and facing.
                        if (Actor(trs) == nifRoot)
                        {
                            // The ES controller instantiates this separate source-prefab root at the event.
                            actor.SetParent(root.transform, false);
                            continue;
                        }
                        var parent = transforms[(long)trs["m_Father"]["pathId"]];
                        actor.SetParent(actors[Actor(parent)].transform, false);
                        actor.localPosition = Vec(trs["m_LocalPosition"]);
                        actor.localScale = Vec(trs["m_LocalScale"]);
                        var r = trs["m_LocalRotation"];
                        actor.localRotation = new Quaternion((float)r["x"], (float)r["y"], (float)r["z"], (float)r["w"]);
                    }
                }
                if (id == 845) BindTransformAnimation(objects, actors);
                float lastParticle = 0;
                foreach (var emitter in objects.Where(o => (string)o["objectType"] == "EllipsoidParticleEmitter").Select(o => o["value"]))
                {
                    long actor = Actor(emitter);
                    JToken Component(string type) => objects.Single(o => (string)o["objectType"] == type && Actor(o["value"]) == actor)["value"];
                    var renderer = Component("ParticleRenderer");
                    var material = NativeEffectPublication.ParticleMaterial(objects, renderer, true, true, false, 1);
                    BuildEmitter(actors[actor], emitter, Component("ParticleAnimator"), renderer, material);
                    lastParticle = Mathf.Max(lastParticle, (float)emitter["maxEnergy"]);
                }
                string prefabPath = $"{Folder}/ES{id}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (!prefab) throw new InvalidDataException("Unable to save " + prefabPath);
                var definition = NativeEffectPublication.PublishNanoEffect(id, closure, prefab);
                // Original emitters autodestruct when the last one-shot particle dies.
                definition.MaximumTimer = Mathf.Max((float)controller["maxTimer"], lastParticle);
                definition.LongestLifetime = 0;
                EditorUtility.SetDirty(definition);
                return definition;
            }
            finally
            {
                foreach (var actor in actors.Values)
                    if (actor) UnityEngine.Object.DestroyImmediate(actor);
            }
        }

        static Vector3 Vec(JToken v) => new((float)v["x"], (float)v["y"], (float)v["z"]);
        static ParticleSystem.MinMaxCurve Range(JToken source, string minimum, string maximum) =>
            new((float)source[minimum], (float)source[maximum]);
        static ParticleSystem.MinMaxCurve Spread(float center, float width) =>
            new(center - Mathf.Abs(width), center + Mathf.Abs(width));

        static void BuildEmitter(GameObject owner, JToken e, JToken a, JToken r, Material material)
        {
            var system = owner.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.duration = (float)e["maxEnergy"];
            main.playOnAwake = (bool)e["m_Emit"] && (bool)e["m_Enabled"];
            main.startLifetime = Range(e, "minEnergy", "maxEnergy");
            main.startSize = Range(e, "minSize", "maxSize");
            main.startSpeed = 0;
            main.simulationSpace = (bool)e["Simulate in Worldspace?"] ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1;
            shape.scale = Vec(e["m_Ellipsoid"]); shape.radiusThickness = 1 - (float)e["m_MinEmitterRange"];
            var emission = system.emission;
            emission.enabled = true; emission.rateOverTime = 0;
            if (!(bool)e["m_OneShot"]) throw new InvalidDataException("Unexpected looping raw Nano emitter.");
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)e["minEmission"], (short)e["maxEmission"]) });

            var velocity = system.velocityOverLifetime;
            Vector3 random = Vec(e["rndVelocity"]), local = Vec(e["localVelocity"]), orbit = Vec(e["tangentVelocity"]);
            velocity.enabled = local != Vector3.zero || random != Vector3.zero || orbit != Vector3.zero;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = Spread(local.x,random.x); velocity.y = Spread(local.y,random.y); velocity.z = Spread(local.z,random.z);
            velocity.orbitalX = orbit.x; velocity.orbitalY = orbit.y; velocity.orbitalZ = orbit.z;
            var inherit = system.inheritVelocity;
            inherit.enabled = main.simulationSpace == ParticleSystemSimulationSpace.World;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial; inherit.curve = (float)e["emitterVelocityScale"];
            var force = system.forceOverLifetime;
            var fixedForce = Vec(a["force"]); var variance = Vec(a["rndForce"]);
            force.enabled = fixedForce != Vector3.zero || variance != Vector3.zero;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = Spread(fixedForce.x,variance.x); force.y = Spread(fixedForce.y,variance.y); force.z = Spread(fixedForce.z,variance.z);
            float damping = (float)a["damping"];
            if (damping > 1) { var drag = system.limitVelocityOverLifetime; drag.enabled = true; drag.drag = Mathf.Log(damping); }
            float growth = (float)a["sizeGrow"];
            if (growth != 0)
            {
                var size = system.sizeOverLifetime; size.enabled = true;
                float meanSize = ((float)e["minSize"] + (float)e["maxSize"]) * .5f;
                size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 1 + growth * main.duration / meanSize));
            }
            if ((bool)a["Does Animate Color?"])
            {
                var color = system.colorOverLifetime; color.enabled = true;
                var colors = new GradientColorKey[5]; var alphas = new GradientAlphaKey[5];
                for (int i = 0; i < 5; i++)
                {
                    uint rgba = (uint)a[$"colorAnimation[{i}]"]["rgba"];
                    Color c = new Color32((byte)rgba,(byte)(rgba>>8),(byte)(rgba>>16),(byte)(rgba>>24));
                    colors[i] = new GradientColorKey(c, i * .25f); alphas[i] = new GradientAlphaKey(c.a, i * .25f);
                }
                var gradient = new Gradient(); gradient.SetKeys(colors, alphas); color.color = gradient;
            }
            var uv = r["UV Animation"];
            var atlas = system.textureSheetAnimation;
            atlas.numTilesX = (int)uv["x Tile"]; atlas.numTilesY = (int)uv["y Tile"];
            atlas.enabled = atlas.numTilesX * atlas.numTilesY > 1;
            float cycles = (float)uv["cycles"];
            if (atlas.enabled)
            {
                // Unity 6's cycleCount is integral. Bake the final partial cycle into the frame curve.
                var frame = new AnimationCurve();
                for (int i = 0; i < cycles; i++)
                {
                    float from = i / cycles, to = Mathf.Min(1, (i + 1) / cycles);
                    frame.AddKey(new Keyframe(from, 0, cycles, cycles));
                    frame.AddKey(new Keyframe(to == 1 ? to : to - .00001f, Mathf.Min(.99999f, (to - from) * cycles), cycles, cycles));
                }
                atlas.cycleCount = 1;
                atlas.frameOverTime = new ParticleSystem.MinMaxCurve(1, frame);
            }
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material; renderer.enabled = (bool)r["m_Enabled"];
        }

        static void BindTransformAnimation(JArray objects, Dictionary<long, GameObject> actors)
        {
            var animation = objects.Single(o => (string)o["objectType"] == "Animation")["value"];
            var source = objects.Single(o => (long)o["pathId"] == (long)animation["m_Animation"]["pathId"])["value"];
            var clip = new AnimationClip { name = (string)source["m_Name"], legacy = true };
            foreach (var (table, property) in new[] { ("m_PositionCurves", "localPosition"), ("m_ScaleCurves", "localScale") })
                foreach (var binding in source[table])
                    foreach (string axis in new[] { "x", "y", "z" })
                    {
                        var keys = binding["curve"]["m_Curve"].Select(k => new Keyframe((float)k["time"], (float)k["value"][axis],
                            (float)k["inSlope"][axis], (float)k["outSlope"][axis])).ToArray();
                        clip.SetCurve((string)binding["path"], typeof(Transform), property + "." + axis, new AnimationCurve(keys));
                    }
            string path = Folder + "/ES845-motion.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip); clip = existing;
            }
            else AssetDatabase.CreateAsset(clip, path);
            var player = actors[(long)animation["m_GameObject"]["pathId"]].AddComponent<Animation>();
            player.AddClip(clip, clip.name); player.clip = clip; player.playAutomatically = true;
        }
    }
}
