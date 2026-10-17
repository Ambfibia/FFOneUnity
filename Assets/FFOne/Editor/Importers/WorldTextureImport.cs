using ContentPaths = FFOne.Content.ContentPaths;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    // Scope new defaults to the imported native world; preserve authored Unity importer settings.
    public sealed class WorldTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string assetPath = ContentPaths.Legacy(this.assetPath);
            if (!(assetPath.StartsWith("Assets/Resources/objects/", System.StringComparison.Ordinal) ||
                  assetPath.StartsWith("Assets/Resources/map/", System.StringComparison.Ordinal))) return;
            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 16384;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = !assetPath.Contains("/weights/") && !assetPath.EndsWith("/heightmap.png", System.StringComparison.Ordinal);
            importer.sRGBTexture = !assetPath.Contains("/weights/") && !assetPath.Contains("/lightmap/") && !assetPath.EndsWith("/heightmap.png", System.StringComparison.Ordinal);
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = assetPath.Contains("/weights/") || assetPath.Contains("/lightmap/") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.alphaIsTransparency = false;
            importer.isReadable = false;
        }
    }
}
