#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FFOne.Creation;
using FFOne.Network;
using FFOne.Integration;
using FFOne.Tutorial;
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
            target = new RenderTexture(UiSmokeLaunch.Dimension("-screen-width", Screen.width),
                UiSmokeLaunch.Dimension("-screen-height", Screen.height), 0, RenderTextureFormat.ARGB32);
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
                UiSmokeLaunch.VerifyLocale(frontend, target);
            })) yield break;
            yield return new WaitForEndOfFrame();
            if (!Step(() => Capture("login"))) yield break;
            if (UiSmokeLaunch.Locale != null)
            {
                if (!Step(() => {
                    AssertHit("edit-account-VisualExisting");
                    Submit(FFView.Require<Button>(root, "edit-account-VisualExisting"));
                })) yield break;
                yield return null; yield return new WaitForEndOfFrame();
                if (!Step(() => {
                    Capture("login-edit-account"); AssertHit("back-to-accounts");
                    Submit(FFView.Require<Button>(root, "back-to-accounts"));
                    Submit(FFView.Require<Button>(root, "add-server-page"));
                })) yield break;
                yield return null; yield return new WaitForEndOfFrame();
                if (!Step(() => {
                    Capture("login-add-server"); AssertHit("add-server");
                    Submit(FFView.Require<Button>(root, "add-server"));
                })) yield break;
                yield return null; yield return new WaitForEndOfFrame();
                if (!Step(() => {
                    Capture("login-invalid-server"); Submit(FFView.Require<Button>(root, "add-server-page"));
                    Submit(FFView.Require<Button>(root, "registration-help"));
                })) yield break;
                yield return null; yield return new WaitForEndOfFrame();
                if (!Step(() => {
                    Capture("registration"); AssertHit("close-registration");
                    Submit(FFView.Require<Button>(root, "close-registration"));
                    frontend.OpenSettings();
                })) yield break;
                foreach (string page in new[] { "graphics", "game-ui", "social", "controls" })
                {
                    yield return null; yield return new WaitForEndOfFrame();
                    if (!Step(() => {
                        string tab = "tab-" + page;
                        AssertHit(tab);
                        Submit(FFView.Require<Button>(root, tab));
                    })) yield break;
                    yield return null; yield return new WaitForEndOfFrame();
                    if (!Step(() => Capture("settings-" + page))) yield break;
                    string dropdown = page == "graphics" ? "resolution" : page == "social" ? "text-locale" : null;
                    if (dropdown == null) continue;
                    if (!Step(() => {
                        AssertHit(dropdown);
                        Submit(FFView.Require<Button>(root, dropdown));
                    })) yield break;
                    yield return null; yield return new WaitForEndOfFrame();
                    if (!Step(() => {
                        Capture("settings-" + dropdown);
                        var options = root.Query<Button>(className: "option-choice").ToList();
                        var option = options[dropdown == "text-locale" && UiSmokeLaunch.Locale == "ru" ? 1 : 0];
                        var pick = root.panel.Pick(option.worldBound.center);
                        if (pick != option && !option.Contains(pick))
                            throw new InvalidOperationException("Settings dropdown is covered: " + dropdown);
                        Submit(option);
                    })) yield break;
                }
                if (!Step(() => Submit(FFView.Require<Button>(root, "settings-close")))) yield break;
            }
            Task loading = null;
            if (!Step(() => loading = frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterSelection))) yield break;
            deadline = Time.realtimeSinceStartup + 180;
            while (!loading.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Step(() => FinishTask(loading, "loaded loading artwork"))) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                AssertLoadingPresentation(true); Capture("loading-resources");
                frontend.SetLoadingResourceProgressVisible(false);
            })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => { AssertLoadingPresentation(false); Capture("loading-background-only"); frontend.HideLoading(); })) yield break;
            Task selection = null;
            if (!Step(() => selection = frontend.ShowSelectionAsync())) yield break;
            deadline = Time.realtimeSinceStartup + 180;
            while (!selection.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Step(() => { FinishTask(selection, "empty character selection"); RequireSelectionReady(); })) yield break;
            yield return null; yield return new WaitForEndOfFrame();
            if (!Step(() => {
                var create = FFView.Require<Button>(root, "create-character");
                if (root.panel.Pick(create.worldBound.center) != create) throw new InvalidOperationException("Create button is covered or outside its visual bounds.");
                if (root.Q<Button>("enter-world").resolvedStyle.display != DisplayStyle.None)
                    throw new InvalidOperationException("An empty roster must not show Enter Game.");
                Capture("selection"); Submit(create);
            })) yield break;
            deadline = Time.realtimeSinceStartup + 120;
            while (root.Q<Button>("name-continue")?.enabledInHierarchy != true && Time.realtimeSinceStartup < deadline)
            {
                var cutscene = FindFirstObjectByType<DexterShipCutscene>();
                if (cutscene && cutscene.IsPlaying) cutscene.Skip();
                yield return null;
            }
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
            })) yield break;
            deadline = Time.realtimeSinceStartup + 120;
            while (!frontend.SelectionReady && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Step(() => { RequireSelectionReady(); SetPreviewRoster(); selection = frontend.ShowSelectionAsync(); })) yield break;
            deadline = Time.realtimeSinceStartup + 180;
            while (!selection.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Step(() => { FinishTask(selection, "occupied character selection"); RequireSelectionReady(); })) yield break;
            for (int frame = 0; frame < 3; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("selection-occupied");
                FrontendAvatarSmokeEvidence.AssertSelection(frontend, OutputDirectory(), "selection-occupied", "NATIVE UI");
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
                RequireSelectionReady();
                frontend.SelectedCharacter.Style.AppearanceComplete = false;
                Submit(FFView.Require<Button>(root, "enter-world"));
            })) yield break;
            deadline = Time.realtimeSinceStartup + 120;
            while ((!frontend.GetComponent<PreviewStage>().DraftReady || root.Q<Button>("save-character")?.enabledInHierarchy != true) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("appearance");
                foreach (string name in new[] { "save-character", "appearance-exit", "gender-boy", "gender-girl", "height-next", "hair-next" }) AssertHit(name);
                Submit(FFView.Require<Button>(root, "gender-girl"));
            })) yield break;
            deadline = Time.realtimeSinceStartup + 120;
            while ((!frontend.GetComponent<PreviewStage>().DraftReady || frontend.GetComponent<PreviewStage>().DraftGender != 2) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForEndOfFrame();
            if (!Step(() => {
                Capture("appearance-girl");
                Submit(FFView.Require<Button>(root, "appearance-exit"));
                // This offline fixture has no logged-in session. Restore selection
                // explicitly, as the real authenticated close path does automatically.
                selection = frontend.ShowSelectionAsync();
            })) yield break;
            deadline = Time.realtimeSinceStartup + 120;
            while (!selection.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (!Step(() => {
                FinishTask(selection, "selection after offline appearance");
                RequireSelectionReady();
                Capture("selection-after-appearance");
                FrontendAvatarSmokeEvidence.AssertSelection(frontend, OutputDirectory(), "selection-after-appearance", "NATIVE UI");
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

        void RequireSelectionReady()
        {
            if (!frontend.SelectionReady || !frontend.GetComponent<PreviewStage>().SelectionReady)
                throw new InvalidOperationException("Character selection is not ready; rendered avatar frames are required before continuing.");
        }

        static void FinishTask(Task operation, string name)
        {
            if (operation == null || !operation.IsCompleted) throw new TimeoutException("Native UI validation timed out: " + name);
            operation.GetAwaiter().GetResult();
        }

        void AssertLoadingPresentation(bool resources)
        {
            var loading = FFView.Require<VisualElement>(root, "world-loading");
            var background = FFView.Require<Image>(loading, "loading-background");
            var window = FFView.Require<VisualElement>(loading, "loading-window");
            if (loading.resolvedStyle.display != DisplayStyle.Flex || !background.image)
                throw new InvalidOperationException("The loaded loading background is missing.");
            if (window.resolvedStyle.display != (resources ? DisplayStyle.Flex : DisplayStyle.None))
                throw new InvalidOperationException("Loading resource dialog visibility does not match its phase.");
            if (resources && !window.resolvedStyle.backgroundImage.texture && !window.resolvedStyle.backgroundImage.sprite)
                throw new InvalidOperationException("The loading resource dialog has no source artwork.");
        }

        static string OutputDirectory() => UiSmokeLaunch.OutputDirectory("UiValidation");

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
                string directory = OutputDirectory();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                Debug.Log("NATIVE UI CAPTURE: " + name);
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }

        void OnDestroy()
        {
            if (panel) Destroy(panel);
            if (target) { target.Release(); Destroy(target); }
        }
    }

    static class UiSmokeLaunch
    {
        static readonly string[] Args = Environment.GetCommandLineArgs();
        static string Argument(string name)
        {
            int index = Array.IndexOf(Args, name);
            return index >= 0 && index + 1 < Args.Length ? Args[index + 1] : null;
        }

        internal static string Locale => (Args.Contains("--native-ui-smoke") || Args.Contains("--gameplay-ui-smoke") ||
            Args.Contains("--live-gameplay-smoke") || Args.Contains("--live-mentor-smoke")) ? Argument("--ui-locale") : null;
        internal static int Dimension(string name, int fallback) =>
            int.TryParse(Argument(name), out int value) && value > 0 ? value : fallback;
        internal static string OutputDirectory(string fallback) =>
            Path.GetFullPath(Argument("--ui-output-dir") ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), fallback));

        internal static void VerifyLocale(FrontendController frontend, RenderTexture target)
        {
            if (Locale != null && (Locale != "en" && Locale != "ru" || frontend.Language != Locale))
                throw new InvalidOperationException($"Requested UI locale {Locale} resolved to {frontend.Language}.");
            Debug.Log($"UI MATRIX LOCALE: {frontend.Language} at {target.width}x{target.height}");
        }
    }

    /// <summary>Development validation shared by the offline and local-account frontend flows.</summary>
    static class FrontendAvatarSmokeEvidence
    {
        public static void AssertSelection(FrontendController frontend, string directory, string name, string logPrefix)
        {
            if (!frontend.SelectionReady || frontend.SelectedCharacter == null)
                throw new InvalidOperationException("An occupied, rendered roster is required for avatar pixel validation.");
            Directory.CreateDirectory(directory);
            AssertAvatar(frontend.PreviewImage, frontend.GetComponent<PreviewStage>(), directory, name + "-main-avatar", logPrefix);
            foreach (var character in frontend.Session.Characters)
            {
                int index = character.Slot - 1;
                if (index < 0 || index >= frontend.SlotPreviewImages.Length)
                    throw new InvalidOperationException("Occupied roster character has no preview slot: " + character.Slot);
                AssertAvatar(frontend.SlotPreviewImages[index], frontend.GetComponent<PreviewStage>(), directory,
                    name + "-slot-" + character.Slot + "-avatar", logPrefix);
            }
        }

        static void AssertAvatar(Image binding, PreviewStage preview, string directory, string name, string logPrefix)
        {
            var source = binding?.image as RenderTexture;
            if (!source || !source.IsCreated()) throw new InvalidOperationException("Avatar has no live render target: " + name);
            if (binding.panel == null || binding.resolvedStyle.display == DisplayStyle.None || binding.worldBound.width <= 0 || binding.worldBound.height <= 0)
                throw new InvalidOperationException("Avatar is not displayed in the selection panel: " + name);
            var resolved = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            Color32[] pixels;
            try
            {
                // Read a resolved target so MSAA does not conceal an empty or stale camera frame.
                Graphics.Blit(source, resolved); RenderTexture.active = resolved;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply();
                pixels = image.GetPixels32();
                // Preserve failed camera targets as well as successful ones for framing QA.
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(image); RenderTexture.ReleaseTemporary(resolved); }
            int foreground = 0, transparent = 0, left = source.width, right = -1, bottom = source.height, top = -1;
            var colors = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a < 16) transparent++;
                if (pixel.a <= 32 || pixel.r >= 240 && pixel.g >= 240 && pixel.b >= 240) continue;
                foreground++;
                int x = i % source.width, y = i / source.width;
                left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                colors.Add((pixel.r / 8 << 10) | (pixel.g / 8 << 5) | pixel.b / 8);
            }
            float coverage = (float)foreground / pixels.Length;
            float width = right < left ? 0 : (float)(right - left + 1) / source.width;
            float height = top < bottom ? 0 : (float)(top - bottom + 1) / source.height;
            string evidence = $"{logPrefix} AVATAR PIXELS {name}: {source.width}x{source.height}, coverage={coverage:P2}, foreground={foreground}, transparent={transparent}, colors={colors.Count}, bounds=({left},{bottom})-({right},{top}), display={binding.worldBound}";
            Debug.Log(evidence);
            var cameraEvidence = new System.Text.StringBuilder(evidence);
            foreach (var camera in preview.GetComponentsInChildren<Camera>(true))
            {
                if (camera.targetTexture != source) continue;
                string details = $"{logPrefix} AVATAR CAMERA {name}: enabled={camera.enabled}, active={camera.gameObject.activeInHierarchy}, position={camera.transform.position}, rotation={camera.transform.eulerAngles}, fieldOfView={camera.fieldOfView}, near={camera.nearClipPlane}, far={camera.farClipPlane}, mask={camera.cullingMask}, target={source.name}, msaa={source.antiAliasing}";
                Debug.Log(details); cameraEvidence.AppendLine().Append(details);
            }
            File.WriteAllText(Path.Combine(directory, name + ".txt"), cameraEvidence.ToString());
            if (coverage < .01f || transparent < pixels.Length / 100 || colors.Count < 12 || width < .10f || height < .15f)
                throw new InvalidOperationException("Selection avatar is blank, white or too small to represent a rendered character: " + evidence);
        }
    }
}
#endif
