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
            string path = Path.Combine(Application.persistentDataPath, "unity-options.json");
            var desktop = Screen.currentResolution;
            preferences = new SettingsPreferences(path, desktop.width, desktop.height);
            if (!File.Exists(path))
            {
                preferences.Committed["text_locale"] = language;
                preferences.Committed["voice_locale"] = language;
                preferences.Open();
            }
            // Explicit player launch dimensions also belong to validation tools and launchers.
            var args = Environment.GetCommandLineArgs();
            applySavedResolution = !Application.isEditor && !args.Any(arg =>
                arg.Equals("-screen-width", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-screen-height", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-screen-fullscreen", StringComparison.OrdinalIgnoreCase));
            preferences.Changed += ApplyOptions;
            settings = new SettingsWindow(Root, preferences);
            settings.Closed += UpdateWorldInputGate;
            ApplyOptions();
        }

        public void OpenSettings()
        {
            if (busy || settings == null || settings.Visible) return;
            CloseWorldMenu(); settings.Open(); UpdateWorldInputGate();
        }

        void ApplyOptions()
        {
            var value = preferences.Committed;
            string nextLanguage = (string)value["text_locale"];
            if (nextLanguage != language) SetLanguage(nextLanguage);
            AudioListener.volume = (bool)value["sound"]["master_enabled"] ? (float)value["sound"]["master"] / 10 : 0;
            if (music) music.volume = AudioGain(value, "music");
            tutorial?.ApplyPreferences(value);
            if (networkWorld)
            {
                networkWorld.Preferences = value;
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
                QualitySettings.globalTextureMipmapLimit = (int)graphics["texture"];
            }
            appliedGraphics = (JObject)graphics.DeepClone();
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
            gameplayUi?.SetInteractionSuspended(modal);
            bool blocked = modal || gameplayUi?.InputBlocked == true || revivePrompt != null || gameplayState?.Snapshot.Hp == 0;
            if (networkWorld) networkWorld.InputBlocked = blocked;
            if (tutorial) tutorial.ExternalInputBlocked = blocked;
        }

        void ToggleWorldMenu() => ToggleWorldMenuCore(false);

        void ToggleWorldMenuCore(bool fromGameplayNavigation)
        {
            if (busy || !fromGameplayNavigation && Time.frameCount == menuDismissedFrame) return;
            if (settings?.Visible == true) { settings.Close(); return; }
            if (worldMenu != null) { CloseWorldMenu(); return; }
            if (gameplayUi?.CloseWindow() == true && !fromGameplayNavigation) { UpdateWorldInputGate(); return; }
            worldMenu = FFView.Instantiate("ui/views/Retrobution/QuitMenu", "quit-root");
            Root.Add(worldMenu);
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
