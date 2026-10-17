using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using FFOne.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class EnvironmentLibraryTests
    {
        [TestCase("nuclearplant_wall_01", "Buildings/Industrial")]
        [TestCase("sb_otnp_generatingplant_01", "Buildings/Industrial")]
        [TestCase("etc_streetlight_01", "StreetFurniture/Lights")]
        [TestCase("etc_busstop01", "StreetFurniture/BusStops")]
        [TestCase("treehouse_01", "Buildings/Residential")]
        [TestCase("etc_tree_01", "Vegetation/Trees")]
        [TestCase("etc_plant_02", "Vegetation/Shrubs")]
        [TestCase("baseballbench", "StreetFurniture/Benches")]
        [TestCase("etc_collision", "Technical/Collision")]
        [TestCase("editable_mesh", "Review/Unclassified")]
        [TestCase("default_water_e01_editable_mesh", "Water/Surfaces")]
        [TestCase("wd_juru_piller", "Architecture/Columns")]
        public void AmbiguousLegacyNamesHaveSpecificCategories(string name, string expected)
            => Assert.That(EnvironmentAssetRules.Classify("objects/unclassified/" + name + "/models/" + name + "/visual"), Is.EqualTo(expected));

        [TestCase("characters/mobs/mob_almonster/model")]
        [TestCase("characters/fusions/fusion_dexter/model")]
        [TestCase("characters/npcs/npc_dexter/model")]
        [TestCase("characters/player/hnpc/banker/model")]
        [TestCase("data/hnpc/catalog")]
        public void ServerActorsNeverEnterEnvironmentLibrary(string identity)
        {
            Assert.That(EnvironmentAssetRules.IsEnvironment(identity), Is.False);
            Assert.Throws<ArgumentException>(() => EnvironmentAssetRules.Classify(identity));
        }
        [Test]
        public void DensityPreservesSourceZRowsAndCounts()
        {
            string path = Path.Combine(Path.GetTempPath(), "ffone-density-" + Guid.NewGuid().ToString("N") + ".png");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels32(new[] { new Color32(3,3,3,255), new Color32(4,4,4,255), new Color32(1,1,1,255), new Color32(2,2,2,255) });
                texture.Apply(); File.WriteAllBytes(ContentPaths.FilePath(path), texture.EncodeToPNG());
                var result = TerrainVegetationImport.ReadDensity(path, 2, 10);
                Assert.That(result[0,0], Is.EqualTo(1)); Assert.That(result[0,1], Is.EqualTo(2));
                Assert.That(result[1,0], Is.EqualTo(3)); Assert.That(result[1,1], Is.EqualTo(4));
                Assert.Throws<InvalidDataException>(() => TerrainVegetationImport.ReadDensity(path, 2, 11));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); if (File.Exists(ContentPaths.FilePath(path))) File.Delete(ContentPaths.FilePath(path)); }
        }
        [Test]
        public void ShiftedTerrainIsNotSilentlyFlattenedIntoHeightmap()
        {
            var definition = JObject.Parse("{ 'heightmap': { 'vertexShifts': [ { 'row': 1, 'column': 2, 'flags': 8 } ] } }");
            Assert.That(TerrainVegetationImport.Incompatibility(definition), Is.Not.Null);
        }
        [Test]
        public void DuplicateNamesInDifferentSourceSetsDoNotCollide()
        {
            string a = EnvironmentAssetRules.DirectoryFor("objects/nature/tree/models/tree/visual", "Vegetation/Trees");
            string b = EnvironmentAssetRules.DirectoryFor("objects/collections/tree/models/tree/visual", "Vegetation/Trees");
            Assert.That(a, Is.Not.EqualTo(b));
        }
        [Test]
        public void TreePositionHeightYawAndScaleSurviveWithoutSurfaceSnapping()
        {
            var root = new GameObject("terrain-test"); var data = new TerrainData { heightmapResolution = 33, size = new Vector3(512, 600, 512) };
            try
            {
                var terrain = root.AddComponent<Terrain>(); terrain.terrainData = data; root.transform.position = new Vector3(1024, -300, 1536);
                var position = new Vector3(1100, -12, 1700); var rotation = Quaternion.Euler(0, 73, 0); var scale = new Vector3(1.5f, 2, 1.5f);
                Assert.That(TerrainTreeAuthoring.TryTransform(position, rotation, scale, terrain, out var tree), Is.True);
                Assert.That((root.transform.position + Vector3.Scale(tree.position, data.size) - position).magnitude, Is.LessThan(.0002f));
                Assert.That(tree.rotation * Mathf.Rad2Deg, Is.EqualTo(73).Within(.001));
                Assert.That(tree.widthScale, Is.EqualTo(1.5f)); Assert.That(tree.heightScale, Is.EqualTo(2));
                Assert.That(TerrainTreeAuthoring.TryTransform(position, Quaternion.Euler(20, 73, 0), scale, terrain, out _), Is.False);
                Assert.That(TerrainTreeAuthoring.TryTransform(position, rotation, new Vector3(1, 2, 3), terrain, out _), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(data); }
        }
    }
}
