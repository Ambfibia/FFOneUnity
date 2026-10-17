using ContentPaths = FFOne.Content.ContentPaths;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class SettingsArtworkImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 1;
        void OnPreprocessTexture()
        {
            string assetPath = ContentPaths.Legacy(this.assetPath);
            if (!assetPath.StartsWith("Assets/Resources/ui/en/option/", System.StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            if (!importer.importSettingsMissing) return;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
        }
    }
}
