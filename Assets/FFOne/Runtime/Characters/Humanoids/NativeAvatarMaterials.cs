using System;
using UnityEngine;

namespace FFOne.Content
{
    public static class NativeAvatarMaterials
    {
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
