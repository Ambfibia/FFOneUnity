using ContentPaths = FFOne.Content.ContentPaths;
using System;
using FFOne.Editor;
using FFOne.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class EnvironmentAssemblyTests
    {
        string directory;
        EnvironmentLibrary.Entry entry;
        [SetUp] public void Setup()
        {
            directory = "Assets/__EnvironmentAssembly_" + Guid.NewGuid().ToString("N");
            EnvironmentLibraryBuilder.Folder(directory);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } }; mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, ContentPaths.Resolve(directory + "/mesh.asset"));
            var root = new GameObject("Fixture");
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>();
                PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(directory + "/model.prefab"));
            }
            finally { Object.DestroyImmediate(root); }
            entry = new EnvironmentLibrary.Entry { Identity = "objects/test/fixture/models/fixture/visual", Category = "Buildings/Residential", PrefabGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(directory + "/model.prefab")) };
        }
        [TearDown] public void Cleanup() => AssetDatabase.DeleteAsset(ContentPaths.Resolve(directory));
        [Test] public void PlaceableVariantSharesItsMeshAndPreservesAuthoredColliderOnReplay()
        {
            var prefab = EnvironmentAssemblyBuilder.Build(entry);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(prefab.activeSelf, Is.True);
            Assert.That(prefab.transform.parent, Is.Null);
            Assert.That(prefab.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(prefab.GetComponent<MeshFilter>().sharedMesh));
            string guid = entry.AssemblyGuid;
            string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(prefab));
            var edit = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(path));
            try { edit.GetComponent<MeshCollider>().enabled = false; PrefabUtility.SaveAsPrefabAsset(edit, ContentPaths.Resolve(path)); }
            finally { PrefabUtility.UnloadPrefabContents(edit); }
            var replay = EnvironmentAssemblyBuilder.Build(entry);
            Assert.That(replay.GetComponent<MeshCollider>().enabled, Is.False);
            Assert.That(entry.AssemblyGuid, Is.EqualTo(guid));
            Assert.That(replay.GetComponent<EnvironmentObject>().SourceIdentity, Is.EqualTo(entry.Identity));
        }
        [Test] public void GrassDoesNotReceiveBlockingCollision()
        {
            entry.Category = "Vegetation/Grass";
            Assert.That(EnvironmentAssemblyBuilder.Build(entry).GetComponentsInChildren<Collider>(), Is.Empty);
        }
        [Test] public void AuthoredColliderOverridesRemainSelectableAsVariantsInOneCard()
        {
            EnvironmentLibraryBuilder.Folder(directory + "/second");
            AssetDatabase.CopyAsset(ContentPaths.Resolve(directory + "/model.prefab"), ContentPaths.Resolve(directory + "/second/model.prefab"));
            var second = new EnvironmentLibrary.Entry { Identity = "objects/test/fixture/models/fixture_variant_0002/visual", Category = entry.Category,
                PrefabGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(directory + "/second/model.prefab")) };
            EnvironmentAssemblyBuilder.Build(entry); var prefab = EnvironmentAssemblyBuilder.Build(second);
            var library = ScriptableObject.CreateInstance<EnvironmentLibrary>(); library.Entries = new[] { entry, second };
            try
            {
                EnvironmentLibraryGrouping.Rebuild(library);
                Assert.That(entry.RenderKey, Is.EqualTo(second.RenderKey), "Equal assemblies are aliases despite different source IDs.");
                string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(prefab)); var edit = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(path));
                try { edit.GetComponent<MeshCollider>().enabled = false; PrefabUtility.SaveAsPrefabAsset(edit, ContentPaths.Resolve(path)); }
                finally { PrefabUtility.UnloadPrefabContents(edit); }
                EnvironmentLibraryGrouping.Rebuild(library);
                Assert.That(entry.CardKey, Is.EqualTo(second.CardKey));
                Assert.That(entry.RenderKey, Is.Not.EqualTo(second.RenderKey), "An authored collider override must not disappear behind an alias.");
            }
            finally { Object.DestroyImmediate(library); }
        }
        [Test] public void CharactersAreRejectedBeforeCreatingAnAssembly()
        {
            entry.Identity = "characters/npcs/npc_dexter/model";
            Assert.Throws<System.IO.InvalidDataException>(() => EnvironmentAssemblyBuilder.Build(entry));
            Assert.That(System.IO.File.Exists(ContentPaths.Resolve(directory + "/environment.prefab")), Is.False);
        }
        [Test] public void WorldAnimationDoesNotModifyAnotherUserOfASharedMaterial()
        {
            var source = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var first = new GameObject("Animated user"); var second = new GameObject("Static user");
            try
            {
                var animated = first.AddComponent<MeshRenderer>(); var untouched = second.AddComponent<MeshRenderer>();
                animated.sharedMaterial = untouched.sharedMaterial = source;
                int originalQueue = source.renderQueue;
                var behaviour = first.AddComponent<WorldBehaviour>();
                var method = typeof(WorldBehaviour).GetMethod("Materials", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var animatedMaterials = (Material[])method.Invoke(behaviour, new object[] { animated });
                animatedMaterials[0].renderQueue = 2458;
                Assert.That(untouched.sharedMaterial, Is.SameAs(source));
                Assert.That(source.renderQueue, Is.EqualTo(originalQueue));
                Assert.That(animatedMaterials[0], Is.Not.SameAs(source));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(source); }
        }
    }
}
