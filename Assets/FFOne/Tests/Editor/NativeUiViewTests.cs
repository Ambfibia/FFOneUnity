using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class NativeUiViewTests
    {
        GameObject owner;
        PanelSettings settings;
        RenderTexture target;

        [Test]
        public void MissingBindingFailsWithTheScreenAndControlName()
        {
            var root = new VisualElement { name = "example-screen" };
            var error = Assert.Throws<InvalidOperationException>(() => FFView.Require<Button>(root, "confirm"));
            Assert.That(error.Message, Does.Contain("example-screen").And.Contain("confirm"));
        }

        [TestCase(600, 600f / 681)]
        [TestCase(681, 1)]
        [TestCase(1080, 1080f / 681)]
        public void OriginalUiScaleDoesNotClampSmallWindows(int height, float expected)
        {
            Assert.That(RetrobutionLayout.Scale(height), Is.EqualTo(expected).Within(.00001));
            Assert.That(RetrobutionLayout.Scale(height, 0), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SelectionUxmlRetainsStylesCoordinatesAndHitTargetsAtThreeViewports()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            // Allocate closure state after the domain reload; Unity's resumable test iterator
            // does not serialize compiler-generated captured-local display classes.
            yield return ExerciseViews();
        }

        IEnumerator ExerciseViews()
        {
            target = new RenderTexture(1920, 1080, 0); target.Create();
            settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(ContentPaths.Resolve("Assets/UI/MainPanel.asset")));
            settings.targetTexture = target; settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 1;
            owner = new GameObject("Native UI acceptance"); owner.SetActive(false);
            var document = owner.AddComponent<UIDocument>(); document.panelSettings = settings; owner.SetActive(true);
            for (int i = 0; i < 10 && document.rootVisualElement == null; i++) yield return null;
            var root = document.rootVisualElement;
            Assert.That(root, Is.Not.Null, "Runtime UIDocument must initialize before binding the view.");
            var view = FFView.Instantiate("ui/views/CharacterSelection", "character-selection");
            Assert.That(view.styleSheets.count, Is.GreaterThanOrEqualTo(2), "Detaching a view must retain its theme and screen styles.");
            root.Add(view);
            var group = FFView.Require<VisualElement>(view, "selection-group");
            var button = FFView.Require<Button>(view, "character-slot-1");
            var preview = FFView.Require<Image>(view, "slot-preview-1");
            Assert.That(preview.pickingMode, Is.EqualTo(PickingMode.Ignore));
            int clicks = 0; button.clicked += () => clicks++;
            foreach (var viewport in new[] { new Vector2(1264,681), new Vector2(1280,720), new Vector2(1920,1080) })
            {
                root.style.width = viewport.x; root.style.height = viewport.y;
                var placement = FrontendLayout.Selection(viewport.x, viewport.y);
                float scale = FrontendLayout.CharacterScale(viewport.y);
                group.style.position = Position.Absolute;
                group.style.left = viewport.x / 2 + 30 * scale;
                group.style.top = viewport.y / 2 - 288 * scale;
                group.style.width = placement.width; group.style.height = placement.height;
                group.style.transformOrigin = new TransformOrigin(0,0);
                group.style.scale = new Scale(new Vector3(scale,scale,1));
                yield return null; yield return null;
                Assert.That(button.layout, Is.EqualTo(new Rect(43,43,413,67)), "UXML must retain the source logical rectangle before the parent transform.");
                Assert.That(button.worldBound.width, Is.EqualTo(413 * scale).Within(.1));
                Assert.That(root.panel.Pick(button.worldBound.center), Is.SameAs(button), "Decorative labels/images must not intercept the slot button.");
                Assert.That(button.resolvedStyle.borderTopWidth, Is.Zero);
                using var submit = NavigationSubmitEvent.GetPooled(); submit.target = button; button.SendEvent(submit);
            }
            Assert.That(clicks, Is.EqualTo(3));
            button.SetEnabled(false);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
            Assert.That(clicks, Is.EqualTo(3), "Disabled controls must reject activation.");

            foreach (var panel in new[] { ("generated-name-panel",672f,365f), ("custom-name-panel",672f,365f), ("appearance-panel",1020f,638f) })
            {
                root.Clear();
                var creation = FFView.Instantiate("ui/views/CharacterCreation", panel.Item1);
                root.Add(creation); yield return null; yield return null;
                Assert.That(creation.resolvedStyle.width, Is.EqualTo(panel.Item2).Within(.1));
                Assert.That(creation.resolvedStyle.height, Is.EqualTo(panel.Item3).Within(.1));
                var wheel = creation.Q("name-wheel-0");
                if (wheel != null)
                {
                    Assert.That(wheel.resolvedStyle.height, Is.EqualTo(150));
                    Assert.That(wheel.Q("name-0-0").layout.y, Is.EqualTo(-30));
                    Assert.That(wheel.Q("name-0-6").layout.y, Is.EqualTo(150));
                }
            }

            root.Clear();
            var controller = owner.AddComponent<FrontendController>(); controller.enabled = false;
            typeof(FrontendController).GetProperty("Root").SetValue(controller, root);
            // Exercise real bindings without connecting to a server or changing user preferences.
            void Invoke(string name) => typeof(FrontendController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
            Invoke("BuildSelection");
            int requestedSlot = 0;
            controller.CreateCharacterRequested += slot => requestedSlot = slot;
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                var create = root.Q<Button>("create-character"); submit.target = create; create.SendEvent(submit);
            }
            Assert.That(requestedSlot, Is.EqualTo(1), "UXML create control must call the existing roster controller.");
            root.Q("character-selection").style.display = DisplayStyle.None;
            foreach (string language in new[] { "en", "ru" })
            {
                typeof(FrontendController).GetField("language", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, language);
                Invoke("ToggleWorldMenu");
                yield return null; yield return null;
                var menu = root.Q("quit-root");
                Assert.That(menu, Is.Not.Null);
                var dialog = menu.Q("quit-dialog");
                Assert.That(dialog.worldBound.width, Is.EqualTo(206 * 1080f / 681).Within(.1));
                var cancel = menu.Q<Button>("cancel");
                Assert.That(root.panel.Pick(cancel.worldBound.center), Is.SameAs(cancel));
                Assert.That(menu.Contains(root.panel.Pick(new Vector2(5,5))), Is.True, "Modal must block the world outside its dialog.");
                var backdrop = cancel.Q(className:"retro-background").resolvedStyle.backgroundImage;
                Assert.That(backdrop.texture != null || backdrop.sprite != null, Is.True, "The generated USS must resolve source artwork in Addressables.");
                SavePreview("quit-" + language);
                using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = cancel; cancel.SendEvent(submit); }
                Assert.That(root.Q("quit-root"), Is.Null, "Source cancel action must dismiss the runtime menu.");
                yield return null;
            }
        }

        void SavePreview(string name)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                Assert.That(image.GetPixels32().Where((_,i) => i % 64 == 0).Select(c => (c.r,c.g,c.b,c.a)).Distinct().Count(), Is.GreaterThan(20));
                Directory.CreateDirectory("Logs/NativeUiPreview");
                File.WriteAllBytes("Logs/NativeUiPreview/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (owner) Object.DestroyImmediate(owner);
            if (settings) Object.DestroyImmediate(settings);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
