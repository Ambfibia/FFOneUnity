using System.Linq;
using System.Reflection;
using FFOne.Editor;
using FFOne.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeWorldColliderTests
    {
        [Test]
        public void OriginalPlayerCapsulePassesTheTutorialJumpRoute()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>("Assets/FFOne/Worlds/Overworld/Cells/map_01_01/Imported/Content.asset");
            var root = new GameObject("tutorial route collision regression");
            try
            {
                var terrain = Object.Instantiate(content.Terrain, root.transform, false);
                // Vegetation's editable heightfield is disabled in play mode.
                foreach (var collider in terrain.GetComponentsInChildren<TerrainCollider>(true)) collider.enabled = false;
                foreach (var placement in content.Colliders.Where(p => !p.Trigger))
                {
                    var host = new GameObject(placement.Name); host.transform.SetParent(root.transform, false);
                    host.transform.SetPositionAndRotation(placement.Position, placement.Rotation); host.transform.localScale = placement.Scale;
                    host.AddComponent<MeshCollider>().sharedMesh = placement.Mesh;
                }
                Physics.SyncTransforms();
                var player = new GameObject("original OwnUser capsule"); player.transform.SetParent(root.transform, false);
                player.transform.position = new Vector3(555.82f, -105, 654.2f);
                var motor = player.AddComponent<CharacterController>(); NativePlayerMotor.Configure(motor);
                var destination = new Vector3(566, -99, 665);
                float vertical = 0; int jumps = 0;
                // Walk and jump over the original rocks; no ground snaps or warps.
                for (int tick = 0; tick < 800; tick++)
                {
                    var direction = Vector3.ProjectOnPlane(destination - player.transform.position, Vector3.up);
                    if (motor.isGrounded && vertical <= 0 && direction.magnitude > .15f)
                    { vertical = 5.68f; jumps++; }
                    else vertical = Mathf.Max(-10, vertical - 10 / 60f);
                    motor.Move((direction.magnitude > .15f ? direction.normalized * .1f : Vector3.zero) + Vector3.up * (vertical / 60f) - Vector3.one * .000001f);
                    if (motor.isGrounded && Vector3.Distance(player.transform.position, destination) < 3) break;
                }
                Assert.That(jumps, Is.GreaterThan(0));
                Assert.That(motor.isGrounded, Is.True, "The jump lesson requires landing before progressing.");
                Assert.That(Vector3.Distance(player.transform.position, destination), Is.LessThan(3), "The original jump marker must be reachable using the production capsule and authored meshes.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void OriginalPlayerCapsuleFitsANativeWidthPassage()
        {
            var root = new GameObject("native passage regression");
            try
            {
                foreach (float x in new[] { -.85f, .85f })
                {
                    var wall = new GameObject("wall"); wall.transform.SetParent(root.transform, false);
                    wall.transform.position = new Vector3(x, 1, 0);
                    wall.AddComponent<BoxCollider>().size = new Vector3(1, 2, 8);
                }
                var player = new GameObject("OwnUser"); player.transform.SetParent(root.transform, false);
                player.transform.position = new Vector3(0, 0, -3);
                var motor = player.AddComponent<CharacterController>(); NativePlayerMotor.Configure(motor);
                Physics.SyncTransforms();
                for (int tick = 0; tick < 120; tick++) motor.Move(Vector3.forward * .05f);
                Assert.That(player.transform.position.z, Is.GreaterThan(2.9f), "A 0.7 m passage admits the original 0.6 m capsule.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(907f, 685f)]
        [TestCase(908f, 682f)]
        public void PublishedTutorialBridgeHasAnUpwardSupportingFace(float x, float z)
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>("Assets/FFOne/Worlds/Overworld/Cells/map_01_01/Imported/Content.asset");
            string sourcePath = AssetDatabase.GUIDToAssetPath("ac3ae5a962f7d5c43bf2c8c0c485555e");
            var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Mesh>().Single();
            var originalIndices = source.triangles;
            var cooked = NativeWorldColliderImport.Publish(source);
            Assert.That(NativeWorldColliderImport.Publish(source), Is.SameAs(cooked));
            Assert.That(source.triangles, Is.EqualTo(originalIndices), "Physics publication must preserve the shared visual source.");
            var placements = content.Colliders.Where(c => c.Mesh == cooked).ToArray();
            Assert.That(placements.Length, Is.GreaterThanOrEqualTo(7));
            bool supported = false;
            foreach (var placement in placements)
            {
                var host = new GameObject("authored bridge collision regression");
                try
                {
                    host.transform.SetPositionAndRotation(placement.Position, placement.Rotation); host.transform.localScale = placement.Scale;
                    var collider = host.AddComponent<MeshCollider>(); collider.sharedMesh = placement.Mesh;
                    Physics.SyncTransforms();
                    if (collider.Raycast(new Ray(new Vector3(x, 14, z), Vector3.down), out var hit, 4))
                    { Assert.That(hit.normal.y, Is.GreaterThan(.9f)); Assert.That(hit.point.y, Is.InRange(11f, 11.2f)); supported = true; }
                }
                finally { Object.DestroyImmediate(host); }
            }
            Assert.That(supported, Is.True, "The authored NPC spawn must hit the front face of the bridge floor.");
        }

        [Test]
        public void TutorialWaterRetainsAnUpwardFrontFace()
        {
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>("Assets/FFOne/Worlds/Overworld/Cells/map_01_01/Imported/Content.asset");
            var placement = content.Colliders.Single(c => c.Name.StartsWith("ffPoison ["));
            var mesh = placement.Mesh;
            var vertices = mesh.vertices; var indices = mesh.triangles;
            int triangle = Enumerable.Range(0, indices.Length / 3).First(i =>
                Mathf.Abs(Vector3.Cross(vertices[indices[i * 3 + 1]] - vertices[indices[i * 3]], vertices[indices[i * 3 + 2]] - vertices[indices[i * 3]]).y) > .1f) * 3;
            var point = (vertices[indices[triangle]] + vertices[indices[triangle + 1]] + vertices[indices[triangle + 2]]) / 3;
            var host = new GameObject("tutorial water collision regression");
            try
            {
                host.transform.SetPositionAndRotation(placement.Position, placement.Rotation); host.transform.localScale = placement.Scale;
                var collider = host.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; Physics.SyncTransforms();
                point = host.transform.TransformPoint(point);
                Assert.That(collider.Raycast(new Ray(point + Vector3.up * 10, Vector3.down), out var hit, 20), Is.True);
                Assert.That(hit.normal.y, Is.GreaterThan(.5f));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void StreamingWaitsForInitializationBeforeUsingItsDependencies()
        {
            var host = new GameObject("stream initialization regression");
            var catalog = ScriptableObject.CreateInstance<WorldCatalog>();
            try
            {
                var stream = host.AddComponent<WorldStreamer>();
                typeof(WorldStreamer).GetProperty(nameof(WorldStreamer.Catalog)).SetValue(stream, catalog);
                Assert.DoesNotThrow(() => typeof(WorldStreamer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stream, null));
                Assert.That(stream.IsReady, Is.False);
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(catalog); }
        }
    }
}
