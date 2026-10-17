using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Integration;
using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class PresentationTests
    {
        [TestCase("ui/en/launcher/login/login_screen_button_bg", 8, 8, 8, 18)]
        [TestCase("ui/en/server-selection/ff-button-normal", 5, 5, 5, 5)]
        [TestCase("ui/en/launcher/login/ff-textfield-normal", 2, 2, 2, 2)]
        [TestCase("ui/en/character/creation/layout/CCLeftIn12BG", 5, 40, 40, 5)]
        [TestCase("ui/en/character/creation/name/CCNameTab1", 5, 10, 5, 21)]
        [TestCase("ui/en/character/creation/name/CCNameTabButtonNormal", 35, 0, 35, 20)]
        [TestCase("ui/en/character/creation/body/checkbox/CCCheckboxChecked", 0, 0, 0, 0)]
        public void ImportedSpritesAndStylesUseTheSameSourceBorders(string path, int left, int bottom, int right, int top)
        {
            var sprite = FFOne.Content.ContentAssets.Load<Sprite>(path);
            Assert.That(sprite, Is.Not.Null, path);
            Assert.That(sprite.border, Is.EqualTo(new Vector4(left, bottom, right, top)));
            var element = new VisualElement(); FFStyle.Background(element, path);
            Assert.That(element.style.backgroundImage.value.sprite, Is.EqualTo(sprite));
            Assert.That(element.style.unitySliceLeft.value, Is.EqualTo(left));
            Assert.That(element.style.unitySliceBottom.value, Is.EqualTo(bottom));
            Assert.That(element.style.unitySliceTop.value, Is.EqualTo(top));
            Assert.That(element.style.unitySliceScale.value, Is.EqualTo(1));
        }
        [Test]
        public void ExplicitCreationBorderDoesNotOverrideTheSharedLoginSprite()
        {
            const string path = "ui/en/launcher/login/ff-textfield-normal";
            var element = new VisualElement();
            FFStyle.Background(element, path, new Vector4(4, 4, 4, 4));
            Assert.That(element.style.backgroundImage.value.sprite, Is.Null);
            Assert.That(element.style.backgroundImage.value.texture, Is.EqualTo(FFOne.Content.ContentAssets.Load<Texture2D>(path)));
            Assert.That(element.style.unitySliceLeft.value, Is.EqualTo(4));
            Assert.That(FFOne.Content.ContentAssets.Load<Sprite>(path).border, Is.EqualTo(new Vector4(2, 2, 2, 2)));
        }
        [Test]
        public void ReskinningButtonDoesNotLeaveOldHoverCallbacksOrBorders()
        {
            var window = ScriptableObject.CreateInstance<EditorWindow>();
            window.Show();
            try
            {
            var button = new Button();
            window.rootVisualElement.Add(button);
            FFStyle.Button(button, "ui/en/launcher/login/ff-textfield-normal", "ui/en/server-selection/ff-button-normal");
            var enter = PointerEnterEvent.GetPooled(); enter.target = button; button.SendEvent(enter); enter.Dispose();
            Assert.That(button.style.unitySliceLeft.value, Is.EqualTo(5));
            FFStyle.Button(button, "ui/en/character/creation/body/checkbox/CCCheckboxChecked");
            var leave = PointerLeaveEvent.GetPooled(); leave.target = button; button.SendEvent(leave); leave.Dispose();
            Assert.That(button.style.unitySliceLeft.value, Is.Zero);
            Assert.That(button.style.backgroundImage.value.sprite.name, Is.EqualTo("CCCheckboxChecked"));
            }
            finally { window.Close(); }
        }
        [Test]
        public void DisplayCaptionsAreUppercaseAndAcceptedFontsContainLatinAndCyrillic()
        {
            Assert.That(FFStyle.Caption("Log in / Запомнить"), Is.EqualTo("LOG IN / ЗАПОМНИТЬ"));
            foreach (var name in new[] { "jeffe", "chaletbook-regular" })
            {
                var source = FFOne.Content.ContentAssets.Load<Font>("fonts/" + name);
                var sdf = FFOne.Content.ContentAssets.Load<FontAsset>("fonts/" + name + "-sdf");
                Assert.That(sdf, Is.Not.Null);
                Assert.That(sdf.sourceFontFile, Is.EqualTo(source));
                Assert.That(sdf.TryAddCharacters("USERNAME ПАРОЛЬ Ёё"), Is.True);
            }
        }
        [Test]
        public void PreviewResolutionSupersamplesPhysicalPixelsAndPreservesAspect()
        {
            Assert.That(PreviewStage.PreviewResolution(new Vector2(812.1f, 861.7f)), Is.EqualTo(new Vector2Int(1625, 1724)));
            Assert.That(PreviewStage.PreviewResolution(new Vector2(550, 583)), Is.EqualTo(new Vector2Int(1100, 1166)));
            Assert.That(PreviewStage.PreviewResolution(new Vector2(8192, 4096)), Is.EqualTo(new Vector2Int(4096, 2048)));
            Assert.That(PreviewRenderQuality.Resolution(new Vector2(64, 80), 1.5f), Is.EqualTo(new Vector2Int(192, 240)), "Panel density must be included once in addition to element transforms.");
            Assert.That(PreviewRenderQuality.Resolution(new Vector2(float.NaN, 80)), Is.EqualTo(Vector2Int.one));
        }
        [Test]
        public void PreviewKeepsItsRenderedTextureThroughPixelJitterAndUnsettledLayout()
        {
            object view = PreviewView();
            var type = view.GetType();
            var resize = typeof(PreviewStage).GetMethod("ResizeTexture", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                resize.Invoke(null, new object[] { view, new Vector2Int(550, 583) });
                var texture = (RenderTexture)type.GetField("Texture").GetValue(view);
                var image = (Image)type.GetField("Target").GetValue(view);
                var camera = (Camera)type.GetField("Camera").GetValue(view);
                foreach (var size in new[] { new Vector2Int(551, 584), new Vector2Int(550, 583), new Vector2Int(650, 683), new Vector2Int(660, 693) })
                {
                    resize.Invoke(null, new object[] { view, size });
                    Assert.That(type.GetField("Texture").GetValue(view), Is.SameAs(texture));
                    Assert.That(image.image, Is.SameAs(texture));
                    Assert.That(camera.targetTexture, Is.SameAs(texture));
                    Assert.That(texture.IsCreated(), Is.True);
                }
                type.GetField("FrameDelay").SetValue(view, 2);
                resize.Invoke(null, new object[] { view, new Vector2Int(660, 693) });
                resize.Invoke(null, new object[] { view, new Vector2Int(660, 693) });
                var resized = (RenderTexture)type.GetField("Texture").GetValue(view);
                Assert.That(resized.width, Is.EqualTo(660));
                Assert.That(resized.height, Is.EqualTo(693));
                Assert.That(image.image, Is.SameAs(resized));
                Assert.That(camera.targetTexture, Is.SameAs(resized));
                Assert.That(type.GetField("FrameDelay").GetValue(view), Is.EqualTo(2));
            }
            finally { ((IDisposable)view).Dispose(); }
        }
        [Test]
        public void FramingAnAvatarDoesNotEnableItsHiddenCamera()
        {
            var owner = new GameObject("Preview stage test");
            owner.SetActive(false);
            var stage = owner.AddComponent<PreviewStage>();
            object view = PreviewView();
            var camera = (Camera)view.GetType().GetField("Camera").GetValue(view);
            try
            {
                camera.enabled = false;
                typeof(PreviewStage).GetMethod("Frame", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, new[] { view });
                Assert.That(view.GetType().GetField("HasFrame").GetValue(view), Is.True);
                Assert.That(camera.enabled, Is.False);
            }
            finally { ((IDisposable)view).Dispose(); UnityEngine.Object.DestroyImmediate(owner); }
        }
        static object PreviewView()
        {
            var type = typeof(PreviewStage).GetNestedType("View", BindingFlags.NonPublic);
            object view = Activator.CreateInstance(type, true);
            var root = new GameObject("Preview resource test");
            var avatar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            avatar.transform.SetParent(root.transform, false);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.enabled = false;
            type.GetField("Root").SetValue(view, root);
            type.GetField("Avatar").SetValue(view, avatar.AddComponent<NativeAvatarPreview>());
            type.GetField("Camera").SetValue(view, camera);
            type.GetField("Target").SetValue(view, new Image());
            return view;
        }
        [Test]
        public void MainSceneAndUrpIncludeTheCharacterInkPass()
        {
            Assert.That(EditorBuildSettings.scenes.Single(s => s.enabled).path, Is.EqualTo("Assets/FFOne/Scenes/Bootstrap/Main.unity"));
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ContentPaths.Resolve("Assets/Settings/PC_RPAsset.asset"));
            Assert.That(pipeline.msaaSampleCount, Is.EqualTo(4));
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ContentPaths.Resolve("Assets/Settings/PC_Renderer.asset"));
            var ink = renderer.rendererFeatures.OfType<RenderObjects>().Where(f => f.settings.filterSettings.PassNames.Contains("FFCharacterOutline")).ToArray();
            Assert.That(ink.Length, Is.EqualTo(2));
            Assert.That(ink.Select(f => f.settings.filterSettings.RenderQueueType), Is.EquivalentTo(new[] { RenderQueueType.Opaque, RenderQueueType.Transparent }));
            Assert.That(Shader.Find("FFOne/NativeToon").isSupported, Is.True);
        }
    }
}
