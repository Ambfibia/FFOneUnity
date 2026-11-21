#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Integration;
using FFOne.Network;
using FFOne.Tutorial;
using FFOne.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Opt-in, real account flow against the isolated local development server.</summary>
    public sealed partial class FrontendRecoverySmoke : MonoBehaviour
    {
        public int Port = 24300;
        FrontendController frontend;
        PreviewStage preview;
        float largestFrame;
        string output;
        string step = "startup";
        int loadingResourceFrames, loadingBackgroundFrames;
        bool capturedLoadingResources, capturedLoadingBackground;
        bool nativeScreenCapture;
        RenderTexture uiCapture, worldCapture;
        PanelSettings capturePanel;

        void Awake()
        {
            if (Application.isEditor) return;
            nativeScreenCapture = Environment.GetCommandLineArgs().Contains("--recovery-native-screen");
            if (nativeScreenCapture) return;
            var document = FindFirstObjectByType<UIDocument>();
            if (!document || !document.panelSettings) return;
            // Windows can clamp a hidden window to the desktop work area. Use
            // the requested render size, as NativeUiSmoke does, for real 4K QA.
            var arguments = Environment.GetCommandLineArgs();
            int Dimension(string name, int fallback)
            {
                int index = Array.IndexOf(arguments, name);
                return index >= 0 && index + 1 < arguments.Length && int.TryParse(arguments[index + 1], out int value) && value > 0 ? value : fallback;
            }
            int width = Dimension("-screen-width", Screen.width), height = Dimension("-screen-height", Screen.height);
            // Rounded UI Toolkit clipping uses stencil. A color-only target
            // renders the minimap's clip mask as a white disk in hidden QA.
            uiCapture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            worldCapture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            uiCapture.Create(); worldCapture.Create();
            capturePanel = Instantiate(document.panelSettings);
            capturePanel.clearColor = true;
            capturePanel.colorClearValue = Color.clear;
            capturePanel.targetTexture = uiCapture;
            document.panelSettings = capturePanel;
        }
        void LateUpdate()
        {
            if (Application.isEditor) return;
            if (nativeScreenCapture) { CaptureLoadingEvidence(); return; }
            var tutorial = FindFirstObjectByType<TutorialController>();
            if (tutorial && tutorial.ViewCamera && !tutorial.ViewCamera.targetTexture)
                tutorial.ViewCamera.targetTexture = worldCapture;
            var world = FindFirstObjectByType<NetworkWorldController>();
            if (world && world.View && !world.View.targetTexture) world.View.targetTexture = worldCapture;
            // The same real-camera render request used by NativeUiSmoke keeps
            // hidden Windows validation independent of swapchain presentation.
            foreach (var camera in FindObjectsByType<Camera>())
            {
                if (!camera.enabled || !camera.targetTexture) continue;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            }
            CaptureLoadingEvidence();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var arguments = Environment.GetCommandLineArgs();
            if (!arguments.Contains("--frontend-recovery-smoke") && !arguments.Contains("--login-outage-smoke")) return;
            var smoke = new GameObject("Frontend recovery validation").AddComponent<FrontendRecoverySmoke>();
            int portIndex = Array.IndexOf(arguments, "--recovery-port");
            if (portIndex >= 0 && portIndex + 1 < arguments.Length && int.TryParse(arguments[portIndex + 1], out int port)) smoke.Port = port;
        }
        void Update()
        {
            largestFrame = Mathf.Max(largestFrame, Time.unscaledDeltaTime);
            if (Time.unscaledDeltaTime > .5f) Debug.Log($"FRONTEND RECOVERY FRAME: {Time.unscaledDeltaTime:F3}s; {step}");
        }
        async void Start()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/FrontendRecovery"));
            Directory.CreateDirectory(output);
            try
            {
                if (Environment.GetCommandLineArgs().Contains("--login-outage-smoke")) await RunLoginOutage();
                else await Run();
                File.WriteAllText(Path.Combine(output, "result.txt"), $"PASSED\nLargest observed frame: {largestFrame:F3} s\nLoading resource capture: {capturedLoadingResources}\nLoading background-only capture: {capturedLoadingBackground}\n");
                Debug.Log($"FRONTEND RECOVERY SMOKE PASSED. Largest frame {largestFrame:F3}s. Evidence: {output}");
                if (!Application.isEditor) Application.Quit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), "FAILED\n" + error);
                Debug.LogException(error);
                if (!Application.isEditor) Application.Quit(1);
            }
        }

        async Task RunLoginOutage()
        {
            Require(DevelopmentConnection.Enabled && Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable) == $"127.0.0.1:{Port}",
                "The login outage smoke requires an isolated local server and profile.");
            await Wait(() => {
                frontend = FindAnyObjectByType<FrontendController>();
                return frontend && frontend.Root?.Q<Label>(className: "frontend-status") != null;
            }, "login outage frontend initialized");
            preview = frontend.GetComponent<PreviewStage>();
            var form = FFView.RequireClass<VisualElement>(frontend.Root, "ff-login-screen");
            var user = FFView.RequireClass<TextField>(form, "ff-login-username");
            var password = FFView.RequireClass<TextField>(form, "ff-login-password");
            var button = FFView.RequireClass<Button>(form, "ff-login-submit");
            var status = FFView.RequireClass<Label>(frontend.Root, "frontend-status");
            var indicator = FFView.RequireClass<VisualElement>(form, "ff-server-health");
            Require(password.isPasswordField, "The login view must mask the password.");
            bool Online() => indicator.ClassListContains("ff-server-health--online") && !indicator.ClassListContains("ff-server-health--offline");
            bool Offline() => indicator.ClassListContains("ff-server-health--offline") && !indicator.ClassListContains("ff-server-health--online");
            void Press(Button control)
            {
                Require(control.enabledInHierarchy, "The login control is disabled.");
                control.Focus();
                using var evt = NavigationSubmitEvent.GetPooled();
                evt.target = control;
                control.SendEvent(evt);
            }
            void StartLogin()
            {
                user.value = "unityalice";
                password.value = "test-only-password";
                Press(button);
            }
            bool FormAvailable() => form.resolvedStyle.display == DisplayStyle.Flex && button.enabledInHierarchy &&
                user.enabledInHierarchy && password.enabledInHierarchy && !frontend.Session.IsLoggedIn;
            async Task CaptureError(string description, string file, string connecting)
            {
                await Wait(() => FormAvailable() && !string.IsNullOrWhiteSpace(status.text) && status.text != connecting,
                    description);
                Require(Offline(), "The indicator must be offline on the frame that the login form recovers.");
                await Frames(3);
                Require(status.resolvedStyle.display == DisplayStyle.Flex, "The network error is not visible.");
                Capture(file);
                Debug.Log($"LOGIN OUTAGE: {description}; {status.text}");
            }

            Press(FFView.RequireClass<Button>(form, "ff-remember-account"));
            await Wait(() => FormAvailable() && Online(), "available initial login form with online indicator");
            await Frames(3); Capture("01-login-ready");
            string connecting = frontend.Text("ui.login.connecting", "CONNECTING...");
            const string rejectedPassword = "wrong-test-password";
            user.value = "unityalice";
            password.value = rejectedPassword;
            password.Focus();
            await Frames(3); Capture("07-password-before-submit");
            Press(button);
            await Wait(() => FormAvailable() && status.text.StartsWith("Login: server error", StringComparison.Ordinal),
                "expected rejection of the disposable password");
            Require(Online() && password.isPasswordField && password.value == rejectedPassword,
                "Authentication rejection must leave an editable, masked field on the online server.");
            await Frames(3); Capture("08-password-after-rejection");
            Debug.Log("LOGIN PASSWORD MASKING PASSED: verified before submission and after server rejection.");
            StartLogin();
            await Wait(() => frontend.Session.IsLoggedIn && frontend.SelectionReady, "initial authenticated roster");
            long uid = frontend.SelectedCharacter?.Uid ?? 0;
            Require(uid > 0, "An occupied validation roster is required.");
            await Frames(3); CaptureSelection("02-roster-online");
            File.WriteAllText(Path.Combine(output, "stop-server.request"), uid.ToString());

            await CaptureError("login form recovered after RustyFusion stopped", "03-server-stopped", connecting);
            StartLogin();
            await CaptureError("login attempt failed while RustyFusion was stopped", "04-login-while-offline", connecting);
            Require(password.isPasswordField && password.value.Length != 0,
                "A failed connection must leave the entered password masked.");
            float observeUntil = Time.realtimeSinceStartup + 2.2f;
            while (Time.realtimeSinceStartup < observeUntil)
            {
                Require(Offline(), "The offline indicator must retain its result throughout a retry.");
                Require(status.resolvedStyle.display == DisplayStyle.Flex && !string.IsNullOrWhiteSpace(status.text),
                    "The connection error disappeared while the server was still offline.");
                await Task.Yield();
            }
            File.WriteAllText(Path.Combine(output, "restart-server.request"), uid.ToString());
            await Wait(() => File.Exists(Path.Combine(output, "server-restarted.ready")), "RustyFusion restarted");
            float restartedAt = Time.realtimeSinceStartup;
            await Wait(() => FormAvailable() && Online() || Time.realtimeSinceStartup - restartedAt >= 5,
                "online indicator after restarting RustyFusion");
            Require(FormAvailable() && Online(), "The online indicator did not recover before another login attempt.");
            Require(status.text.Length == 0 && status.style.display == DisplayStyle.None,
                "The online probe did not clear the previous connection error in the same update.");
            await Frames(3);
            Require(status.resolvedStyle.display == DisplayStyle.None, "The previous connection error is still rendered.");
            Capture("05-server-online-again");
            Debug.Log("LOGIN CONNECTION ERROR RECOVERY PASSED: the offline error cleared before another login attempt.");
            Debug.Log($"SERVER INDICATOR SMOKE PASSED: offline immediately and stayed offline during retries; online observed {Time.realtimeSinceStartup - restartedAt:F3}s after restart confirmation.");

            StartLogin();
            await Wait(() => frontend.Session.IsLoggedIn && frontend.SelectionReady, "reconnected authenticated roster");
            Require(frontend.SelectedCharacter?.Uid == uid, "Reconnection did not restore the same character.");
            await Frames(3); CaptureSelection("06-roster-after-restart");
            Debug.Log($"LOGIN OUTAGE SMOKE PASSED: character UID {uid} before and after restart.");
        }

        async Task Run()
        {
            await Wait(() => {
                frontend = FindFirstObjectByType<FrontendController>();
                return frontend && frontend.Root?.Q<Button>("create-character") != null;
            }, "frontend initialization");
            preview = frontend.GetComponent<PreviewStage>();
            string stamp = DateTime.UtcNow.ToString("MMddHHmmss");
            var arguments = Environment.GetCommandLineArgs();
            int existingIndex = Array.IndexOf(arguments, "--recovery-existing-account");
            string existingAccount = existingIndex < 0 ? null : existingIndex + 1 < arguments.Length && !arguments[existingIndex + 1].StartsWith("-", StringComparison.Ordinal)
                ? arguments[existingIndex + 1] : throw new ArgumentException("--recovery-existing-account requires a local validation account name.");
            string account = existingAccount ?? "Recovery" + stamp;
            Debug.Log("FRONTEND RECOVERY ACCOUNT: " + account);
            float loginStarted = Time.realtimeSinceStartup;
            await frontend.Session.LoginAsync("127.0.0.1", Port, account, "local-validation");
            float selectionStarted = Time.realtimeSinceStartup;
            await frontend.ShowSelectionAsync();
            Debug.Log($"FRONTEND RECOVERY TIMING: login={selectionStarted - loginStarted:F3}s, rendered-selection={Time.realtimeSinceStartup - selectionStarted:F3}s.");
            await Wait(() => frontend.SelectionReady, "rendered character selection");
            if (nativeScreenCapture) await Awaitable.EndOfFrameAsync();
            else await Frames(1);
            if (existingAccount != null)
            {
                Require(frontend.Session.Characters.Count > 0 && frontend.SelectedCharacter != null,
                    "The existing validation account must have an occupied roster.");
                CaptureSelection("selection-cold-occupied");
                Require(Application.isEditor || capturedLoadingResources, "The cold occupied roster did not present a loaded loading resource dialog.");
                Debug.Log("FRONTEND RECOVERY: cold occupied roster validated; existing-account branch sends no character save, deletion or world-entry request.");
                return;
            }
            Capture("selection-empty");
            Submit("create-character");
            await WaitForCutscene("before-creation");
            await Wait(() => frontend.Root.Q<Button>("random-name")?.enabledInHierarchy == true, "name creation");
            var audio = frontend.GetComponent<RetrobutionAudioController>();
            int creatorStarts = audio.CreatorStarts;
            Require(audio.CurrentMode == RetrobutionAudioController.Mode.Creation, "Name creation lost its shared UI soundtrack.");
            var random = frontend.Root.Q<Button>("random-name");
            var wheel = frontend.Root.Q("name-wheel-0");
            for (int n = 0; n < 20; n++)
            {
                Submit("random-name");
                await Task.Yield();
                Require(ReferenceEquals(wheel, frontend.Root.Q("name-wheel-0")), "Random name replaced the wheel UI.");
            }
            Require(ReferenceEquals(random, frontend.Root.Q<Button>("random-name")), "Random name replaced its button.");
            Submit("custom-name");
            frontend.Root.Q<TextField>("custom-character-name").value = "UiProbe T" + stamp;
            Submit("name-continue");
            await Wait(() => frontend.Root.Q<Button>("save-character")?.enabledInHierarchy == true && preview.DraftReady, "appearance after accepted name");
            Capture("appearance");
            for (int n = 0; n < 4; n++)
            {
                Submit("gender-girl");
                await Wait(() => preview.DraftReady && preview.DraftGender == 2, "female appearance");
                Submit("gender-boy");
                await Wait(() => preview.DraftReady && preview.DraftGender == 1, "male appearance");
            }
            var camera = preview.DraftCamera;
            await Frames(5);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            for (int n = 0; n < 5; n++)
            {
                Submit("shirt-next");
                await Wait(() => preview.DraftReady, "clothing appearance");
                await Frames(4);
                Require(Vector3.Distance(position, camera.transform.position) < .0001f && Quaternion.Angle(rotation, camera.transform.rotation) < .001f,
                    "Changing clothing moved the preview camera pivot.");
            }
            Capture("appearance-after-clothes");
            Require(audio.CreatorStarts == creatorStarts, "Name/appearance/clothing transitions restarted the creator soundtrack.");
            Submit("save-character");
            await WaitForCutscene("before-tutorial");
            await WaitForTutorial();
            var tutorial = FindFirstObjectByType<TutorialController>();
            Require(audio.CurrentMode == RetrobutionAudioController.Mode.Tutorial && !audio.Creator.isPlaying,
                "The creator/loading soundtrack leaked into the first tutorial frame.");
            long uid = frontend.SelectedCharacter.Uid;
            Require(tutorial.WorldStream.LoadedSceneTileIds.SequenceEqual(new[] { TutorialController.WorldTileId }), "Tutorial is not using the authored Tech Square scene.");
            Require(tutorial.WorldStream.ColliderCount >= 441, "Tutorial scenery colliders are missing.");
            Require(tutorial.ViewCamera.clearFlags == CameraClearFlags.Skybox && tutorial.ViewCamera.GetComponent<Skybox>()?.material,
                "Tutorial has no original skybox.");
            Require(frontend.Session.IsInWorld && frontend.Session.IsWorldLoaded, "Tutorial was presented before the server loading handshake.");
            Require(!frontend.Root.Q("world-loading").visible || frontend.Root.Q("world-loading").resolvedStyle.display == DisplayStyle.None, "Loading screen still covers tutorial.");
            Capture("tutorial-opening");
            Require(Mathf.Abs(Mathf.DeltaAngle(tutorial.Player.eulerAngles.y, 90)) < .1f,
                "The first tutorial frame lost the source server angle 270 / Unity yaw 90.");
            if (Environment.GetCommandLineArgs().Contains("--tutorial-cinematic-smoke")) await ValidateCinematics(tutorial);
            bool HudShown() => frontend.Root.Q("tutorial-gameplay-ui")?.resolvedStyle.display == DisplayStyle.Flex;
            await Wait(() => tutorial.IsCinematic || HudShown(), "opening tutorial timeline or HUD");
            if (tutorial.IsCinematic) tutorial.SendMessage("SkipScene", SendMessageOptions.RequireReceiver);
            await Wait(HudShown, "tutorial HUD after the introduction");
            await Frames(2);
            Capture("tutorial-hud");
            Require(Physics.RaycastAll(tutorial.PlayerUnityPosition + Vector3.up * 20, Vector3.down, 50)
                .Any(hit => tutorial.WorldStream.Owns(hit.collider)), "No authored floor beneath the tutorial spawn.");
            Require(tutorial.PlayerUnityPosition.y > -150, "Tutorial player fell through Tech Square.");
            tutorial.Cancel();
            await Wait(() => !frontend.Session.IsInWorld && frontend.SelectionReady && frontend.Root.Q<Button>("enter-world")?.enabledInHierarchy == true, "return to rendered character selection");
            await Frames(3);
            CaptureSelection("selection-after-tutorial");
            // Reserve a second name, leave appearance unfinished, then resume
            // through the real roster. This must render both occupied portraits
            // without turning the name-only server record into a saved avatar.
            Submit("create-character");
            await Wait(() => FindFirstObjectByType<DexterShipCutscene>()?.IsPlaying == true, "second character intro");
            FindFirstObjectByType<DexterShipCutscene>().Skip();
            await Wait(() => frontend.Root.Q<Button>("random-name")?.enabledInHierarchy == true, "second character name");
            Submit("custom-name");
            frontend.Root.Q<TextField>("custom-character-name").value = "Pending T" + stamp;
            Submit("name-continue");
            await Wait(() => frontend.Root.Q<Button>("save-character")?.enabledInHierarchy == true && preview.DraftReady, "second character draft");
            Submit("appearance-exit");
            await Wait(() => frontend.SelectionReady, "unfinished second roster portrait");
            frontend.SelectRosterSlot(2);
            await Wait(() => frontend.SelectionReady, "selected unfinished second character");
            await Frames(3); CaptureSelection("selection-pending-second");
            Require(!frontend.SelectedCharacter.Style.AppearanceComplete && frontend.SelectedCharacter.Equipment[1].ItemId == 0,
                "Rendering the pending second character changed its authoritative appearance.");
            Submit("enter-world");
            await Wait(() => frontend.Root.Q<Button>("save-character")?.enabledInHierarchy == true && preview.DraftReady, "resume second character creation");
            Capture("appearance-resumed-second");
            long secondUid = frontend.SelectedCharacter.Uid;
            Submit("save-character");
            await WaitForCutscene("second-character-intro");
            await WaitForTutorial();
            Require(frontend.SelectedCharacter.Uid == secondUid && frontend.SelectedCharacter.Style.AppearanceComplete,
                "Saving the second character in the same login session did not enter its tutorial.");
            Capture("tutorial-second-character");
            FindFirstObjectByType<TutorialController>().Cancel();
            await Wait(() => frontend.SelectionReady, "return from resumed creation");
            Require(frontend.Session.Characters.Count(c => c.Style.AppearanceComplete) == 2,
                "The roster lost a completed character after saving the second slot.");
            CaptureSelection("selection-second-saved");
            frontend.SelectRosterSlot(1);
            await Wait(() => frontend.SelectionReady, "first character reselection");
            Submit("enter-world");
            await WaitForCutscene("reentry-intro");
            await WaitForTutorial();
            Require(frontend.SelectedCharacter.Uid == uid, "Reentry selected a different character.");
            Capture("tutorial-reentry");
            if (Environment.GetCommandLineArgs().Contains("--recovery-overworld"))
            {
                // Exercise the ordinary completion callback on this disposable
                // account, including the server handshake and first world frame.
                FindFirstObjectByType<TutorialController>().SendMessage("Finish", SendMessageOptions.RequireReceiver);
                NetworkWorldController world = null;
                await Wait(() => (world = FindFirstObjectByType<NetworkWorldController>()) && world.IsReady && world.MovementEnabled &&
                    audio.CurrentMode == RetrobutionAudioController.Mode.World, "overworld first presentation");
                Require(frontend.Session.IsWorldLoaded && frontend.LoadingPhase == FrontendLoadingPhase.Hidden,
                    "Overworld was presented before its server and loading transition completed.");
                var p = world.Player.position;
                var zone = audio.Catalog.Find(audio.Catalog.Music, new Vector2(p.x, p.z), world.MapNumber != 0);
                if (zone != null) await Wait(() => audio.Bgm.CurrentAddress == zone.Address, "overworld region soundtrack");
                await Frames(3);
                var minimap = frontend.Root.Q("hud-minimap");
                Require(minimap != null && minimap.worldBound.width > 0 &&
                    Mathf.Abs(minimap.worldBound.xMax - frontend.Root.worldBound.xMax) < 2,
                    "The first overworld HUD did not anchor the minimap to the right edge.");
                Capture("overworld-entry");
                var cells = FindObjectsByType<EnvironmentCellBehaviour>(FindObjectsSortMode.None).Where(c => c.Definition).ToArray();
                Require(cells.Length > 0 && cells.All(c => c.Initialized && c.Definition.SourceBindingsVersion == 1),
                    "World entry used an unpublished cell definition.");
                Require(Resources.FindObjectsOfTypeAll<WorldTileContent>().Length == 0,
                    "World entry loaded a legacy cell manifest.");
                int colliderCount = cells.Sum(c => c.GetComponentsInChildren<MeshCollider>(true).Count(m => m.enabled && m.sharedMesh));
                Require(colliderCount > 0, "World entry lost authored scene colliders.");
                Debug.Log("CELL SOURCE BINDINGS PASSED: " + cells.Length + " initialized cells; " + colliderCount + " authored mesh colliders; no legacy manifests.");
                VerifyNativeEnvironment(cells);
                await VerifyHnpcInterest(world.Replicas);
                if (arguments.Contains("--recovery-input-actions")) await VerifyGameplayInput(world);
            }
        }

        async Task VerifyHnpcInterest(NetworkWorldEntities replicas)
        {
            NetworkEntityView[] residents = null;
            await Wait(() => {
                residents = replicas.Actors.Where(v => v.Definition?.HnpcAppearance >= 0 && !v.Entity.Hidden).ToArray();
                return residents.Length > 0 && residents.All(v => v.Avatar && v.Avatar.IsReady);
            }, "overworld HNPC appearances");
            Require(residents.All(v => v.Avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.enabled && r.sharedMesh)),
                "A resident HNPC was published without a skinned mesh.");

            // Use an independent interest set so a duplicate ID and a leave during preparation
            // exercise the normal packet handler without changing the live shard's actors.
            var root = new GameObject("HNPC interest regression");
            try
            {
                var probe = root.AddComponent<NetworkWorldEntities>();
                probe.Begin(1, null);
                var data = new byte[36];
                Protocol0104.Write32(data, 0, 77);
                Protocol0104.Write32(data, 4, residents[0].Entity.NpcType);
                Protocol0104.Write32(data, 8, 100);
                var enter = new ProtocolFrame(ProtocolWorld.NpcEnter, data);
                probe.Receive(enter);
                var replaced = probe.Actors.Single();
                probe.Receive(enter);
                var departing = probe.Actors.Single();
                var leave = new byte[4]; Protocol0104.Write32(leave, 0, 77);
                probe.Receive(new ProtocolFrame(ProtocolWorld.NpcLeave, leave));
                probe.Receive(enter);
                var current = probe.Actors.Single();
                Require(!replaced.Avatar.IsReady && !departing.Avatar.IsReady && !current.Avatar.IsReady,
                    "A resident HNPC assembled inline while dispatching interest packets.");
                await Wait(() => current.Avatar.IsReady && !replaced && !departing, "HNPC replacement after interest exit");
                Require(probe.VisibleCount == 1 && root.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.transform.IsChildOf(current.transform)) &&
                    probe.ContentFailures.Count == 0, "Stale HNPC appearance after replacement or interest exit.");
            }
            finally { Destroy(root); }
            Debug.Log("FRONTEND RECOVERY HNPC PASSED: " + residents.Length + " live appearances; duplicate enter and leave/reenter");
        }
        async Task WaitForCutscene(string name)
        {
            await Wait(() => FindFirstObjectByType<DexterShipCutscene>()?.IsPlaying == true, name);
            var scene = FindFirstObjectByType<DexterShipCutscene>();
            Capture(name + "-first-frame");
            if (Environment.GetCommandLineArgs().Contains("--recovery-quick-roster"))
            {
                scene.Skip();
                await Wait(() => !scene.IsPlaying, name + " skip");
                return;
            }
            await Wait(() => scene.Elapsed >= 6, name + " rendered performance");
            AssertSubtitles();
            Capture(name);
            // Observe starts with an authored lean towards the camera, then
            // returns at clip time 3.4. Keep that close-up evidence, and validate
            // the later silhouette separately from the ship background/UI.
            Debug.Log("FRONTEND RECOVERY: " + name + " raw " + scene.CaptureSceneRender(Path.Combine(output, name + "-scene.png"), name != "before-creation"));
            if (name == "before-creation")
            {
                // GPU readback can stall the unscaled scene clock while the
                // Animator advances by the clamped frame delta. Validate the
                // returned pose after the actual observe clip has reached it.
                await Wait(() => scene.GetComponentsInChildren<NativeNpcAnimator>(true).Any(animation =>
                    animation.CurrentClip == "observe" && animation.MotionTime >= 4),
                    name + " returned from close-up");
                Capture(name + "-returned");
                Debug.Log("FRONTEND RECOVERY: " + name + " returned raw " + scene.CaptureSceneRender(Path.Combine(output, name + "-returned-scene.png")));
            }
            // Validate natural transitions for both new-character scenes and the
            // skip path on reentry through the ordinary character selection.
            if (name == "reentry-intro") scene.Skip();
            await Wait(() => !scene.IsPlaying, name + " completion");
        }
        void AssertSubtitles()
        {
            var area = frontend.Root.Q("dexter-ship-subtitle-area");
            var speaker = area?.Q<Label>("dexter-ship-subtitle-speaker");
            var dialogue = area?.Q<Label>("dexter-ship-subtitle-dialogue");
            Require(!string.IsNullOrWhiteSpace(speaker?.text) && !string.IsNullOrWhiteSpace(dialogue?.text), "Cutscene speech has no localized subtitles.");
            var screen = frontend.Root.worldBound;
            var bounds = dialogue.worldBound;
            Require(area.worldBound.width > screen.width * .5f && bounds.width > screen.width * .4f &&
                bounds.xMin >= screen.xMin && bounds.xMax <= screen.xMax && bounds.yMin >= screen.yMin && bounds.yMax <= screen.yMax,
                "Cutscene subtitle layout is clipped or outside the viewport: " + bounds + "; area=" + area.worldBound);
            Debug.Log($"FRONTEND RECOVERY SUBTITLES: {screen.width}x{screen.height}; {speaker.text}{dialogue.text}; bounds={bounds}");
        }
        async Task WaitForTutorial() => await Wait(() => {
            var tutorial = FindFirstObjectByType<TutorialController>();
            return tutorial && tutorial.IsReady && tutorial.enabled && tutorial.GameplayHudPortrait?.panel != null;
        }, "authored tutorial scene and HUD");
        void Submit(string name)
        {
            var button = frontend.Root.Q<Button>(name) ?? throw new InvalidOperationException("Missing UI button: " + name);
            Require(button.enabledInHierarchy, "Disabled UI button: " + name);
            button.Focus();
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
        async Task Wait(Func<bool> condition, string step)
        {
            this.step = step;
            float started = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - started > 180)
                    throw new TimeoutException("Frontend recovery: " + step + "; " + frontend?.Root?.Q<Label>("connection-status")?.text);
                if (!this) throw new OperationCanceledException();
                await Task.Yield();
            }
            Debug.Log($"FRONTEND RECOVERY: {step}; waited={Time.realtimeSinceStartup - started:F3}s");
        }
        static async Task Frames(int count) { for (int n = 0; n < count; n++) await Task.Yield(); }
        void CaptureSelection(string name)
        {
            Require(frontend.SelectionReady && preview.SelectionReady, "Selection was captured before its avatars rendered.");
            Capture(name);
            FrontendAvatarSmokeEvidence.AssertSelection(frontend, output, name, "FRONTEND RECOVERY");
        }
        void CaptureLoadingEvidence()
        {
            if (!frontend || output == null || !uiCapture && !nativeScreenCapture) return;
            var loading = frontend.Root?.Q("world-loading");
            var background = loading?.Q<Image>("loading-background");
            var window = loading?.Q("loading-window");
            bool shown = loading?.resolvedStyle.display == DisplayStyle.Flex && background?.image != null;
            bool resources = shown && window?.resolvedStyle.display == DisplayStyle.Flex &&
                (window.resolvedStyle.backgroundImage.texture || window.resolvedStyle.backgroundImage.sprite);
            bool backgroundOnly = shown && window?.resolvedStyle.display == DisplayStyle.None;
            loadingResourceFrames = resources ? loadingResourceFrames + 1 : 0;
            loadingBackgroundFrames = backgroundOnly ? loadingBackgroundFrames + 1 : 0;
            // Observe a rendered frame, not merely the hierarchy mutation that opened it.
            if (!capturedLoadingResources && loadingResourceFrames >= 2)
            { capturedLoadingResources = true; Capture("loading-resources"); }
            if (!capturedLoadingBackground && loadingBackgroundFrames >= 2)
            { capturedLoadingBackground = true; Capture("loading-background-only"); }
        }
        void Capture(string name)
        {
            string previousStep = step; step = "capture " + name;
            float started = Time.realtimeSinceStartup;
            if (Application.isEditor) { ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png")); step = previousStep; return; }
            if (nativeScreenCapture)
            {
                // Capture the ordinary swapchain output when requested.
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                try { AssertShipComposite(name, screen); File.WriteAllBytes(Path.Combine(output, name + ".png"), screen.EncodeToPNG()); }
                finally { Destroy(screen); step = previousStep; Debug.Log($"FRONTEND RECOVERY CAPTURE: {name}; {Time.realtimeSinceStartup - started:F3}s"); }
                return;
            }
            var image = new Texture2D(uiCapture.width, uiCapture.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = uiCapture;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                var tutorial = FindFirstObjectByType<TutorialController>();
                var worldController = FindFirstObjectByType<NetworkWorldController>();
                if (tutorial && tutorial.IsReady || worldController && worldController.IsReady)
                {
                    var ui = image.GetPixels();
                    RenderTexture.active = worldCapture;
                    image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                    var world = image.GetPixels();
                    for (int n = 0; n < world.Length; n++)
                    {
                        // UI Toolkit's render target contains premultiplied color.
                        world[n] = ui[n] + world[n] * (1 - ui[n].a); world[n].a = 1;
                    }
                    image.SetPixels(world); image.Apply();
                }
                AssertShipComposite(name, image);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(image); Debug.Log($"FRONTEND RECOVERY CAPTURE: {name}; {Time.realtimeSinceStartup - started:F3}s"); step = previousStep; }
        }
        void OnDestroy()
        {
            if (capturePanel) Destroy(capturePanel);
            if (uiCapture) { uiCapture.Release(); Destroy(uiCapture); }
            if (worldCapture) { worldCapture.Release(); Destroy(worldCapture); }
        }
        static void AssertShipComposite(string name, Texture2D image)
        {
            if (name != "before-tutorial" && name != "before-creation-returned") return;
            // Validate the composited UI, not just the offscreen actor camera:
            // an opaque fade can hide the ship while voices and subtitles pass.
            var pixels = image.GetPixels32(); int visible = 0, samples = 0;
            for (int y = image.height / 4; y < image.height * 3 / 4; y += 8)
                for (int x = image.width / 10; x < image.width * 3 / 10; x += 8)
                {
                    var color = pixels[y * image.width + x]; samples++;
                    if (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) > 40) visible++;
                }
            Require(visible > samples / 4, "The actual ship UI composite is black behind its subtitles: " + name);
            Debug.Log($"FRONTEND SHIP COMPOSITE PASSED: {name}; visible={visible}/{samples}.");
        }
        static void Require(bool value, string failure) { if (!value) throw new InvalidOperationException(failure); }
    }
}
#endif
