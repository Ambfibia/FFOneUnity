using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FFOne.Editor;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class WorldContentTests
    {
        [Test]
        public void WorldCameraRetainsCrossingTerrainWithoutAdmittingDistantObjects()
        {
            var owner = new GameObject("world projection test");
            try
            {
                var camera = owner.AddComponent<Camera>();
                camera.nearClipPlane = .1f; camera.farClipPlane = 340;
                camera.fieldOfView = 45; camera.aspect = 16f / 9;
                WorldCameraProjection.Apply(camera);
                var clip = camera.projectionMatrix * new Vector4(0, 0, -700, 1);
                Assert.That(clip.z / clip.w, Is.LessThan(1), "Admitted terrain must not be sliced at far=340");
                var planes = GeometryUtility.CalculateFrustumPlanes(camera.cullingMatrix);
                Assert.That(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(0, 0, 330), Vector3.one * 40)), Is.True);
                Assert.That(GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(0, 0, 700), Vector3.one * 40)), Is.False);
                Assert.That(camera.farClipPlane, Is.EqualTo(340));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void BlendedWorldImportWritesRgbUsingUnityChannelBits()
        {
            var material = new Material(Shader.Find("FFOne/NativeWorld"));
            try
            {
                var definition = JObject.Parse("{\"extras\":{\"shaderName\":\"normal_blendSrcalphaInvsrcalpha\"}}");
                NativeWorldMaterialImport.Apply(null, new JObject(), definition, material);
                var rgb = UnityEngine.Rendering.ColorWriteMask.Red | UnityEngine.Rendering.ColorWriteMask.Green | UnityEngine.Rendering.ColorWriteMask.Blue;
                Assert.That((int)material.GetFloat("_ColorMask"), Is.EqualTo((int)rgb));
            }
            finally { Object.DestroyImmediate(material); }
        }

        // This is a source-contract check: the tutorial may select a world-owned
        // dong, but that does not remove that dong from the main-world registry.
        [Test]
        public void ImportedWorldRetainsEveryCatalogTileIncludingOneOne()
        {
            string source = NativeContentImport.DefaultSource;
            var definition = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(Path.Combine(source, "map/catalog.json"))));
            var expected = definition["tiles"]!.Select(t => (string)t["tileId"]).ToArray();
            var oneOne = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(Path.Combine(source, "map/tiles/map_01_01/scene.json"))));
            Assert.That((string)oneOne["scope"], Is.EqualTo("worldMap"));
            var imported = FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog");
            Assert.That(imported, Is.Not.Null, "Import Entire Native World must finish before this integration check.");
            Assert.That(imported.Tiles.Select(t => t.Id), Is.EquivalentTo(expected));
            Assert.That(imported.Tiles.Select(t => t.Id).Distinct().Count(), Is.EqualTo(expected.Length));
            Assert.That(imported.Atmospheres.Length, Is.EqualTo(expected.Length));
            Assert.That(imported.Tiles.Any(t => t.Id == "map_01_01"), Is.True);
        }

        [Test]
        public void NativeGridFixturePreservesPrecisionShiftsAndFrontFaceCollision()
        {
            string oldFolder = WorldTerrainImport.SharedFolder;
            string folder = "Assets/WorldTerrainTest_" + Guid.NewGuid().ToString("N");
            ContentPaths.CreateFolder("Assets", Path.GetFileName(folder));
            WorldTerrainImport.SharedFolder = folder;
            GameObject root = null;
            try
            {
                var definition = JObject.Parse("{\"trueName\":\"terrain precision fixture\",\"scale\":{\"sampleSpacingX\":4,\"sampleSpacingZ\":4,\"heightScale\":600}," +
                    "\"heightmap\":{\"vertexShifts\":[{\"column\":42,\"row\":37,\"flags\":6}]}}");
                var samples = new float[129, 129];
                for (int row = 0; row < 129; row++) for (int column = 0; column < 129; column++)
                    samples[row, column] = (8192 + column * 31 + row * 17) / 32767f;
                samples[100, 100] = 49152 / 32767f;
                var mesh = Invoke<Mesh>("TerrainMesh", definition, samples, folder);
                Assert.That(mesh.vertexCount, Is.EqualTo(16641));
                Assert.That(mesh.triangles.Length, Is.EqualTo(98304));
                Assert.That(mesh.triangles.Take(3), Is.EqualTo(new[] { 0, 129, 1 }));
                Assert.That(mesh.vertices[128].x, Is.EqualTo(512));
                Assert.That(mesh.vertices[128 * 129].z, Is.EqualTo(512));
                Assert.That(mesh.vertices[100 * 129 + 100].y, Is.EqualTo(49152 / 32767f * 600).Within(.0001),
                    "Preserve Gray16/32767 instead of byte precision or normalized clamping.");
                int shifted = 37 * 129 + 42;
                Assert.That(mesh.vertices[shifted].x, Is.EqualTo(172));
                Assert.That(mesh.vertices[shifted].z, Is.EqualTo(144));
                Assert.That(mesh.uv[shifted], Is.EqualTo(new Vector2(42.5f / 128, 1 - 36.5f / 128)));
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
                var equivalent = Invoke<Mesh>("TerrainMesh", definition.DeepClone(), samples, folder);
                Assert.That(equivalent, Is.SameAs(mesh), "Equal immutable terrain meshes share one Unity asset.");

                root = new GameObject("terrain collision fixture");
                root.transform.position = new Vector3(512, -300, 512);
                var collider = root.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                Physics.SyncTransforms();
                var ray = new Ray(new Vector3(512 + 43, 2000, 512 + 62), Vector3.down);
                Assert.That(collider.Raycast(ray, out var hit, 4000), Is.True);
                float expected = (8192 + 10.75f * 31 + 15.5f * 17) / 32767f * 600 - 300;
                Assert.That(hit.point.y, Is.EqualTo(expected).Within(.001), "Collision uses the original grid triangles after reflection.");
                Assert.That(hit.normal.y, Is.GreaterThan(.9f));
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                WorldTerrainImport.SharedFolder = oldFolder;
                AssetDatabase.DeleteAsset(ContentPaths.Resolve(folder));
            }
        }

        [Test]
        public void NativeRootChainComposesRotationScaleAndMirroredTranslation()
        {
            var instance = JObject.Parse("{\"transform\":{\"translation\":[2,5,7],\"rotation\":[0,0,0,1],\"scale\":[1,1,1]}," +
                "\"rootChain\":{\"nodes\":[{\"nativeLocalTransform\":{\"translation\":[-512,0,512],\"rotation\":[0,-0.7071067812,0,0.7071067812],\"scale\":[2,2,2]}}]}}");
            var root = new GameObject("terrain root chain fixture");
            try
            {
                Invoke<object>("ApplyComposedTransform", root.transform, instance);
                Assert.That(Vector3.Distance(root.transform.position, new Vector3(526, 10, 516)), Is.LessThan(.001));
                Assert.That(Vector3.Distance(root.transform.forward, Vector3.right), Is.LessThan(.00001));
                Assert.That(Vector3.Distance(root.transform.localScale, Vector3.one * 2), Is.LessThan(.00001));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExactLightmapFixtureRetainsAuthoredMipCountAndGammaInterpolationState()
        {
            string suffix = "WorldTerrainTest_" + Guid.NewGuid().ToString("N");
            string sourceFolder = NativeContentImport.Destination + "/" + suffix;
            string outputFolder = "Assets/" + suffix;
            string oldFolder = WorldTerrainImport.SharedFolder;
            ContentPaths.CreateFolder(NativeContentImport.Destination, suffix);
            ContentPaths.CreateFolder("Assets", suffix);
            WorldTerrainImport.SharedFolder = outputFolder;
            try
            {
                WritePng(sourceFolder + "/base.png", 2, new Color32(63, 127, 191, 255));
                WritePng(sourceFolder + "/mip.png", 1, new Color32(211, 101, 17, 255));
                AssetDatabase.ImportAsset(ContentPaths.Resolve(sourceFolder + "/base.png"), ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(ContentPaths.Resolve(sourceFolder + "/mip.png"), ImportAssetOptions.ForceSynchronousImport);
                var image = JObject.Parse("{\"width\":2,\"height\":2,\"colorSpace\":\"srgb\",\"sampler\":{\"filterMode\":{\"serializedValue\":1}," +
                    "\"wrapMode\":{\"serializedValue\":1},\"anisoLevel\":1,\"mipBias\":0.25},\"mips\":[" +
                    "{\"level\":0,\"width\":2,\"height\":2,\"path\":\"base.png\"},{\"level\":1,\"width\":1,\"height\":1,\"path\":\"mip.png\"}]}");
                var texture = Invoke<Texture2D>("ExactTexture", NativeContentImport.Destination, image, suffix, "Lightmap");
                Assert.That(texture.width, Is.EqualTo(2));
                Assert.That(texture.mipmapCount, Is.EqualTo(2));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(texture.mipMapBias, Is.EqualTo(.25f));
                Assert.That(GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat), Is.False,
                    "Native terrain lightmap interpolates gamma-encoded bytes before final framebuffer conversion.");
                string texturePath = ContentPaths.Legacy(AssetDatabase.GetAssetPath(texture));
                string textureGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(texturePath));
                var reused = Invoke<Texture2D>("ExactTexture", NativeContentImport.Destination, image.DeepClone(), suffix, "Lightmap");
                // AssetDatabase may produce a different managed wrapper after CreateAsset.
                // Reuse promises the same persisted Unity asset and stable GUID.
                Assert.That(textureGuid, Is.Not.Empty);
                Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(reused)), Is.EqualTo(texturePath));
                Assert.That(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(reused)))), Is.EqualTo(textureGuid));
            }
            finally
            {
                WorldTerrainImport.SharedFolder = oldFolder;
                AssetDatabase.DeleteAsset(ContentPaths.Resolve(sourceFolder));
                AssetDatabase.DeleteAsset(ContentPaths.Resolve(outputFolder));
            }
        }

        static T Invoke<T>(string name, params object[] arguments)
        {
            var method = typeof(WorldTerrainImport).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, name);
            try { return (T)method.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        static void WritePng(string path, int resolution, Color32 value)
        {
            var image = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
            try { image.SetPixels32(Enumerable.Repeat(value, resolution * resolution).ToArray()); image.Apply(); File.WriteAllBytes(ContentPaths.FilePath(path), image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
