using System;
using System.IO;
using System.Linq;
using FFOne.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        SettingsPreferences preferences;
        SettingsWindow settings;
        VisualElement worldMenu;
        int menuDismissedFrame = -1;
        JObject appliedGraphics;
        bool applySavedResolution;

        void InitializeOptions()
        {
            PrepareAudioPreferences();
            // Explicit player launch dimensions also belong to validation tools and launchers.
            var args = Environment.GetCommandLineArgs();
            applySavedResolution = !Application.isEditor && !args.Any(arg =>
                arg.Equals("-screen-width", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-screen-height", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-screen-fullscreen", StringComparison.OrdinalIgnoreCase));
            preferences.Changed += ApplyOptions;
            settings = new SettingsWindow(UiLayers.For(Root).Host(UiLayer.Settings), preferences);
            settings.Closed += UpdateWorldInputGate;
            settings.UnignoreRequested += slot => gameplayUi?.UnignorePlayer(slot);
            ApplyOptions();
        }

        void PrepareAudioPreferences()
        {
            if (preferences == null)
            {
                string path = Integration.DevelopmentConnection.OptionsPath(Application.persistentDataPath);
                var desktop = Screen.currentResolution;
                preferences = new SettingsPreferences(path, desktop.width, desktop.height);
                if (!File.Exists(path))
                {
                    preferences.Committed["text_locale"] = language;
                    preferences.Committed["voice_locale"] = language;
                    preferences.Open();
                }
            }
            var value = preferences.Committed;
            AudioListener.volume = (bool)value["sound"]["master_enabled"] ? (float)value["sound"]["master"] / 10 : 0;
            audio?.SetEffectsGain(AudioGain(value, "effects"));
            audio?.SetGain(AudioGain(value, "music"), AudioGain(value, "ambient"), playMusic);
        }

        public void OpenSettings()
        {
            if (busy || settings == null || settings.Visible) return;
            CloseWorldMenu();
            RefreshBlockedPlayers();
            settings.Open();
            UpdateWorldInputGate();
        }

        void RefreshBlockedPlayers()
        {
            if (settings == null) return;
            settings.SetBlockedPlayers(gameplayUi?.Social.Friends
                .Where(player => player?.Blocked == true)
                .Select(player => (player.Slot, player.Name)) ?? Enumerable.Empty<(int, string)>());
        }

        void ApplyOptions()
        {
            var value = preferences.Committed;
            GameplayInput.Apply(value);
            bool scaleUi = (bool?)value["display"]?["scale_ui"] ?? true;
            bool scaleChanged = RetrobutionLayout.AutomaticScale != scaleUi;
            RetrobutionLayout.AutomaticScale = scaleUi;
            string nextLanguage = (string)value["text_locale"];
            if (nextLanguage != language) SetLanguage(nextLanguage);
            PrepareAudioPreferences();
            tutorial?.ApplyPreferences(value, playMusic);
            gameplayUi?.ApplyDisplayPreferences(value);
            if (networkWorld)
            {
                networkWorld.Preferences = value;
                networkWorld.SetDanceMusicGain(playMusic ? AudioGain(value, "music") : 0);
                networkWorld.MouseSensitivity = (float)value["input"]["sensitivity"] / 5;
                networkWorld.InvertMouseY = (bool)value["input"]["invert_y"];
            }
            var graphics = (JObject)value["graphics"];
            if (appliedGraphics == null || !JToken.DeepEquals(graphics, appliedGraphics))
            {
                if (appliedGraphics == null ? applySavedResolution : ((int)graphics["width"] != (int)appliedGraphics["width"] ||
                    (int)graphics["height"] != (int)appliedGraphics["height"] ||
                    (bool)graphics["windowed"] != (bool)appliedGraphics["windowed"]))
                    Screen.SetResolution((int)graphics["width"], (int)graphics["height"],
                        (bool)graphics["windowed"] ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
                QualitySettings.anisotropicFiltering = (bool)graphics["anisotropic"] ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
                Shader.SetGlobalFloat("_FFOneDisableTerrainAnisotropy", (bool)graphics["anisotropic"] ? 0 : 1);
                Shader.SetGlobalFloat("_FFOneDisableToon", (bool)graphics["toon"] ? 0 : 1);
                QualitySettings.globalTextureMipmapLimit = (int)graphics["texture"];
            }
            appliedGraphics = (JObject)graphics.DeepClone();
            if (scaleChanged)
            {
                Layout();
                LayoutLoadingScreen();
                settings?.Layout();
                gameplayUi?.RefreshViewport();
            }
        }

        public static float AudioGain(JObject options, string channel) =>
            (bool)options["sound"][channel + "_enabled"] ? (float)options["sound"][channel] / 10 : 0;

        void UpdateWorldInputGate()
        {
            menuDismissedFrame = Time.frameCount;
            ApplyWorldInputGate();
        }

        void ApplyWorldInputGate()
        {
            bool modal = worldMenu != null || settings?.Visible == true || busy;
            gameplayUi?.SetInteractionSuspended(modal || revivePrompt != null || gameplayState?.Snapshot.Hp == 0);
            bool blocked = modal || gameplayUi?.InputBlocked == true || revivePrompt != null || gameplayState?.Snapshot.Hp == 0;
            if (networkWorld)
            {
                networkWorld.InputBlocked = blocked;
                networkWorld.MenuAutoRun = !modal && revivePrompt == null && gameplayState?.Snapshot.Hp > 0 && gameplayUi?.AllowsMenuAutoRun == true;
                networkWorld.InventoryOpen = gameplayUi?.Windows.Current is GameplayWindow.Inventory or GameplayWindow.Nanos or GameplayWindow.Journal;
            }
            if (tutorial) tutorial.ExternalInputBlocked = blocked;
        }

        void ToggleWorldMenu() => ToggleWorldMenuCore(false);

        void ToggleWorldMenuCore(bool fromGameplayNavigation)
        {
            if (busy || !fromGameplayNavigation && Time.frameCount == menuDismissedFrame) return;
            if (settings?.Visible == true) { settings.Close(); return; }
            if (worldMenu != null) { CloseWorldMenu(); return; }
            if (!fromGameplayNavigation && gameplayUi?.CloseWindow() == true) { UpdateWorldInputGate(); return; }
            worldMenu = FFView.Instantiate("ui/views/Retrobution/QuitMenu", "quit-root");
            UiLayers.For(Root).Add(worldMenu, UiLayer.Dialog);
            var dialog = FFView.Require<VisualElement>(worldMenu, "quit-dialog");
            var backdrop = FFView.Require<VisualElement>(worldMenu, "quit-backdrop");
            void Place()
            {
                int width = Mathf.RoundToInt(Root.resolvedStyle.width), height = Mathf.RoundToInt(Root.resolvedStyle.height);
                if (width <= 0 || height <= 0) return;
                float scale = RetrobutionLayout.Scale(height);
                FFWidgets.Rect(dialog, RetrobutionLayout.QuitDialog(width, height));
                FFWidgets.Rect(backdrop, RetrobutionLayout.QuitBackdrop(width, height));
                foreach (var element in new[] { dialog, backdrop })
                {
                    element.style.transformOrigin = new TransformOrigin(0, 0);
                    element.style.scale = new Scale(new Vector3(scale, scale, 1));
                }
            }
            worldMenu.RegisterCallback<GeometryChangedEvent>(_ => Place()); Place();
            BindWorldMenu("change-character", "ui.quit_menu.change_character", "CHANGE CHARACTER", () => CancelTutorial());
            BindWorldMenu("quit-game", "ui.quit_menu.quit_game", "QUIT GAME", QuitWorld);
            BindWorldMenu("cancel", "ui.common.cancel", "CANCEL", CloseWorldMenu);
            UpdateWorldInputGate();
        }

        void BindWorldMenu(string name, string key, string fallback, Action action)
        {
            var button = FFView.Require<Button>(worldMenu, name);
            var label = FFView.Require<Label>(button, name + "-label");
            label.text = FFStyle.Caption(T(key, fallback));
            FFWidgets.Font(label, false, 12);
            button.clicked += action;
        }

        async void QuitWorld()
        {
            if (worldTransition || worldLifetime == null) return;
            worldTransition = true; SetBusy(true);
            var operation = worldLifetime;
            try
            {
                await movementTask;
                await Session.LeaveWorldAsync(operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                DisposeWorldFlow(); Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (CurrentWorldOperation(operation))
                {
                    DisposeWorldFlow(); SetWorldPresentation(false);
                    if (Session.IsLoggedIn) ShowSelection(); else ShowLogin();
                    ShowError(error.Message);
                }
            }
            finally { if (this) SetBusy(false); }
        }

        void CloseWorldMenu()
        {
            worldMenu?.RemoveFromHierarchy(); worldMenu = null; UpdateWorldInputGate();
        }
    }
}
