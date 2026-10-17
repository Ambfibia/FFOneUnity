#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using FFOne.Creation;
using FFOne.Network;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    /// <summary>Opt-in validation of shipped UI bundles and local actions; never logs into an account.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class NativeUiSmoke : MonoBehaviour
    {
        FrontendController frontend;
        VisualElement root;
        RenderTexture target;
        PanelSettings panel;

        void LateUpdate()
        {
            // Hidden Windows swapchains can skip automatic camera rendering. Keep
            // the real preview cameras and their render textures in the capture.
            foreach (var camera in FindObjectsByType<Camera>())
            {
                if (!camera.enabled || !camera.targetTexture) continue;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if (Environment.GetCommandLineArgs().Contains("--native-ui-smoke"))
                new GameObject("Native UI player validation").AddComponent<NativeUiSmoke>();
        }

        void Awake()
        {
            // Hidden Windows players may not present their swapchain. Render the actual
            // runtime panel offscreen so validation does not depend on window visibility.
            var document = FindFirstObjectByType<UIDocument>();
            if (!document || !document.panelSettings) return;
            var args = Environment.GetCommandLineArgs();
            int Dimension(string name, int fallback)
            {
                int index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int value) ? value : fallback;
            }
            target = new RenderTexture(Dimension("-screen-width", Screen.width), Dimension("-screen-height", Screen.height), 0, RenderTextureFormat.ARGB32);
            target.Create();
            panel = Instantiate(document.panelSettings);
            panel.targetTexture = target;
            document.panelSettings = panel;
        }

        IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline)
            {
                frontend = FindFirstObjectByType<FrontendController>();
                if (frontend && frontend.Root?.Q<Button>("create-character") != null) break;
                yield return null;
            }
            if (!Step(() => {
                if (!frontend || frontend.Root == null) throw new InvalidOperationException("Frontend did not initialize.");
                root = frontend.Root;
                FFView.Require<Button>(root, "login-submit");
                Debug.Log("NATIVE UI PLAYER SMOKE STARTED");
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Step(() => Capture("login"))) yield break;
            if (!Step(frontend.ShowSelection)) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var create = FFView.Require<Button>(root, "create-character");
                if (root.panel.Pick(create.worldBound.center) != create) throw new InvalidOperationException("Create button is covered or outside its visual bounds.");
                if (root.Q<Button>("enter-world").resolvedStyle.display != DisplayStyle.None)
                    throw new InvalidOperationException("An empty roster must not show Enter Game.");
                Capture("selection"); Submit(create);
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var creator = FFView.Require<VisualElement>(root, "character-creation");
                FFView.Require<Button>(creator, "name-continue");
                FFView.Require<VisualElement>(creator, "name-wheel-0");
                Capture("generated-name"); Submit(FFView.Require<Button>(creator, "custom-name"));
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var creator = FFView.Require<VisualElement>(root, "character-creation");
                var field = FFView.Require<TextField>(creator, "custom-character-name");
                field.value = "Test Hero";
                Capture("custom-name"); Submit(FFView.Require<Button>(creator, "creation-exit"));
                if (root.Q("character-creation") != null) throw new InvalidOperationException("Creation Exit did not close the view.");
                SetPreviewRoster(); frontend.ShowSelection();
            })) yield break;
            for (int frame = 0; frame < 12; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("selection-occupied");
                Submit(FFView.Require<Button>(root, "delete-character"));
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var field = FFView.Require<TextField>(root, "delete-first-name");
                field.value = "Wrong";
                if (root.Q<Button>("confirm-delete").enabledSelf) throw new InvalidOperationException("Delete accepted a different name.");
                field.value = frontend.SelectedCharacter.FirstName;
                if (!root.Q<Button>("confirm-delete").enabledSelf) throw new InvalidOperationException("Delete rejected the matching first name.");
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("delete-character");
                AssertHit("cancel-delete"); AssertHit("confirm-delete");
                Submit(FFView.Require<Button>(root, "cancel-delete"));
                frontend.SelectedCharacter.Style.AppearanceComplete = false;
                Submit(FFView.Require<Button>(root, "enter-world"));
            })) yield break;
            for (int frame = 0; frame < 12; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("appearance");
                foreach (string name in new[] { "save-character", "appearance-exit", "gender-boy", "gender-girl", "height-next", "hair-next" }) AssertHit(name);
                Submit(FFView.Require<Button>(root, "gender-girl"));
            })) yield break;
            for (int frame = 0; frame < 12; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("appearance-girl");
                Submit(FFView.Require<Button>(root, "appearance-exit"));
                typeof(FusionClient).GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(frontend.Session, Array.Empty<CharacterInfo>());
                frontend.SendMessage("ToggleWorldMenu", SendMessageOptions.RequireReceiver);
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var menu = FFView.Require<VisualElement>(root, "quit-root");
                var cancel = FFView.Require<Button>(menu, "cancel");
                if (root.panel.Pick(cancel.worldBound.center) != cancel) throw new InvalidOperationException("Quit menu hit target does not match its geometry.");
                var background = cancel.Q(className:"retro-background").resolvedStyle.backgroundImage;
                if (background.texture == null && background.sprite == null) throw new InvalidOperationException("Missing source skin texture in player bundle.");
                Capture("quit"); Submit(cancel);
                if (root.Q("quit-root") != null) throw new InvalidOperationException("Quit menu Cancel did not close the view.");
                Debug.Log($"NATIVE UI PLAYER SMOKE PASSED at {target.width}x{target.height}: login, empty/occupied selection, delete confirmation, generated/custom name, male/female appearance, quit menu, bounds and hit targets. Offline preview roster only; no login, deletion or character-save requests performed.");
            })) yield break;
            Application.Quit(0);
        }

        void SetPreviewRoster()
        {
            var catalog = new CreationCatalog();
            CharacterInfo Sample(int slot, int gender, string first, string last)
            {
                int First(string category) => (int)catalog.Choices(category, gender)[0]["value"];
                var info = CreationCatalog.Preview(new CharacterCreation {
                    Slot = slot, FirstName = first, LastName = last,
                    Style = new CharacterStyle { Gender = gender, Height = 2, Body = 1, FaceStyle = First("face"), HairStyle = First("hair"), AppearanceComplete = true },
                    UpperBodyId = First("shirt"), LowerBodyId = First("pants"), FootId = First("shoes")
                });
                info.Uid = slot; info.Level = 4 - slot; info.X = 614400; info.Y = 153600;
                return info;
            }
            typeof(FusionClient).GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(frontend.Session, new[] { Sample(1, 1, "Amfibi", "Omnik"), Sample(2, 2, "Clancy", "Galactichammer") });
        }

        void AssertHit(string name)
        {
            var button = FFView.Require<Button>(root, name);
            var bounds = button.worldBound;
            if (bounds.xMin < 0 || bounds.yMin < 0 || bounds.xMax > target.width + 1 || bounds.yMax > target.height + 1)
                throw new InvalidOperationException("Control is outside the viewport: " + name + " " + bounds);
            var picked = root.panel.Pick(bounds.center);
            if (picked != button && !button.Contains(picked)) throw new InvalidOperationException("Control is covered: " + name);
            var label = button.Q<Label>(name + "-label");
            if (label != null && label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined).x > label.contentRect.width + 1)
                throw new InvalidOperationException("Control caption is clipped: " + name);
        }

        static bool Step(Action action)
        {
            try { action(); return true; }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); return false; }
        }

        static void Submit(Button button)
        {
            if (!button.enabledInHierarchy) throw new InvalidOperationException("Disabled action: " + button.name);
            button.Focus();
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }

        void Capture(string name)
        {
            if (!target) throw new InvalidOperationException("UI render target was not initialized.");
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                if (image.GetPixels32().Where((_, i) => i % 64 == 0).Select(c => (c.r,c.g,c.b,c.a)).Distinct().Count() < 20)
                    throw new InvalidOperationException("The UI panel capture is empty: " + name);
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "UiValidation");
                var args = Environment.GetCommandLineArgs();
                int output = Array.IndexOf(args, "--ui-output-dir");
                if (output >= 0 && output + 1 < args.Length) directory = Path.GetFullPath(args[output + 1]);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }

        void OnDestroy()
        {
            if (panel) Destroy(panel);
            if (target) { target.Release(); Destroy(target); }
        }
    }
}
#endif
