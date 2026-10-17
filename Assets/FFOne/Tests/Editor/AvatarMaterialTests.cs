using ContentPaths = FFOne.Content.ContentPaths;
using System.Linq;
using FFOne.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Tests
{
    public sealed class AvatarMaterialTests
    {
        [Test]
        public void AuthoredToonRampChangesRenderedPaletteWhileComicStyleKeepsItsPalette()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This render regression requires a graphics device.");
            var shader = Shader.Find("FFOne/NativeToon");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            var redRamp = Ramp(new Color(.8f, .1f, .1f, 1));
            var greenRamp = Ramp(new Color(.1f, .8f, .1f, 1));
            try
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", new Color(.5f, .5f, .5f, 1));
                material.SetFloat("_Toon", 1);
                material.SetFloat("_RimIntensity", 0);
                material.SetFloat("_Outline", 0);
                material.SetFloat("_Cull", 0);
                NativeAvatarMaterials.Configure(material, "shirt");
                material.SetTexture("_ShaderMap", redRamp);
                var red = RenderSurface(material);
                material.SetTexture("_ShaderMap", greenRamp);
                var green = RenderSurface(material);
                Assert.That(red.r, Is.GreaterThan(.2f), "The surface must render into the target.");
                Assert.That(green.g, Is.GreaterThan(.2f));
                Assert.That(red.r, Is.GreaterThan(red.g * 4), "Source toon shading must sample ramp RGB.");
                Assert.That(green.g, Is.GreaterThan(green.r * 4));

                material.SetFloat("_Comic", 1);
                material.SetTexture("_ShaderMap", redRamp);
                var comicRed = RenderSurface(material);
                material.SetTexture("_ShaderMap", greenRamp);
                var comicGreen = RenderSurface(material);
                Assert.That(Vector3.Distance(Rgb(comicRed), Rgb(comicGreen)), Is.LessThan(.02f),
                    "The separate comic style keeps its native palette.");
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(redRamp);
                Object.DestroyImmediate(greenRamp);
            }
        }

        [TestCase("m")]
        [TestCase("f")]
        public void FaceDecalDrawsAfterSkinWithoutChangingAuthoredBlendOrDepth(string gender)
        {
            string face = gender + "_face_001_type01";
            string path = $"Assets/Resources/characters/player/items/mask/{face}/models/{face}/model.glb";
            var source = AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(path)).OfType<Material>().ToArray();
            Assert.That(source.Length, Is.EqualTo(2));
            var copies = source.Select(m => new Material(m)).ToArray();
            try
            {
                for (int i = 0; i < copies.Length; i++)
                {
                    NativeAvatarMaterials.Configure(copies[i], "face");
                    foreach (string property in new[] { "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull", "_Toon" })
                        Assert.That(copies[i].GetFloat(property), Is.EqualTo(source[i].GetFloat(property)), property);
                    Assert.That(copies[i].GetColor("_Color"), Is.EqualTo(source[i].GetColor("_Color")));
                    Assert.That(copies[i].GetTexture("_ShaderMap"), Is.EqualTo(source[i].GetTexture("_ShaderMap")));
                    Assert.That(source[i].renderQueue, Is.EqualTo(2900), "Shared imported material stays intact.");
                }
                var skin = copies.Single(m => m.name.Contains("sub"));
                var decal = copies.Single(m => m.name.Contains("main"));
                Assert.That(skin.renderQueue, Is.LessThan(decal.renderQueue),
                    "Separate renderer bounds must not choose the face layer order.");
            }
            finally
            {
                foreach (var material in copies) Object.DestroyImmediate(material);
            }
        }

        static Texture2D Ramp(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        static Vector3 Rgb(Color color) => new Vector3(color.r, color.g, color.b);

        static Color RenderSurface(Material material)
        {
            var mesh = new Mesh {
                vertices = new[] { new Vector3(-.9f, -.9f, -1), new Vector3(.9f, -.9f, -1),
                    new Vector3(.9f, .9f, -1), new Vector3(-.9f, .9f, -1) },
                normals = Enumerable.Repeat(Vector3.forward, 4).ToArray(),
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            var commands = new CommandBuffer { name = "Avatar toon ramp regression" };
            var previousTarget = RenderTexture.active;
            var previousLight = Shader.GetGlobalVector("_MainLightPosition");
            var previousCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
            try
            {
                target.Create();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(false, true, Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,
                    GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, .01f, 10), true));
                commands.SetGlobalVector("_MainLightPosition", new Vector4(0, 0, 1, 0));
                commands.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(0, 0, 0, 1));
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
                return pixels.GetPixel(8, 8);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                Shader.SetGlobalVector("_MainLightPosition", previousLight);
                Shader.SetGlobalVector("_WorldSpaceCameraPos", previousCamera);
                commands.Dispose();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
