using System;
using System.Collections.Generic;
using UnityEngine;

namespace FFOne.World
{
    // The Rust contract groups sibling prefab parts, quantizes one shared
    // center/end, and bypasses every group whose bounds contain its fade band.
    public sealed class WorldObjectRange : MonoBehaviour
    {
        sealed class Group
        {
            internal Transform Tile;
            internal readonly List<WorldObjectRange> Members = new();
            internal Vector4 CenterEnd;
            internal bool Finalized, Unbounded, Applied, ResidencyPending;
        }
        static readonly Dictionary<(int, string), Group> Groups = new();
        static readonly MaterialPropertyBlock Block = new();
        static readonly int RangeId = Shader.PropertyToID("_FFObjectRange");
        static int updatedFrame = -1;
        static Vector3 previousCamera;
        static bool hasCamera, contractsChanged;
        Group group;
        (int, string) key;
        Renderer[] renderers;
        bool coordinator;
        float presentationEnd;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Groups.Clear(); updatedFrame = -1; hasCamera = false; contractsChanged = false; }

        public static void Attach(GameObject model, float cpuPresentationEnd = 0)
        {
            if (!model || model.GetComponent<WorldObjectRange>()) return;
            var tile = model.transform.parent;
            if (!tile) return;
            var member = model.AddComponent<WorldObjectRange>();
            member.enabled = false; // One coordinator per tile performs all range work.
            member.renderers = model.GetComponentsInChildren<Renderer>(true);
            member.presentationEnd = cpuPresentationEnd;
            string owner = PrefabOwner(model.name) ?? "visual:" + model.GetInstanceID();
            member.key = (tile.GetInstanceID(), owner);
            if (!Groups.TryGetValue(member.key, out var group))
                Groups.Add(member.key, group = new Group { Tile = tile });
            member.group = group; group.Members.Add(member);
            foreach (var renderer in member.renderers)
            {
                if (renderer is SkinnedMeshRenderer) group.Unbounded = true;
                foreach (var material in renderer.sharedMaterials)
                    if (!material || material.shader.name != "FFOne/NativeWorld") group.Unbounded = true;
            }
            if (model.GetComponentInChildren<Animation>(true)) group.Unbounded = true;
            var driver = tile.GetComponent<WorldObjectRange>();
            if (!driver) { driver = tile.gameObject.AddComponent<WorldObjectRange>(); driver.coordinator = true; }
        }

        public static void FinalizeTile(GameObject tile)
        {
            if (!tile) return;
            foreach (var group in Groups.Values)
            {
                if (group.Tile != tile.transform) continue;
                group.Finalized = true;
                group.ResidencyPending = true;
                if (!group.Unbounded)
                {
                    bool hasBounds = false; Vector3 minimum = Vector3.positiveInfinity, maximum = Vector3.negativeInfinity;
                    foreach (var member in group.Members)
                    {
                        if (!member) continue;
                        // Match Rust's local aggregation followed by one member-to-world transform.
                        Vector3 localMin = Vector3.positiveInfinity, localMax = Vector3.negativeInfinity;
                        bool hasLocal = false;
                        foreach (var renderer in member.renderers)
                        {
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (!filter || !filter.sharedMesh) { group.Unbounded = true; break; }
                            Bounds local = filter.sharedMesh.bounds;
                            Matrix4x4 matrix = member.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                            Accumulate(local.min, local.max, matrix, ref localMin, ref localMax); hasLocal = true;
                        }
                        if (group.Unbounded) break;
                        if (hasLocal) { Accumulate(localMin, localMax, member.transform.localToWorldMatrix, ref minimum, ref maximum); hasBounds = true; }
                    }
                    if (!hasBounds || !TryContract(minimum, maximum, out group.CenterEnd)) group.Unbounded = true;
                }
                foreach (var member in group.Members)
                {
                    if (!member) continue;
                    foreach (var renderer in member.renderers)
                    {
                        if (!renderer) continue;
                        if (group.Unbounded && !group.Applied) continue;
                        renderer.GetPropertyBlock(Block);
                        Block.SetVector(RangeId, group.Unbounded ? Vector4.zero : group.CenterEnd);
                        renderer.SetPropertyBlock(Block);
                    }
                }
                group.Applied = !group.Unbounded; contractsChanged = true;
            }
        }

