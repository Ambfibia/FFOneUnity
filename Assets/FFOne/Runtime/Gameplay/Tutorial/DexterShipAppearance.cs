using System;
using UnityEngine;

namespace FFOne.Tutorial
{
    public static class DexterShipAppearance
    {
        public const string DexterBodyMaterial = "npc_dexter2-main-link_a.dds";

        /// <summary>Apply InitMode's slot-zero body texture to its published primitive.</summary>
        public static int BindDexterBody(GameObject actor, Texture2D texture)
        {
            if (!actor) throw new ArgumentNullException(nameof(actor));
            if (!texture) throw new ArgumentNullException(nameof(texture));
            int count = 0;
            var properties = new MaterialPropertyBlock();
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    // The accepted GLB explicitly marks only this source slot as
                    // npcTableTextureWritable. Its other main-named materials own
                    // sword/glass textures and must retain those authored bindings.
                    if (!materials[i] || materials[i].name != DexterBodyMaterial) continue;
                    renderer.GetPropertyBlock(properties, i);
                    properties.SetTexture("_MainTex", texture);
                    renderer.SetPropertyBlock(properties, i);
                    properties.Clear(); count++;
                }
            }
            if (count == 0) throw new InvalidOperationException("Dexter ship body material is absent from the accepted model.");
            return count;
        }
    }
}
