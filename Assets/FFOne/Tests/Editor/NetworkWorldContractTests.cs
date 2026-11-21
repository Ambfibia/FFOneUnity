using ContentPaths = FFOne.Content.ContentPaths;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class NetworkWorldContractTests
    {
        [Test]
        public void PublishedKndZiplineUsesItsNativeVolumeAndLeavesTheActiveCell()
        {
            const string path = "Assets/FFOne/Worlds/Overworld/Cells/map_12_15/CellDefinition.asset";
            var asset = AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(path);
            Assert.IsNotNull(asset);
            Assert.IsNotNull(asset.Effects);
            var data = JObject.Parse(asset.Effects.Behaviour);
            var trigger = data["triggers"].First(t => (string)t["kind"] == "zipline" && (bool)t["enabled"]);
            var volume = data["triggerVolumes"].First(v => (string)v["node"] == (string)trigger["node"] && (bool)v["isTrigger"]);
            var root = new GameObject("KND zipline contract");
            var camera = root.AddComponent<Camera>();
            var fixture = new JObject { ["triggers"] = new JArray(trigger), ["triggerVolumes"] = new JArray(volume) };
            try
            {
                var owner = WorldBehaviour.Attach(root, fixture.ToString(), new(), new(), camera);
                var matrix = WorldBehaviour.NativeMatrix(trigger["worldMatrix"]);
                var center = matrix.MultiplyPoint3x4(WorldBehaviour.NativeVector(volume["center"]));
                var route = WorldZipline.Find(camera, center);
                Assert.IsNotNull(route, "The original volume admits a player at its local center.");
                Assert.AreSame(owner, route.Owner);
                Assert.That(Vector3.Distance(route.Start, route.End), Is.GreaterThan(.01f));
                Assert.That(route.Speed, Is.EqualTo((float)trigger["velocity"]));
                Assert.IsNull(WorldZipline.Find(camera, center + Vector3.up * 100));
                root.SetActive(false);
                Assert.IsNull(WorldZipline.Find(camera, center), "An unloading cell must never admit a ride.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(5f, false, true)]
        [TestCase(5f, true, false)]
        [TestCase(20f, false, false)]
        public void ZiplineMovementReplicatesAttachmentOnlyDuringTheRide(float moved, bool detached, bool attached)
        {
            var request = ProtocolZipline.Movement((100, 200, 300), (400, 500, 600),
                (.25f, -.5f, .75f), moved, 20f, -24, 600, detached);
            Assert.AreEqual(76, request.Length);
            Assert.AreEqual(100, Protocol0104.Read32(request, 8));
            Assert.AreEqual(.75f, BitConverter.Int32BitsToSingle(Protocol0104.Read32(request, 52)));
            Assert.AreEqual(detached ? 1 : 0, Protocol0104.Read32(request, 56));
            var response = new byte[88];
            Array.Copy(request, response, request.Length);
            Protocol0104.Write32(response, 76, 12);
            Protocol0104.Write64(response, 80, 99L);
            Assert.IsTrue(ProtocolWorld.TryDecode(new ProtocolFrame(ProtocolZipline.Broadcast, response), out var updates));
            var entity = updates.Single().Entity;
            Assert.AreEqual((12, 400, 500, 600, -24, 600), (entity.Id, entity.X, entity.Y, entity.Z, entity.Angle, entity.Speed));
            Assert.AreEqual(99UL, entity.ServerTime);
            Assert.AreEqual(attached, entity.Ziplining);
            Assert.AreEqual(!attached, entity.Jumping);
        }

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