        public static void MarkDynamic(Transform target)
        {
            if (!target) return;
            var own = target.GetComponent<WorldObjectRange>();
            if (own && own.group != null) MakeUnbounded(own.group);
            foreach (var member in target.GetComponentsInChildren<WorldObjectRange>(true))
                if (member.group != null) MakeUnbounded(member.group);
        }
        static void MakeUnbounded(Group group)
        {
            if (group.Unbounded) return;
            group.Unbounded = true;
            foreach (var member in group.Members)
            {
                if (!member) continue;
                foreach (var renderer in member.renderers)
                {
                    if (!renderer) continue;
                    renderer.forceRenderingOff = false;
                    if (!group.Applied) continue;
                    renderer.GetPropertyBlock(Block); Block.SetVector(RangeId, Vector4.zero); renderer.SetPropertyBlock(Block);
                }
            }
            group.Applied = false;
        }

        void LateUpdate()
        {
            if (!coordinator || updatedFrame == Time.frameCount) return;
            var camera = Camera.main;
            if (!camera) return;
            updatedFrame = Time.frameCount;
            Vector3 position = camera.transform.position;
            bool moved = !hasCamera || (position - previousCamera).sqrMagnitude >= 16;
            if (!moved && !contractsChanged) return;
            if (moved) { hasCamera = true; previousCamera = position; }
            contractsChanged = false;
            foreach (var group in Groups.Values)
            {
                if (!group.Finalized || group.Unbounded || !group.Tile || !group.Tile.gameObject.activeInHierarchy) continue;
                if (!moved && !group.ResidencyPending) continue;
                group.ResidencyPending = false;
                var center = new Vector3(group.CenterEnd.x, group.CenterEnd.y, group.CenterEnd.z);
                foreach (var member in group.Members)
                {
                    if (!member) continue;
                    float end = member.presentationEnd > 0 ? member.presentationEnd : group.CenterEnd.w;
                    bool hidden = (position - center).sqrMagnitude >= end * end;
                    foreach (var renderer in member.renderers)
                        if (renderer && renderer.forceRenderingOff != hidden) renderer.forceRenderingOff = hidden;
                }
            }
        }

        // Public for focused parity tests. The result is the exact decoded Rust tag in Unity space.
        public static bool TryContract(Vector3 minimum, Vector3 maximum, out Vector4 centerEnd)
        {
            centerEnd = Vector4.zero;
            Vector3 center = (minimum + maximum) * .5f;
            float radius = (maximum - minimum).magnitude * .5f;
            if (!Quantize(-center.x, -8192, 16, 512, out int x) || !Quantize(center.z, 0, 16, 512, out int z) ||
                !Quantize(center.y, -18000, 20, 1024, out int y) || float.IsNaN(radius) || float.IsInfinity(radius)) return false;
            int code = Mathf.Min(15, (int)Mathf.Floor((Mathf.Clamp(radius * 35, 75, 340) - 75) / 265 * 15 + .5f));
            Vector3 decoded = new(8192 - (x + .5f) * 16, -18000 + (y + .5f) * 20, (z + .5f) * 16);
            float end = 75 + code / 15f * 265;
            Vector3 low = Abs(minimum - decoded), high = Abs(maximum - decoded);
            Vector3 farthest = Vector3.Max(low, high);
            if (farthest.magnitude + 32 >= end) return false;
            centerEnd = new Vector4(decoded.x, decoded.y, decoded.z, end); return true;
        }
        static bool Quantize(float value, float minimum, float step, int levels, out int code)
        {
            code = 0;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value >= minimum + step * levels) return false;
            code = (int)Mathf.Floor((value - minimum) / step); return true;
        }
        static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        static void Accumulate(Vector3 minimum, Vector3 maximum, Matrix4x4 matrix, ref Vector3 aggregateMin, ref Vector3 aggregateMax)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? minimum.x : maximum.x,
                    (i & 2) == 0 ? minimum.y : maximum.y, (i & 4) == 0 ? minimum.z : maximum.z));
                aggregateMin = Vector3.Min(aggregateMin, p); aggregateMax = Vector3.Max(aggregateMax, p);
            }
        }
        static string PrefabOwner(string name)
        {
            int start = name.IndexOf("[prefab:", StringComparison.Ordinal);
            if (start < 0) return null;
            start += 8; int end = name.IndexOf(':', start);
            return end > start ? name.Substring(start, end - start) : null;
        }
        void OnDestroy()
        {
            if (group == null) return;
            group.Members.Remove(this);
            if (group.Members.Count == 0) Groups.Remove(key);
        }
    }
}
