using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    /// <summary>The two Unity-authored Dexter ship scenes surrounding character creation.</summary>
    public sealed class DexterShipCutscene : MonoBehaviour
    {
        sealed class PreparedAssets
        {
            public GameObject[] Models;
            public Texture2D Scanline, DexterBody;
            public Shader Shader;
            public VisualTreeAsset Presentation;
            public AudioClip Music;
            public NativeEffectDefinition ComputerClosure;
            public Dictionary<string, AudioClip> Clips;
            public bool Valid(bool creation) => Presentation && Scanline && DexterBody && Shader && Music
                && (creation || ComputerClosure) && Models.All(model => model) && Clips.Values.All(clip => clip);
        }
        const int SceneLayer = 30;
        const string PresentationAddress = "ui/views/Retrobution/GameplayHud";
        [SerializeField] FrontendController frontend;
        readonly Dictionary<(bool Creation, string VoiceLanguage), PreparedAssets> preparedAssets = new();
        readonly Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal);
        readonly List<AudioSource> loops = new();
        readonly GameObject[] actors = new GameObject[3];
        readonly string[] currentClips = new string[3];
        VisualElement overlay, sceneCanvas, background, blackout, subtitleArea, titleCanvas, skipCanvas;
        Image hologram;
        Label speaker, dialogue, title, titleShadow, loading, skipLabel;
        GameObject sceneRoot, computer;
        Camera view;
        RenderTexture sceneTexture, outputTexture;
        Material hologramMaterial;
        AudioSource voice, sounds;
        TutorialEffectAssets computerAssets;
        TutorialEffectPlayback computerPlayback;
        CancellationTokenSource playback;
        bool skip, frameReady, flicker;
        uint randomState;
        float opacity = .8f, offsetY;
        string locationCaption;
        int displayedSubtitleLine;
        public bool IsPlaying { get; private set; }
        public string VoiceLanguage { get; set; }
        public float Elapsed { get; private set; }
        public void Skip() { if (IsPlaying) skip = true; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Validate the actor silhouette, independently of the UI/background capture.</summary>
        public string CaptureSceneRender(string path, bool requireSilhouette = true)
        {
            if (!IsPlaying || !frameReady || !sceneTexture) throw new InvalidOperationException("Dexter ship scene has not rendered.");
            var previous = RenderTexture.active;
            var pixels = new Texture2D(sceneTexture.width, sceneTexture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = sceneTexture;
                pixels.ReadPixels(new UnityEngine.Rect(0, 0, sceneTexture.width, sceneTexture.height), 0, 0);
                pixels.Apply();
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                var colors = pixels.GetPixels32();
                int clear = colors.Count(pixel => pixel.a <= 4), covered = colors.Count(pixel => pixel.a >= 16);
                var report = new System.Text.StringBuilder();
                report.Append($"elapsed={Elapsed:F3} timeScale={Time.timeScale:F3} clear={clear}/{colors.Length} covered={covered}/{colors.Length}");
                for (int actor = 0; actor < actors.Length; actor++)
                {
                    report.Append($"; actor{actor}={currentClips[actor]}");
                    foreach (var animation in actors[actor].GetComponentsInChildren<NativeNpcAnimator>(true))
                    {
                        if (animation.CurrentMotion)
                            report.Append($" time={animation.MotionTime:F3}/{animation.CurrentMotion.length:F3} speed={animation.Animator.speed:F3}");
                    }
                    var root = actors[actor].GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == "Bip01 NonAccum");
                    if (root) report.Append(" root=" + view.transform.InverseTransformPoint(root.position).ToString("F3"));
                    foreach (var renderer in actors[actor].GetComponentsInChildren<Renderer>(true))
                        report.Append($" [{renderer.name} enabled={renderer.enabled} center={view.transform.InverseTransformPoint(renderer.bounds.center):F3} size={renderer.bounds.size:F3}]");
                }
                if (requireSilhouette && (clear < colors.Length / 100 || covered < colors.Length / 100))
                    throw new InvalidOperationException("Dexter ship actor silhouette is empty or fills the complete render texture: " + report);
                return report.ToString();
            }
            finally { RenderTexture.active = previous; Release(pixels); }
        }
