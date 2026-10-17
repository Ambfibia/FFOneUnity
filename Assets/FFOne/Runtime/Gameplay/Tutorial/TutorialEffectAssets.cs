using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FFOne.Tutorial
{
    // Reads the accepted editable native closure, never a legacy Unity bundle.
    internal sealed class TutorialEffectAssets : IDisposable
    {
        internal int Id;
        internal GameObject Model;
        internal JObject Controller;
        internal JArray Objects;
        internal readonly List<TutorialParticlePlan> Emitters = new List<TutorialParticlePlan>();
        internal readonly List<JToken> FloatCurves = new List<JToken>();
        internal Material TrailMaterial;
        internal int TrailLength;
        internal float TrailHeight, TrailStep;
        bool worldShader;
        readonly List<Object> generated = new List<Object>();
        internal float MaximumTimer => (float?)Controller?["maxTimer"] ?? -1;
        internal bool DisableUpdate => ((int?)Controller?["disableUpdate"] ?? 1) != 0;
        internal float LongestLifetime => (float?)Controller?["longestLifeTime"] ?? 0;

        internal static TutorialEffectAssets Load(int id, string modelPath, bool projectile = false, bool worldShader = false)
        {
            string route = (projectile || id == 15 || id == 391 ? "map/shared/projectiles/effects/es" : "map/shared/effects/es") + id + ".closure";
            var data = FFOne.Content.NativeData.Object(route);
            var result = new TutorialEffectAssets { Id = id, Objects = (JArray)data["objects"], worldShader = worldShader };
            try
            {
                if (modelPath != null)
                    result.Model = FFOne.Content.NativeModelAssets.Load(modelPath) ?? throw new InvalidDataException("Missing tutorial effect mesh " + modelPath);
                result.Controller = result.Objects.Where(o => (string)o["objectType"] == "MonoBehaviour")
                    .Select(o => (JObject)o["value"]).FirstOrDefault(v => v["particles"] != null && v["maxTimer"] != null);
                foreach (var clip in result.Objects.Where(o => (string)o["objectType"] == "AnimationClip"))
                    if (clip["value"]?["m_FloatCurves"] is JArray curves) result.FloatCurves.AddRange(curves);
                foreach (var source in result.Objects.Where(o => (string)o["objectType"] == "MonoBehaviour" && o["value"]?["numberPerGeneration"] != null))
                {
                    var settings = (JObject)source["value"];
                    long actor = (long)settings["m_GameObject"]["pathId"];
                    if ((int)settings["m_iGenType"] == 12)
                    {
                        var trail = result.Objects.Select(o => o["value"]).Single(v => v["trailLength"] != null && (long?)v["m_GameObject"]?["pathId"] == actor);
                        result.TrailLength = Mathf.CeilToInt((float)settings["numberPerGeneration"]);
                        result.TrailHeight = (float)settings["initialSize"];
                        result.TrailStep = (float)trail["fTimes"];
                        result.TrailMaterial = result.ParticleMaterial(result.Component(actor, "MeshRenderer"));
                        continue;
                    }
                    if (result.Controller == null) throw new InvalidDataException("Effect controller missing for ES" + id);
                    var renderer = result.Component(actor, "ParticleRenderer");
                    var animator = result.Component(actor, "ParticleAnimator");
                    var keys = result.Controller["particles"].Single(p => (long)p["particlePrefab"]["pathId"] == actor)["scriptKeys"];
                    result.Emitters.Add(new TutorialParticlePlan {
                        Settings = settings, Keys = (JArray)keys, Animator = animator,
                        Width = Curve(renderer["m_WidthCurve"]), Height = Curve(renderer["m_HeightCurve"]),
                        Rotation = Curve(renderer["m_RotationCurve"]), RenderMode = (int)renderer["m_StretchParticles"],
                        XTiles = (int)renderer["UV Animation"]["x Tile"], YTiles = (int)renderer["UV Animation"]["y Tile"],
                        Cycles = (float)renderer["UV Animation"]["cycles"], Material = result.ParticleMaterial(renderer)
                    });
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        JObject Component(long actor, string type) => (JObject)Objects.Single(o => (string)o["objectType"] == type && (long?)o["value"]?["m_GameObject"]?["pathId"] == actor)["value"];
        JObject Value(long path, string type) => (JObject)Objects.Single(o => (long)o["pathId"] == path && (string)o["objectType"] == type)["value"];
        Material ParticleMaterial(JObject renderer)
        {
            var material = Value((long)renderer["m_Materials"][0]["pathId"], "Material");
            var properties = material["m_SavedProperties"];
            var environment = properties["m_TexEnvs"].Single(e => (string)e[0]["name"] == "_MainTex")[1];
            var texture = Value((long)environment["m_Texture"]["pathId"], "Texture2D");
            var shader = Value((long)material["m_Shader"]["pathId"], "Shader");
            string script = (string)shader["m_Script"];
            var sourceBlend = Regex.Match(script, worldShader ? @"Blend\s+(\w+)\s+(\w+)" : @"Blend\s+(One|SrcColor|SrcAlpha)\s+One\b");
            if (!sourceBlend.Success)
                throw new InvalidDataException("Unsupported authored tutorial particle shader.");
            int format = (int)texture["m_TextureFormat"];
            var output = new Texture2D((int)texture["m_Width"], (int)texture["m_Height"], format switch {
                10 => TextureFormat.DXT1, 12 => TextureFormat.DXT5,
                2 => TextureFormat.ARGB4444, 3 => TextureFormat.RGB24, 4 => TextureFormat.RGBA32,
                _ => throw new InvalidDataException("Unsupported tutorial texture format " + format)
            }, (bool)texture["m_MipMap"], false) { name = (string)texture["m_Name"], filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            generated.Add(output);
            output.LoadRawTextureData(Convert.FromBase64String((string)texture["image data"]["base64"]));
            output.Apply(false, true);
            var particleShader = FFOne.Content.ContentAssets.Load<Shader>(worldShader ? "World/Particle" : "Tutorial/Particle");
            if (!particleShader) throw new InvalidDataException("Missing FFOne tutorial particle shader.");
            var result = new Material(particleShader) { name = (string)material["m_Name"], mainTexture = output };
            generated.Add(result);
            var tint = properties["m_Colors"].FirstOrDefault(c => (string)c[0]["name"] == "_TintColor")?[1];
            result.SetColor("_TintColor", tint == null ? Color.white : new Color((float)tint["r"], (float)tint["g"], (float)tint["b"], (float)tint["a"]));
            result.SetFloat("_SrcBlend", (float)Blend(sourceBlend.Groups[1].Value));
            if (worldShader)
            {
                result.SetFloat("_DstBlend", (float)Blend(sourceBlend.Groups[2].Value));
                result.SetFloat("_ZWrite", Regex.IsMatch(script, @"ZWrite\s+Off", RegexOptions.IgnoreCase) ? 0 : 1);
                result.SetFloat("_Cull", Regex.IsMatch(script, @"Cull\s+Off", RegexOptions.IgnoreCase) ? 0 : Regex.IsMatch(script, @"Cull\s+Front", RegexOptions.IgnoreCase) ? 1 : 2);
            }
            result.SetFloat("_LegacyAccumulationGain", Id == 668 ? 2 : 1);
            var queue = Regex.Match(script, "Transparent\\+(\\d+)");
            result.renderQueue = 3000 + (queue.Success ? int.Parse(queue.Groups[1].Value) : 0);
            return result;
        }

        internal void ConfigureMeshMaterial(Material material)
        {
            string name = material.name.Replace(" (Instance)", "");
            var definition = Objects.FirstOrDefault(o => (string)o["objectType"] == "Material" && (string)o["name"] == name)?["value"];
            if (definition == null) return;
            string script = (string)Value((long)definition["m_Shader"]["pathId"], "Shader")["m_Script"];
            var blend = Regex.Match(script, @"Blend\s+(\w+)\s+(\w+)");
            if (blend.Success)
            {
                material.SetFloat("_SrcBlend", (float)Blend(blend.Groups[1].Value));
                material.SetFloat("_DstBlend", (float)Blend(blend.Groups[2].Value));
            }
            material.SetFloat("_ZWrite", Regex.IsMatch(script, @"ZWrite\s+Off", RegexOptions.IgnoreCase) ? 0 : 1);
            material.SetFloat("_Cull", Regex.IsMatch(script, @"Cull\s+Off", RegexOptions.IgnoreCase) ? 0 : 2);
            foreach (var texture in definition["m_SavedProperties"]?["m_TexEnvs"] ?? new JArray())
            {
                if ((string)texture[0]["name"] != "_MainTex") continue;
                var value = texture[1];
                material.SetVector("_MainTexPivot", new Vector4((float)value["m_Pivot"]["x"], (float)value["m_Pivot"]["y"], 0, 0));
                material.SetFloat("_MainTexRotation", (float)value["m_Rotation"]);
            }
        }
        static BlendMode Blend(string name) => name.ToLowerInvariant() switch {
            "zero" => BlendMode.Zero, "one" => BlendMode.One, "srccolor" => BlendMode.SrcColor,
            "srcalpha" => BlendMode.SrcAlpha, "dstcolor" => BlendMode.DstColor, "dstalpha" => BlendMode.DstAlpha,
            "oneminussrcalpha" => BlendMode.OneMinusSrcAlpha, "oneminussrccolor" => BlendMode.OneMinusSrcColor,
            "oneminusdstcolor" => BlendMode.OneMinusDstColor, "oneminusdstalpha" => BlendMode.OneMinusDstAlpha,
            _ => throw new InvalidDataException("Unsupported effect blend factor " + name)
        };

        internal static Vector3 Vector(JToken value)
        {
            if (value == null) return Vector3.zero;
            return value is JArray ? new Vector3((float)value[0], (float)value[1], (float)value[2])
                : new Vector3((float)value["x"], (float)value["y"], (float)value["z"]);
        }
        internal static AnimationCurve Curve(JToken definition)
        {
            var keys = definition?["m_Curve"] as JArray;
            if (keys == null) return new AnimationCurve();
            var result = new AnimationCurve(keys.Select(k => new Keyframe((float)k["time"], (float)k["value"], (float)k["inSlope"], (float)k["outSlope"])).ToArray());
            result.preWrapMode = result.postWrapMode = WrapMode.ClampForever;
            return result;
        }
        internal static void Release(Object item) { if (!item) return; if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item); }
        public void Dispose() { foreach (var item in generated) Release(item); generated.Clear(); }
    }

    internal sealed class TutorialParticlePlan
    {
        internal JObject Settings, Animator;
        internal JArray Keys;
        internal AnimationCurve Width, Height, Rotation;
        internal int RenderMode, XTiles, YTiles;
        internal float Cycles;
        internal Material Material;
    }
}
