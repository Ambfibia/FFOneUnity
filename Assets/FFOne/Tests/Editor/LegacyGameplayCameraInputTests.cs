using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class LegacyGameplayCameraInputTests
    {
        [Test]
        public void CameraAdaptsPixelMotionThroughTheOriginalMouseAxesAndSensitivity()
        {
            var look = LegacyGameplayCameraInput.Look(new Vector2(100, 50));
            Assert.That(look.x, Is.EqualTo(36).Within(.0001));
            Assert.That(look.y, Is.EqualTo(-9).Within(.0001));
            var configured = LegacyGameplayCameraInput.Look(new Vector2(100, 50), .5f, true);
            Assert.That(configured.x, Is.EqualTo(18).Within(.0001));
            Assert.That(configured.y, Is.EqualTo(4.5f).Within(.0001));
        }
    }
}
