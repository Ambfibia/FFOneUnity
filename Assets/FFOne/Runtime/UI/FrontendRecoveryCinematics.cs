#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Tutorial;
using FFOne.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Playables;

namespace FFOne.UI
{
    public sealed partial class FrontendRecoverySmoke
    {
        async Task VerifyGameplayInput(NetworkWorldController world)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                var options = SettingsPreferences.Defaults();
                var forward = options["input"]["bindings"]["Up"];
                forward["primary"] = "<Keyboard>/t";
                forward["alternate"] = "";
                forward["pad"] = "<Gamepad>/dpad/up";
                GameplayInput.Apply(options);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp));
                await Frames(3);
                Require(world.MoveDirection == 0, "A direction held while the gamepad connected moved the avatar.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                await Frames(4);
                Require(world.MoveDirection == 0, "The original forward key still moved the avatar after rebinding.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
                await Frames(4);
                Require(world.MoveDirection == WorldCoordinates.Direction(0, 1), "The rebound key did not move the avatar forward.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await Frames(3);
                Require(world.MoveDirection == 0, "The forward action remained held after release.");

                InputSystem.QueueStateEvent(pad, new GamepadState());
                await Frames(3);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp));
                await Frames(4);
                Require(world.MoveDirection == WorldCoordinates.Direction(0, 1), "The rebound gamepad direction did not move the avatar.");
                InputSystem.RemoveDevice(pad);
                await Frames(3);
                Require(world.MoveDirection == 0, "Disconnecting a held gamepad left the avatar moving.");
                InputSystem.AddDevice(pad);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp));
                await Frames(3);
                Require(world.MoveDirection == 0, "The reconnected gamepad resumed a stale forward action.");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                await Frames(3);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadUp));
                await Frames(4);
                Require(world.MoveDirection == WorldCoordinates.Direction(0, 1), "The reconnected gamepad did not accept a fresh forward action.");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                await Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Home));
                await Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await Frames(3);
                Require(world.MoveDirection == WorldCoordinates.Direction(0, 1), "Auto-run did not engage.");
                world.SetPlayerDead(true);
                try
                {
                    await Frames(4);
                    Require(world.MoveDirection == 0, "The incapacitated avatar kept auto-running.");
                }
                finally { world.SetPlayerDead(false); }
                await Frames(4);
                Require(world.MoveDirection == 0, "Revival restored the cancelled auto-run.");
                Debug.Log("NATIVE GAMEPLAY INPUT PASSED: rebound keyboard, gamepad hotplug and auto-run cancellation on death.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                if (pad.added) InputSystem.RemoveDevice(pad);
                GameplayInput.Apply(world.Preferences);
            }
        }

        async Task ValidateCinematics(TutorialController tutorial)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(TutorialController);
            var playback = (TutorialCinematics)type.GetField("cinematics", flags).GetValue(tutorial);
            float previousScale = Time.timeScale;
            bool audible = Environment.GetCommandLineArgs().Contains("--recovery-audio");
            TutorialAudioProbe probe = audible ? tutorial.ViewCamera.gameObject.AddComponent<TutorialAudioProbe>() : null;
            var backgrounds = new[] { "voice", "music", "ambient" }.Select(name => (AudioSource)type.GetField(name, flags).GetValue(tutorial)).ToArray();
            var previousMute = backgrounds.Select(source => source.mute).ToArray();
            // Isolate actor/scene SFX so background music cannot satisfy the
            // listener's non-silence check when animation audio is missing.
            if (audible) foreach (var source in backgrounds) source.mute = true;
            // HUD attachment precedes the first Update that advances tutorial stage 0/0.
            // Let the opening timeline own a frame before freezing tutorial input.
            await Wait(() => tutorial.IsCinematic, "opening tutorial timeline");
            tutorial.ExternalInputBlocked = true;
            try
            {
                Require(frontend.Root.Q("tutorial-overlay")?.pickingMode == PickingMode.Ignore,
                    "Tutorial presentation must leave gameplay and HUD pointer targets reachable.");
                await Frames(2);
                Require(frontend.Root.Q<Button>("tutorial-cinematic-skip")?.worldBound.width > 0,
                    "The native skip button has no layout.");
                tutorial.ExternalInputBlocked = false;
                Submit("tutorial-cinematic-skip");
                tutorial.ExternalInputBlocked = true;
                Require(playback.Finished, "The native skip button did not stop the scene.");
                Require(Mathf.Abs(tutorial.ViewCamera.fieldOfView - 45) < .01f && Mathf.Abs(tutorial.ViewCamera.nearClipPlane - .15f) < .001f,
                    "Tutorial camera does not match the source projection.");
                Require(tutorial.ViewCamera.projectionMatrix.m22 < -1.00001f,
                    "The tutorial retained the overworld's infinite raster projection.");
                var skyline = tutorial.Actor(2);
                Require(skyline && skyline.GetComponent<TutorialActor>().IsScenery &&
                    Vector3.Distance(skyline.transform.position, new Vector3(973, -76, 702)) < .01f,
                    "The persistent tutorial skyline is absent from the first scene.");
                Require(tutorial.WorldStream.NeighborWaterVisuals > 0, "The actual tutorial did not admit surrounding infected water.");
                Require(Mathf.Abs(tutorial.ViewCamera.farClipPlane - 340) < .01f, "Tutorial camera lost the accepted native horizon.");
                Vector4 renderedFog = Vector4.zero;
                void ObserveFog(ScriptableRenderContext context, Camera view)
                { if (view == tutorial.ViewCamera) renderedFog = Shader.GetGlobalVector("_FFWorldFog"); }
                RenderPipelineManager.beginCameraRendering += ObserveFog;
                try { await Frames(3); }
                finally { RenderPipelineManager.beginCameraRendering -= ObserveFog; }
                Require(renderedFog.w >= Mathf.Sqrt(-Mathf.Log(.02f)) / tutorial.ViewCamera.farClipPlane - .00001f,
                    "The actual tutorial world render has no distance fog.");
                Debug.Log($"TUTORIAL ENVIRONMENT PASSED: neighbor water={tutorial.WorldStream.NeighborWaterVisuals}; rendered fog={renderedFog}; far={tutorial.ViewCamera.farClipPlane}.");
                foreach (var point in new[] { new Vector3(907, 14, 685), new Vector3(908, 14, 682) })
                    Require(Physics.RaycastAll(point, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore)
                        .Any(hit => tutorial.WorldStream.Owns(hit.collider) && hit.normal.y > .9f && hit.point.y > 11),
                        "The actual tutorial bridge has no upward support beneath its NPC spawn: " + point);
                Debug.Log("TUTORIAL BRIDGE SUPPORT PASSED: both authored NPC spawn points.");
                Time.timeScale = 3;
                foreach (var scene in TutorialStages.All.Where(s => s.Scene != TutorialScene.None).OrderBy(s => s.Scene))
                {
                    // Match the ordinary progression's persistent actors at the
                    // entry to each finite scene. No server progression is mutated.
                    if (scene.Scene == TutorialScene.InfectionA)
                    {
                        tutorial.WarpPlayer(new Vector3(596, -82, 784));
                        type.GetMethod("UpdateCamera", flags).Invoke(tutorial, null);
                    }
                    if (scene.Scene == TutorialScene.InfectionB)
                    {
                        // This conversation follows walking up to Dexter and
                        // accepting his mission, rather than the initial warp.
                        tutorial.WarpPlayer(new Vector3(590, -133, 984));
                        type.GetMethod("UpdateCamera", flags).Invoke(tutorial, null);
                        tutorial.SpawnActor(3000, 2673, new Vector3(587, -133, 984), 262);
                        // Ordinary play has already grounded Dexter while the
                        // player walks to him and accepts the mission. Camera
                        // expressions capture that settled position once.
                        Physics.SyncTransforms();
                        for (int i = 0; i < 120; i++)
                            type.GetMethod("GroundActors", flags).Invoke(tutorial, new object[] { 1f / 60 });
                    }
                    if (scene.Scene == TutorialScene.InfectionC)
                    {
                        tutorial.SpawnActor(3000, 2902, new Vector3(562, -133, 967));
                        tutorial.SpawnActor(4000, 2678, new Vector3(563, -133, 967), 266);
                    }
                    if (scene.Scene == TutorialScene.NanoPowerA)
                    {
                        // Exercise the ordinary warp/next-chapter path rather
                        // than hiding a stale camera by preparing the shot here.
                        type.GetMethod("SetStage", flags).Invoke(tutorial, new object[] { 4, 16, false });
                        type.GetField("warpTarget", flags).SetValue(tutorial, new Vector3(907.65f, 10.43f, 715.58f));
                        type.GetField("pendingWarp", flags).SetValue(tutorial, 1.5f);
                        type.GetMethod("Evaluate", flags).Invoke(tutorial, null);
                        Require((float)type.GetField("fade", flags).GetValue(tutorial) == 1 &&
                            Vector3.Distance(tutorial.ViewCamera.transform.position, tutorial.Player.position) < 12,
                            "Cave exit revealed an old camera before the bridge scene.");
                        await Frames(2); Capture("cinematic-bridge-warp-first-frame");
                        type.GetMethod("Evaluate", flags).Invoke(tutorial, null);
                    }
                    else type.GetMethod("SetStage", flags).Invoke(tutorial, new object[] { scene.Chapter, scene.Step, false });
                    Require(playback.Director && playback.Director.playableGraph.IsValid(), "The scene has no live PlayableDirector graph.");
                    Require(playback.Director.playableGraph.GetRootPlayable(0).GetPlayableType() == typeof(TutorialSceneBehaviour),
                        "The scene bypassed its native Timeline mixer.");
                    var cuts = playback.TimelineClips.Where(c => (string)c.Action["type"] == "Camera" &&
                            ((string)c.Action["args"][0]["type"] == "CurrentTarget" || (string)c.Action["args"][0]["type"] == "Start"))
                        .Select(c => (float)c.Start + .25f).Distinct().ToArray();
                    int capture = 0, shot = 0, frame = 0;
                    bool locationCaptured = false, voiceCaptionCaptured = false, panCaptured = false;
                    bool voiceExpected = playback.TimelineClips.Any(c => (string)c.Action["type"] == "AudioCue" && (int)c.Action["kind"] == 0);
                    while (!playback.Finished)
                    {
                        await Task.Yield();
                        Require(string.IsNullOrEmpty(tutorial.LastError), tutorial.LastError);
                        playback.Tick(Time.deltaTime);
                        tutorial.PanPlayer.Tick(Time.deltaTime);
                        type.GetMethod("GroundActors", flags).Invoke(tutorial, new object[] { Mathf.Min(Time.deltaTime, .1f) });
                        Require(Finite(tutorial.ViewCamera.transform.position) && Finite(tutorial.Player.position), "Nonfinite cinematic transform: " + scene.Scene);
                        foreach (var actor in FindObjectsByType<TutorialActor>(FindObjectsSortMode.None))
                            Require(Finite(actor.transform.position) && actor.transform.position.y > -300, "Actor fell out of tutorial: " + actor.name);
                        if (scene.Scene == TutorialScene.BasicMove && !locationCaptured && playback.Elapsed >= 3.4f)
                        {
                            Require((bool)type.GetField("locationCaption", flags).GetValue(tutorial),
                                "The Tech Square title was routed to ordinary dialogue subtitles.");
                            await Frames(2);
                            Require(!string.IsNullOrEmpty(frontend.Root.Q<Label>(className: "tutorial-location")?.text),
                                "The native location label has no text.");
                            Capture("cinematic-location-caption"); locationCaptured = true;
                        }
                        if (!voiceCaptionCaptured && tutorial.VoiceSubtitleVisible)
                        {
                            string caption = tutorial.VoiceSubtitleText;
                            Require(caption.Contains(":"), "Voice subtitle has no speaker: " + scene.Scene);
                            Require(!caption.StartsWith("content."), "Voice subtitle is an unresolved localization key.");
                            if (tutorial.Russian) Require(caption.Any(c => c >= 'А' && c <= 'я'), "Russian voice subtitle is untranslated.");
                            await Frames(2);
                            var voice = frontend.Root.Q(className: "tutorial-voice");
                            var dialogue = voice?.Q<Label>(className: "tutorial-dialogue-body");
                            Require(dialogue != null && dialogue.parent.resolvedStyle.display == DisplayStyle.Flex &&
                                dialogue.worldBound.width > 0 && !string.IsNullOrEmpty(dialogue.text),
                                "Voice subtitle is absent from the native panel: " + scene.Scene);
                            Capture("cinematic-" + scene.Scene + "-voice-subtitle");
                            voiceCaptionCaptured = true;
                        }
                        if (!panCaptured && tutorial.PanPlayer.Active && tutorial.PanPlayer.ElapsedSeconds >= 1)
                        {
                            await Frames(2);
                            var pan = frontend.Root.Q("tutorial-pan");
                            Require(pan != null && pan.resolvedStyle.display == DisplayStyle.Flex &&
                                pan.style.overflow.value == Overflow.Hidden &&
                                pan.Children().OfType<Image>().Count(image => image.image) == TutorialPanPlayer.FrameCount,
                                "The Planet Fusion pan did not present its clipped native image strip.");
                            Capture("cinematic-planet-pan");
                            panCaptured = true;
                        }
                        if (scene.Scene == TutorialScene.NanoPowerA && playback.Elapsed < 2)
                            foreach (int id in new[] { 5100, 5101 })
                                Require(tutorial.Actor(id).transform.position.y >= 10.8f, "NPC fell through the tutorial bridge: " + id);
                        if (scene.Scene == TutorialScene.InfectionB)
                        {
                            var target = (Vector3)typeof(TutorialCinematics).GetField("cameraCurrentTarget", flags).GetValue(playback);
                            var eye = tutorial.ViewCamera.transform.position;
                            var offset = eye - target;
                            Require(!Physics.RaycastAll(target, offset.normalized, offset.magnitude, ~0, QueryTriggerInteraction.Ignore)
                                .Any(hit => tutorial.WorldStream.Owns(hit.collider)), "InfectionB camera is occluded by the authored world.");
                            if (playback.Elapsed > 2 && playback.Elapsed < 15)
                            {
                                var renderers = tutorial.Actor(3000).GetComponentsInChildren<Renderer>();
                                var bounds = renderers[0].bounds;
                                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                                var center = tutorial.ViewCamera.WorldToViewportPoint(bounds.center);
                                Require(center.z > 0 && center.y > .2f && center.y < .8f,
                                    "The settled Dexter conversation is cropped out of its opening shot: " + center);
                            }
                        }
                        bool periodic = playback.Elapsed >= capture * 5 && capture < 10;
                        bool cut = shot < cuts.Length && playback.Elapsed >= cuts[shot];
                        if (periodic || cut)
                        {
                            await Frames(2);
                            Debug.Log($"TUTORIAL SHOT: {scene.Scene}; t={playback.Elapsed:F3}; camera={tutorial.ViewCamera.transform.position}; " +
                                string.Join("; ", FindObjectsByType<TutorialActor>(FindObjectsSortMode.None)
                                    .Select(actor => actor.RuntimeId + "=" + actor.transform.position)));
                            if (periodic) Capture("cinematic-" + scene.Scene + "-" + capture++);
                            if (cut) Capture("cinematic-" + scene.Scene + "-shot-" + shot++);
                        }
                        if (++frame > 20000) throw new TimeoutException("Cinematic did not finish: " + scene.Scene);
                    }
                    Require(!voiceExpected || voiceCaptionCaptured, "No visible voice subtitles in cinematic: " + scene.Scene);
                    if (scene.Scene == TutorialScene.BasicCombatC) Require(panCaptured, "The Planet Fusion pan never appeared.");
                    if (voiceExpected) Debug.Log("TUTORIAL VOICE SUBTITLES PASSED: " + scene.Scene);
                    Debug.Log("TUTORIAL CINEMATIC PASSED: " + scene.Scene + "; duration=" + playback.Duration);
                }
                await ValidateTutorialHud(tutorial);
                var effects = (TutorialCinematicEffects)type.GetField("effects", flags).GetValue(tutorial);
                Require(tutorial.AnimationSoundsPlayed > 0 && effects.RootAnimationEffectsPlayed > 0,
                    "The actual client did not dispatch animation sounds and root effects.");
                Require(effects.NativeProjectileTrailsSpawned > 0, "Tutorial combat did not create native wound projectile trails.");
                Debug.Log($"NATIVE WOUND TRAIL GAMEPLAY PASSED: {effects.NativeProjectileTrailsSpawned} source projectiles.");
                if (audible)
                {
                    Require(probe.Samples > 0 && probe.Peak > .0001f, "The actual listener produced no audible cinematic SFX.");
                    Debug.Log($"TUTORIAL AUDIBLE SFX PASSED: events={tutorial.AnimationSoundsPlayed}; rootEffects={effects.RootAnimationEffectsPlayed}; samples={probe.Samples}; peak={probe.Peak}.");
                }
                await ValidateTutorialMotion(tutorial);
                foreach (var actor in FindObjectsByType<TutorialActor>(FindObjectsSortMode.None)) tutorial.RemoveActor(actor.RuntimeId);
                tutorial.WarpPlayer(TutorialController.StartPosition);
                tutorial.FacePlayer(Quaternion.Euler(0, 90, 0));
                foreach (var renderer in tutorial.Player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                type.GetField("nanoActive", flags).SetValue(tutorial, false);
                if (tutorial.NanoTransform) tutorial.NanoTransform.gameObject.SetActive(false);
                type.GetField("hiddenHud", flags).SetValue(tutorial, 0);
                type.GetField("eventScene", flags).SetValue(tutorial, false);
                type.GetMethod("SetStage", flags).Invoke(tutorial, new object[] { 0, 1, false });
                Debug.Log("TUTORIAL CINEMATIC SMOKE PASSED: all ten natural scenes.");
            }
            finally
            {
                for (int i = 0; i < backgrounds.Length; i++) if (backgrounds[i]) backgrounds[i].mute = previousMute[i];
                if (probe) Destroy(probe);
                tutorial.ExternalInputBlocked = false; Time.timeScale = previousScale;
            }
        }

        async Task ValidateTutorialHud(TutorialController tutorial)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(TutorialController);
            tutorial.SetFade(0); tutorial.SetBars(0); tutorial.Subtitle("");
            type.GetField("hiddenHud", flags).SetValue(tutorial, 0);
            type.GetField("eventScene", flags).SetValue(tutorial, false);
            type.GetField("picture", flags).SetValue(tutorial, null);
            foreach (var renderer in tutorial.Player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            var paused = type.GetField("paused", flags);
            paused.SetValue(tutorial, true);
            await Frames(2);
            Require(frontend.Root.Q("tutorial-pause")?.resolvedStyle.display == DisplayStyle.Flex &&
                frontend.Root.Q("tutorial-presentation")?.resolvedStyle.display == DisplayStyle.None,
                "Pause did not replace the native tutorial presentation.");
            Capture("tutorial-pause");
            tutorial.ExternalInputBlocked = false;
            Submit("tutorial-resume");
            tutorial.ExternalInputBlocked = true;
            Require(!(bool)paused.GetValue(tutorial), "The native pause button did not resume the tutorial.");
            tutorial.WarpPlayer(new Vector3(566, -100, 665)); tutorial.FacePlayer(Quaternion.identity);
            type.GetMethod("UpdateCamera", flags).Invoke(tutorial, null);
            void Stage(int chapter, int step) => type.GetMethod("SetStage", flags).Invoke(tutorial, new object[] { chapter, step, false });
            Stage(0, 3);
            await Frames(3);
            var hintArrow = frontend.Root.Q<Image>(className: "tutorial-hint-arrow");
            Require(hintArrow?.image && hintArrow.resolvedStyle.display == DisplayStyle.Flex, "The camera lesson must show its arrow.");
            Capture("tutorial-look-arrow");
            foreach (var lesson in new[] { (0, 7), (4, 5), (4, 9), (4, 15) })
            {
                Stage(lesson.Item1, lesson.Item2);
                if (lesson == (4, 9)) type.GetMethod("TickAuxiliary", flags).Invoke(tutorial, new object[] { 5.1f });
                await Frames(3);
                var instruction = frontend.Root.Q<Label>(className: "tutorial-instruction");
                Require(!string.IsNullOrWhiteSpace(instruction.text) && instruction.text.Any(char.IsLetter)
                    && instruction.text == instruction.text.ToUpperInvariant(), "The central instruction lost its source capitalization at " + lesson);
                Capture("tutorial-instruction-" + lesson.Item1 + "-" + lesson.Item2);
            }
            Stage(0, 1);
            Require(tutorial.IsCinematic, "The gamepad skip check must start during a running cinematic.");
            var cutscenePad = InputSystem.AddDevice<Gamepad>();
            try
            {
                InputSystem.QueueStateEvent(cutscenePad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                tutorial.ExternalInputBlocked = false;
                type.GetMethod("Update", flags).Invoke(tutorial, null);
                Require(!tutorial.IsCinematic, "Gamepad A did not skip the active tutorial cinematic.");
            }
            finally { tutorial.ExternalInputBlocked = true; InputSystem.RemoveDevice(cutscenePad); }
            void Settle()
            {
                Physics.SyncTransforms();
                for (int i = 0; i < 120; i++) type.GetMethod("GroundActors", flags).Invoke(tutorial, new object[] { 1f / 60 });
            }

            var hostile = tutorial.SpawnActor(7000, 2674, tutorial.Player.position + Vector3.forward * 4);
            Settle();
            var targetActor = hostile.GetComponent<TutorialActor>();
            Stage(1, 2); type.GetField("target", flags).SetValue(tutorial, targetActor);
            type.GetMethod("Attack", flags).Invoke(tutorial, new object[] { targetActor });
            var hostileAnimator = hostile.GetComponentInChildren<NativeNpcAnimator>(true);
            Require(hostileAnimator && hostileAnimator.CurrentMotion && !hostileAnimator.CurrentMotion.isLooping &&
                hostileAnimator.CurrentClip == (targetActor.Hp == 0 ? "death" : "wound"),
                "The actual tutorial attack did not select the native wound or death state.");
            await Frames(4);
            Require(frontend.Root.Q("tutorial-gameplay-ui").enabledInHierarchy,
                "Input-only blocking applied the disabled tint to the rendered tutorial HUD.");
            var tutorialHud = frontend.Root.Q("tutorial-gameplay-ui");
            await Wait(() => tutorialHud.Q<Image>("tutorial-minimap-tile-0")?.image, "shared tutorial minimap tile");
            Require(tutorialHud.Q("tutorial-minimap")?.resolvedStyle.display == DisplayStyle.Flex &&
                tutorialHud.Q("nano-hud-root")?.resolvedStyle.display == DisplayStyle.Flex &&
                tutorialHud.Q("nano-slot-0")?.resolvedStyle.display == DisplayStyle.None,
                "The ordinary minimap and empty Nano belt must render during the early combat lesson.");
            Capture("hud-combat-target");
            Require(frontend.Root.Q("tutorial-target-info")?.resolvedStyle.display == DisplayStyle.Flex &&
                frontend.Root.Q<Image>("tutorial-target-portrait")?.image, "The actual hostile target HUD did not render its frame and portrait.");
            Require(frontend.Root.Q("tutorial-combat-frame")?.resolvedStyle.display == DisplayStyle.Flex,
                "An actual tutorial attack did not activate the source combat frame.");

            tutorial.RemoveActor(7000);
            var friendly = tutorial.SpawnActor(7001, 2671, tutorial.Player.position + Vector3.forward * 3);
            Settle();
            Stage(3, 1); type.GetField("target", flags).SetValue(tutorial, friendly.GetComponent<TutorialActor>());
            type.GetField("currentTask", flags).SetValue(tutorial, 2248);
            await Frames(4); Capture("hud-friendly-target-objective");
            Require(frontend.Root.Q("tutorial-current-objective")?.resolvedStyle.display == DisplayStyle.Flex,
                "The current tutorial mission did not render beneath NanoCom.");

            Stage(5, 4); type.GetField("target", flags).SetValue(tutorial, null);
            type.GetField("nanoActive", flags).SetValue(tutorial, true);
            tutorial.NanoTransform.position = tutorial.Player.position + Vector3.right * 1.3f + Vector3.up * 1.6f;
            tutorial.NanoTransform.gameObject.SetActive(true);
            await Frames(4); Capture("hud-active-nano");
            Require(frontend.Root.Q("tutorial-active-nano-info")?.resolvedStyle.display == DisplayStyle.Flex,
                "The active Nano info did not render.");

            Stage(3, 11); type.GetField("journal", flags).SetValue(tutorial, true);
            await Frames(4); Capture("hud-journal");
            Require(type.GetField("tutorialWindowPanel", flags).GetValue(tutorial) is VisualElement panel && panel.parent != null,
                "The actual tutorial journal window did not render.");
            type.GetField("journal", flags).SetValue(tutorial, false);
            type.GetField("target", flags).SetValue(tutorial, null);
            Debug.Log("TUTORIAL HUD SMOKE PASSED: early minimap and Nano belt, hostile, friendly, objective, active Nano and journal.");
            Debug.Log("TUTORIAL NATIVE OVERLAY PASSED: skip, subtitles, location, pan and pause.");
        }
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        async Task ValidateTutorialMotion(TutorialController tutorial)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(TutorialController);
            // Diagnostic events must survive window focus changes and cannot share
            // state with OS devices. The production input handler and motor still consume them.
            InputSystem.RegisterLayout("{\"name\":\"RecoveryKeyboard\",\"extend\":\"Keyboard\",\"canRunInBackground\":true}");
            InputSystem.RegisterLayout("{\"name\":\"RecoveryMouse\",\"extend\":\"Mouse\",\"canRunInBackground\":true}");
            var keyboard = (Keyboard)InputSystem.AddDevice("RecoveryKeyboard");
            var mouse = (Mouse)InputSystem.AddDevice("RecoveryMouse");
            type.GetMethod("SetStage", flags).Invoke(tutorial, new object[] { 0, 11, false });
            tutorial.WarpPlayer(TutorialController.StartPosition); tutorial.FacePlayer(Quaternion.Euler(0, 90, 0));
            var motor = tutorial.Player.GetComponent<CharacterController>();
            bool Grounded() => (bool)type.GetMethod("IsPlayerGrounded", flags).Invoke(tutorial, null);
            Time.timeScale = 1;
            try
            {
                // Exercise the production look handler with neither mouse
                // button held, after the introductory look-only steps. The
                // validation bypasses OS focus, as the keyboard drive below
                // does, while retaining real InputSystem deltas and movement.
                keyboard.MakeCurrent(); mouse.MakeCurrent();
                InputState.Change(mouse.delta, new Vector2(100, 0));
                float beforeYaw = (float)type.GetField("yaw", flags).GetValue(tutorial);
                type.GetField("readCameraMotion", flags).SetValue(tutorial, true);
                type.GetMethod("HandleInput", flags).Invoke(tutorial, new object[] { 1f / 60 });
                float afterYaw = (float)type.GetField("yaw", flags).GetValue(tutorial);
                Require(Mathf.Abs(Mathf.DeltaAngle(beforeYaw, afterYaw)) > 30,
                    "Mouse steering without a held button failed after the look tutorial.");
                type.GetField("readCameraMotion", flags).SetValue(tutorial, false);
                InputState.Change(mouse.delta, Vector2.zero);
                tutorial.FacePlayer(Quaternion.Euler(0, 90, 0));
                await Drive(60);
                Require(Grounded(), "The actual tutorial player could not settle on the authored floor.");
                var start = tutorial.Player.position;
                await Drive(30, Key.W); Capture("motion-run");
                Require(Vector3.Distance(start, tutorial.Player.position) > 1,
                    $"Forward input did not move the actual tutorial player. enabled={keyboard.enabled}; W={keyboard.wKey.isPressed}; " +
                    $"forwardAllowed={tutorial.Stage.Allows(0)}; from={start}; to={tutorial.Player.position}; " +
                    $"menu={type.GetField("mainMenu", flags).GetValue(tutorial)}; journal={type.GetField("journal", flags).GetValue(tutorial)}; " +
                    $"interacting={type.GetField("interacting", flags).GetValue(tutorial)}; eventScene={type.GetField("eventScene", flags).GetValue(tutorial)}; HUD={tutorial.HudConsumedInput}");
                await Drive(20, Key.S); Capture("motion-backward");
                await Drive(30, Key.W, Key.D, Key.RightArrow); Capture("motion-turn-strafe");
                Require(Mathf.Abs(Mathf.DeltaAngle(90, tutorial.Player.eulerAngles.y)) > 30,
                    "Turning input did not turn the actual tutorial player.");
                // Return to the known flat start floor for the jump check;
                // the steering sample can leave it on a sloped ledge.
                tutorial.WarpPlayer(TutorialController.StartPosition);
                await Drive(60);
                Require(Grounded(), "The jump check did not begin on the authored floor.");
                // Measure the peak: a released jump can already have landed by
                // a fixed screenshot frame now that release correctly cuts ascent.
                float tap = await JumpPeak(3);
                float held = await JumpPeak(60);
                Require(tap > .15f && held > 2, $"Jump input did not lift the actual tutorial player: tap={tap:F3}, held={held:F3}.");
                Require(held > tap + 1, $"Space hold did not change actual jump height: tap={tap:F3}, held={held:F3}.");
                Require(Grounded() && Vector3.Angle(tutorial.Player.up, Vector3.up) < .01f,
                    "The actual tutorial player did not land upright.");
                Capture("motion-held-jump-landed");
                Debug.Log($"TUTORIAL VARIABLE JUMP PASSED: tap={tap:F3}m; held={held:F3}m.");
                Debug.Log("TUTORIAL MOTION SMOKE PASSED: run, backward, strafe, turn, jump and landing.");
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveLayout("RecoveryKeyboard");
                InputSystem.RemoveLayout("RecoveryMouse");
            }

            async Task<float> JumpPeak(int holdFrames)
            {
                tutorial.WarpPlayer(TutorialController.StartPosition);
                await Drive(60);
                float floor = tutorial.Player.position.y, peak = floor;
                for (int frame = 0; frame < 150; frame++)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(frame < holdFrames ? new[] { Key.Space } : Array.Empty<Key>()));
                    await Task.Yield();
                    keyboard.MakeCurrent(); mouse.MakeCurrent();
                    type.GetMethod("HandleInput", flags).Invoke(tutorial, new object[] { 1f / 60 });
                    type.GetMethod("UpdateCamera", flags).Invoke(tutorial, null);
                    peak = Mathf.Max(peak, tutorial.Player.position.y);
                    if (frame == 25) Capture(holdFrames > 3 ? "motion-held-jump" : "motion-tap-jump");
                }
                Require(Grounded() && Mathf.Abs(tutorial.Player.position.y - floor) < .1f, "Variable jump did not return to its floor.");
                return peak - floor;
            }

            async Task Drive(int frames, params Key[] keys)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                    // Let the ordinary input update consume the event. A second
                    // manual update can clear wasPressedThisFrame before Jump.
                    await Task.Yield();
                    keyboard.MakeCurrent(); mouse.MakeCurrent();
                    if (keys.Contains(Key.Space)) Debug.Log($"TUTORIAL MOTION JUMP INPUT: grounded={Grounded()}; nativeBelow={motor.isGrounded}; down={keyboard.spaceKey.wasPressedThisFrame}; pressed={keyboard.spaceKey.isPressed}; y={tutorial.Player.position.y}");
                    type.GetMethod("HandleInput", flags).Invoke(tutorial, new object[] { 1f / 60 });
                    type.GetMethod("UpdateCamera", flags).Invoke(tutorial, null);
                }
                await Frames(2);
            }
        }
    }
}
#endif
