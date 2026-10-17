using UnityEngine;

namespace FFOne.Runtime.World
{
    // Unity does not serialize material vector arrays in .mat assets. Store the
    // accepted per-material constants beside the tile and apply once on admission.
    [ExecuteAlways]
    public sealed class WorldTerrainParameters : MonoBehaviour
    {
        [SerializeField] MeshRenderer target;
        [SerializeField] Vector4[] scales, sizes, tints, weights;

        public void Configure(MeshRenderer renderer, Vector4[] layerScales, Vector4[] sourceSizes, Vector4[] layerTints, Vector4[] weightSamplers)
        {
            target = renderer; scales = layerScales; sizes = sourceSizes; tints = layerTints; weights = weightSamplers;
            Apply();
        }
        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Apply()
        {
            if (!target || scales == null || sizes == null || tints == null || weights == null) return;
            var material = target.sharedMaterial;
            material.SetVectorArray("_LayerScaleMode", scales);
            material.SetVectorArray("_LayerSourceSize", sizes);
            material.SetVectorArray("_LayerTint", tints);
            material.SetVectorArray("_WeightSampler", weights);
        }
    }
}
