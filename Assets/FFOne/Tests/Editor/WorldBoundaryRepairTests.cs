using System.IO;
using System.Linq;
using FFOne.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class WorldBoundaryRepairTests
    {
        [Test]
        public void RepairChangesOnlyReviewedVerticesAndKeepsSharedSources()
        {
            var receipt=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-applied.json"));
            var plan=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-repair-plan.json"));
            Assert.That(receipt.Count,Is.EqualTo(8));
            foreach(var cell in receipt.Properties())
            {
                string originalPath=FFOne.Content.ContentPaths.Resolve((string)cell.Value["originalMesh"]);
                var original=AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
                var repaired=AssetDatabase.LoadAssetAtPath<Mesh>((string)cell.Value["meshPath"]);
                Assert.That(EnvironmentWorldWorkspace.Hash(originalPath),Is.EqualTo((string)cell.Value["originalHash"]));
                Assert.That(repaired.triangles,Is.EqualTo(original.triangles));Assert.That(repaired.uv,Is.EqualTo(original.uv));
                var expected=plan["cells"][cell.Name]["changes"].Select(v=>(int)v["index"]).ToArray();
                var a=original.vertices;var b=repaired.vertices;
                Assert.That(Enumerable.Range(0,a.Length).Where(i=>a[i]!=b[i]).ToArray(),Is.EquivalentTo(expected));
                foreach(int i in expected){Assert.That(a[i].x,Is.EqualTo(b[i].x));Assert.That(a[i].z,Is.EqualTo(b[i].z));}
            }
        }
        [Test]
        public void RepeatingReviewedRepairLeavesAdoptedScenesUnchanged()
        {
            var receipt=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-applied.json"));
            var hashes=receipt.Properties().ToDictionary(c=>c.Name,c=>EnvironmentWorldWorkspace.Hash(EnvironmentWorldWorkspace.ScenePath(c.Name)));
            WorldBoundaryInvestigation.ApplyReviewedRepairs();
            foreach(var pair in hashes)Assert.That(EnvironmentWorldWorkspace.Hash(EnvironmentWorldWorkspace.ScenePath(pair.Key)),Is.EqualTo(pair.Value));
        }
        [Test]
        public void ReviewedSharedCoordinatesNowHaveOneHeight()
        {
            var plan=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-repair-plan.json"));
            var after=JObject.Parse(File.ReadAllText("Tools/ContentMigration/world-boundary-surfaces-after.json"));
            foreach(var point in plan["points"])
            {
                var heights=after.Properties().SelectMany(c=>c.Value["boundary"])
                    .Where(v=>Mathf.Abs((float)v[1]-(float)point["x"])<.001f && Mathf.Abs((float)v[3]-(float)point["z"])<.001f)
                    .Select(v=>(float)v[2]).ToArray();
                Assert.That(heights.Length,Is.GreaterThanOrEqualTo(2));Assert.That(heights.Max()-heights.Min(),Is.LessThan(.0001f));
            }
        }
    }
}
