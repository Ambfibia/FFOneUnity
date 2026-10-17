using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using FFOne.Content;
using FFOne.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class NativeAssetPublisherTests
    {
        string directory, source, resource, publishedDirectory, manifestDirectory;

        [SetUp]
        public void SetUp()
        {
            string name = "__NativePublicationTest_" + Guid.NewGuid().ToString("N");
            directory = "Assets/Resources/" + name;
            resource = name + "/source";
            source = directory + "/source.glb";
            publishedDirectory = NativeAssetPublisher.DefaultDestination + "/" + name;
            manifestDirectory = NativeAssetPublisher.ManifestRoot + "/" + name;
            ContentPaths.CreateFolder("Assets/Resources", name);
            WriteFixture(source);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(source), ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(source)), Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            const string indexPath = NativeAssetPublisher.DefaultDestination + "/Index.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(indexPath));
            if (catalog)
            {
                catalog.Bindings = catalog.Bindings.Where(b => !(b.OriginalResource ?? "").StartsWith(resource.Substring(0, resource.LastIndexOf('/')) + "/", StringComparison.Ordinal)).ToArray();
                if (catalog.Bindings.Length == 0) AssetDatabase.DeleteAsset(ContentPaths.Resolve(indexPath));
                else { EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
            }
            AssetDatabase.DeleteAsset(ContentPaths.Resolve(publishedDirectory));
            AssetDatabase.DeleteAsset(ContentPaths.Resolve(manifestDirectory));
            AssetDatabase.DeleteAsset(ContentPaths.Resolve(directory));
        }

        [Test]
        public void OrganizedPublicationKeepsGuidAndIsReusedOnRepublish()
        {
            var prefab = NativeAssetPublisher.Publish(source);
            var plan = NativeAssetPublisher.Preflight(source);
            string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(plan.PrefabPath));
            string oldDirectory = publishedDirectory;
            string newDirectory = publishedDirectory + "_organized";
            Assert.That(AssetDatabase.MoveAsset(ContentPaths.Resolve(publishedDirectory), ContentPaths.Resolve(newDirectory)), Is.Empty);
            publishedDirectory = newDirectory;
            var saved = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(plan.ManifestPath)));
            saved.PrefabPath = saved.PrefabPath.Replace(oldDirectory + "/", newDirectory + "/");
            foreach (var entry in saved.Assets) entry.Path = entry.Path.Replace(oldDirectory + "/", newDirectory + "/");
            File.WriteAllText(ContentPaths.FilePath(plan.ManifestPath), JsonConvert.SerializeObject(saved));
            var index = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeAssetPublisher.DefaultDestination + "/Index.asset"));
            index.Bindings.Single(b => b.OriginalResource == resource).EditableResource = saved.PrefabPath.Substring("Assets/Resources/".Length).Replace(".prefab", "");
            EditorUtility.SetDirty(index); AssetDatabase.SaveAssetIfDirty(index);
            var replay = NativeAssetPublisher.Preflight(source);
            Assert.That(replay.AlreadyPublished, Is.True);
            Assert.That(replay.PrefabPath, Is.EqualTo(saved.PrefabPath));
            Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(replay.PrefabPath)), Is.EqualTo(guid));
            Assert.That(NativeModelAssets.Load(resource), Is.SameAs(prefab));
        }

        [Test]
        public void PublicationRemapsMeshesMaterialsClipsAndKeepsSkinAndAnimationBindings()
        {
            var plan = NativeAssetPublisher.Preflight(source);
            Assert.That(plan.MeshCount, Is.EqualTo(1));
            Assert.That(plan.MaterialCount, Is.EqualTo(1));
            Assert.That(plan.AnimationCount, Is.EqualTo(1));
            Assert.That(ContentPaths.DirectoryExists(publishedDirectory), Is.False, "Preflight must not write.");
            var prefab = NativeAssetPublisher.Publish(source);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Regular));
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(skin.sharedMesh)), Does.EndWith(".asset"));
            Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(skin.sharedMaterial)), Does.EndWith(".mat"));
            Assert.That(skin.bones.Single().IsChildOf(prefab.transform), Is.True);
            Assert.That(skin.sharedMesh.bindposes.Length, Is.EqualTo(1));
            Assert.That(skin.sharedMesh.boneWeights.Length, Is.EqualTo(3));
            var clip = prefab.GetComponent<Animation>().GetClip("stand1");
            Assert.That(clip, Is.Not.Null, "Legacy animation state names must survive publication: " +
                string.Join(",", AnimationUtility.GetAnimationClips(prefab).Select(c => c.name)));
            Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(clip)), Does.EndWith(".anim"));
            Assert.That(AnimationUtility.GetCurveBindings(clip).Length, Is.EqualTo(3));
            Assert.That(ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(plan.PrefabPath), true)), Does.Not.Contain(source));
            Assert.That(plan.ManifestPath, Does.StartWith("Assets/Editor/NativePublications/"));
            Assert.That(plan.ManifestPath, Does.Not.Contain("/Resources/"));
            Assert.That(ContentPaths.GetFiles(plan.Directory, "*.json"), Is.Empty);
            Assert.That(NativeModelAssets.Load(resource), Is.SameAs(prefab));
            Assert.That(NativeModelAssets.Resolve(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(source))), Is.SameAs(prefab));
        }

        [Test]
        public void RepeatedPublicationPreservesMaterialMeshPrefabEditsAndGuids()
        {
            var prefab = NativeAssetPublisher.Publish(source);
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = skin.sharedMesh;
            var material = skin.sharedMaterial;
            var clip = prefab.GetComponent<Animation>().GetClip("stand1");
            string meshPath = ContentPaths.Legacy(AssetDatabase.GetAssetPath(mesh)), materialPath = ContentPaths.Legacy(AssetDatabase.GetAssetPath(material));
            string meshGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(meshPath)), materialGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(materialPath));
            mesh.vertices = new[] { Vector3.zero, Vector3.right * 4, Vector3.up };
            material.renderQueue = 2451;
            clip.frameRate = 17;
            EditorUtility.SetDirty(mesh); EditorUtility.SetDirty(material); EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(mesh); AssetDatabase.SaveAssetIfDirty(material); AssetDatabase.SaveAssetIfDirty(clip);
            string prefabPath = ContentPaths.Legacy(AssetDatabase.GetAssetPath(prefab));
            var edit = PrefabUtility.LoadPrefabContents(ContentPaths.Resolve(prefabPath));
            try
            {
                new GameObject("Authored child").transform.SetParent(edit.transform, false);
                PrefabUtility.SaveAsPrefabAsset(edit, ContentPaths.Resolve(prefabPath));
            }
            finally { PrefabUtility.UnloadPrefabContents(edit); }
            var again = NativeAssetPublisher.Publish(source);
            Assert.That(again.transform.Find("Authored child"), Is.Not.Null);
            Assert.That(again.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertices[1].x, Is.EqualTo(4));
            Assert.That(again.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.renderQueue, Is.EqualTo(2451));
            Assert.That(again.GetComponent<Animation>().GetClip("stand1").frameRate, Is.EqualTo(17));
            Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(meshPath)), Is.EqualTo(meshGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(materialPath)), Is.EqualTo(materialGuid));
            Assert.That(NativeAssetPublisher.Preflight(source).AlreadyPublished, Is.True);
        }

        [Test]
        public void ChangedSourceIsAConflictAndNeverOverwritesPublishedWork()
        {
            var prefab = NativeAssetPublisher.Publish(source);
            string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(prefab));
            string original = File.ReadAllText(ContentPaths.FilePath(path));
            WriteFixture(source, 2);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(source), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<InvalidDataException>(() => NativeAssetPublisher.Publish(source));
            Assert.That(File.ReadAllText(ContentPaths.FilePath(path)), Is.EqualTo(original));
        }

        [Test]
        public void OccupiedDestinationAndInvalidPathsFailBeforeWriting()
        {
            var plan = NativeAssetPublisher.Preflight(source);
            EnsureFolder(plan.Directory);
            File.WriteAllText(ContentPaths.FilePath(plan.PrefabPath), "user content");
            Assert.Throws<IOException>(() => NativeAssetPublisher.Publish(source));
            Assert.That(File.ReadAllText(ContentPaths.FilePath(plan.PrefabPath)), Is.EqualTo("user content"));
            Assert.That(File.Exists(ContentPaths.FilePath(plan.ManifestPath)), Is.False);
            Assert.Throws<InvalidDataException>(() => NativeAssetPublisher.Preflight(source, "Assets/../outside"));
            Assert.Throws<ArgumentException>(() => NativeModelAssets.EditableResourcePath("../source"));
        }

        [UnityTest]
        public IEnumerator RuntimeLoaderUsesOriginalUntilAnEditablePrefabIsPublished()
        {
            Assert.That(NativeModelAssets.Load(resource), Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(source))));
            var fallback = NativeModelAssets.LoadAsync(resource);
            yield return fallback;
            Assert.That(fallback.asset, Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(source))));
            var prefab = NativeAssetPublisher.Publish(source);
            var editable = NativeModelAssets.LoadAsync(resource);
            yield return editable;
            Assert.That(editable.asset, Is.SameAs(prefab));
        }

        [Test]
        public void BatchedPublicationKeepsDistinctModelsAndSurvivesReimportAndMixedReplay()
        {
            string second = directory + "/second.glb", third = directory + "/third.glb";
            WriteFixture(second, 2); WriteFixture(third, 3);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(second), ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(third), ImportAssetOptions.ForceSynchronousImport);
            var published = NativeAssetPublisher.PublishBatch(new[] { source, second });
            foreach (var prefab in published)
            {
                string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(prefab));
                AssetDatabase.ImportAsset(ContentPaths.Resolve(path), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
                Assert.That(reloaded.GetComponent<Animation>().GetClip("stand1"), Is.Not.Null);
                Assert.That(ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(path), true)).Any(p => p.EndsWith(".glb")), Is.False);
                float expected = path.Contains("/second/") ? 2 : 1;
                Assert.That(Mathf.Abs(reloaded.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertices[1].x), Is.EqualTo(expected));
            }
            var first = NativeModelAssets.Load(resource);
            var material = first.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            material.renderQueue = 2457; EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(first))));
            NativeAssetPublisher.PublishBatch(new[] { source, third });
            Assert.That(NativeModelAssets.Load(resource).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial.renderQueue, Is.EqualTo(2457));
            Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(first)))), Is.EqualTo(guid));
            Assert.That(NativeAssetPublisher.Preflight(third).AlreadyPublished, Is.True);
            Assert.That(ContentPaths.GetDirectories(publishedDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "source", "second", "third" }));
        }

        [Test]
        public void LastModelConflictPreventsAnyWritesToTheFirstModelInABatch()
        {
            string lastSource = directory + "/zz-last.glb";
            WriteFixture(lastSource);
            AssetDatabase.ImportAsset(ContentPaths.Resolve(lastSource), ImportAssetOptions.ForceSynchronousImport);
            var first = NativeAssetPublisher.Preflight(source);
            var last = NativeAssetPublisher.Preflight(lastSource);
            EnsureFolder(last.Directory);
            File.WriteAllText(ContentPaths.FilePath(last.PrefabPath), "authored content");
            Assert.Throws<IOException>(() => NativeAssetPublicationBatch.PublishFilesOrFolders(new[] { directory }));
            Assert.That(ContentPaths.DirectoryExists(first.Directory), Is.False);
            Assert.That(File.Exists(ContentPaths.FilePath(first.ManifestPath)), Is.False);
            Assert.That(File.ReadAllText(ContentPaths.FilePath(last.PrefabPath)), Is.EqualTo("authored content"));
        }

        [Test]
        public void AuthoredRuntimeBindingIsNeverSilentlyReplaced()
        {
            var published = NativeAssetPublisher.Publish(source);
            var authored = new GameObject("Authored override");
            GameObject custom;
            try { custom = PrefabUtility.SaveAsPrefabAsset(authored, ContentPaths.Resolve(directory + "/custom.prefab")); }
            finally { Object.DestroyImmediate(authored); }
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(source));
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeAssetPublisher.DefaultDestination + "/Index.asset"));
            catalog.Bindings.Single(b => b.OriginalResource == resource).EditableResource = resource.Substring(0, resource.LastIndexOf('/')) + "/custom";
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            Assert.Throws<InvalidDataException>(() => NativeAssetPublisher.Publish(source));
            Assert.That(NativeModelAssets.Resolve(original), Is.SameAs(custom));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(published)))), Is.SameAs(published));
        }

        [Test]
        public void RealFusionButtercupPrefabMetadataDoesNotBecomeAContentDependency()
        {
            const string realSource = "Assets/Resources/characters/fusions/fusion_buttercup/fusion_buttercup.glb";
            string destination = directory + "/RealPublication";
            var plan = NativeAssetPublisher.Preflight(realSource, destination);
            try
            {
                var prefab = NativeAssetPublisher.Publish(realSource, destination);
                Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, Is.GreaterThan(0));
                Assert.That(ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(plan.PrefabPath), true)), Does.Not.Contain(realSource));
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    using var serialized = new SerializedObject(component);
                    var metadata = serialized.FindProperty("m_PrefabAsset");
                    if (metadata != null && metadata.objectReferenceValue)
                        Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(metadata.objectReferenceValue)), Is.Not.EqualTo(realSource));
                }
            }
            finally
            {
                // This test uses a custom destination namespace for an actual source;
                // remove only its own provenance namespace, never the real publication.
                string variant = NativeAssetPublisher.ManifestRoot + "/Variants/" + Hash128.Compute(destination);
                AssetDatabase.DeleteAsset(ContentPaths.Resolve(variant));
            }
        }

        internal static void WriteFixture(string path, float extent = 1)
        {
            using var binary = new MemoryStream();
            using var writer = new BinaryWriter(binary, Encoding.UTF8, true);
            var views = new JArray(); var accessors = new JArray();
            int Floats(string type, int count, params float[] values)
            {
                int offset = (int)binary.Position;
                foreach (float value in values) writer.Write(value);
                views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 });
                accessors.Add(new JObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["count"] = count, ["type"] = type });
                return accessors.Count - 1;
            }
            int position = Floats("VEC3", 3, 0, 0, 0, extent, 0, 0, 0, 1, 0);
            int inverse = Floats("MAT4", 1, 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
            int weight = Floats("VEC4", 3, 1,0,0,0, 1,0,0,0, 1,0,0,0);
            int times = Floats("SCALAR", 2, 0, 1);
            int translation = Floats("VEC3", 2, 0,0,0, 0,1,0);
            int jointOffset = (int)binary.Position;
            for (int i = 0; i < 12; i++) writer.Write((ushort)0);
            views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = jointOffset, ["byteLength"] = 24 });
            accessors.Add(new JObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5123, ["count"] = 3, ["type"] = "VEC4" });
            int joints = accessors.Count - 1;
            var json = new JObject {
                ["asset"] = new JObject { ["version"] = "2.0" },
                ["buffers"] = new JArray(new JObject { ["byteLength"] = binary.Length }), ["bufferViews"] = views, ["accessors"] = accessors,
                ["nodes"] = new JArray(new JObject { ["name"] = "Joint" }, new JObject { ["name"] = "Surface", ["mesh"] = 0, ["skin"] = 0 }),
                ["skins"] = new JArray(new JObject { ["inverseBindMatrices"] = inverse, ["joints"] = new JArray(0), ["skeleton"] = 0 }),
                ["materials"] = new JArray(new JObject { ["name"] = "Surface" }),
                ["meshes"] = new JArray(new JObject { ["name"] = "Triangle", ["primitives"] = new JArray(new JObject {
                    ["attributes"] = new JObject { ["POSITION"] = position, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weight }, ["material"] = 0 }) }),
                ["animations"] = new JArray(new JObject { ["name"] = "stand1",
                    ["samplers"] = new JArray(new JObject { ["input"] = times, ["output"] = translation }),
                    ["channels"] = new JArray(new JObject { ["sampler"] = 0, ["target"] = new JObject { ["node"] = 0, ["path"] = "translation" } }) })
            };
            byte[] text = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
            int padded = (text.Length + 3) / 4 * 4;
            using var file = new BinaryWriter(File.Create(ContentPaths.FilePath(path)));
            file.Write(0x46546c67u); file.Write(2u); file.Write((uint)(12 + 8 + padded + 8 + binary.Length));
            file.Write((uint)padded); file.Write(0x4e4f534au); file.Write(text);
            for (int i = text.Length; i < padded; i++) file.Write((byte)' ');
            file.Write((uint)binary.Length); file.Write(0x004e4942u); file.Write(binary.ToArray());
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path))) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            ContentPaths.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
