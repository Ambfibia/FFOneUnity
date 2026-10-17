using ContentPaths = FFOne.Content.ContentPaths;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace FFOne.Editor.Integration
{
    public static class ClientPresentationSetup
    {
        public static void Ensure()
        {
            foreach (string name in new[] { "jeffe", "chaletbook-regular", "comic-sans-ms-bold" }) EnsureFont(name);
            foreach (string platform in new[] { "PC", "Mobile" })
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ContentPaths.Resolve($"Assets/Settings/{platform}_RPAsset.asset"));
                pipeline.msaaSampleCount = 4;
                EditorUtility.SetDirty(pipeline);
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ContentPaths.Resolve($"Assets/Settings/{platform}_Renderer.asset"));
                Outline(renderer, false); Outline(renderer, true);
                renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
        }
        static void EnsureFont(string name)
        {
            string path = $"Assets/Resources/fonts/{name}-sdf.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(ContentPaths.Resolve(path))) return;
            string source = $"Assets/Resources/fonts/{name}." + (name == "jeffe" ? "otf" : "ttf");
            var font = AssetDatabase.LoadAssetAtPath<Font>(ContentPaths.Resolve(source));
            if (!font) throw new FileNotFoundException("Accepted font is missing", source);
            var asset = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name + "-sdf";
            asset.isMultiAtlasTexturesEnabled = true;
            AssetDatabase.CreateAsset(asset, ContentPaths.Resolve(path));
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
            EditorUtility.SetDirty(asset);
        }
        static void Outline(UniversalRendererData renderer, bool transparent)
        {
            string name = "FF Character Ink " + (transparent ? "Transparent" : "Opaque");
            var feature = renderer.rendererFeatures.OfType<RenderObjects>().FirstOrDefault(f => f.name == name);
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<RenderObjects>(); feature.name = name;
                AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature);
            }
            feature.settings.passTag = name;
            feature.settings.Event = transparent ? RenderPassEvent.AfterRenderingTransparents : RenderPassEvent.AfterRenderingOpaques;
            feature.settings.filterSettings.PassNames = new[] { "FFCharacterOutline" };
            feature.settings.filterSettings.LayerMask = ~0;
            feature.settings.filterSettings.RenderQueueType = transparent ? RenderQueueType.Transparent : RenderQueueType.Opaque;
            feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None;
            feature.Create(); EditorUtility.SetDirty(feature);
        }
    }
}
