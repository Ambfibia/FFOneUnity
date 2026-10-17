using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.World
{
    internal static class WorldBehaviourEffectAssets
    {
        internal sealed class Plan
        {
            internal string Id, Name, MaterialIdentity;
            internal int References, Generation, RenderMode, XTiles, YTiles;
            internal float Lifetime, InitialSize, RandomPosition, RandomAngle, RandomVelocity, Rate, Count, Cycles;
            internal Vector3 Plane, InitialVelocity, Force;
            internal bool InitialEmit, AnimateColor, RendererEnabled;
            internal AnimationCurve Width, Height, Rotation;
            internal Color[] Colors;
            internal Material Material;
            internal bool Persistent => Lifetime >= 300 && Rate <= 1f / 300 && Count > 0 && Count <= 1;
        }
        sealed class SharedMaterial { internal Material Value; internal string Texture; internal int References; }
        sealed class SharedTexture { internal Texture2D Value; internal int References; }
        static readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>();
        static readonly Dictionary<string, SharedMaterial> materials = new Dictionary<string, SharedMaterial>();
        static readonly Dictionary<string, SharedTexture> textures = new Dictionary<string, SharedTexture>();
        internal static int PlanCount => plans.Count;
        internal static int MaterialCount => materials.Count;
        internal static int TextureCount => textures.Count;
        internal static Plan Acquire(JToken closure)
        {
            string id = (string)closure["id"];
            if (plans.TryGetValue(id, out var shared)) { shared.References++; return shared; }
            var objects = (JArray)closure["objects"];
            long actor = (long)closure["root"]["pathId"];
            JToken Component(string type) => objects.SingleOrDefault(o => (string)o["objectType"] == type
                && (long?)o["value"]?["m_GameObject"]?["pathId"] == actor)?["value"];
            var settings = objects.Where(o => (string)o["objectType"] == "MonoBehaviour" && o["value"]?["numberPerGeneration"] != null)
                .Select(o => o["value"]).SingleOrDefault(s => (long?)s["m_GameObject"]?["pathId"] == actor)
                ?? throw new InvalidDataException("No ParticleEmitterController in " + id);
            var renderer = Component("ParticleRenderer") ?? throw new InvalidDataException("No particle renderer in " + id);
            var animator = Component("ParticleAnimator") ?? throw new InvalidDataException("No particle animator in " + id);
            int generation = (int)settings["m_iGenType"], mode = (int)renderer["m_StretchParticles"];
            if (generation < 0 || generation > 11) throw new InvalidDataException("Unsupported particle generator " + generation + " in " + id);
            if (mode != 0 && mode != 4 && mode != 5) throw new InvalidDataException("Unsupported particle render mode " + mode + " in " + id);
            if ((float?)animator["damping"] != 1 || (float?)animator["sizeGrow"] != 0 || (bool?)animator["stopSimulation"] == true
                || WorldBehaviour.Vector(animator["rndForce"]) != Vector3.zero || WorldBehaviour.Vector(animator["localRotationAxis"]) != Vector3.zero
                || WorldBehaviour.Vector(animator["worldRotationAxis"]) != Vector3.zero)
                throw new InvalidDataException("Unsupported non-default ParticleAnimator in " + id);
            if ((settings["textureKeys"] as JArray)?.Count > 0) throw new InvalidDataException("Unsupported particle textureKeys in " + id);
            var plan = new Plan {
                Id = id, References = 1, Name = (string)settings["particleName"], Generation = generation, RenderMode = mode,
                Lifetime = (float)settings["lifeTime"], InitialSize = (float)settings["initialSize"], RandomPosition = (float)settings["randomPosition"],
                RandomAngle = (float)settings["randomAngle"], RandomVelocity = (float)settings["randomVelocity"], Rate = (float)settings["generationsPerSecond"],
                Count = (float)settings["numberPerGeneration"], Plane = WorldBehaviour.Vector(settings["plane"]), InitialVelocity = WorldBehaviour.Vector(settings["initVelocity"]),
                Force = WorldBehaviour.Vector(animator["force"]), InitialEmit = (int?)settings["emit"] != 0, RendererEnabled = (bool?)renderer["m_Enabled"] ?? true,
                Width = Curve(renderer["m_WidthCurve"]), Height = Curve(renderer["m_HeightCurve"]), Rotation = Curve(renderer["m_RotationCurve"]),
                XTiles = Math.Max(1, (int)renderer["UV Animation"]["x Tile"]), YTiles = Math.Max(1, (int)renderer["UV Animation"]["y Tile"]),
                Cycles = (float)renderer["UV Animation"]["cycles"], AnimateColor = (bool)animator["Does Animate Color?"], Colors = new Color[5]
            };
            if (plan.Lifetime <= 0 || plan.Rate < 0 || plan.Count < 0) throw new InvalidDataException("Invalid particle timing in " + id);
            for (int i = 0; i < 5; i++)
            { uint packed = (uint)animator["colorAnimation["+i+"]"]["rgba"]; plan.Colors[i] = new Color((packed&255)/255f, ((packed>>8)&255)/255f, ((packed>>16)&255)/255f, (packed>>24)/255f); }
            long materialId = (long)renderer["m_Materials"][0]["pathId"];
            var material = objects.Single(o => (string)o["objectType"] == "Material" && (long)o["pathId"] == materialId);
            plan.MaterialIdentity = Identity(material);
            plan.Material = AcquireMaterial(material, objects); plans.Add(id, plan); return plan;
        }
        static string Identity(JToken item) => (string)item["canonicalBlake3"] ?? (string)item["asset"] + "#" + (string)item["pathId"];
        static Material AcquireMaterial(JToken source, JArray objects)
        {
            string key = Identity(source);
            if (materials.TryGetValue(key, out var existing)) { existing.References++; return existing.Value; }
            var definition = source["value"];
            JToken Object(long path, string type) => objects.Single(o => (long)o["pathId"] == path && (string)o["objectType"] == type);
            var properties = definition["m_SavedProperties"];
            var texEnv = properties["m_TexEnvs"].Single(t => (string)t[0]["name"] == "_MainTex")[1];
            var textureSource = Object((long)texEnv["m_Texture"]["pathId"], "Texture2D");
            string textureKey = Identity(textureSource);
            string script = (string)Object((long)definition["m_Shader"]["pathId"], "Shader")["value"]["m_Script"];
            var blend = Regex.Match(script, @"Blend\s+(\w+)\s+(\w+)", RegexOptions.IgnoreCase);
            if (!blend.Success) throw new InvalidDataException("Missing authored particle blend in " + key);
            var shader = FFOne.Content.ContentAssets.Load<Shader>("World/Particle");
            if (!shader) throw new InvalidDataException("Missing World/Particle shader");
            var material = new Material(shader) { name = (string)definition["m_Name"] };
            try
            {
                material.SetFloat("_SrcBlend", (float)Blend(blend.Groups[1].Value)); material.SetFloat("_DstBlend", (float)Blend(blend.Groups[2].Value));
                material.SetFloat("_ZWrite", Regex.IsMatch(script, @"ZWrite\s+Off", RegexOptions.IgnoreCase) ? 0 : 1);
                material.SetFloat("_Cull", Regex.IsMatch(script, @"Cull\s+Off", RegexOptions.IgnoreCase) ? 0 : Regex.IsMatch(script, @"Cull\s+Front", RegexOptions.IgnoreCase) ? 1 : 2);
                var tint = properties["m_Colors"].FirstOrDefault(c => (string)c[0]["name"] == "_TintColor")?[1];
                material.SetColor("_TintColor", tint == null ? Color.white : new Color((float)tint["r"], (float)tint["g"], (float)tint["b"], (float)tint["a"]));
                var queue = Regex.Match(script, @"\""Queue\""\s*=\s*\""(Transparent|Geometry)([+-]\d+)?", RegexOptions.IgnoreCase);
                material.renderQueue = queue.Success ? (queue.Groups[1].Value.Equals("Geometry", StringComparison.OrdinalIgnoreCase) ? 2000 : 3000)
                    + (queue.Groups[2].Success ? int.Parse(queue.Groups[2].Value) : 0) : 3000;
                material.mainTexture = AcquireTexture(textureSource);
                // Raw authored closure textures use Unity rows, unlike the published static-world PNGs.
                material.mainTextureOffset = new Vector2((float?)texEnv["m_Offset"]?["x"] ?? 0, (float?)texEnv["m_Offset"]?["y"] ?? 0);
                material.mainTextureScale = new Vector2((float?)texEnv["m_Scale"]?["x"] ?? 1, (float?)texEnv["m_Scale"]?["y"] ?? 1);
                materials.Add(key, new SharedMaterial { Value = material, Texture = textureKey, References = 1 }); return material;
            }
            catch { WorldBehaviour.Release(material); throw; }
        }
        static Texture2D AcquireTexture(JToken source)
        {
            string key = Identity(source);
            if (textures.TryGetValue(key, out var existing)) { existing.References++; return existing.Value; }
            var definition = source["value"]; int format = (int)definition["m_TextureFormat"];
            var texture = new Texture2D((int)definition["m_Width"], (int)definition["m_Height"], format switch {
                10 => TextureFormat.DXT1, 12 => TextureFormat.DXT5, 2 => TextureFormat.ARGB4444, 3 => TextureFormat.RGB24, 4 => TextureFormat.RGBA32,
                _ => throw new InvalidDataException("Unsupported world particle texture format " + format)
            }, (bool)definition["m_MipMap"], false) { name = (string)definition["m_Name"], filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            if (definition["m_TextureSettings"] is JObject settings)
            {
                int filter = (int?)settings["m_FilterMode"] ?? 1, wrap = (int?)settings["m_WrapMode"] ?? 0;
                if (filter >= 0 && filter <= 2) texture.filterMode = (FilterMode)filter;
                if (wrap >= 0 && wrap <= 3) texture.wrapMode = (TextureWrapMode)wrap;
                texture.anisoLevel = Mathf.Clamp((int?)settings["m_Aniso"] ?? 1, 0, 16);
            }
            try
            {
                texture.LoadRawTextureData(Convert.FromBase64String((string)definition["image data"]["base64"])); texture.Apply(false, true);
                textures.Add(key, new SharedTexture { Value = texture, References = 1 }); return texture;
            }
            catch { WorldBehaviour.Release(texture); throw; }
        }
        static BlendMode Blend(string value) => value.ToLowerInvariant() switch {
            "zero" => BlendMode.Zero, "one" => BlendMode.One, "srccolor" => BlendMode.SrcColor, "srcalpha" => BlendMode.SrcAlpha,
            "dstcolor" => BlendMode.DstColor, "dstalpha" => BlendMode.DstAlpha, "oneminussrcalpha" => BlendMode.OneMinusSrcAlpha,
            "oneminussrccolor" => BlendMode.OneMinusSrcColor, "oneminusdstcolor" => BlendMode.OneMinusDstColor, "oneminusdstalpha" => BlendMode.OneMinusDstAlpha,
            _ => throw new InvalidDataException("Unsupported world particle blend " + value)
        };
        static AnimationCurve Curve(JToken definition)
        {
            var keys = definition?["m_Curve"] as JArray ?? new JArray();
            return new AnimationCurve(keys.Select(k => new Keyframe((float)k["time"], (float)k["value"], (float)k["inSlope"], (float)k["outSlope"])).ToArray());
        }
        internal static void Release(Plan plan)
        {
            if (--plan.References > 0) return;
            plans.Remove(plan.Id);
            var material = materials[plan.MaterialIdentity]; if (--material.References > 0) return;
            materials.Remove(plan.MaterialIdentity); WorldBehaviour.Release(material.Value);
            var texture = textures[material.Texture]; if (--texture.References > 0) return;
            textures.Remove(material.Texture); WorldBehaviour.Release(texture.Value);
        }
    }
}
