using System.Linq;
using FFOne.Editor;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeCollisionRefinementTests
    {
        [Test]
        public void LargeTriangleKeepsItsAreaBoundaryAndUpwardCollisionAfterRefinement()
        {
            var mesh = new Mesh(); var host = new GameObject("large native triangle regression");
            try
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right * 512, Vector3.forward * 512 };
                mesh.triangles = new[] { 0, 2, 1 };
                Assert.That(NativeCollisionMeshRefinement.Refine(mesh), Is.True);
                var v = mesh.vertices; var t = mesh.triangles; double area = 0;
                CollectionAssert.Contains(v, Vector3.zero);
                CollectionAssert.Contains(v, Vector3.right * 512);
                CollectionAssert.Contains(v, Vector3.forward * 512);
                for (int i = 0; i < t.Length; i += 3)
                {
                    var normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    Assert.That(normal.y, Is.GreaterThan(0)); area += normal.magnitude * .5;
                    for (int k = 0; k < 3; k++)
                        Assert.That(Vector3.Distance(v[t[i + k]], v[t[i + (k + 1) % 3]]), Is.LessThanOrEqualTo(256));
                }
                Assert.That(area, Is.EqualTo(512d * 512 / 2).Within(.001));
                host.transform.position = Vector3.one * 10000;
                var collider = host.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; Physics.SyncTransforms();
                foreach (var point in new[] { new Vector3(1, 2, 1), new Vector3(200, 2, 200), new Vector3(10, 2, 500) })
                    Assert.That(collider.Raycast(new Ray(host.transform.position + point, Vector3.down), out _, 4), Is.True);
                Assert.That(collider.Raycast(new Ray(host.transform.position + new Vector3(300, 2, 300), Vector3.down), out _, 4), Is.False,
                    "Refinement must not fill space outside the original triangle.");
                Assert.That(NativeCollisionMeshRefinement.Refine(mesh), Is.False, "Repeated refinement must be stable.");
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(mesh); }
        }
    }
}
