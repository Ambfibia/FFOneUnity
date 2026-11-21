using ContentPaths = FFOne.Content.ContentPaths;
using System.Collections;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class SettingsWindowTests
    {
        GameObject owner;
        PanelSettings panelSettings;
        RenderTexture target;
        SettingsWindow settings;
        Keyboard keyboard;
        Gamepad gamepad;
        int worldClicks;

        [UnityTest]
        public IEnumerator NativeWindowDropdownsLocalesCaptureAndModalPicking()
        {
            // A runtime UIDocument is required: an unattached VisualElement does not exercise picking,
            // resolved USS, popup positions, text/font layout or the actual UI rendering backend.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("This acceptance test requires a graphics device; run without -nographics.");

            target = new RenderTexture(1264, 681, 0, RenderTextureFormat.ARGB32);
            target.Create();
            var productionPanel = AssetDatabase.LoadAssetAtPath<PanelSettings>(ContentPaths.Resolve("Assets/UI/MainPanel.asset"));
            Assert.That(productionPanel, Is.Not.Null, "Use the production panel's runtime theme and font settings for visual acceptance.");
            panelSettings = Object.Instantiate(productionPanel);
            panelSettings.targetTexture = target;
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = 1;
            owner = new GameObject("Settings native panel acceptance");
            owner.SetActive(false);
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            owner.SetActive(true);
            // Unity 6 initializes a runtime document during its enable/update lifecycle.
            // Wait for that lifecycle instead of dereferencing the root during AddComponent.
            for (int frame = 0; frame < 10 && document.rootVisualElement == null; frame++) yield return null;
            Assert.That(document.rootVisualElement, Is.Not.Null, "The enabled UIDocument must create its runtime root.");
            var root = document.rootVisualElement;
            root.style.width = 1264; root.style.height = 681;
            worldClicks = 0;
            var worldButton = new Button(() => worldClicks++) { name = "world-underlay" };
            worldButton.style.position = Position.Absolute;
            worldButton.style.left = worldButton.style.top = worldButton.style.right = worldButton.style.bottom = 0;
            root.Add(worldButton);
            var preferences = new SettingsPreferences(); // Intentionally no path: never touch user preferences.
            settings = new SettingsWindow(root, preferences);
            preferences.Changed += ApplyBindings;
            yield return null; yield return null;
            Assert.That(root.panel, Is.Not.Null);
            Assert.That(settings.Visible, Is.False);

            settings.Open(); yield return null; yield return null;
            var overlay = root.Q("settings-overlay");
            Assert.That(settings.Visible, Is.True);
            Assert.That(root.Q<Button>("tab-graphics").text, Is.EqualTo("GRAPHICS & SOUND"));
            Assert.That(root.Q("settings-window").resolvedStyle.width, Is.EqualTo(1020).Within(.1f));
            Assert.That(root.Q("page-graphics").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.panel.Pick(new Vector2(4,4)), Is.SameAs(overlay), "The visible modal must absorb world clicks outside its window.");
            Assert.That(root.Q<Button>("settings-close").resolvedStyle.unitySliceLeft, Is.Zero,
                "A circular close icon must render as a single image.");
            var frameImage = root.Q("settings-frame").resolvedStyle.backgroundImage;
            var frameTexture = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/option/outer-frame");
            Assert.That(frameTexture, Is.Not.Null, "Option textures must import as Texture2D, never auto-detected cubemaps.");
            Assert.That(frameImage.texture == frameTexture || frameImage.sprite != null && frameImage.sprite.texture == frameTexture, Is.True, "The frame must render its actual native artwork, not the missing-image warning texture.");
            WritePreview("settings-en-graphics.png");

            Submit(root.Q<Button>("detail")); yield return null;
            var choices = root.Query<Button>(className:"option-choice").ToList();
            Assert.That(choices.Count, Is.EqualTo(5));
            Assert.That(root.panel.Pick(choices[4].worldBound.center), Is.SameAs(choices[4]),
                "The dropdown must receive clicks above the settings page.");
            Submit(choices[4]); yield return null;
            Assert.That(preferences.Get<int>("graphics.detail"), Is.EqualTo(4));
            Assert.That(preferences.Get<bool>("graphics.glow"), Is.False);
            Assert.That((int)preferences.Committed["graphics"]["detail"], Is.EqualTo(2), "Graphics remain draft until Apply or Save.");
            Submit(root.Q<Button>("settings-apply"));
            Assert.That((int)preferences.Committed["graphics"]["detail"], Is.EqualTo(4));

            Submit(root.Q<Button>("tab-social")); yield return null;
            Assert.That(root.Q("page-social").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Button>("text-locale").resolvedStyle.unitySliceRight, Is.EqualTo(27),
                "Narrow dropdowns must preserve the native arrow cap.");
            WritePreview("settings-en-social.png");
            Submit(root.Q<Button>("text-locale")); yield return null;
            choices = root.Query<Button>(className:"option-choice").ToList();
            Assert.That(choices.Select(c => c.text), Is.EqualTo(new[] { "ENGLISH", "RUSSIAN" }));
            WritePreview("settings-en-language-dropdown.png");
            Submit(choices[1]); yield return null; yield return null;
            Assert.That(preferences.Get<string>("text_locale"), Is.EqualTo("ru"));
            Assert.That(preferences.Get<string>("voice_locale"), Is.EqualTo("en"));
            Assert.That(root.Q<Button>("settings-save").text, Is.EqualTo(LocalizationCatalog.Lookup("ui.option.save_exit", "ru")));
            Assert.That(root.Q<Button>("voice-locale").text, Is.EqualTo(LocalizationCatalog.Lookup("ui.option.language.english", "ru")));
            var blockedHelp = root.Q("page-social").Q<Label>(className:"option-description");
            var helpSize = blockedHelp.MeasureTextSize(blockedHelp.text, blockedHelp.resolvedStyle.width,
                VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
            Assert.That(helpSize.y, Is.LessThanOrEqualTo(blockedHelp.resolvedStyle.height + 1), "Russian blocked-player help must fit before the list.");
            var save = root.Q<Button>("settings-save");
            var saveSize = save.MeasureTextSize(save.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Assert.That(saveSize.x, Is.LessThanOrEqualTo(save.resolvedStyle.width - save.resolvedStyle.paddingLeft - save.resolvedStyle.paddingRight + 1), "Russian Save and Exit must fit its original button rectangle.");
            WritePreview("settings-ru-social.png");
            Submit(root.Q<Button>("voice-locale")); yield return null;
            Assert.That(root.Query<Button>(className:"option-choice").ToList().Count, Is.EqualTo(2));
            WritePreview("settings-ru-voice-dropdown.png");
            Key(overlay, KeyCode.Escape); yield return null;
            Assert.That(settings.Visible, Is.True, "Escape first dismisses the dropdown, not the entire modal.");
            Assert.That(root.Query<Button>(className:"option-choice").ToList(), Is.Empty);

            Submit(root.Q<Button>("tab-controls")); yield return null;
            var controls = root.Q("page-controls");
            var sensitivityLabels = controls.Query<Label>(className:"option-slider-range-start").ToList();
            var sensitivitySliders = controls.Query<Slider>(className:"option-slider").ToList();
            Assert.That(sensitivityLabels.Count, Is.EqualTo(2));
            Assert.That(sensitivityLabels.Zip(sensitivitySliders, (label, slider) => slider.worldBound.xMin - label.worldBound.xMax),
                Is.All.GreaterThanOrEqualTo(6f), "Sensitivity captions must clear their tracks.");
            WritePreview("settings-ru-controls.png");
            var forwardPrimary = root.Q<Button>("binding-Up-primary");
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            Assert.That(forwardPrimary, Is.Not.Null);
            Submit(forwardPrimary); yield return null;
            var prompt = root.Q<Label>("capture-prompt");
            Assert.That(prompt.text, Does.Not.Contain("{action}"));
            Assert.That(prompt.text, Does.Not.Contain("{seconds}"));
            yield return Press(InputKey.Delete);
            Assert.That(preferences.Get<string>("input.bindings.Up.primary"), Is.Empty);
            Assert.That(prompt.text, Is.Empty);
            Submit(forwardPrimary); yield return Press(InputKey.T);
            Assert.That(preferences.Get<string>("input.bindings.Up.primary"), Is.EqualTo("<Keyboard>/t"));
            var forward = GameplayInput.Map.FindAction("Up", true);
            Assert.That(forward.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/w"), "A draft must not change the live action.");
            Submit(root.Q<Button>("binding-Down-primary")); yield return Press(InputKey.T);
            Assert.That(preferences.Get<string>("input.bindings.Down.primary"), Is.EqualTo("<Keyboard>/s"));
            Assert.That(root.Q("settings-popup-layer").childCount, Is.EqualTo(1), "Duplicate input must open a blocking message.");
            WritePreview("settings-ru-duplicate-binding.png");
            Key(overlay,KeyCode.Escape); Key(overlay,KeyCode.Escape); yield return null;
            Assert.That(settings.Visible, Is.False);
            Assert.That((string)preferences.Committed["input"]["bindings"]["Up"]["primary"], Is.EqualTo("<Keyboard>/w"), "Closing discards uncommitted mapping edits.");
            Assert.That(root.panel.Pick(new Vector2(4,4)), Is.SameAs(worldButton), "Closing restores the world hit-test surface.");
            Submit(worldButton); Assert.That(worldClicks, Is.EqualTo(1));
            settings.Open(); yield return null;
            Assert.That(root.Q("page-graphics").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            var rangeLabels = root.Q("page-graphics").Query<Label>(className:"option-slider-range-start").ToList();
            Assert.That(rangeLabels.Count, Is.EqualTo(7));
            var sliders = root.Q("page-graphics").Query<Slider>(className:"option-slider").ToList();
            Assert.That(rangeLabels.Zip(sliders, (label, slider) => slider.worldBound.xMin - label.worldBound.xMax),
                Is.All.GreaterThanOrEqualTo(6f), "The Russian range captions must clear the slider track.");
            WritePreview("settings-ru-graphics.png");

            Submit(root.Q<Button>("tab-controls"));
            Submit(forwardPrimary); yield return Press(InputKey.T);
            Submit(root.Q<Button>("binding-Up-pad"));
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.LeftShoulder));
            yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
            Assert.That(preferences.Get<string>("input.bindings.Up.pad"), Is.EqualTo("<Gamepad>/leftShoulder"));
            Submit(root.Q<Button>("settings-apply"));
            Assert.That(forward.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/t"));
            Assert.That(forward.bindings[2].effectivePath, Is.EqualTo("<Gamepad>/leftShoulder"));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            settings?.Dispose(); settings = null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            if (Application.isPlaying) GameplayInput.Apply(SettingsPreferences.Defaults());
            if (owner) Object.DestroyImmediate(owner);
            if (panelSettings) Object.DestroyImmediate(panelSettings);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
        static void Submit(Button button)
        {
            Assert.That(button, Is.Not.Null);
            button.Focus();
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
        }
        static void Key(VisualElement element, KeyCode key)
        {
            using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = key })) { evt.target = element; element.SendEvent(evt); }
        }
        IEnumerator Press(InputKey key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
        void ApplyBindings() => GameplayInput.Apply(settings.Preferences.Committed);
        void WritePreview(string name)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                var pixels = image.GetPixels32();
                Assert.That(pixels.Where((_,i) => i % 64 == 0).Select(c => (c.r,c.g,c.b,c.a)).Distinct().Count(), Is.GreaterThan(20), "Panel capture must contain rendered artwork/text, not a cleared render target.");
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/MigrationPreview"));
                Directory.CreateDirectory(ContentPaths.FilePath(directory)); File.WriteAllBytes(ContentPaths.FilePath(Path.Combine(directory,name)),image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
    }
}
