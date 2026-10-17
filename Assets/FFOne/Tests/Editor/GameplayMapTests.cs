using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayMapTests
    {
        [Test]
        public void SourceMapImagesResolveThroughImportedUxml()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/FFOne/Content/UI/Views/Retrobution/WorldMap.uxml");
            Assert.That(asset, Is.Not.Null);
            var tree = asset.CloneTree();
            foreach (string name in new[] { "map-image", "map-player", "map-frame", "map-unavailable",
                "map-payzone-0", "map-payzone-1", "map-payzone-2", "map-payzone-3",
                "map-freezone-0", "map-freezone-1", "map-darkzone-0", "map-darkzone-1" })
            {
                var image = tree.Q<Image>(name);
                Assert.That(image, Is.Not.Null, name);
                Assert.That(RetrobutionImage.Bind(image), Is.Not.Null, name + " must resolve the source PNG through the imported USS asset URL.");
                Assert.That(image.style.backgroundImage.value.texture, Is.Null, "Background must be cleared before UV cropping.");
                Assert.That(image.image.width, Is.GreaterThan(1));
            }
            foreach (string name in new[] { "zoom-in", "zoom-out", "pan-up", "pan-down", "pan-left", "pan-right", "my-view", "world-view" })
                Assert.That(tree.Q<Button>(name), Is.Not.Null, name);
        }

        [Test]
        public void SourceZoneSelectionDoesNotTreatInstanceIdsAsRegionIndices()
        {
            Assert.That(RetrobutionMapLayout.Zone(0, new Vector3(6000, 0, 1000)), Is.EqualTo(RetrobutionMapLayout.Future));
            Assert.That(RetrobutionMapLayout.Zone(0, new Vector3(2000, 0, 6000)), Is.EqualTo(RetrobutionMapLayout.Dark));
            Assert.That(RetrobutionMapLayout.Zone(0, new Vector3(1000, 0, 1000)), Is.EqualTo(RetrobutionMapLayout.Tutorial));
            Assert.That(RetrobutionMapLayout.Zone(0, new Vector3(4000, 0, 4000)), Is.EqualTo(RetrobutionMapLayout.Overworld));
            Assert.That(RetrobutionMapLayout.Zone(1, new Vector3(6000, 0, 1000)), Is.EqualTo(RetrobutionMapLayout.Unavailable));
        }

        [Test]
        public void SourceUvClampsRegionsAndKeepsBottomOriginMarker()
        {
            Rect full = RetrobutionMapLayout.Camera(RetrobutionMapLayout.Overworld, 0, new Vector2(.2f, .9f));
            Assert.That(full, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Rect zoom = RetrobutionMapLayout.Camera(RetrobutionMapLayout.Future, 2, Vector2.one);
            Rect bounds = RetrobutionMapLayout.Bounds(RetrobutionMapLayout.Future);
            Assert.That(zoom.xMin, Is.EqualTo(bounds.xMin).Within(.00001f));
            Assert.That(zoom.yMax, Is.EqualTo(bounds.yMax).Within(.00001f));
            Assert.That(zoom.width, Is.EqualTo(.25f));
            Assert.That(zoom.height, Is.EqualTo(.25f * .6568421f));
            var top = RetrobutionMapLayout.Marker(new Vector3(4096, 0, 8192), full, new Rect(0,0,950,624));
            Assert.That(top.x, Is.EqualTo(467)); Assert.That(top.y, Is.EqualTo(-8));
        }

        [Test]
        public void SourceWorldAspectUsesOneThirdOffsetAndTwoThirdExtentReduction()
        {
            Rect rect = RetrobutionMapLayout.DrawRect(RetrobutionMapLayout.Overworld, 0, 2048, 2048);
            Assert.That(rect.x, Is.EqualTo((950f - 624) / 3).Within(.0001f));
            Assert.That(rect.width, Is.EqualTo(950f - (950f - 624) * 2 / 3).Within(.0001f));
            Assert.That(rect.height, Is.EqualTo(624));
            Assert.That(RetrobutionMapLayout.DrawRect(RetrobutionMapLayout.Overworld, 1, 2048,2048), Is.EqualTo(new Rect(0,0,950,624)));
        }
    }
}
