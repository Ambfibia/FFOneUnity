using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Editor;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class EnvironmentSharingTests
    {
        string identity, directory, previousIndex;
        HashSet<string> originalPool;
        readonly List<string> publications = new();
        [SetUp] public void Setup()
        {
            previousIndex = File.Exists(ContentPaths.FilePath(EnvironmentSharedAssets.IndexPath)) ? File.ReadAllText(ContentPaths.FilePath(EnvironmentSharedAssets.IndexPath)) : null;
            publications.Clear();
            identity = "objects/test/__EnvironmentSharing_" + Guid.NewGuid().ToString("N");
            directory = "Assets/Resources/" + identity;
            EnvironmentLibraryBuilder.Folder(directory + "/models/a");
            EnvironmentLibraryBuilder.Folder(directory + "/models/b");
            originalPool = ContentPaths.Legacy(AssetDatabase.GetAllAssetPaths()).Where(p => p.StartsWith(EnvironmentSharedAssets.Root + "/")).ToHashSet();
        }
        [TearDown] public void Cleanup()
        {
            var index = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeAssetPublisher.DefaultDestination + "/Index.asset"));
            if (index) { index.Bindings = index.Bindings.Where(b => !b.OriginalResource.StartsWith(identity + "/")).ToArray(); EditorUtility.SetDirty(index); AssetDatabase.SaveAssetIfDirty(index); }
            foreach (string path in publications.Distinct()) AssetDatabase.DeleteAsset(ContentPaths.Resolve(path));
            AssetDatabase.DeleteAsset(ContentPaths.Resolve(NativeAssetPublisher.ManifestRoot + "/" + identity));
            AssetDatabase.DeleteAsset(ContentPaths.Resolve(directory));
            foreach (string folder in publications.Select(Path.GetDirectoryName).Distinct())
                if (folder.Replace('\\', '/').EndsWith("/" + identity.Split('/').Last(), StringComparison.Ordinal)) AssetDatabase.DeleteAsset(ContentPaths.Resolve(folder));
            foreach (string path in ContentPaths.Legacy(AssetDatabase.GetAllAssetPaths()).Where(p => p.StartsWith(EnvironmentSharedAssets.Root + "/") && !originalPool.Contains(p)).OrderByDescending(p => p.Length)) AssetDatabase.DeleteAsset(ContentPaths.Resolve(path));
            if (previousIndex != null) File.WriteAllText(ContentPaths.FilePath(EnvironmentSharedAssets.IndexPath), previousIndex);
            EnvironmentSharedAssets.ResetIndex();
        }
        string Fixture(string name, float extent = 1)
        {
            EnvironmentLibraryBuilder.Folder(directory + "/models/" + name);
            string path = directory + "/models/" + name + "/model.glb";
            NativeAssetPublisherTests.WriteFixture(path, extent);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(path), ImportAssetOptions.ForceSynchronousImport);
            publications.Add(NativeAssetPublisher.Preflight(path).Directory);
            return path;
        }
        [Test] public void EqualResourcesShareAcrossModelsAndAuthoredChangesSurviveReimport()
        {
            string a = Fixture("a"), b = Fixture("b");
            var prefabs = NativeAssetPublisher.PublishBatch(new[] { a, b });
            var first = prefabs[0].GetComponentInChildren<SkinnedMeshRenderer>();
            var second = prefabs[1].GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(first.sharedMesh, Is.SameAs(second.sharedMesh));
            Assert.That(first.sharedMaterial, Is.SameAs(second.sharedMaterial));
            Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(first.sharedMesh)), Does.StartWith(EnvironmentSharedAssets.Root));
            string meshGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(first.sharedMesh))));
            first.sharedMaterial.renderQueue = 2459;
            first.sharedMesh.vertices = new[] { Vector3.zero, Vector3.right * 7, Vector3.up };
            EditorUtility.SetDirty(first.sharedMaterial); EditorUtility.SetDirty(first.sharedMesh); AssetDatabase.SaveAssets();
            var plan = NativeAssetPublisher.Preflight(a);
            var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(plan.ManifestPath)));
            manifest.SourceDependencyHash = "previous importer revision";
            File.WriteAllText(ContentPaths.FilePath(plan.ManifestPath), JsonConvert.SerializeObject(manifest));
            foreach (string source in new[] { a, b }) AssetDatabase.ImportAsset(ContentPaths.Resolve(source), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            NativeAssetPublisher.PublishBatch(new[] { a, b });
            Assert.That(first.sharedMaterial.renderQueue, Is.EqualTo(2459));
            Assert.That(first.sharedMesh.vertices[1].x, Is.EqualTo(7));
            Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(first.sharedMesh)))), Is.EqualTo(meshGuid));
            Assert.That(NativeAssetPublisher.Preflight(a).AlreadyPublished, Is.True);
            var fresh = NativeAssetPublisher.Publish(Fixture("c")).GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(fresh.sharedMesh, Is.Not.SameAs(first.sharedMesh), "An edited shared mesh must not be reused for unchanged source geometry.");
            Assert.That(Mathf.Abs(fresh.sharedMesh.vertices[1].x), Is.EqualTo(1));
            Assert.That(fresh.sharedMaterial.renderQueue, Is.Not.EqualTo(2459));
            Assert.That(first.sharedMaterial.renderQueue, Is.EqualTo(2459));
        }
        [Test] public void DifferentGeometryKeepsItsMeshWhileMaterialsAreShared()
        {
            var prefabs = NativeAssetPublisher.PublishBatch(new[] { Fixture("a"), Fixture("b", 3) });
            var first = prefabs[0].GetComponentInChildren<SkinnedMeshRenderer>();
            var second = prefabs[1].GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(first.sharedMesh, Is.Not.SameAs(second.sharedMesh));
            Assert.That(first.sharedMaterial, Is.SameAs(second.sharedMaterial));
        }
        [Test] public void FingerprintIgnoresNamesButKeepsMaterialAndVertexDifferences()
        {
            string source = Fixture("a");
            var material = AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(source)).OfType<Material>().Single();
            var copy = Object.Instantiate(material);
            try
            {
                copy.name = "Another source name";
                Assert.That(EnvironmentSharedAssets.Fingerprint(copy), Is.EqualTo(EnvironmentSharedAssets.Fingerprint(material)));
                copy.renderQueue += 1;
                Assert.That(EnvironmentSharedAssets.Fingerprint(copy), Is.Not.EqualTo(EnvironmentSharedAssets.Fingerprint(material)));
            }
            finally { Object.DestroyImmediate(copy); }
        }
        [Test] public void FamilyGroupingOnlyStripsTheExporterVariantSuffix()
        {
            Assert.That(EnvironmentLibraryGrouping.FamilyKey("objects/furniture/bench/models/bench_variant_0002/visual"), Is.EqualTo("objects/furniture/bench/models/bench/visual"));
            Assert.That(EnvironmentLibraryGrouping.FamilyKey("objects/collections/building/models/roof_02/visual"), Is.Not.EqualTo(EnvironmentLibraryGrouping.FamilyKey("objects/collections/building/models/door_02/visual")));
        }
    }
}
