using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Editor
{
    // Accepted static-world GLBs use the original material metadata at extras,
    // unlike animated NPC GLBs whose normalized metadata is under extras.ffone.
    public static class NativeWorldMaterialImport
    {
        public static void Apply(AssetImportContext context, JObject document, JToken definition, Material material)
        {
            var data = definition["extras"];
            // Toon characters share the fixed-function fog contract with world
            // props. Configure this before the world-only normalized path exits.
            if (material.HasProperty("_FixedFogMode"))
                material.SetFloat("_FixedFogMode", FixedFogMode(definition));
            if (data?["ffone"] != null)
            {
                ApplyNormalizedWorld(context, document, data["ffone"], material);
                return;
            }
            string shaderName = (string)data?["shaderName"];
            if (string.IsNullOrEmpty(shaderName)) return;
            var shader = Shader.Find(shaderName == "ffWater" ? "FFOne/NativeWater" : "FFOne/NativeWorld");
            if (!shader) throw new InvalidDataException("Missing accepted static-world shader: " + shaderName);
            material.shader = shader;
            var fallback = definition["pbrMetallicRoughness"]?["baseColorFactor"];
            Vector4 diffuse = fallback == null ? Vector4.one : new Vector4((float)fallback[0], (float)fallback[1], (float)fallback[2], (float)fallback[3]);
            foreach (var color in data["colors"] ?? new JArray())
                if ((string)color["name"] == "_Color") { var c = color["value"]; diffuse = new Vector4((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]); }
            if (material.HasProperty("_Color")) material.SetVector("_Color", diffuse);
            if (material.HasProperty("_AmbColor")) material.SetVector("_AmbColor", diffuse);
            if (material.HasProperty("_Emission")) material.SetVector("_Emission", new Vector4(diffuse.x * .8f, diffuse.y * .8f, diffuse.z * .8f, 1));
            foreach (var entry in data["colors"] ?? new JArray())
            {
                string name = (string)entry["name"];
                var c = entry["value"];
                if (material.HasProperty(name)) material.SetVector(name, new Vector4((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]));
            }
            foreach (var entry in data["floats"] ?? new JArray())
                if (material.HasProperty((string)entry["name"])) material.SetFloat((string)entry["name"], (float)entry["value"]);
            foreach (var slot in data["exactTextureSlots"] ?? new JArray())
            {
                string name = (string)slot["name"], identity = (string)slot["textureId"];
                if (identity == null || !material.HasProperty(name)) continue;
                var texture = document["textures"]?.SingleOrDefault(t => (string)t["name"] == identity);
                if (texture == null) throw new InvalidDataException("Static texture identity is absent: " + identity);
                string uri = (string)document["images"]![(int)texture["source"]!]["uri"];
                Bind(context, document, material, name, uri, texture);
                material.SetTextureScale(name, new Vector2((float)slot["scale"]!["x"], (float)slot["scale"]!["y"]));
                material.SetTextureOffset(name, new Vector2((float)slot["offset"]!["x"], (float)slot["offset"]!["y"]));
                if (name == "_MainTex")
                {
                    material.SetFloat("_MainTexRotation", (float?)slot["rotation"] ?? 0);
                    var pivot = slot["pivot"] as JObject;
                    material.SetVector("_MainTexPivot", new Vector4((float?)pivot?["x"] ?? 0, (float?)pivot?["y"] ?? 0, 0, 0));
                }
            }
            if (shaderName == "ffWater") { material.renderQueue = 3002; return; }
            bool cutout = shaderName.Contains("InvsrcalphaTest", StringComparison.Ordinal);
            bool depthOff = shaderName.Contains("zwriteOff", StringComparison.Ordinal);
            bool additiveOne = shaderName.Contains("blendOneOne", StringComparison.Ordinal);
            bool additiveAlpha = shaderName.Contains("blendSrcalphaOne", StringComparison.Ordinal);
            bool blended = shaderName.Contains("blend", StringComparison.Ordinal);
            bool cutoutDepthOnly = cutout && depthOff;
            bool glow = shaderName is "normal_glow_blendSrcalphaInvsrcalpha" or "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff"
                or "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff_vertexColorAD" or "normal_glow_blendOneOne_zwriteOff_vertexColorAD";
            // Most v1 vertex-color aliases map to their ordinary static plans.
            // This one glow additive plan retains family9 and supplies white when COLOR_0 is absent.
            material.SetFloat("_VertexColorAD", shaderName == "normal_glow_blendOneOne_zwriteOff_vertexColorAD" ? 1 : 0);
            material.SetFloat("_GlowMask", glow ? 1 : 0);
            material.SetFloat("_FixedFogMode", UsesFixedFog(shaderName) ? additiveOne || additiveAlpha ? 2 : 1 : 0);
            material.SetFloat("_Cull", shaderName.Contains("cullOff", StringComparison.Ordinal) ? 0 : 2);
            material.SetFloat("_ZWrite", depthOff ? 0 : 1);
            material.SetFloat("_SrcBlend", cutout || additiveOne || !blended ? (int)BlendMode.One : (int)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", cutout || !blended ? (int)BlendMode.Zero : additiveOne || additiveAlpha ? (int)BlendMode.One
                : shaderName.Contains("Invsrccolor", StringComparison.Ordinal) ? (int)BlendMode.OneMinusSrcColor : (int)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_DepthCutout", cutout && !depthOff ? 1 : 0);
            material.SetFloat("_AlphaTest", cutoutDepthOnly ? 1 : 0);
            material.SetFloat("_AmbientEnabled", additiveOne && !shaderName.Contains("Test", StringComparison.Ordinal) || shaderName is
                "RetroLit" or "normal_blendSrcalphaInvsrcalpha" or "normal_blendSrcalphaInvsrcalpha_vertexColorAD"
                or "normal_glow_blendSrcalphaInvsrcalpha" or "normal_blendSrcalphaInvsrccolor"
                or "normal_blendSrccolorInvsrcalpha_zwriteOff_cullOff" or "normal_blendSrcalphaOneTest_cullOff" ? 1 : 0);
            material.SetFloat("_ColorMask", blended && !cutout ? 14 : 15);
            material.renderQueue = cutout ? 2900 : !blended ? 2000 : additiveOne && depthOff ? 3011 : additiveAlpha && depthOff ? 3010 : 3000;
            // Preserve exact queue when the accepted shader text explicitly declares it.
            foreach (var tag in data["legacyRenderState"]?["shaderEvidence"]?["tags"] ?? new JArray())
            {
                var match = Regex.Match((string)tag["text"] ?? "", "Queue\\\"\\s*=\\s*\\\"(Transparent|Geometry)([+-]\\d+)?");
                if (match.Success) material.renderQueue = (match.Groups[1].Value == "Transparent" ? 3000 : 2000) + (match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0);
            }
            ApplyExactStaticExceptions(shaderName, material);
        }
        public static int FixedFogMode(JToken definition)
        {
            var data = definition["extras"];
            string declared = (string)data?["ffone"]?["declaredShaderName"] ?? (string)data?["shaderName"] ?? "";
            bool additive = (string)data?["ffone"]?["passes"]?.FirstOrDefault()?["blend"]?["destinationColor"] == "one";
            return FixedFogMode(declared, additive);
        }
        internal static int FixedFogMode(string declared, bool additive) => UsesFixedFog(declared) ? additive ? 2 : 1 : 0;
        internal static bool IsActorAdditiveMesh(string name) => name is
            "normal_blendOneOne_zwriteOff_cullOff" or "normal_blendSrcalphaOne" or
            "normal_blendSrcalphaOne_zwriteOff" or "normal_blendSrcalphaOne_zwriteOff_cullOff" or
            "normal_blendSrcalphaOne_zwriteOff_cullOff_vertexColorAD" or "normal_blendSrcalphaOneTest_cullOff";
        static void ApplyNormalizedWorld(AssetImportContext context, JObject document, JToken data, Material material)
        {
            string name = (string)data["declaredShaderName"] ?? "";
            if ((string)data["nativeSurfaceStyle"] == "cel" || !(name == "normal" || name.StartsWith("normal_",StringComparison.Ordinal))
                || !WorldModelTextureAssets.IsWorld(context.assetPath, document)) return;
            material.shader = Shader.Find("FFOne/NativeWorld") ?? throw new InvalidDataException("Missing FFOne/NativeWorld shader");
            foreach (var entry in data["colors"] ?? new JArray())
            {
                string property = (string)entry["name"]; var c = entry["value"];
                if (material.HasProperty(property)) material.SetVector(property,new Vector4((float)c[0],(float)c[1],(float)c[2],(float)c[3]));
            }
            foreach (var entry in data["floats"] ?? new JArray())
                if (material.HasProperty((string)entry["name"])) material.SetFloat((string)entry["name"],(float)entry["value"]);
            foreach (var binding in data["textureBindings"] ?? new JArray())
            {
                string slot = (string)binding["slot"], uri = (string)binding["uri"];
                if (string.IsNullOrEmpty(uri) || !material.HasProperty(slot)) continue;
                Bind(context,document,material,slot,uri,binding);
                if (binding["scale"] is JArray scale) material.SetTextureScale(slot,new Vector2((float)scale[0],(float)scale[1]));
                if (binding["offset"] is JArray offset) material.SetTextureOffset(slot,new Vector2((float)offset[0],(float)offset[1]));
                if (slot == "_MainTex")
                {
                    material.SetFloat("_MainTexRotation",(float?)binding["rotation"] ?? 0);
                    if (binding["pivot"] is JArray pivot) material.SetVector("_MainTexPivot",new Vector4((float)pivot[0],(float)pivot[1],0,0));
                }
            }
            var passes = data["passes"] as JArray ?? new JArray();
            var pass = passes.FirstOrDefault();
            var blend = pass?["blend"]; bool blended = (bool?)blend?["enabled"] ?? false;
            BlendMode source = blended ? Blend((string)blend["sourceColor"]) : BlendMode.One;
            BlendMode destination = blended ? Blend((string)blend["destinationColor"]) : BlendMode.Zero;
            bool alpha = (string)pass?["alphaTest"]?["mode"] == "enabled", twoPass = passes.Count > 1 && alpha;
            string cull = (string)pass?["cull"] ?? "back";
            SetPass(material,source,destination,cull == "off" ? 0 : cull == "front" ? 1 : 2,
                (bool?)pass?["zWrite"] == false ? 0 : 1,(int?)pass?["colorMask"] ?? 15,alpha && !twoPass ? 1 : 0,twoPass ? 1 : 0,
                (int?)data["renderQueue"] ?? (blended ? 3000 : 2000));
            var reference = pass?["alphaTest"]?["reference"];
            if (alpha && reference != null)
                material.SetFloat("_Cutoff",(float?)reference["resolved_value"] ?? (float?)reference["value"] ?? .9f);
            material.SetFloat("_VertexColorAD",name.EndsWith("_vertexColorAD",StringComparison.Ordinal) ? 1 : 0);
            material.SetFloat("_GlowMask",name.StartsWith("normal_glow_",StringComparison.Ordinal) ? 1 : 0);
            material.SetFloat("_AmbientEnabled",destination == BlendMode.One && !name.Contains("Test",StringComparison.Ordinal)
                || name is "normal_blendSrcalphaInvsrcalpha" or "normal_blendSrcalphaInvsrcalpha_vertexColorAD"
                or "normal_glow_blendSrcalphaInvsrcalpha" or "normal_blendSrcalphaInvsrccolor"
                or "normal_blendSrccolorInvsrcalpha_zwriteOff_cullOff" or "normal_blendSrcalphaOneTest_cullOff" ? 1 : 0);
            material.SetFloat("_FixedFogMode",UsesFixedFog(name) ? destination == BlendMode.One ? 2 : 1 : 0);
        }
        internal static BlendMode Blend(string value) => value switch {
            "zero" => BlendMode.Zero,"one" => BlendMode.One,"sourceColor" => BlendMode.SrcColor,"oneMinusSourceColor" => BlendMode.OneMinusSrcColor,
            "sourceAlpha" => BlendMode.SrcAlpha,"oneMinusSourceAlpha" => BlendMode.OneMinusSrcAlpha,"destinationColor" => BlendMode.DstColor,
            "oneMinusDestinationColor" => BlendMode.OneMinusDstColor,"destinationAlpha" => BlendMode.DstAlpha,"oneMinusDestinationAlpha" => BlendMode.OneMinusDstAlpha,
            _ => throw new InvalidDataException("Unsupported normalized world blend factor: " + value)
        };
        static bool UsesFixedFog(string name) => name is
            "SkinnedToonShading_blendSrcalphaInvsrcalpha" or "SkinnedToonShading_blendSrcalphaInvsrcalpha e1"
            or "SkinnedToonShading_blendSrcalphaInvsrcalpha_cullOff" or "Custom/SkinnedToonShading_blendSrcalphaInvsrcalpha_Rim_transparent"
            or "SkinnedFusionMatterLightDir_blendSrcalphaInvsrcalpha" or "ToonShading_blendSrcalphaInvsrcalpha"
            or "normal" or "RetroLit" or "normal_blendSrcalphaInvsrcalpha" or "normal_blendSrcalphaInvsrcalpha_vertexColorAD"
            or "normal_blendSrcalphaInvsrcalpha_cullOff" or "normal_blendSrcalphaInvsrcalpha_cullOff_vertexColorAD"
            or "normal_blendSrcalphaInvsrcalpha_zwriteOff" or "normal_blendSrcalphaInvsrcalpha_zwriteOff_cullOff"
            or "normal_blendSrccolorInvsrcalpha_zwriteOff_cullOff" or "normal_blendSrcalphaInvsrcalpha_zwriteOff_cullOff_vertexColorAD"
            or "normal_blendSrcalphaInvsrcalphaTest" or "normal_blendSrcalphaInvsrcalphaTest_zwriteOff"
            or "normal_blendSrcalphaInvsrcalphaTest_cullOff" or "normal_blendSrcalphaInvsrcalphaTest_cullOff_vertexColorAD"
            or "normal_blendSrcalphaInvsrcalphaTest_zwriteOff_cullOff" or "normal_glow_blendSrcalphaInvsrcalpha"
            or "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff" or "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff_vertexColorAD"
            or "normal_transparentcutoutinfrontofWater" or "normal_blendSrcalphaInvsrccolor" or "normal_blendSrcalphaZero"
            or "normal_blendSrcalphaOne_zwriteOff_cullOff" or "normal_blendSrcalphaOne_zwriteOff_cullOff_vertexColorAD"
            or "normal_blendSrcalphaOneTest_cullOff";

        static void ApplyExactStaticExceptions(string name, Material material)
        {
            // Accepted static_shader_plan has literal aliases that suffix heuristics cannot reproduce.
            switch (name)
            {
                case "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff":
                case "normal_glow_blendSrcalphaInvsrcalphaTest_cullOff_vertexColorAD":
                    SetPass(material, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 0, 1, 7, 1, 0, 3000); break;
                case "normal_glow_blendOneOne_zwriteOff_vertexColorAD":
                    SetPass(material, BlendMode.One, BlendMode.One, 0, 0, 15, 0, 0, 3001); break;
                case "normal_blendOneOneTest":
                    SetPass(material, BlendMode.One, BlendMode.One, 2, 0, 7, 1, 0, 3000); break;
                case "normal_blendOneOne":
                case "normal_blendOneOne_vertexColorAD":
                    SetPass(material, BlendMode.One, BlendMode.One, 2, 1, 7, 0, 0, 3000); break;
                case "normal_blendOneOne_zwriteOff":
                case "normal_blendOneOne_zwriteOff_vertexColorAD":
                    SetPass(material, BlendMode.One, BlendMode.One, 2, 0, 7, 0, 0, 3011); break;
                case "normal_blendSrcalphaOne":
                case "normal_blendSrcalphaOne_vertexColorAD":
                    SetPass(material, BlendMode.SrcAlpha, BlendMode.One, 2, 1, 7, 0, 0, 3000); break;
                case "normal_blendSrcalphaOne_zwriteOff":
                    SetPass(material, BlendMode.SrcAlpha, BlendMode.One, 2, 0, 7, 0, 0, 3010); break;
                case "normal_blendSrccolorInvsrcalpha_zwriteOff_cullOff":
                    SetPass(material, BlendMode.SrcColor, BlendMode.OneMinusSrcAlpha, 0, 0, 7, 0, 0, 3010); break;
                case "normal_blendSrcalphaZero":
                    SetPass(material, BlendMode.SrcAlpha, BlendMode.Zero, 2, 1, 15, 0, 0, 3000); break;
                case "normal_transparentcutoutinfrontofWater":
                    SetPass(material, BlendMode.One, BlendMode.Zero, 0, 1, 15, 0, 1, 3001); break;
            }
        }
        static void SetPass(Material material, BlendMode source, BlendMode destination, int cull, int depth, int mask, int alpha, int second, int queue)
        {
            material.SetFloat("_SrcBlend", (int)source); material.SetFloat("_DstBlend", (int)destination);
            material.SetFloat("_Cull", cull); material.SetFloat("_ZWrite", depth); material.SetFloat("_ColorMask", FFOne.World.WorldMaterialState.UnityColorMask(mask));
            material.SetFloat("_AlphaTest", alpha); material.SetFloat("_DepthCutout", second); material.renderQueue = queue;
        }
        static void Bind(AssetImportContext context, JObject document, Material material, string slot, string uri, JToken binding)
        {
            if (string.IsNullOrEmpty(uri)) throw new InvalidDataException("Static texture has no editable URI.");
            material.SetTexture(slot, WorldModelTextureAssets.Resolve(context, document, uri, binding, slot));
        }
    }
}
