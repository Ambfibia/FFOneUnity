using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using FFOne.Editor;
using FFOne.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class EnvironmentCompoundTests
    {
        string directory;
        [SetUp] public void Setup() { directory = "Assets/__CompoundTest_" + Guid.NewGuid().ToString("N"); EnvironmentLibraryBuilder.Folder(directory); }
        [TearDown] public void Cleanup() { AssetDatabase.DeleteAsset(ContentPaths.Resolve(directory)); }
        [Test] public void ConnectingNestedPartsPreservesObjectReferencesWorldPoseAndOverrides()
        {
            var template = new GameObject("model"); template.AddComponent<BoxCollider>();
            var nested = PrefabUtility.SaveAsPrefabAsset(template, ContentPaths.Resolve(directory + "/part.prefab")); Object.DestroyImmediate(template);
            var original = new GameObject("whole");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(nested); visual.name = "visual_000"; visual.transform.SetParent(original.transform);
            var collision = new GameObject("collision_001"); collision.transform.SetParent(original.transform); collision.AddComponent<BoxCollider>();
            original.AddComponent<EnvironmentCompound>().Parts = new[] { new EnvironmentCompound.Part { Role = "visual", Target = visual.transform }, new EnvironmentCompound.Part { Role = "collision", Target = collision.transform } };
            var prefab = PrefabUtility.SaveAsPrefabAsset(original, ContentPaths.Resolve(directory + "/whole.prefab")); Object.DestroyImmediate(original);
            var instance = new GameObject("authored");
            try
            {
                instance.transform.SetPositionAndRotation(new Vector3(1927, -57, 3012), Quaternion.Euler(0, 138, 0));
                var child = (GameObject)PrefabUtility.InstantiatePrefab(nested); child.name = "visual_000"; child.transform.SetParent(instance.transform, false);
                child.transform.localPosition = new Vector3(1, 2, 3); child.GetComponent<BoxCollider>().enabled = false;
                var col = new GameObject("collision_001"); col.transform.SetParent(instance.transform, false); col.AddComponent<BoxCollider>().size = new Vector3(4, 5, 6);
                var parts = new[] { child.transform, col.transform }; var matrices = parts.Select(p => p.localToWorldMatrix).ToArray();
                EnvironmentCompoundBuilder.Connect(instance, prefab, "fixture#42", parts, new[] { "source visual", "source collision" }, matrices);
                Assert.That(instance.GetComponent<EnvironmentCompound>().Parts[0].Target, Is.SameAs(child.transform));
                Assert.That(child.GetComponent<BoxCollider>().enabled, Is.False);
                Assert.That(col.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(4, 5, 6)));
                Assert.That(ContentPaths.Legacy(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance)), Is.EqualTo(directory + "/whole.prefab"));
                Assert.That(ContentPaths.Legacy(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child)), Is.EqualTo(directory + "/part.prefab"));
                Assert.That(instance.GetComponent<EnvironmentCompound>().SourceRoot, Is.EqualTo("fixture#42"));
            }
            finally { Object.DestroyImmediate(instance); }
        }
        [Test] public void OriginalTruckOwnershipIncludesFivePartsAndItsOwnCollider()
        {
            var doc = EnvironmentCompoundBuilder.Read("map_03_05");
            var group = doc.Groups.Single(g => g.SourceRoot == "BuildPlayer-Map_03_05#3105");
            Assert.That(group.Parts.Count(p => p.Role == "visual"), Is.EqualTo(5));
            Assert.That(group.Parts.Count(p => p.Role == "collision"), Is.EqualTo(1));
            Assert.That(group.Parts.Any(p => p.SourceNode == "BuildPlayer-Map_03_05#3942"), Is.False, "Nearby cargo is a separate source object.");
            Assert.That(Vector3.Distance(EnvironmentCompoundBuilder.Matrix(group.RootChain).GetColumn(3), new Vector3(1927.5951f, -57.0351f, 3012.1658f)), Is.LessThan(.001f));
        }
        [Test] public void OriginalMachineBuildingHasOneRootForAll56VisualParts()
        {
            var group = EnvironmentCompoundBuilder.Read("map_03_05").Groups.Single(g => g.SourceRoot == "BuildPlayer-Map_03_05#7597");
            Assert.That(group.Parts.Count(p => p.Role == "visual"), Is.EqualTo(56));
            Assert.That(group.Parts.Count(p => p.Role == "collision"), Is.EqualTo(1));
        }
        [Test] public void AcceptedScaleAndHeightRepairsTakePrecedenceOverOriginalBadTransforms()
        {
            var root = Matrix4x4.TRS(new Vector3(100, -69, 200), Quaternion.Euler(0, 135, 0), Vector3.one * 1.22f);
            var local = new[] { Matrix4x4.Scale(Vector3.one * 42.7f), Matrix4x4.Scale(Vector3.one * 52.094f) };
            var corrected = Matrix4x4.TRS(new Vector3(100, -51, 200), Quaternion.Euler(0, 135, 0), Vector3.one * 1.22f);
            var result = EnvironmentCompoundBuilder.ApplyAcceptedTransforms(root, local, new[] { corrected, corrected }, out var parts);
            Assert.That(Vector3.Distance(result.GetColumn(3), corrected.GetColumn(3)), Is.LessThan(.001f));
            for (int p = 0; p < parts.Length; p++)
                for (int i = 0; i < 16; i++) Assert.That(Mathf.Abs((result * parts[p])[i] - corrected[i]), Is.LessThan(.001f));
            Assert.That(parts[0].lossyScale.x, Is.EqualTo(1).Within(.001f));
        }
        [Test] public void EmptyLegacyQuaternionPreservesTranslationAndScaleWithoutLoggingErrors()
        {
            var matrix = EnvironmentCompoundBuilder.Matrix(new[] { new EnvironmentCompoundBuilder.Pose {
                Position = new[] { 1f, 2f, 3f }, Rotation = new float[4], Scale = new[] { 2f, 2f, 2f } } });
            Assert.That(matrix, Is.EqualTo(Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.identity, Vector3.one * 2)));
        }
    }
}
