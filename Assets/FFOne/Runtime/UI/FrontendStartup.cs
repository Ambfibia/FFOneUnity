using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        bool frontendInitialized;

        async Task InitializeFrontendAsync(CancellationToken cancellation)
        {
            float started = Time.realtimeSinceStartup;
            await ContentAssets.InitializeAsync(cancellation);
            // Root is already published: creator/preview components can subscribe while
            // Addressables and immutable locale parsing yield to the player loop.
            await Task.WhenAll(
                LocalizationCatalog.PreloadAsync("en", cancellation),
                LocalizationCatalog.PreloadAsync("ru", cancellation),
                ContentAssets.GetAsync<FontAsset>("fonts/jeffe-sdf", cancellation),
                ContentAssets.GetAsync<FontAsset>("fonts/chaletbook-regular-sdf", cancellation),
                RetrobutionLoadingPresentation.LoadBackgroundAsync(language, cancellation),
                ContentAssets.GetAsync<Texture2D>("ui/en/gameplay/loading/loadingwindow", cancellation),
                ContentAssets.GetAsync<Texture2D>("ui/ru/gameplay/loading/loadingwindow", cancellation),
                ContentAssets.GetAsync<Texture2D>("ui/en/gameplay/loading/loadingbar", cancellation));
            cancellation.ThrowIfCancellationRequested();
            float foundation = Time.realtimeSinceStartup - started;
            audio = gameObject.AddComponent<RetrobutionAudioController>();
            await PreloadFrontendAssetsAsync(cancellation);
            float preload = Time.realtimeSinceStartup - started - foundation;

            Root.styleSheets.Add(ContentAssets.Load<StyleSheet>("ui/styles/FFTheme"));
            var styles = ContentAssets.Load<StyleSheet>("ui/styles/Frontend");
            if (styles) Root.styleSheets.Add(styles);
            FFWidgets.Font(Root);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            float buildStarted = Time.realtimeSinceStartup;
            BuildLogin();
            float loginBuild = Time.realtimeSinceStartup - buildStarted;
            await Task.Yield();
            cancellation.ThrowIfCancellationRequested();

            // Publish the first actionable selection controls and initialize options in
            // one continuation, so other components cannot observe a half-built frontend.
            buildStarted = Time.realtimeSinceStartup;
            BuildSelection();
            float selectionBuild = Time.realtimeSinceStartup - buildStarted;
            status = FFWidgets.Label(UiLayers.For(Root).Host(UiLayer.Status), "connection-status", "", 0, 0, 580, 60, true, 14);
            status.AddToClassList("frontend-status");
            status.style.display = DisplayStyle.None;
            Root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            buildStarted = Time.realtimeSinceStartup;
            frontendInitialized = true;
            InitializeOptions();
            ShowLogin();
            float optionsBuild = Time.realtimeSinceStartup - buildStarted;
            Debug.Log($"FRONTEND STARTUP READY: elapsed={Time.realtimeSinceStartup - started:F3}s, foundation={foundation:F3}s, preload={preload:F3}s, login-build={loginBuild:F3}s, selection-build={selectionBuild:F3}s, options-build={optionsBuild:F3}s.", this);
        }

        async Task PreloadFrontendAssetsAsync(CancellationToken cancellation)
        {
            var requests = new List<Func<Task>>();
            void Load<T>(string address) where T : UnityEngine.Object => requests.Add(async () => { await ContentAssets.GetAsync<T>(address, cancellation); });
            void Skin(string address) { Load<Sprite>(address); Load<Texture2D>(address); }

            foreach (string view in new[] { "Login", "CharacterSelection", "Settings" }) Load<VisualTreeAsset>("ui/views/" + view);
            foreach (string style in new[] { "FFTheme", "Frontend", "CharacterSelection", "Settings" }) Load<StyleSheet>("ui/styles/" + style);
            Load<NativeDataDocument>(NativeData.Resource("ui/option-labels"));
            Load<TextAsset>("ui/layout-overrides");
            Load<AudioClip>("audio/music/00_main_theme");
            foreach (string cue in new[] { "tab_click01", "yes_button", "no_button", "mouse_click01", "mouse_click02", "mouse_click03", "mouse_click04", "mouse_click05" })
                Load<AudioClip>("audio/sfx/ui/" + cue);

            Load<Texture2D>("ui/en/launcher/login/login_screen_bg_16x10");
            Skin("ui/en/launcher/login/login_screen_button_bg");
            Skin("ui/en/launcher/login/ff-textfield-normal");
            foreach (string state in new[] { "normal", "hover", "active" }) Skin("ui/en/server-selection/ff-button-" + state);
            // Account profiles and options create these controls during their first build.
            foreach (string image in new[] { "radio-checked", "radio-empty", "color-selected" }) Load<Texture2D>("ui/en/option/" + image);
            foreach (string image in new[] { "scroll-track", "scroll-thumb" }) Skin("ui/en/user-equip/" + image);

            for (int frame = 1; frame <= 10; frame++) Load<Texture2D>("ui/en/character/selection/backgrounds/future/CSFutureBG_" + frame);
            Load<Texture2D>("ui/en/character/selection/background/CSBG");
            foreach (string image in new[] { "CCRotateLeftNormal", "CCRotateLeftOver", "CCRotateRightNormal", "CCRoatateRightOver" }) Skin("ui/en/character/creation/camera/" + image);
            foreach (string image in new[] { "blue_button_normal", "blue_button_over", "red_button_normal", "red_button_over", "CSEnterButton", "CSEnterGameOver", "CSFullScreenButton", "CSFullToWindow", "FullscreenModeButton_Over" })
                Skin(FFWidgets.Controls + image);
            foreach (string locale in new[] { "en", "ru" })
                foreach (string image in new[] { "musicoff", "musicon" }) Skin("ui/" + locale + "/character/selection/controls/" + image);

            // AssetLoader.GetProgress(eStep0) aggregates creator and selection
            // resources. Preserve that session total when the caption advances.
            var creatorRequests = new Func<Task>[] {
                async () => { await ContentAssets.GetAsync<VisualTreeAsset>("ui/views/CharacterCreation", cancellation); },
                async () => { await ContentAssets.GetAsync<StyleSheet>("ui/styles/CharacterCreation", cancellation); },
                async () => { await ContentAssets.GetAsync<NativePlayerAnimationProfile>("characters/player/shared/animation/male-profile", cancellation); },
                async () => { await ContentAssets.GetAsync<NativePlayerAnimationProfile>("characters/player/shared/animation/female-profile", cancellation); },
                () => audio.InitializeAsync(cancellation)
            };
            float creatorShare = (float)creatorRequests.Length / (creatorRequests.Length + requests.Count);
            SetLoadingProgressRange(0, creatorShare);
            await ShowLoadingAsync(FrontendLoadingStage.CharacterCreation);
            int creatorCompleted = 0;
            await Task.WhenAll(creatorRequests.Select(async request => {
                await request(); SetLoadingProgress((float)++creatorCompleted / creatorRequests.Length, 1);
            }));
            PrepareAudioPreferences();
            audio.SetMode(RetrobutionAudioController.Mode.Lobby);
            SetLoadingProgressRange(creatorShare, 1);
            await ShowLoadingAsync(FrontendLoadingStage.CharacterSelection);

            // Bound concurrent first-use handles rather than loading an artwork folder or
            // forcing synchronous WaitForCompletion from each individual widget builder.
            const int batchSize = 12;
            for (int offset = 0; offset < requests.Count; offset += batchSize)
            {
                cancellation.ThrowIfCancellationRequested();
                await Task.WhenAll(requests.Skip(offset).Take(batchSize).Select(request => request()));
                float progress = (float)Mathf.Min(offset + batchSize, requests.Count) / requests.Count;
                SetLoadingProgress(progress, progress);
            }
        }
    }
}
