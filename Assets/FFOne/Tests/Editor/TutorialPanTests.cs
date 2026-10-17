using ContentPaths = FFOne.Content.ContentPaths;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialPanTests
    {
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        [TestCase(1024, 768)]
        public void EightSquareFramesFillTheViewportAtBothEndsWithoutGaps(float width, float height)
        {
            var viewport = new Rect(15, 30, width, height);
            Assert.That(TutorialPanPlayer.FrameRect(viewport, 0, 0).xMin, Is.EqualTo(viewport.xMin));
            Assert.That(TutorialPanPlayer.FrameRect(viewport, 7, 23).xMax, Is.EqualTo(viewport.xMax).Within(.01f));
            foreach (float time in new[] { 0, 8, 21.5f, 23 })
            {
                Rect previous = TutorialPanPlayer.FrameRect(viewport, 0, time);
                for (int i = 1; i < 8; i++)
                {
                    Rect current = TutorialPanPlayer.FrameRect(viewport, i, time);
                    Assert.That(current.width, Is.EqualTo(height));
                    Assert.That(current.height, Is.EqualTo(height));
                    Assert.That(current.y, Is.EqualTo(viewport.y));
                    Assert.That(current.xMin, Is.EqualTo(previous.xMax).Within(.01f));
                    previous = current;
                }
            }
        }

        [Test]
        public void PanClockStopsAndReplaysFromStartWhileReleaseKeepsResourcesReusable()
        {
            using var player = new TutorialPanPlayer();
            player.Preload();
            Assert.That(player.Loaded, Is.True);
            Assert.That(player.Active, Is.False);
            player.Start(); player.Tick(8); player.Tick(-5); player.Tick(float.NaN);
            Assert.That(player.ElapsedSeconds, Is.EqualTo(8));
            player.Stop(); player.Tick(5);
            Assert.That(player.ElapsedSeconds, Is.EqualTo(8));
            player.Release(); Assert.That(player.Loaded, Is.False);
            player.Start(); Assert.That(player.ElapsedSeconds, Is.Zero); Assert.That(player.Active, Is.True);
        }
    }
}
