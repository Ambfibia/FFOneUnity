using UnityEngine;

namespace FFOne.Runtime.World
{
    // Match Bevy's AABB-centered VisibilityRange, including the chunk radius.
    public sealed class WorldTerrainGrassVisibility : MonoBehaviour
    {
        public float Distance = 80;
        MeshRenderer target;
        void Awake() => target = GetComponent<MeshRenderer>();
        void LateUpdate()
        {
            var camera = Camera.main;
            if (!camera || !target) return;
            Bounds bounds = target.bounds;
            float end = Distance + bounds.extents.magnitude;
            target.enabled = (camera.transform.position - bounds.center).sqrMagnitude <= end * end;
        }
    }
}
