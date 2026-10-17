using UnityEngine;

namespace FFOne.World
{
    // Geometry is authored Y-up. Existing map TRS and behaviour tracks remain in source space.
    public sealed class WorldModelBasis : MonoBehaviour
    {
        public bool SourceBasisApplied;
        public static readonly Quaternion SourceToAuthoring = Quaternion.Euler(-90, 0, 0);
        public static void ApplySource(GameObject instance)
        {
            var basis = instance.GetComponent<WorldModelBasis>();
            if (!basis || basis.SourceBasisApplied) return;
            foreach (Transform child in instance.transform)
            {
                child.localPosition = Quaternion.Inverse(SourceToAuthoring) * child.localPosition;
                child.localRotation = Quaternion.Inverse(SourceToAuthoring) * child.localRotation;
            }
            basis.SourceBasisApplied = true;
        }
    }
}
