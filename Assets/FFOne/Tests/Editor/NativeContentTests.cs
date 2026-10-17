using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Editor;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;
using CharacterStyle = FFOne.Network.CharacterStyle;
using EquipmentItem = FFOne.Network.EquipmentItem;
using FFOne.Creation;
using CharacterCreation = FFOne.Network.CharacterCreation;

namespace FFOne.Tests
{
    public sealed class NativeContentTests
    {
        [Test]
        public void ImportedPathsCannotEscapeTheirRoot()
        {
            Assert.Throws<InvalidDataException>(() => NativeContentImport.ResolveSafe("Assets", "../../outside.txt"));
            Assert.That(NativeContentImport.ResolveSafe("Assets", "Resources/fonts/jeffe.otf"), Does.EndWith(Path.Combine("fonts", "jeffe.otf")));
        }
        [Test]
        public void ImportReplayAndConflictPreflightNeverReplaceEditedContent()
        {
            string root = Path.Combine(Path.GetTempPath(), "ffone-import-test-" + Guid.NewGuid().ToString("N"));
            string relative = "test-" + Guid.NewGuid().ToString("N") + ".txt";
            string destination = NativeContentImport.ResolveSafe(NativeContentImport.Destination, relative);
            try
            {
                Directory.CreateDirectory(ContentPaths.FilePath(root));
                File.WriteAllText(ContentPaths.FilePath(Path.Combine(root, relative)), "accepted");
                Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(destination)!));
                File.WriteAllText(ContentPaths.FilePath(destination), "accepted");
                Assert.That(NativeContentImport.Preflight(root, new[] { relative }), Does.Contain("1 identical"));
                File.WriteAllText(ContentPaths.FilePath(destination), "local edit");
                Assert.Throws<IOException>(() => NativeContentImport.Preflight(root, new[] { relative }));
                Assert.That(File.ReadAllText(ContentPaths.FilePath(destination)), Is.EqualTo("local edit"));
                Assert.That(File.ReadAllText(ContentPaths.FilePath(Path.Combine(root, relative))), Is.EqualTo("accepted"));
            }
            finally
            {
                if (File.Exists(ContentPaths.FilePath(destination))) File.Delete(ContentPaths.FilePath(destination));
                if (File.Exists(ContentPaths.FilePath(destination + ".meta"))) File.Delete(ContentPaths.FilePath(destination + ".meta"));
                if (ContentPaths.DirectoryExists(root)) Directory.Delete(ContentPaths.FilePath(root), true);
            }
        }
        [Test]
        public void ImportedSkeletonRetainsSelectionAnimationAndAcceptedFonts()
        {
            foreach (var gender in new[] { "male", "female" })
            {
                var rig = FFOne.Content.ContentAssets.Load<GameObject>($"characters/player/{gender}/base/{gender}_skeleton");
                Assert.That(rig, Is.Not.Null, "Run FFOne/Content Import before the asset acceptance tests.");
                var animation = rig.GetComponent<Animation>();
                foreach (var clip in new[] { "stand1", "height_Add", "shape_Add", "height", "shape" })
                    Assert.That(animation.GetClip(clip), Is.Not.Null, gender + "/" + clip);
            }
            foreach (var font in new[] { "jeffe", "comic-sans-ms-bold", "chaletbook-regular" }) Assert.That(FFOne.Content.ContentAssets.Load<Font>("fonts/" + font), Is.Not.Null);
        }
        [Test]
        public void NativeMeshRoundTripRestoresUnityBasisUvWindingAndSkin()
        {
            const string relative = "characters/player/items/mask/m_face_001_type01/models/m_face_001_type01/model.glb";
            var document = NativeGlbDocument.Read(NativeContentImport.Destination + "/" + relative);
            var source = document.Json["meshes"]![0]!["primitives"]![0]!;
            var prefab = FFOne.Content.ContentAssets.Load<GameObject>(relative.Substring(0, relative.LastIndexOf('.')));
            var mesh = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].sharedMesh;
            var positions = document.Values((int)source["attributes"]!["POSITION"]!);
            var uv = document.Values((int)source["attributes"]!["TEXCOORD_0"]!);
            Assert.That(mesh.vertexCount, Is.EqualTo(positions.Length));
            for (int i = 0; i < positions.Length; i++)
            {
                Assert.That(Vector3.Distance(mesh.vertices[i], new Vector3(-positions[i][0], positions[i][1], positions[i][2])), Is.LessThan(1e-6));
                Assert.That(Vector2.Distance(mesh.uv[i], new Vector2(uv[i][0], 1 - uv[i][1])), Is.LessThan(1e-6));
            }
            var indices = document.Values((int)source["indices"]!).Select(v => (int)v[0]).ToArray();
            Assert.That(mesh.triangles.Take(3).ToArray(), Is.EqualTo(new[] { indices[0], indices[2], indices[1] }));
            Assert.That(mesh.boneWeights.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.bindposes.Length, Is.GreaterThan(0));
        }
        [Test]
        public void CubicSelectionAnimationPreservesHermiteDerivatives()
        {
            const string relative = "characters/player/male/base/male_skeleton.glb";
            var document = NativeGlbDocument.Read(NativeContentImport.Destination + "/" + relative);
            var definition = document.Json["animations"]!.Single(a => (string)a["name"] == "stand1");
            var channel = definition["channels"]!.First(c => (string)c["target"]!["path"] == "translation");
            var sampler = definition["samplers"]![(int)channel["sampler"]!]!;
            Assert.That((string)sampler["interpolation"], Is.EqualTo("CUBICSPLINE"));
            var values = document.Values((int)sampler["output"]!);
            var rig = FFOne.Content.ContentAssets.Load<GameObject>("characters/player/male/base/male_skeleton");
            var clip = rig.GetComponent<Animation>().GetClip("stand1");
            string nodeName = (string)document.Json["nodes"]![(int)channel["target"]!["node"]!]!["name"];
            var target = rig.GetComponentsInChildren<Transform>(true).Single(t => t.name == nodeName);
            string path = AnimationUtility.CalculateTransformPath(target, rig.transform);
            // Unity canonicalizes SetCurve's public transform property to its serialized binding name.
            var binding = AnimationUtility.GetCurveBindings(clip).Single(b => b.path == path && b.propertyName == "m_LocalPosition.x");
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            Assert.That(curve, Is.Not.Null);
            Assert.That(curve.keys[0].value, Is.EqualTo(-values[1][0]).Within(1e-6));
            Assert.That(curve.keys[0].inTangent, Is.EqualTo(-values[0][0]).Within(1e-6));
            Assert.That(curve.keys[0].outTangent, Is.EqualTo(-values[2][0]).Within(1e-6));
        }
        [TestCase(1, "m")]
        [TestCase(2, "w")]
        public void CreatorAvatarAssemblesOntoAcceptedMaleAndFemaleActorRoots(int genderCode, string actorRoot)
        {
            var catalog = new CreationCatalog();
            int ItemId(string category) => (int)catalog.Choices(category, genderCode)[0]["value"]!;
            var request = new CharacterCreation {
                Style = new CharacterStyle { Gender = genderCode, FaceStyle = ItemId("face"), HairStyle = ItemId("hair"), EyeColor = 10 },
                UpperBodyId = ItemId("shirt"), LowerBodyId = ItemId("pants"), FootId = ItemId("shoes")
            };
            var root = new GameObject("Avatar acceptance " + genderCode);
            try
            {
                var preview = root.AddComponent<NativeAvatarPreview>();
                preview.Apply(CreationCatalog.Preview(request));
                Assert.That(preview.IsReady, Is.True, preview.LastError);
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(skins.Length, Is.GreaterThanOrEqualTo(5));
                foreach (var skin in skins)
                {
                    Assert.That(skin.rootBone.name, Is.EqualTo(actorRoot));
                    Assert.That(skin.bones.Length, Is.EqualTo(skin.sharedMesh.bindposes.Length));
                    Assert.That(skin.bones.All(bone => bone && bone.IsChildOf(skin.rootBone)), Is.True);
                }
                preview.Clear();
                Assert.That(preview.IsReady, Is.False);
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
