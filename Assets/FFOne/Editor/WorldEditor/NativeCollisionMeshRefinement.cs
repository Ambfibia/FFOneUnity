using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Editor
{
    public static class NativeCollisionMeshRefinement
    {
        // PhysX warns about edges over 500 units. Bisect only oversized
        // triangles; all new points remain on the original surface/edges.
        public static bool Refine(Mesh mesh, float maximumEdge = 256)
        {
            var vertices = new List<Vector3>(mesh.vertices);
            var source = mesh.triangles;
            float limit = maximumEdge * maximumEdge;
            bool oversized = false;
            for (int i = 0; i < source.Length && !oversized; i += 3)
                for (int k = 0; k < 3; k++)
                    oversized |= (vertices[source[i + k]] - vertices[source[i + (k + 1) % 3]]).sqrMagnitude > limit;
            if (!oversized) return false;
            var output = new List<int>(source.Length);
            var pending = new Stack<(int A, int B, int C)>();
            var midpoints = new Dictionary<ulong, int>();
            for (int i = 0; i < source.Length; i += 3)
            {
                pending.Push((source[i], source[i + 1], source[i + 2]));
                while (pending.Count > 0)
                {
                    var t = pending.Pop();
                    float ab = (vertices[t.A] - vertices[t.B]).sqrMagnitude;
                    float bc = (vertices[t.B] - vertices[t.C]).sqrMagnitude;
                    float ca = (vertices[t.C] - vertices[t.A]).sqrMagnitude;
                    if (Mathf.Max(ab, Mathf.Max(bc, ca)) <= limit)
                    { output.Add(t.A); output.Add(t.B); output.Add(t.C); continue; }
                    // Rotate cyclically, preserving winding, to split the longest edge.
                    if (bc >= ab && bc >= ca) t = (t.B, t.C, t.A);
                    else if (ca >= ab && ca >= bc) t = (t.C, t.A, t.B);
                    ulong key = ((ulong)(uint)Mathf.Min(t.A, t.B) << 32) | (uint)Mathf.Max(t.A, t.B);
                    if (!midpoints.TryGetValue(key, out int midpoint))
                    {
                        midpoint = vertices.Count;
                        vertices.Add((vertices[t.A] + vertices[t.B]) * .5f);
                        midpoints.Add(key, midpoint);
                    }
                    pending.Push((midpoint, t.B, t.C));
                    pending.Push((t.A, midpoint, t.C));
                }
            }
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetTriangles(output, 0); mesh.RecalculateBounds();
            return true;
        }
    }
}
