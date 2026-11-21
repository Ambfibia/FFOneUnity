using FFOne.Integration;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class AvatarPreviewCameraTests
    {
        GameObject host;
        Camera camera;

        [SetUp]
        public void CreateCamera()
        {
            host = new GameObject("source avatar camera pose fixture");
            camera = host.AddComponent<Camera>(); camera.enabled = false;
        }

        [TearDown]
        public void ReleaseCamera() => Object.DestroyImmediate(host);

        // Primary Camera0..3 overrides differ from the class defaults and from
        // both RTCameras. Inventory/HUD retain their separately accepted poses.
        [TestCase(AvatarPreviewView.Selection, 2, .75f, .3f, 1000)]
        [TestCase(AvatarPreviewView.Creation, 2, .7f, .3f, 1000)]
        [TestCase(AvatarPreviewView.Roster, .5f, .12f, .01f, 1000)]
        [TestCase(AvatarPreviewView.Hud, .5f, .2f, .001f, 100)]
        [TestCase(AvatarPreviewView.Inventory, 2.65f, 1, .3f, 1000)]
        public void ViewUsesItsSourceLensAndAddsHeightAfterLookAt(AvatarPreviewView kind, float distance, float height, float near, float far)
        {
            var target = new Vector3(18.5f, 7.25f, -12);
            Vector3 offset = kind == AvatarPreviewView.Hud
                ? new Vector3(-Mathf.Sin(20 * Mathf.Deg2Rad), 0, Mathf.Cos(20 * Mathf.Deg2Rad)) * distance
                : kind == AvatarPreviewView.Roster ? Vector3.forward * distance
                : new Vector3(0, Mathf.Sin(10 * Mathf.Deg2Rad), Mathf.Cos(10 * Mathf.Deg2Rad)) * distance;
            AvatarPreviewCamera.Apply(camera, kind, target, Quaternion.identity);
            Assert.That(camera.fieldOfView, Is.EqualTo(45));
            Assert.That(camera.nearClipPlane, Is.EqualTo(near).Within(.00001f));
            Assert.That(camera.farClipPlane, Is.EqualTo(far));
            Assert.That(Vector3.Distance(camera.transform.position, target + offset + Vector3.up * height), Is.LessThan(.00001f));
            Assert.That(Vector3.Angle(camera.transform.forward, -offset), Is.LessThan(.05f), "LookAt must use the position before fHeight is added.");
            Assert.That(Vector3.Angle(camera.transform.forward, target - camera.transform.position), Is.GreaterThan(1), "Re-aiming after the height shift changes the source composition.");
            Assert.That(camera.enabled, Is.False, "A pose change must not activate an unbound preview camera.");
        }

        [Test]
        public void RosterUsesWorldForwardWhileHudFollowsAvatarYawWithoutAUiBinding()
        {
            var focus = new Vector3(120, 1001.5f, -40);
            AvatarPreviewCamera.Apply(camera, AvatarPreviewView.Roster, focus, Quaternion.identity);
            var rosterPosition = camera.transform.position; var rosterRotation = camera.transform.rotation;
            camera.aspect = .63f;
            AvatarPreviewCamera.Apply(camera, AvatarPreviewView.Roster, focus, Quaternion.Euler(0, 90, 0));
            Assert.That(camera.targetTexture, Is.Null);
            Assert.That(Vector3.Distance(camera.transform.position, rosterPosition), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(camera.transform.rotation, rosterRotation), Is.LessThan(.05f));

            AvatarPreviewCamera.Apply(camera, AvatarPreviewView.Hud, focus, Quaternion.Euler(0, 90, 0));
            // Avatar yaw 90 plus the HUD's authored -20 yaw yields 70 degrees.
            var hudOffset = new Vector3(Mathf.Sin(70 * Mathf.Deg2Rad), 0, Mathf.Cos(70 * Mathf.Deg2Rad)) * .5f;
            Assert.That(Vector3.Distance(camera.transform.position, focus + hudOffset + Vector3.up * .2f), Is.LessThan(.00003f));
            Assert.That(Vector3.Angle(camera.transform.forward, -hudOffset), Is.LessThan(.05f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(.001f).Within(.000001f));
            Assert.That(camera.farClipPlane, Is.EqualTo(100));

            // Switching the same unbound camera back must restore the roster
            // profile rather than retaining the HUD lens or orbit.
            AvatarPreviewCamera.Apply(camera, AvatarPreviewView.Roster, focus, Quaternion.Euler(0, 90, 0));
            Assert.That(Vector3.Distance(camera.transform.position, rosterPosition), Is.LessThan(.00001f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(.01f).Within(.000001f));
            Assert.That(camera.farClipPlane, Is.EqualTo(1000));
        }

        [Test]
        public void MainPreviewZoomDoesNotChangePortraitOrInventoryDistance()
        {
            var target = new Vector3(-4, 2, 9);
            AvatarPreviewCamera.Apply(camera, AvatarPreviewView.Creation, target, Quaternion.identity, 1);
            var basePosition = camera.transform.position - Vector3.up * .7f;
            Assert.That(Vector3.Distance(basePosition, target), Is.EqualTo(2f / 3).Within(.00001f));
            foreach (var kind in new[] { AvatarPreviewView.Roster, AvatarPreviewView.Hud, AvatarPreviewView.Inventory })
            {
                AvatarPreviewCamera.Apply(camera, kind, target, Quaternion.identity, 3);
                var expected = camera.transform.position;
                AvatarPreviewCamera.Apply(camera, kind, target, Quaternion.identity, 1);
                Assert.That(Vector3.Distance(camera.transform.position, expected), Is.LessThan(.00001f), kind.ToString());
            }
        }
    }
}
