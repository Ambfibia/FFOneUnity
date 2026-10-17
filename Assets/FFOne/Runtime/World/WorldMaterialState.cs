using UnityEngine;

namespace FFOne.World
{
    public static class WorldMaterialState
    {
        // Published fixed-function masks encode R/G/B/A as 1/2/4/8;
        // Unity render state encodes them as 8/4/2/1.
        public static int UnityColorMask(int source) => ((source & 1) << 3) | ((source & 2) << 1) | ((source & 4) >> 1) | ((source & 8) >> 3);

        public static void Prepare(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material && material.shader.name == "FFOne/NativeWorld" && material.GetFloat("_ColorMask") == 7)
                        material.SetFloat("_ColorMask", 14);
        }
    }
}
