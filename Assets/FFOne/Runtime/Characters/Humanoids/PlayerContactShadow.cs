using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Characters
{
    public sealed class PlayerContactShadow : MonoBehaviour
    {
        const float MaxDrop = 8f;
        readonly RaycastHit[] hits = new RaycastHit[16];
        MeshRenderer mark;
        Mesh mesh;
        Material material;

        void Start()
        {
            var shader = Resources.Load<Shader>("PlayerContactShadow");
            if (!shader) { enabled = false; return; }
            var surface = new GameObject("Player contact shadow");
            surface.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Contact shadow plane" };
            mesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            material = new Material(shader);
            mark = surface.AddComponent<MeshRenderer>();
            mark.sharedMaterial = material;
            mark.shadowCastingMode = ShadowCastingMode.Off;
            mark.receiveShadows = false;
            mark.enabled = false;
        }

        void LateUpdate()
        {
            Vector3 origin = transform.position + Vector3.up * .35f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, MaxDrop + .35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit ground = default;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance >= nearest || hit.normal.y < .5f || hit.collider.transform.IsChildOf(transform)) continue;
                nearest = hit.distance;
                ground = hit;
            }
            mark.enabled = float.IsFinite(nearest);
            if (!mark.enabled) return;
            mark.transform.SetPositionAndRotation(ground.point + ground.normal * .025f, Quaternion.FromToRotation(Vector3.up, ground.normal));
            mark.transform.localScale = Vector3.one * (.58f * (1 + .04f * Mathf.Max(0, nearest - .35f)));
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
