using ContentPaths = FFOne.Content.ContentPaths;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NetworkWorldContractTests
    {
        [Test]
        public void ProtocolPositionUsesCentimetresAndZUpWithoutBevyReflection()
        {
            var unity = WorldCoordinates.FromServer(125, 375, -250);
            Assert.AreEqual(new Vector3(1.25f, -2.5f, 3.75f), unity);
            Assert.AreEqual(new Vector3Int(125, 375, -250), WorldCoordinates.ToServer(unity));
            // Legacy casts truncate toward zero; rounding negative coordinates changes tile-boundary behavior.
            Assert.AreEqual(new Vector3Int(-125, 375, -250), WorldCoordinates.ToServer(new Vector3(-1.259f, -2.509f, 3.759f)));
        }

        [TestCase(-180, 0f)]
        [TestCase(-90, 90f)]
        [TestCase(0, 180f)]
        [TestCase(90, 270f)]
        [TestCase(179, 359f)]
        public void EntryHeadingAndOutgoingHeadingAreInverse(int protocol, float unity)
        {
            Assert.AreEqual(unity, WorldCoordinates.FromServerHeading(protocol));
            Assert.AreEqual(protocol, WorldCoordinates.ToServerHeading(unity));
        }

        [Test]
        public void HeadingWrapsAndTruncatesBeforeTheProtocolHalfTurn()
        {
            Assert.AreEqual(-90, WorldCoordinates.ToServerHeading(450.9f));
            Assert.AreEqual(90, WorldCoordinates.ToServerHeading(-89.1f));
            Assert.AreEqual(0, WorldCoordinates.FromServerHeading(180));
        }

        [TestCase(0, 0, 0)]
        [TestCase(0, 1, 1)]
        [TestCase(1, 1, 2)]
        [TestCase(1, 0, 3)]
        [TestCase(1, -1, 4)]
        [TestCase(0, -1, 5)]
        [TestCase(-1, -1, 6)]
        [TestCase(-1, 0, 7)]
        [TestCase(-1, 1, 8)]
        public void DirectionUsesLegacyCompassKeys(float side, float forward, byte expected)
            => Assert.AreEqual(expected, WorldCoordinates.Direction(side, forward));

        [Test]
        public void CoverageRequiresOverworldAndActualImportedTileInterior()
        {
            var catalog = ScriptableObject.CreateInstance<WorldCatalog>();
            try
            {
                Assert.IsFalse(NetworkWorldController.HasCoverage(null, 0, Vector3.zero));
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 0, Vector3.zero));
                catalog.Tiles = new[]
                {
                    new WorldTileReference { Id = "a", Minimum = new Vector3(-100, -10, -200), Maximum = new Vector3(0, 10, -100),
                        OverflowCenters = new[] { new Vector2(500, 500) } },
                    new WorldTileReference { Id = "b", Minimum = new Vector3(0, -10, -200), Maximum = new Vector3(100, 10, -100) }
                };
                Assert.IsTrue(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(-100, 9000, -200)), "Minimum edges included; elevation does not select the tile.");
                Assert.IsTrue(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(0, 0, -150)), "Shared boundary belongs to the next tile.");
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(100, 0, -150)), "Maximum X is excluded.");
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(-50, 0, -100)), "Maximum Z is excluded.");
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(-100.01f, 0, -150)), "Nearby is not covered.");
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 0, new Vector3(500, 0, 500)), "Distant visual overflow does not establish playable map coverage.");
                Assert.IsFalse(NetworkWorldController.HasCoverage(catalog, 1, new Vector3(-50, 0, -150)), "Overworld geometry cannot stand in for an instance.");
            }
            finally { Object.DestroyImmediate(catalog); }
        }
    }
}