#endif

        /// <summary>Warm only asset handles under the caller's loading screen.</summary>
        public Task PreloadBeforeCreationAsync(string language, CancellationToken cancellation = default)
            => LoadAssetsAsync(true, language, cancellation);
        /// <summary>Warm only asset handles; ES740 runtime materials are created by playback.</summary>
        public Task PreloadBeforeTutorialAsync(string language, CancellationToken cancellation = default)
            => LoadAssetsAsync(false, language, cancellation);

        public Task PlayBeforeCreationAsync(VisualElement parent, string language, CancellationToken cancellation = default)
            => PlayAsync(parent, language, true, cancellation);
        public Task PlayBeforeTutorialAsync(VisualElement parent, string language, CancellationToken cancellation = default)
            => PlayAsync(parent, language, false, cancellation);

        async Task PlayAsync(VisualElement parent, string language, bool creation, CancellationToken cancellation)
        {
            if (playback != null) throw new InvalidOperationException("A Dexter ship scene is already playing.");
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            playback = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = playback.Token;
            bool completed = false;
            try
            {
                if (!frontend) TryGetComponent(out frontend);
                frontend?.PlayCreationMusic();
                skip = frameReady = flicker = false; Elapsed = 0; randomState = 0xd37e2510;
                displayedSubtitleLine = -1;
                locationCaption = LocalizationCatalog.Lookup("content.tutorial.scene.1.1", language);
                BuildOverlay(parent, language, creation);
                var cues = creation ? DexterShipTimeline.CreationCues(locationCaption.Length) : DexterShipTimeline.Tutorial;
                await PrepareAsync(creation, language, token);
                token.ThrowIfCancellationRequested();
                if (!this) throw new OperationCanceledException(token);
                BuildScene(creation);
                Present(creation, language, 0);
                view.enabled = true;
                RenderPipelineManager.endCameraRendering += CameraRendered;
                float waitStarted = Time.realtimeSinceStartup;
                while (!frameReady)
                {
                    token.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartup - waitStarted > 30) throw new TimeoutException("Dexter ship camera did not render.");
                    await Task.Yield();
                }
                loading.AddToClassList("ui-suppressed");
                IsPlaying = true;
                foreach (var animation in sceneRoot.GetComponentsInChildren<Animation>(true))
                    foreach (AnimationState state in animation) state.speed = 1;
                foreach (var animation in sceneRoot.GetComponentsInChildren<NativeNpcAnimator>(true))
                    animation.Animator.speed = 1;
                if (computerPlayback) computerPlayback.enabled = true;
                int cursor = 0;
                float started = Time.unscaledTime;
                float duration = creation ? DexterShipTimeline.CreationDuration(locationCaption.Length) : DexterShipTimeline.TutorialDuration;
                while (!skip && Elapsed < duration)
                {
                    token.ThrowIfCancellationRequested();
                    Elapsed = Mathf.Min(duration, Time.unscaledTime - started);
                    Present(creation, language, Elapsed);
                    while (cursor < cues.Length && cues[cursor].Time <= Elapsed) PlayCue(cues[cursor++]);
                    var keys = Keyboard.current;
                    if (keys?.spaceKey.wasPressedThisFrame == true || keys?.escapeKey.wasPressedThisFrame == true
                        || Gamepad.current?.buttonSouth.wasPressedThisFrame == true) skip = true;
                    await Task.Yield();
                }
                completed = true;
            }
            finally
            {
                IsPlaying = false;
                // NameEvent keeps SoundUtil's loop for the creator. StartTutorial
                // stops it before the tutorial world's own timeline takes over.
                if (!creation || !completed) frontend?.StopCreationMusic();
                Cleanup();
                playback?.Dispose(); playback = null;
            }
        }

        async Task<PreparedAssets> LoadAssetsAsync(bool creation, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string voiceLanguage = VoiceLanguage == "ru" || VoiceLanguage == "en" ? VoiceLanguage : language == "ru" ? "ru" : "en";
            var key = (creation, voiceLanguage);
            if (preparedAssets.TryGetValue(key, out var prepared) && prepared.Valid(creation)) return prepared;
            var cues = creation ? DexterShipTimeline.Creation : DexterShipTimeline.Tutorial;
            var models = DexterShipTimeline.ActorModels.Select(path => NativeModelAssets.PreloadAsync(path, token)).ToList();
            if (!creation) models.Add(NativeModelAssets.PreloadAsync(DexterShipTimeline.ComputerModel, token));
            var uiTask = ContentAssets.GetAsync<VisualTreeAsset>(PresentationAddress, token);
            var scanlineTask = ContentAssets.GetAsync<Texture2D>(DexterShipTimeline.Scanline, token);
            var shaderTask = ContentAssets.GetAsync<Shader>("Tutorial/DexterShipHologram", token);
            var bodyTextureTask = ContentAssets.GetAsync<Texture2D>(DexterShipTimeline.DexterBodyTexture, token);
            var audio = cues.Where(c => c.Action != 3).Select(c => (Key:c.Resource("en"), Path:c.Resource(c.Voice ? voiceLanguage : "en")))
                .Distinct().Select(async route => {
                    var clip = await ContentAssets.GetAsync<AudioClip>(route.Path, token);
                    if (!clip && route.Path != route.Key) clip = await ContentAssets.GetAsync<AudioClip>(route.Key, token);
                    if (!clip) throw new InvalidOperationException("Missing Dexter ship audio: " + route.Path);
                    return (route.Key, Clip:clip);
                }).ToArray();
            var musicTask = ContentAssets.GetAsync<AudioClip>("audio/music/charactercreation_loop", token);
            var effectTask = creation ? Task.FromResult<NativeEffectDefinition>(null)
                : ContentAssets.GetAsync<NativeEffectDefinition>(NativeEffectDefinition.Resource(740), token);
            await Task.WhenAll(models.Cast<Task>().Concat(audio).Append(uiTask).Append(scanlineTask).Append(shaderTask).Append(musicTask).Append(effectTask)
                .Append(bodyTextureTask));
            token.ThrowIfCancellationRequested();
            prepared = new PreparedAssets {
                Models = models.Select(task => task.Result).ToArray(), Presentation = uiTask.Result, Scanline = scanlineTask.Result,
                Shader = shaderTask.Result, Music = musicTask.Result, DexterBody = bodyTextureTask.Result,
                ComputerClosure = effectTask.Result,
                Clips = audio.ToDictionary(task => task.Result.Key, task => task.Result.Clip, StringComparer.Ordinal)
            };
            if (!prepared.Valid(creation))
                throw new InvalidOperationException("Missing native Dexter ship presentation assets.");
            // Loading an AudioClip handle can precede background PCM decoding.
            // Freeze playback until all voices/effects can start on their cues.
            foreach (var clip in prepared.Clips.Values.Append(prepared.Music).Distinct())
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                float deadline = Time.realtimeSinceStartup + 30;
                while (clip.loadState == AudioDataLoadState.Loading)
                {
                    token.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Dexter ship audio is not ready: " + clip.name);
                    await Task.Yield();
                }
                if (clip.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("Dexter ship audio failed: " + clip.name);
            }
            // These are retained content assets, never scene objects or generated
            // material instances. Cleanup of one performance leaves this warm set
            // available for the other caller and subsequent character re-entry.
            preparedAssets[key] = prepared;
            return prepared;
        }

        async Task PrepareAsync(bool creation, string language, CancellationToken token)
        {
            var assets = await LoadAssetsAsync(creation, language, token);
            token.ThrowIfCancellationRequested();
            foreach (var clip in assets.Clips) clips[clip.Key] = clip.Value;
            sceneRoot = new GameObject("Dexter ship event scene");
            sceneRoot.transform.SetParent(transform, false); sceneRoot.transform.localPosition = new Vector3(0, 5000, 0);
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = new GameObject(new[] { "Dexter NPC 728", "Dee Dee NPC 701", "Computress NPC 730" }[i]);
                actor.transform.SetParent(sceneRoot.transform, false);
                Instantiate(assets.Models[i], actor.transform, false);
                actors[i] = actor;
            }
            DexterShipAppearance.BindDexterBody(actors[0], assets.DexterBody);
            if (!creation) computerAssets = TutorialEffectAssets.Load(740, DexterShipTimeline.ComputerModel);
            voice = sceneRoot.AddComponent<AudioSource>(); sounds = sceneRoot.AddComponent<AudioSource>();
            hologramMaterial = new Material(assets.Shader) { name = "Dexter ship hologram" };
            hologramMaterial.SetTexture("_DecalTex", assets.Scanline);
        }

        void BuildScene(bool creation)
        {
            var cameraObject = new GameObject("Dexter ship render camera"); cameraObject.transform.SetParent(sceneRoot.transform, false);
            view = cameraObject.AddComponent<Camera>(); view.enabled = false;
            view.cullingMask = 1 << SceneLayer; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.clear;
            view.fieldOfView = 45; view.nearClipPlane = .001f; view.farClipPlane = 1000; view.allowHDR = false;
            view.transform.localPosition = new Vector3(0, 1, 2); view.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var data = view.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            sceneTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { name = "Dexter ship 512x512 scene" };
            outputTexture = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32) { name = "Dexter ship hologram output" };
            sceneTexture.Create(); outputTexture.Create(); view.targetTexture = sceneTexture; hologram.image = outputTexture;
            var lightObject = new GameObject("Dexter ship light"); lightObject.transform.SetParent(sceneRoot.transform, false);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1; light.cullingMask = 1 << SceneLayer;
            light.transform.localRotation = Quaternion.Euler(30, -30, 0);
            if (!creation)
            {
                computer = new GameObject("Exact ES740 inventory computer"); computer.transform.SetParent(sceneRoot.transform, false);
                computerPlayback = computer.AddComponent<TutorialEffectPlayback>();
                computerPlayback.Initialize(computerAssets, 1, view); computerPlayback.enabled = false;
            }
            foreach (var child in sceneRoot.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = SceneLayer;
            SetClip(0, DexterShipTimeline.DexterClip(creation, 0, locationCaption.Length)); SetClip(1, "stand1"); SetClip(2, "stand1");
            actors[1].transform.localPosition = new Vector3(5, -.8f, -2);
            actors[2].transform.localPosition = new Vector3(.5f, -.2f, -.5f);
            foreach (var animation in sceneRoot.GetComponentsInChildren<Animation>(true))
                foreach (AnimationState state in animation) state.speed = 0;
            foreach (var animation in sceneRoot.GetComponentsInChildren<NativeNpcAnimator>(true))
            {
                // Choreography and audio use the unscaled scene clock, including after long frames.
                animation.Animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animation.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animation.Animator.speed = 0;
            }
        }
        void CameraRendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view || !sceneTexture || !outputTexture || !hologramMaterial) return;
            hologramMaterial.SetFloat("_Opacity", opacity); hologramMaterial.SetFloat("_OffsetY", offsetY / 512);
            Graphics.Blit(sceneTexture, outputTexture, hologramMaterial); frameReady = true;
        }
        void SetClip(int actor, string clip)
        {
            if (currentClips[actor] == clip) return;
            bool once = actor == 0 && (clip == "observe" || clip == "excellent" || clip == "deedeeno");
            bool found = false;
            foreach (var animation in actors[actor].GetComponentsInChildren<NativeNpcAnimator>(true))
            {
                if (animation.Play(clip, !once, 0, true)) found = true;
            }
            if (!found) throw new InvalidOperationException("Dexter ship animation missing: " + clip);
            currentClips[actor] = clip;
        }
        void Present(bool creation, string language, float t)
        {
            SetClip(0, DexterShipTimeline.DexterClip(creation, t, locationCaption.Length)); SetClip(2, DexterShipTimeline.ComputressClip(creation, t));
            actors[0].transform.localPosition = DexterShipTimeline.DexterPosition(creation, t);
            actors[0].transform.localRotation = !creation && t >= 27.4f ? Quaternion.Euler(0, -70, 0) : Quaternion.identity;
            actors[1].transform.localPosition = DexterShipTimeline.DeeDeePosition(creation, t, out var rotation, out var clip);
            actors[1].transform.localRotation = rotation; SetClip(1, clip);
            if (computer) computer.SetActive(t < 27.4f);
            opacity = flicker ? .8f * Mathf.Min(1, Mathf.Abs((Mathf.Sin(t * Mathf.PI) + Mathf.Cos(t * Mathf.PI * .7f) + RandomRange(-.05f, .05f)) * 7)) : .8f;
            if (RandomRange(0, 10) < 1) flicker = !flicker;
            offsetY = (Mathf.Sin(t * Mathf.PI * .7f) + Mathf.Cos(t * Mathf.PI) + RandomRange(-.1f, .1f)) * 5;
            Vector2 shake = Vector2.zero;
            if (!creation && t >= 15.8f && t < 32.5f)
            {
                float amount = t >= 29.9f ? 3 : t >= 27.4f ? 1.5f : .5f;
                shake = new Vector2((Mathf.Sin(t * Mathf.PI * 7) + Mathf.Cos(t * Mathf.PI * 9) + RandomRange(-.2f, .2f)) * amount * 15,
                    (Mathf.Sin(t * Mathf.PI * 3) + Mathf.Cos(t * Mathf.PI * 5) + RandomRange(-.3f, .3f)) * amount * 10);
            }
            float width = overlay.contentRect.width, height = overlay.contentRect.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            { width = Screen.width; height = Screen.height; }
            Layout(width, height, shake, creation);
            hologram.style.top = height / 2 - 256 + shake.y - offsetY;
            bool showHologram = creation ? t >= DexterShipTimeline.CreationReveal(locationCaption.Length) : t < 32.5f;
            hologram.EnableInClassList("ui-suppressed", !showHologram);
            float fade = DexterShipTimeline.Fade(creation, t, locationCaption.Length);
            // An opacity-zero scaled UI Toolkit panel can retain an opaque
            // draw on Vulkan. Remove a fully clear fade from rendering, and
            // encode partial fades in the rectangle's actual color alpha.
            blackout.EnableInClassList("ui-suppressed", fade <= 0);
            blackout.style.backgroundColor = new Color(0, 0, 0, fade);
            int line = DexterShipTimeline.SubtitleLine(creation, t, locationCaption.Length);
            if (line != displayedSubtitleLine)
            {
                DexterShipTimeline.SplitSubtitle(line == 0 ? "" : LocalizationCatalog.Lookup("content.tutorial.scene.1." + line, language), out string name, out string text);
                speaker.text = name;
                dialogue.text = text;
                displayedSubtitleLine = line;
            }
            if (creation)
            {
                title.text = t >= DexterShipTimeline.CreationReveal(locationCaption.Length) + 1 ? ""
                    : locationCaption.Substring(0, Mathf.Min(Mathf.Max(0, locationCaption.Length - 1), Mathf.FloorToInt(t / .05f) + 1));
                title.text = DexterShipTimeline.NormalizeSubtitle(title.text);
                titleShadow.text = title.text;
                title.style.opacity = fade;
                titleShadow.style.opacity = fade > .9f ? 1 : 0;
            }
            else
            {
                title.text = t >= 33 ? language == "ru" ? "ОШИБКА ПЕРЕМЕЩЕНИЯ ВО ВРЕМЕНИ" : "TIMEJUMP ERROR" : "";
                title.style.opacity = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2));
            }
        }
        void PlayCue(DexterShipTimeline.Cue cue)
        {
            if (cue.Action == 1 || cue.Action == 3) StopLoops();
            if (cue.Action == 3) return;
            var clip = clips[cue.Resource("en")];
            if (cue.Action == 1 || cue.Action == 2)
            {
                var source = sceneRoot.AddComponent<AudioSource>(); source.clip = clip; source.loop = true; source.Play(); loops.Add(source);
            }
            else if (cue.Voice) voice.PlayOneShot(clip);
            else sounds.PlayOneShot(clip);
        }
        void StopLoops() { foreach (var source in loops) if (source) { source.Stop(); Release(source); } loops.Clear(); }
        float RandomRange(float min, float max)
        {
            randomState ^= randomState << 13; randomState ^= randomState >> 17; randomState ^= randomState << 5;
            return min + (max - min) * (randomState / (float)uint.MaxValue);
        }

        void BuildOverlay(VisualElement parent, string language, bool creation)
        {
            overlay = FFView.InstantiateClass(PresentationAddress, "dexter-ship-cutscene");
            UiLayers.For(parent).Add(overlay, UiLayer.Cinematic);
            overlay.Focus();
            sceneCanvas = Role<VisualElement>("dexter-ship-scene-canvas");
            background = Role<VisualElement>("dexter-ship-background");
            blackout = Role<VisualElement>(creation ? "dexter-ship-blackout--before" : "dexter-ship-blackout--after");
            hologram = Role<Image>("dexter-ship-hologram");
            subtitleArea = Role<VisualElement>("dexter-ship-subtitle-area");
            speaker = Role<Label>("dexter-ship-subtitle-speaker");
            dialogue = Role<Label>("dexter-ship-subtitle-dialogue");
            skipCanvas = Role<VisualElement>("dexter-ship-skip-canvas");
            skipLabel = Role<Label>("dexter-ship-skip-label");
            skipLabel.text = language == "ru" ? "Нажмите ПРОБЕЛ, чтобы пропустить." : "Press SPACE BAR to skip.";
            loading = Role<Label>("dexter-ship-loading");
            loading.text = language == "ru" ? "ЗАГРУЗКА..." : "LOADING...";
            if (creation)
            {
                titleCanvas = Role<VisualElement>("dexter-ship-title-canvas");
                titleCanvas.RemoveFromClassList("ui-suppressed");
                title = Role<Label>("dexter-ship-title");
                titleShadow = Role<Label>("dexter-ship-title-shadow");
            }
            else
            {
                title = Role<Label>("dexter-ship-error");
                title.RemoveFromClassList("ui-suppressed");
            }
        }

        T Role<T>(string name) where T : VisualElement => FFView.RequireClass<T>(overlay, name);

        void Layout(float width, float height, Vector2 shake, bool creation)
        {
            // Preserve the original independent GUI matrices: scene around center,
            // location around the bottom-left bar edge, voice area around its top
            // center, and the skip label around the top-center of the screen.
            float scale = height / 681, bar = height * DexterShipTimeline.BlackBarRatio;
            sceneCanvas.style.transformOrigin = new TransformOrigin(width / 2, height / 2, 0);
            sceneCanvas.style.scale = new Scale(new Vector3(scale, scale, 1));
            background.style.left = (width - 1920) / 2 + shake.x;
            background.style.top = (height - 1440) / 2 + shake.y;
            hologram.style.left = width / 2 - 256 + shake.x;
            // Keep the accepted 60.44304% subtitle width in screen space. Only
            // the text scales: scaling its margins and then the container again
            // collapsed the area to zero at 1080p and above.
            float textScale = FFOne.UI.FrontendLayout.CharacterScale(width, height);
            float margin = width * .1977848f;
            FFWidgets.Rect(subtitleArea, margin, height - bar, (width - 2 * margin) / textScale, bar / textScale);
            subtitleArea.style.scale = new Scale(new Vector3(textScale, textScale, 1));
            skipCanvas.style.scale = new Scale(new Vector3(scale, scale, 1));
            if (creation)
            {
                titleCanvas.style.transformOrigin = new TransformOrigin(0, height - bar, 0);
                titleCanvas.style.scale = new Scale(new Vector3(scale, scale, 1));
                title.style.top = height - bar - 100;
                titleShadow.style.top = height - bar - 99;
            }
        }
        void OnDestroy() { playback?.Cancel(); Cleanup(); preparedAssets.Clear(); }
        void Cleanup()
        {
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            StopLoops();
            if (sceneRoot) foreach (var audio in sceneRoot.GetComponents<AudioSource>()) audio.Stop();
            if (view) { view.enabled = false; view.targetTexture = null; }
            overlay?.RemoveFromHierarchy(); overlay = null;
            sceneCanvas = background = blackout = subtitleArea = titleCanvas = skipCanvas = null;
            speaker = dialogue = title = titleShadow = loading = skipLabel = null; locationCaption = null;
            if (sceneTexture) { sceneTexture.Release(); Release(sceneTexture); } sceneTexture = null;
            if (outputTexture) { outputTexture.Release(); Release(outputTexture); } outputTexture = null;
            Release(hologramMaterial); hologramMaterial = null;
            if (sceneRoot) sceneRoot.SetActive(false); Release(sceneRoot); sceneRoot = null;
            computerAssets?.Dispose(); computerAssets = null; computerPlayback = null; computer = null;
            Array.Clear(actors, 0, actors.Length); Array.Clear(currentClips, 0, currentClips.Length); clips.Clear();
        }
        static void Release(UnityEngine.Object asset) { if (!asset) return; if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); }
    }
}
