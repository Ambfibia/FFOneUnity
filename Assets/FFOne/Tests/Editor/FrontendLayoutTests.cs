using ContentPaths = FFOne.Content.ContentPaths;
using System.Linq;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class FrontendLayoutTests
    {
        [TestCase(1920, 1080)]
        [TestCase(1920, 1200)]
        [TestCase(1280, 720)]
        [TestCase(1280, 800)]
        [TestCase(1024, 768)]
        public void CompleteCharacterScreensFitBothAxes(int width, int height)
        {
            float scale = FrontendLayout.CharacterScale(width, height);
            foreach (var rect in new[] { FrontendLayout.Selection(width, height), FrontendLayout.Avatar(width, height) })
            {
                float x = width / 2f + (rect.x - width / 2f) * scale;
                float y = height / 2f + (rect.y - height / 2f) * scale;
                Assert.That(x, Is.GreaterThanOrEqualTo(0));
                Assert.That(x + rect.width * scale, Is.LessThanOrEqualTo(width));
                Assert.That(y, Is.GreaterThanOrEqualTo(0));
                Assert.That(y + rect.height * scale, Is.LessThanOrEqualTo(height));
            }
            var appearance = FrontendLayout.Centered(width, height, new Vector2(1020, 638), scale);
            Assert.That(appearance.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(appearance.y, Is.GreaterThanOrEqualTo(0));
            Assert.That(appearance.x + appearance.width * scale, Is.LessThanOrEqualTo(width));
            Assert.That(appearance.y + appearance.height * scale, Is.LessThanOrEqualTo(height));
        }

        [Test]
        public void OriginalCharacterSelectionGroupsStayCenteredAt1280By720()
        {
            Assert.That(FrontendLayout.Selection(1280, 720), Is.EqualTo(new Rect(670, 72, 478, 600)));
            Assert.That(FrontendLayout.Avatar(1280, 720), Is.EqualTo(new Rect(130, 45, 600, 600)));
            Assert.That(FrontendLayout.Chrome(1280, 720), Is.EqualTo(new Rect(-320, -360, 1920, 1440)));
            Assert.That(FrontendLayout.SlotY, Is.EqualTo(new[] { 43f, 141f, 237f, 334f }));
        }

        [TestCase(600f)]
        [TestCase(681f)]
        [TestCase(720f)]
        [TestCase(1080f)]
        public void CharacterSelectionUsesReference681PixelScale(float height)
        {
            Assert.That(FrontendLayout.CharacterScale(height), Is.EqualTo(height / 681f).Within(.000001));
            var avatar = FrontendLayout.Avatar(1280, height);
            var selection = FrontendLayout.Selection(1280, height);
            float scale = FrontendLayout.CharacterScale(height);
            Assert.That(avatar.y * scale + height / 2 * (1 - scale),
                Is.EqualTo(height * (0.5f - 315f / 681f)).Within(.0001));
            Assert.That(selection.y * scale + height / 2 * (1 - scale),
                Is.EqualTo(height * (0.5f - 288f / 681f)).Within(.0001));
        }

        [TestCase(0f)]
        [TestCase(5.419f)]
        [TestCase(5.42f)]
        [TestCase(54.199f)]
        [TestCase(54.2f)]
        [TestCase(137.5f)]
        public void PanoramicBackgroundWrapHasNoGapsAtFrameAndStripBoundaries(float time)
        {
            var positions = Enumerable.Range(0, 10).Select(i => FrontendLayout.BackgroundX(i, time)).OrderBy(x => x).ToArray();
            Assert.That(positions[0], Is.LessThanOrEqualTo(0));
            Assert.That(positions[0] + 542, Is.GreaterThan(0));
            for (int i = 1; i < positions.Length; i++) Assert.That(positions[i] - positions[i - 1], Is.EqualTo(542).Within(.002));
            Assert.That(positions[9] + 542, Is.GreaterThanOrEqualTo(1920));
        }

        [Test]
        public void ServerCoordinatesResolveOnHorizontalXYPlaneAndPreserveRegions()
        {
            var future = CharacterLocations.Resolve(614400, 153600);
            Assert.That(future.District, Is.EqualTo("Sector V"));
            Assert.That(future.Background, Is.EqualTo("future/CSFutureBG_"));
            var present = CharacterLocations.Resolve(358400, 409600);
            Assert.That(present.District, Is.EqualTo("Sector V"));
            Assert.That(present.Background, Is.EqualTo("suburbs/CSSuburbsBG_"));
            Assert.That(CharacterLocations.Resolve(-1, -1), Is.Null);
        }
    }
}
