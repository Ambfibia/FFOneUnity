using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Player and Nano previews use the same physical-pixel render budget.</summary>
    public static class PreviewRenderQuality
    {
        public static Vector2Int Resolution(VisualElement target) =>
            Resolution(target.worldBound.size, target.panel?.scaledPixelsPerPoint ?? 1);

        public static Vector2Int Resolution(Vector2 size, float pixelsPerPoint = 1)
        {
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
                return Vector2Int.one;
            if (!float.IsFinite(pixelsPerPoint) || pixelsPerPoint <= 0) pixelsPerPoint = 1;
            // worldBound includes element transforms, but is still measured in panel points.
            // Render at twice the displayed pixel size so thin outlines survive downsampling.
            float scale = Mathf.Min(2 * pixelsPerPoint, 4096 / Mathf.Max(size.x, size.y));
            return new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(size.x * scale)), Mathf.Max(1, Mathf.CeilToInt(size.y * scale)));
        }

        public static RenderTexture Create(Vector2Int size, string name)
        {
            var texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) {
                name = name, antiAliasing = 4, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, useDynamicScale = false
            };
            texture.antiAliasing = Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(texture.descriptor));
            return texture;
        }
    }
}
