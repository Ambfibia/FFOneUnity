using System;
using UnityEngine;

namespace FFOne.Content
{
    public static class NativeAvatarMaterials
    {
        internal static void Bind(Material material, string category, Texture2D primary,
            Texture2D secondary, Texture2D skin, Color skinTint, Color hairTint)
        {
            bool bodyPart = category is "face" or "hair" or "shirt" or "pants" or "shoes";
            string name = material.name;
            if (name.Contains("sub", StringComparison.OrdinalIgnoreCase))
            {
                var texture = bodyPart ? skin : secondary;
                if (texture) material.SetTexture("_MainTex", texture);
                if (bodyPart) Tint(material, skinTint);
            }
            else if (primary && !name.Contains("override", StringComparison.OrdinalIgnoreCase) &&
                (bodyPart || name.Contains("main", StringComparison.OrdinalIgnoreCase)))
            {
                material.SetTexture("_MainTex", primary);
            }
            if (name.Contains("hair", StringComparison.OrdinalIgnoreCase)) Tint(material, hairTint);
            Configure(material, category);
        }

        static void Tint(Material material, Color tint)
        {
            material.SetColor("_Color", new Color(tint.r * .5f, tint.g * .5f, tint.b * .5f, 1));
            material.SetColor("_Emission", material.GetColor("_Emission") * tint);
        }

        public static void Configure(Material material, string category)
        {
            if (!material || material.shader.name != "FFOne/NativeToon") return;
            // Avatar materials retain their authored toon ramps. The native comic
            // palette is a separate rendering style used by imported world content.
            material.SetFloat("_Comic", 0);
            material.SetFloat("_InkRim", 0);
            if (category != "face") return;

            // The skin and eye decal are separate skinned renderers. At one queue
            // Unity sorts them by different bounds, which changes as the head turns.
            // Draw the authored decal after its skin without changing its blend/depth.
            bool skin = material.name.Contains("sub", StringComparison.OrdinalIgnoreCase);
            material.renderQueue = skin ? 2900 : 2901;
        }
    }
}
