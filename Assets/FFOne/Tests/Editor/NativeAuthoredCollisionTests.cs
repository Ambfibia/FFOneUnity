using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeAuthoredCollisionTests
    {
        [Test]
        public void SavedNativeSceneSupportsTheOriginalJumpLessonAndBridge()
        {
            const string path = "Assets/FFOne/Worlds/Overworld/Cells/map_01_01/map_01_01.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var player = new GameObject("native scene route probe");
            try
            {
                var roots = scene.GetRootGameObjects();
                var colliders = roots.SelectMany(r => r.GetComponentsInChildren<MeshCollider>(true)).ToArray();
                Assert.That(colliders.Length, Is.GreaterThanOrEqualTo(442));
                foreach (var c in colliders)
                {
                    Assert.That(c.sharedMesh, Is.Not.Null, c.name);
                    Assert.That(AssetDatabase.GetAssetPath(c.sharedMesh), Is.Empty, "External collision asset: " + c.name);
                }
                // This editable terrain is a vegetation canvas; runtime disables
                // its approximate heightfield and uses the exact triangle surface.
                foreach (var c in roots.SelectMany(r => r.GetComponentsInChildren<TerrainCollider>(true))) c.enabled = false;
                Physics.SyncTransforms();
                foreach (var point in new[] { new Vector3(907, 14, 685), new Vector3(908, 14, 682) })
                    Assert.That(Physics.RaycastAll(point, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore)
                        .Any(h => h.collider.gameObject.scene == scene && h.normal.y > .9f && h.point.y > 11), Is.True, "Bridge support at " + point);
                player.transform.position = new Vector3(555.82f, -105, 654.2f);
                var motor = player.AddComponent<CharacterController>(); NativePlayerMotor.Configure(motor);
                var destination = new Vector3(566, -99, 665);
                float velocity = 0, age = 0; int jumps = 0;
                for (int tick = 0; tick < 800; tick++)
                {
                    var delta = Vector3.ProjectOnPlane(destination - player.transform.position, Vector3.up);
                    bool launch = NativePlayerMotor.IsGrounded(motor, velocity) && delta.magnitude > .15f;
                    if (launch) { velocity = 5.68f * NativePlayerMotor.JumpMultiplier; age = 0; jumps++; }
                    NativePlayerMotor.Step(motor, delta.magnitude > .15f ? delta.normalized * 6 : Vector3.zero,
                        ref velocity, ref age, 1f / 60, true, launch);
                    if (NativePlayerMotor.IsGrounded(motor, velocity) && Vector3.Distance(player.transform.position, destination) < 3) break;
                }
                Assert.That(jumps, Is.GreaterThan(0));
                Assert.That(NativePlayerMotor.IsGrounded(motor, velocity), Is.True);
                Assert.That(Vector3.Distance(player.transform.position, destination), Is.LessThan(3), "Saved native colliders must admit the ledge route.");
            }
            finally { Object.DestroyImmediate(player); EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void CameraKeepsItsNearPlaneOutOfACliff()
        {
            var root = new GameObject("camera clearance regression"); root.transform.position = Vector3.one * 10000;
            try
            {
                var player = new GameObject("player"); player.transform.SetParent(root.transform, false);
                var wall = new GameObject("cliff", typeof(BoxCollider)); wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = new Vector3(0, 1.6f, -3); wall.GetComponent<BoxCollider>().size = new Vector3(20, 10, .1f);
                var camera = new GameObject("camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                camera.fieldOfView = 45; camera.nearClipPlane = .15f; camera.aspect = 16f / 9;
                Physics.SyncTransforms();
                var focus = player.transform.position + Vector3.up * 1.6f;
                var offset = NativePlayerCamera.ResolveOffset(camera, player.transform, focus, Vector3.back * 6);
                Assert.That(offset.magnitude, Is.InRange(2.5f, 2.8f));
                Assert.That(Physics.CheckSphere(focus + offset, .19f, ~0, QueryTriggerInteraction.Ignore), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
