using UnityEngine;

namespace FFOne
{
    public static class NativePlayerCamera
    {
        public static Vector3 ResolveOffset(Camera camera, Transform player, Vector3 focus, Vector3 offset)
        {
            // Sweep the near plane as well as the camera centre. A centre ray
            // leaves the corners of the image inside rocks at grazing angles.
            float radius = Mathf.Max(.2f, camera.nearClipPlane * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) * camera.aspect);
            float length = offset.magnitude;
            if (length < .001f) return offset;
            foreach (var hit in Physics.SphereCastAll(focus, radius, offset / length, length, ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(player)) continue;
                length = Mathf.Min(length, Mathf.Max(.05f, hit.distance - .05f));
            }
            return offset.normalized * length;
        }
    }
}
