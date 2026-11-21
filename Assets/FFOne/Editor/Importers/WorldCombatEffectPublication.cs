using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Editor
{
    public static class WorldCombatEffectPublication
    {
        const string Root = "Assets/FFOne/Content/VFX";
        const string Folder = Root + "/Published/CombatResults";
        static readonly string[] Sources = { "skill-hits", "shinies", "npc-skills" };

        [MenuItem("FFOne/Publish/Combat and E.G.G. effects")]
        public static void Publish()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Root + "/Published", "CombatResults");
            var effects = new List<NativeEffectDefinition>();
            foreach (string group in Sources)
            {
                string source = Root + "/Source/" + group + "/catalog.json";
                var catalog = JObject.Parse(File.ReadAllText(source));
                foreach (var row in catalog["effects"])
                {
                    int id = (int)row["id"];
                    // Reuse the already published canonical definition when present.
                    var effect = AssetDatabase.LoadAssetAtPath<NativeEffectDefinition>($"{Root}/Published/Effects/ES{id}.asset");
                    if (!effect)
                    {
                        effect = Asset<NativeEffectDefinition>($"{Folder}/ES{id}.asset");
                        effect.Id = id;
                        effect.Identity = $"effects/{group}/catalog#{id}";
                        effect.SourceHash = AssetDatabase.GetAssetDependencyHash(source).ToString();
                        effect.MaximumTimer = (float)row["maximum_timer"];
                        effect.LongestLifetime = (float)row["longest_lifetime"];
                        effect.DisableUpdate = (bool)row["disable_update"];
                        effect.ModelPath = (string)row["mesh"];
                        if (!string.IsNullOrEmpty(effect.ModelPath))
                        {
                            string model = effect.ModelPath.Substring("effects/".Length);
                            string prefab = Root + "/Prefabs/" + Path.ChangeExtension(model, null) + "/model.prefab";
                            effect.Model = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                            if (!effect.Model) throw new FileNotFoundException("Missing native effect model", prefab);
                        }
                        effect.Emitters = row["emitters"].Select((e, i) => Emitter(e, effect.Identity, i)).ToArray();
                        var animation = row["material_animation"]?["curves"];
                        if (animation != null)
                            effect.FloatCurves = animation.Select(c => new NativeEffectFloatCurve { Path = (string)c["node_name"],
                                Attribute = MaterialProperty((string)c["property"]), Curve = Curve(c["keys"]) }).ToArray();
                        EditorUtility.SetDirty(effect);
                    }
                    effects.Add(effect);
                }
            }
            var index = Asset<WorldNanoEffectLibrary>(Root + "/Resources/WorldCombatEffects.asset");
            index.Effects = effects.OrderBy(e => e.Id).ToArray();
            EditorUtility.SetDirty(index);
            const string decalPath = Root + "/Resources/EruptionGround.mat";
            var decal = AssetDatabase.LoadAssetAtPath<Material>(decalPath);
            if (!decal)
            {
                decal = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/FFOne/Worlds/Overworld/Settings/Particle.shader"));
                AssetDatabase.CreateAsset(decal, decalPath);
            }
            decal.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Source/npc-skills/eruption-mark.png");
            if (!decal.mainTexture) throw new FileNotFoundException("Missing eruption ground art");
            decal.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); decal.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            decal.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f)); decal.renderQueue = 3001;
            EditorUtility.SetDirty(decal);
            AssetDatabase.SaveAssets();
            Debug.Log($"Published {index.Effects.Length} typed combat effect definitions.");
        }

        static NativeParticleSettings Emitter(JToken e, string identity, int index)
        {
            return new NativeParticleSettings {
                Id = identity + "/" + index, Name = (string)e["name"], Generation = (int)e["generation"],
                RenderMode = (int)e["render_mode"], XTiles = (int)e["uv_tiles"][0], YTiles = (int)e["uv_tiles"][1],
                Lifetime = (float)e["lifetime"], InitialSize = (float)e["initial_size"], Rate = (float)e["generations_per_second"],
                Count = (float)e["number_per_generation"], Cycles = (float)e["uv_cycles"], RandomPosition = (float)e["random_position"],
                RandomAngle = (float)e["random_angle"], RandomVelocity = (float)e["random_velocity"],
                Plane = Vector(e["generator_plane"]), InitialVelocity = Vector(e["generator_velocity"]), Force = Vector(e["force"]),
                InitialEmit = (bool?)e["script_keys"].First?["emit"] ?? true, AnimateColor = (bool)e["animate_color"],
                Width = Curve(e["width_curve"]), Height = Curve(e["height_curve"]), Rotation = Curve(e["rotation_curve"]),
                Colors = e["colors"].Select(Tint).ToArray(),
                Keys = e["script_keys"].Select(k => new NativeEffectKey { Time = (float)k["time"], Emit = (bool)k["emit"],
                    Translation = Vector(k["translation"]) }).ToArray(), WorldMaterial = Material(e)
            };
        }
        static Material Material(JToken e)
        {
            string texturePath = (string)e["texture"].First;
            string mode = (string)e["blend_mode"];
            Color tint = Tint(e["material_tint"]);
            string stem = Path.GetFileNameWithoutExtension(texturePath);
            string key = Hash128.Compute(texturePath + mode + e["material_tint"].ToString(Newtonsoft.Json.Formatting.None)).ToString();
            string path = $"{Folder}/{stem}-{key.Substring(0, 12)}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/FFOne/Worlds/Overworld/Settings/Particle.shader");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Source/" + texturePath.Substring("effects/".Length));
            if (!material.mainTexture) throw new FileNotFoundException("Missing combat effect texture", texturePath);
            var blend = mode switch {
                "SrcAlphaOne" => (BlendMode.SrcAlpha, BlendMode.One), "OneOne" => (BlendMode.One, BlendMode.One),
                "SrcColorOne" => (BlendMode.SrcColor, BlendMode.One),
                "OneMinusSrcAlphaOne" => (BlendMode.OneMinusSrcAlpha, BlendMode.One),
                "OneMinusDstColorOne" => (BlendMode.OneMinusDstColor, BlendMode.One),
                "SrcAlphaOneMinusSrcAlpha" => (BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha),
                "OneOneMinusSrcAlpha" => (BlendMode.One, BlendMode.OneMinusSrcAlpha),
                _ => throw new InvalidDataException("Unknown authored effect blend " + mode)
            };
            material.SetFloat("_SrcBlend", (float)blend.Item1); material.SetFloat("_DstBlend", (float)blend.Item2);
            material.SetColor("_TintColor", tint); material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }
        static T Asset<T>(string path) where T : ScriptableObject
        {
            var value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value) return value;
            value = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(value, path); return value;
        }
        static string MaterialProperty(string key) => key switch {
            "UvOffsetX" => "_MainTex.offset.x", "UvOffsetY" => "_MainTex.offset.y", "UvRotationDegrees" => "_MainTex.rotation",
            "EmissionRed" => "_Emission.r", "EmissionGreen" => "_Emission.g", "EmissionBlue" => "_Emission.b",
            "AmbientRed" => "_Ambient.r", "AmbientGreen" => "_Ambient.g", "AmbientBlue" => "_Ambient.b",
            "BaseRed" => "_Color.r", "BaseGreen" => "_Color.g", "BaseBlue" => "_Color.b",
            _ => throw new InvalidDataException("Unknown effect material property " + key)
        };
        static Vector3 Vector(JToken v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
        static Color Tint(JToken c) => new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]);
        static AnimationCurve Curve(JToken keys) => new AnimationCurve(keys.Select(k => new Keyframe((float)k[0], (float)k[1], (float)k[2], (float)k[3])).ToArray())
            { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }
}
