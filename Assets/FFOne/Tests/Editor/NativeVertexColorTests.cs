using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using System.Text;
using FFOne.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeVertexColorTests
    {
        [TestCase(5121, 3)]
        [TestCase(5121, 4)]
        [TestCase(5123, 3)]
        [TestCase(5123, 4)]
        [TestCase(5126, 4)]
        public void ColorAccessorPreservesNormalizedValuesStrideOffsetsAndAlpha(int component, int width)
        {
            string path = Path.Combine(Path.GetTempPath(), "ffone-color-" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                float maximum = component == 5121 ? 255 : component == 5123 ? 65535 : 1;
                float middle = component == 5121 ? 128 : component == 5123 ? 32768 : .375f;
                WriteColorGlb(path, component, width, new[] { 0, middle, maximum, middle, maximum, 0, middle, maximum });
                var document = NativeGlbDocument.Read(path);
                var colors = NativeGlbImporter.VertexColors(document, 0, 2);
                Assert.That(colors.Length, Is.EqualTo(2));
                Assert.That(colors[0].r, Is.Zero);
                Assert.That(colors[0].g, Is.EqualTo(middle / maximum).Within(1e-7));
                Assert.That(colors[0].b, Is.EqualTo(1));
                Assert.That(colors[0].a, Is.EqualTo(width == 4 ? middle / maximum : 1).Within(1e-7));
                Assert.That(colors[1].r, Is.EqualTo(1));
                Assert.That(colors[1].g, Is.Zero);
                Assert.That(colors[1].b, Is.EqualTo(middle / maximum).Within(1e-7));
                Assert.That(colors[1].a, Is.EqualTo(1));
                Assert.Throws<InvalidDataException>(() => NativeGlbImporter.VertexColors(document, 0, 3));
                if (component != 5126)
                {
                    document.Json["accessors"][0]["normalized"] = false;
                    Assert.Throws<InvalidDataException>(() => NativeGlbImporter.VertexColors(document, 0, 2));
                }
            }
            finally { if (File.Exists(ContentPaths.FilePath(path))) File.Delete(ContentPaths.FilePath(path)); }
        }

        [Test]
        public void AuthoredHologramUsesWhiteAmbientDiffuseFallbackAndOrdinaryMaterialsStayDisabled()
        {
            var hologram = FFOne.Content.ContentAssets.Load<GameObject>("map/shared/effects/models/es739/Tutorial_dexterhologram");
            Assert.That(hologram, Is.Not.Null);
            int checkedMeshes = 0;
            foreach (var renderer in hologram.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.sharedMaterial.GetFloat("_VertexColorAD") < .5f) continue;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.colors.All(color => color == Color.white), Is.True);
                checkedMeshes++;
            }
            Assert.That(checkedMeshes, Is.GreaterThan(0), "Reimport the tutorial ES739 GLB after updating the importer.");
            var material = new Material(Shader.Find("FFOne/NativeToon"));
            try { Assert.That(material.GetFloat("_VertexColorAD"), Is.Zero); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }

        static void WriteColorGlb(string path, int component, int width, float[] values)
        {
            int bytes = component == 5121 ? 1 : component == 5123 ? 2 : 4;
            int stride = ((width * bytes + 3) / 4) * 4 + 4;
            byte[] binary = new byte[8 + stride * 2];
            using (var stream = new MemoryStream(binary))
            using (var writer = new BinaryWriter(stream))
                for (int vertex = 0; vertex < 2; vertex++)
                {
                    stream.Position = 8 + vertex * stride;
                    for (int channel = 0; channel < width; channel++)
                    {
                        float value = values[vertex * 4 + channel];
                        if (component == 5121) writer.Write((byte)value);
                        else if (component == 5123) writer.Write((ushort)value);
                        else writer.Write(value);
                    }
                }
            var document = new JObject {
                ["asset"] = new JObject { ["version"] = "2.0" },
                ["buffers"] = new JArray(new JObject { ["byteLength"] = binary.Length }),
                ["bufferViews"] = new JArray(new JObject { ["buffer"] = 0, ["byteOffset"] = 4, ["byteLength"] = binary.Length - 4, ["byteStride"] = stride }),
                ["accessors"] = new JArray(new JObject { ["bufferView"] = 0, ["byteOffset"] = 4, ["componentType"] = component,
                    ["count"] = 2, ["type"] = "VEC" + width, ["normalized"] = component != 5126 })
            };
            byte[] json = Encoding.UTF8.GetBytes(document.ToString(Formatting.None));
            int padded = (json.Length + 3) / 4 * 4;
            using var file = new BinaryWriter(File.Create(ContentPaths.FilePath(path)));
            file.Write(0x46546c67u); file.Write(2u); file.Write((uint)(12 + 8 + padded + 8 + binary.Length));
            file.Write((uint)padded); file.Write(0x4e4f534au); file.Write(json);
            for (int i = json.Length; i < padded; i++) file.Write((byte)' ');
            file.Write((uint)binary.Length); file.Write(0x004e4942u); file.Write(binary);
        }
    }
}
