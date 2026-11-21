using System;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public enum FrontendLoadingStage { CharacterCreation, CharacterSelection, Tutorial, FutureAssets, FutureArea, PastAssets, PastArea }
    public enum FrontendLoadingPhase { Hidden, Resources, Scene }

    public sealed partial class FrontendController
    {
        VisualElement loadingScreen, loadingWindow, loadingFill, loadingCurrentFill;
        Image loadingBackground;
        Label loadingCaption, loadingResource, loadingTip;
        Task loadingArtwork;
        string loadingArtworkLanguage;
        float loadingOverallProgress, loadingCurrentProgress;
        float loadingProgressBase, loadingProgressSpan = 1;
        int loadingVersion;
        public FrontendLoadingStage LoadingStage { get; private set; }
        public FrontendLoadingPhase LoadingPhase { get; private set; }

        public Task ShowLoadingAsync(FrontendLoadingStage stage, bool showResourceProgress = true)
            => ShowLoadingStageAsync(stage, showResourceProgress);

        async Task ShowLoadingStageAsync(FrontendLoadingStage stage, bool showResourceProgress)
        {
            int version = ++loadingVersion;
            bool opening = loadingScreen == null || loadingScreen.style.display == DisplayStyle.None;
            if (loadingScreen == null) BuildLoadingScreen();
            bool changed = LoadingStage != stage;
            if (opening) loadingOverallProgress = 0;
            if (opening || changed) loadingCurrentProgress = 0;
            LoadingStage = stage;
            if (opening)
            {
                SetLoadingResourceProgressVisible(showResourceProgress);
                // Primary GameTips has 18 lines, without a trailing newline.
                // Random.Range's exclusive maximum preserves its last-line quirk.
                loadingTip.text = LoadingText("ui.loading.tip." + UnityEngine.Random.Range(1, 18).ToString("00"));
            }
            if (changed) Debug.Log($"FRONTEND LOADING: {stage}/{(showResourceProgress ? FrontendLoadingPhase.Resources : FrontendLoadingPhase.Scene)}");
            string section = LoadingSection(stage);
            loadingCaption.text = LoadingStageCaption(stage);
            loadingResource.text = LoadingText("ui.loading.current." + section);
            ApplyLoadingProgress();
            loadingScreen.style.display = DisplayStyle.Flex;
            SetLoadingResourceProgressVisible(showResourceProgress);
            LayoutLoadingScreen();
            if (loadingArtwork == null || loadingArtworkLanguage != language)
            {
                loadingArtworkLanguage = language;
                loadingArtwork = BindLoadingArtAsync(language);
            }
            try { await loadingArtwork; }
            catch { loadingArtwork = null; throw; }
            // The fully skinned overlay must be presented before scene assembly.
            await Task.Yield(); await Task.Yield();
            if (version != loadingVersion) return;
        }

        void BuildLoadingScreen()
        {
            loadingScreen = FFView.InstantiateClass("ui/views/Login", "ff-loading");
            loadingScreen.name = "world-loading";
            loadingBackground = FFView.RequireClass<Image>(loadingScreen, "ff-loading-background");
            loadingWindow = FFView.RequireClass<VisualElement>(loadingScreen, "ff-loading-window");
            loadingTip = FFView.RequireClass<Label>(loadingScreen, "ff-loading-tip");
            loadingResource = FFView.RequireClass<Label>(loadingScreen, "ff-loading-resource");
            loadingCaption = FFView.RequireClass<Label>(loadingScreen, "ff-loading-caption");
            loadingCurrentFill = FFView.RequireClass<VisualElement>(loadingScreen, "ff-loading-current");
            loadingFill = FFView.RequireClass<VisualElement>(loadingScreen, "ff-loading-overall");
            loadingWindow.name = "loading-window";
            loadingCurrentFill.name = "loading-current-fill";
            loadingFill.name = "loading-overall-fill";
            loadingScreen.RegisterCallback<GeometryChangedEvent>(_ => LayoutLoadingScreen());
            UiLayers.For(Root).Add(loadingScreen, UiLayer.Loading);
        }

        void LayoutLoadingScreen()
        {
            if (loadingWindow == null) return;
            int width = Mathf.RoundToInt(Root.resolvedStyle.width), height = Mathf.RoundToInt(Root.resolvedStyle.height);
            if (width <= 0 || height <= 0) { width = Screen.width; height = Screen.height; }
            Rect rect = RetrobutionLoadingPresentation.Window(width, height);
            loadingWindow.style.left = rect.x; loadingWindow.style.top = rect.y;
            float scale = RetrobutionLayout.Scale(height);
            loadingWindow.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        async Task BindLoadingArtAsync(string requestedLanguage)
        {
            var background = await RetrobutionLoadingPresentation.LoadBackgroundAsync(requestedLanguage, lifetime.Token);
            if (!this || requestedLanguage != language || loadingScreen == null) return;
            if (!background) throw new InvalidOperationException("Retrobution loading screen artwork is missing.");
            loadingBackground.image = background;
            loadingScreen.EnableInClassList("ff-loading--ru", requestedLanguage == "ru");
        }

        public void SetLoadingProgress(float overall, float current, string currentResource = null)
        {
            if (float.IsFinite(overall)) loadingOverallProgress = Mathf.Max(loadingOverallProgress, loadingProgressBase + loadingProgressSpan * Mathf.Clamp01(overall));
            if (float.IsFinite(current)) loadingCurrentProgress = Mathf.Clamp01(current);
            if (currentResource != null && loadingResource != null) loadingResource.text = currentResource;
            ApplyLoadingProgress();
        }

        void SetLoadingProgressRange(float start, float end)
        { loadingProgressBase = start; loadingProgressSpan = end - start; }

        // AssetLoader.FullScreen omits DrawPercent after resource packages are
        // ready, while DrawAll keeps the background during Dong/render readiness.
        public void SetLoadingResourceProgressVisible(bool visible)
        {
            var phase = visible ? FrontendLoadingPhase.Resources : FrontendLoadingPhase.Scene;
            if (LoadingPhase != phase)
            {
                LoadingPhase = phase;
                Debug.Log($"FRONTEND LOADING: {LoadingStage}/{phase}");
            }
            if (loadingWindow != null) loadingWindow.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ApplyLoadingProgress()
        {
            if (loadingFill != null) loadingFill.style.width = RetrobutionLoadingPresentation.FillWidth(loadingOverallProgress);
            if (loadingCurrentFill != null) loadingCurrentFill.style.width = RetrobutionLoadingPresentation.FillWidth(loadingCurrentProgress);
        }

        string LoadingText(string key) => LocalizationCatalog.Lookup(key, language);
        string LoadingStageCaption(FrontendLoadingStage stage)
        {
            // AssetLoader.FullScreen 06000084: initial groups, then Future/Past
            // assets and area are separate readiness checks, in that order.
            return stage switch {
                FrontendLoadingStage.FutureAssets => language == "ru" ? "ЗАГРУЗКА РЕСУРСОВ БУДУЩЕГО" : "LOADING FUTURE ZONE ASSETS",
                FrontendLoadingStage.FutureArea => language == "ru" ? "ЗАГРУЗКА ОБЛАСТИ БУДУЩЕГО" : "LOADING FUTURE ZONE AREA",
                FrontendLoadingStage.PastAssets => language == "ru" ? "ЗАГРУЗКА РЕСУРСОВ ПРОШЛОГО" : "LOADING PAST ZONE ASSETS",
                FrontendLoadingStage.PastArea => language == "ru" ? "ЗАГРУЗКА ОБЛАСТИ ПРОШЛОГО" : "LOADING PAST ZONE AREA",
                _ => LoadingText("ui.loading." + LoadingSection(stage))
            };
        }
        static string LoadingSection(FrontendLoadingStage stage) => stage switch {
            FrontendLoadingStage.CharacterCreation => "character_creation",
            FrontendLoadingStage.CharacterSelection => "character_selection", FrontendLoadingStage.Tutorial => "tutorial", _ => "world"
        };
        public void HideLoading()
        {
            loadingVersion++;
            LoadingPhase = FrontendLoadingPhase.Hidden;
            loadingProgressBase = 0; loadingProgressSpan = 1;
            if (loadingScreen != null) loadingScreen.style.display = DisplayStyle.None;
        }

        public async Task EnterCreatedCharacterAsync(int slot)
        {
            SelectRosterSlot(slot);
            if (SelectedCharacter == null) throw new InvalidOperationException("Created character is missing from the accepted roster.");
            await RunOperation(EnterSelectedWorldAsync, language == "ru" ? "ЗАГРУЗКА ОБУЧЕНИЯ..." : "LOADING TUTORIAL...");
        }
    }

    // Primary main.unity3d SHA256 01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef.
    // AssetLoader.FullScreen/DrawPercent + AssetLoaderSkin1377; DrawAll uses ScaleAndCrop.
    // Textures: sharedassets0.assets181 LoadingWindow,605 LoadingBar,369 load.
    // Keep the accepted previous-build load.png route intact; this explicit primary route is separate.
    public static class RetrobutionLoadingPresentation
    {
        public const string BackgroundAddress = "ui/en/gameplay/loading/retrobution-load";
        internal const string RussianBackgroundAddress = "ui/ru/gameplay/loading/load";

        internal static async Task<Texture2D> LoadBackgroundAsync(string language, CancellationToken cancellation = default)
        {
            if (language == "ru")
            {
                var localized = await ContentAssets.GetAsync<Texture2D>(RussianBackgroundAddress, cancellation);
                if (localized) return localized;
            }
            return await ContentAssets.GetAsync<Texture2D>(BackgroundAddress, cancellation);
        }

        public static Rect Window(int width, int height)
        {
            float scale = RetrobutionLayout.Scale(height);
            return new Rect(width / 2 - 254 * scale, height / 2 - 154 * scale, 509 * scale, 309 * scale);
        }
        public static float FillWidth(float progress) => 15 + 448 * (float.IsFinite(progress) ? Mathf.Clamp01(progress) : 0);
    }
}
