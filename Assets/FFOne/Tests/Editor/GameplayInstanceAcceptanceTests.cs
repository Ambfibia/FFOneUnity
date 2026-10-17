using System.Linq;
using FFOne.Content;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class GameplayInstanceAcceptanceTests
    {
        [Test] public void EveryInstanceIdentityResolvesItsPublishedSceneAndColliderContent()
        {
            var routes = WorldTravelCatalog.Load();
            var world = ContentAssets.Load<WorldCatalog>("World/Catalog");
            var maps = routes.Maps.Where(m => m.Key > 0).ToArray();
            Assert.GreaterOrEqual(maps.Length, 151);
            Assert.Less(maps.Select(m => m.Value).Distinct().Count(), maps.Length,
                "Map identities remain separate even when the original game reuses a cell.");
            foreach (var map in maps)
            {
                var candidates = world.Tiles.Where(t => t.X == map.Value.X && t.Z == map.Value.Z).ToArray();
                Assert.AreEqual(1, candidates.Length, "Map " + map.Key + " has one original geometry route.");
                Assert.IsNotEmpty(candidates[0].SceneAddress, "Map " + map.Key + " has a published authored scene.");
                Assert.Greater(candidates[0].Visuals, 0, "Map " + map.Key + " has authored visuals.");
                Assert.Greater(candidates[0].Colliders, 0, "Map " + map.Key + " has authored collider content.");
            }
        }

        [Test] public void InstanceFilterKeepsOnlyItsOriginalCellEvenWhenNearbyOverworldCellsExist()
        {
            var owner = new GameObject("Instance admission test");
            try
            {
                var stream = owner.AddComponent<WorldStreamer>();
                var world = ContentAssets.Load<WorldCatalog>("World/Catalog");
                foreach (var map in WorldTravelCatalog.Load().Maps.Where(m => m.Key > 0))
                {
                    stream.MapNumber = map.Key;
                    CollectionAssert.AreEqual(world.Tiles.Where(t => t.X == map.Value.X && t.Z == map.Value.Z).Select(t => t.Id),
                        world.Tiles.Where(stream.Includes).Select(t => t.Id), "Map " + map.Key + " admission cannot include an unrelated neighboring cell.");
                }
                stream.MapNumber = int.MaxValue;
                Assert.IsFalse(world.Tiles.Any(stream.Includes), "An unknown instance never falls back to overworld geometry.");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test] public void RepairedMapDeclarationsAdmitExactPortalPositionsWhileUnrelatedMismatchesStayBlocked()
        {
            var routes = WorldTravelCatalog.Load();
            var world = ContentAssets.Load<WorldCatalog>("World/Catalog");
            foreach (int id in new[] { 282, 286 })
            {
                var warp = routes.Warps.Single(w => w.Id == id);
                Assert.IsFalse(NetworkWorldController.HasCoverage(world, warp.Map, WorldCoordinates.FromServer(warp.X, warp.Y, warp.Z)),
                    "Original warp " + id + " must not silently admit the wrong scene.");
            }
            var alternate = routes.Warps.Single(w => w.Id == 284);
            Assert.AreEqual(135, alternate.Map);
            Assert.IsTrue(NetworkWorldController.HasCoverage(world, alternate.Map, WorldCoordinates.FromServer(alternate.X, alternate.Y, alternate.Z)));
            foreach (int id in new[] { 305, 307, 309, 317, 319, 321, 322, 323 })
            {
                var repaired = routes.Warps.Single(w => w.Id == id);
                Assert.IsTrue(NetworkWorldController.HasCoverage(world, repaired.Map, WorldCoordinates.FromServer(repaired.X, repaired.Y, repaired.Z)),
                    "Reviewed production warp " + id + " resolves its exact published geometry.");
            }
            Assert.IsFalse(routes.Maps.ContainsKey(150), "A stale reference is repaired without inventing a new map identity.");
            Assert.IsFalse(routes.Warps.Any(w => w.Map == 150));
            Assert.AreEqual(149, routes.Warps.Single(w => w.Id == 317).Map);
            foreach (var (id, donor, map) in new[] { (321, 5, 4), (322, 291, 136), (323, 319, 152) })
            {
                var repaired = routes.Warps.Single(w => w.Id == id);
                var original = routes.Warps.Single(w => w.Id == donor);
                Assert.AreEqual(map, repaired.Map);
                Assert.AreEqual((original.X, original.Y, original.Z, original.Level, original.Task,
                    original.RequiredType, original.RequiredItem, original.ConsumedType, original.ConsumedItem, original.Cost),
                    (repaired.X, repaired.Y, repaired.Z, repaired.Level, repaired.Task,
                    repaired.RequiredType, repaired.RequiredItem, repaired.ConsumedType, repaired.ConsumedItem, repaired.Cost));
            }
        }
    }
}
