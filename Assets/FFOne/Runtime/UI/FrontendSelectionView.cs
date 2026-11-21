using System;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        Label selectionTitle;

        void BuildSelection()
        {
            selectionRoot = FFView.InstantiateClass("ui/views/CharacterSelection", "roster-character-selection");
            Root.Insert(0, selectionRoot);
            TElement Find<TElement>(string role) where TElement : VisualElement =>
                FFView.RequireClass<TElement>(selectionRoot, "roster-" + role);

            Button ActionButton(string role, string value, Action action, bool playClick = false)
            {
                var button = Find<Button>(role);
                button.text = FFStyle.Caption(value);
                AutoFit(button);
                if (playClick) button.clicked += () => PlayUiSound("button");
                button.clicked += action;
                return button;
            }

            for (int i = 0; i < backgrounds.Length; i++)
            {
                backgrounds[i] = Find<Image>("background-" + i);
                backgrounds[i].image = Texture("ui/en/character/selection/backgrounds/future/CSFutureBG_" + (i + 1));
            }
            chrome = Find<VisualElement>("selection-chrome");
            avatarGroup = Find<VisualElement>("avatar-group");
            PreviewImage = Find<Image>("selected-avatar-preview");
            avatarName = Find<Label>("selected-avatar-name");
            avatarStatus = Find<Label>("selected-avatar-status");
            AutoFit(avatarName);
            AutoFit(avatarStatus);
            Hold(Find<Button>("rotate-left"), delta => RotatePreview?.Invoke(-100 * delta));
            Hold(Find<Button>("rotate-right"), delta => RotatePreview?.Invoke(100 * delta));
            selectionGroup = Find<VisualElement>("selection-group");
            selectionTitle = Find<Label>("selection-title");
            selectionTitle.text = FFStyle.Caption(T("ui.character_select.title", "SELECT A CHARACTER"));
            AutoFit(selectionTitle);
            var rows = selectionGroup.Query<TemplateContainer>(className: "selection-slot").ToList();
            if (rows.Count != slotButtons.Length) throw new InvalidOperationException("Character selection UXML requires four roster slots.");
            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slot = i + 1;
                var row = slotRows[i] = rows[i];
                slotButtons[i] = row.Q<Button>(className: "selection-slot-button");
                slotButtons[i].clicked += () => SelectSlot(slot);
                // Keep the established layout override key while letting styles target every template instance.
                slotButtons[i].name = "character-slot-" + slot;
                slotButtons[i].RegisterCallback<PointerEnterEvent>(_ => HighlightSlot(slot - 1, true));
                slotButtons[i].RegisterCallback<PointerLeaveEvent>(_ => HighlightSlot(slot - 1, false));
                slotButtons[i].RegisterCallback<FocusInEvent>(_ => HighlightSlot(slot - 1, true));
                slotButtons[i].RegisterCallback<FocusOutEvent>(_ => HighlightSlot(slot - 1, false));
                emptyLabels[i] = row.Q<Label>(className: "selection-slot-empty");
                emptyLabels[i].text = FFStyle.Caption(T("ui.character_select.empty", "EMPTY"));
                AutoFit(emptyLabels[i]);
                SlotPreviewImages[i] = row.Q<Image>(className: "selection-slot-preview");
                slotNames[i] = row.Q<Label>(className: "selection-slot-name");
                slotLevels[i] = row.Q<Label>(className: "selection-slot-level");
                slotLocations[i] = row.Q<Label>(className: "selection-slot-location");
                AutoFit(slotNames[i]);
                AutoFit(slotLevels[i]);
                AutoFit(slotLocations[i]);
            }
            delete = ActionButton("delete-character", T("ui.character_select.delete", "DELETE CHARACTER"), DeleteDialog, true);
            create = ActionButton("create-character", T("ui.character_select.create", "CREATE CHARACTER"), () => RequestCreate(), true);
            enter = ActionButton("enter-world", T("ui.character_select.enter", "ENTER THE GAME"), EnterWorld, true);
            musicButton = ActionButton("music-toggle", "", ToggleMusic);
            quit = ActionButton("quit", T("ui.common.quit", "QUIT"), Quit);
            fullscreen = ActionButton("fullscreen", "", ToggleFullscreen);
            RefreshSelectionControls();
        }

        void RefreshSelectionControls(bool? isFullscreen = null)
        {
            selectionRoot.EnableInClassList("roster--ru", language == "ru");
            musicButton.EnableInClassList("roster-music--off", playMusic);
            musicButton.EnableInClassList("roster-music--on", !playMusic);
            fullscreen.EnableInClassList("roster-fullscreen--windowed", !(isFullscreen ?? Screen.fullScreen));
        }

        public void SetCreationPresentationActive(bool active)
        {
            SetSelectionVisible(!active && Session.IsLoggedIn && !Session.IsInWorld);
        }

        public void PlayUiSound(string cue) => audio?.PlayUi(cue);

        public void PlayCreationMusic() => audio?.PlayCreator();
        public void StopCreationMusic() => audio?.StopCreator();
        public void ResumeSelectionMusic() => audio?.SetMode(RetrobutionAudioController.Mode.Lobby);

        public void SelectRosterSlot(int slot) => SelectSlot(slot);

        void ToggleMusic()
        {
            playMusic = !playMusic;
            PlayerPrefs.SetInt("ffone.music", playMusic ? 1 : 0);
            PlayerPrefs.Save();
            ApplyOptions();
            RefreshSelectionControls();
        }

        public void ToggleFullscreen()
        {
            bool nextFullscreen = !Screen.fullScreen;
            if (preferences != null)
            {
                preferences.Open();
                preferences.Set("graphics.windowed", !nextFullscreen);
                preferences.Set("graphics.width", Screen.width);
                preferences.Set("graphics.height", Screen.height);
                preferences.Apply();
            }
            // A launcher can override the actual mode while the saved value already
            // matches the requested one, so the graphics change event may be a no-op.
            Screen.SetResolution(Screen.width, Screen.height,
                nextFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            RefreshSelectionControls(nextFullscreen);
        }

        void Quit()
        {
            if (busy) return;
            Session.Dispose();
            Session = new FFOne.Network.FusionClient();
            ShowLogin();
        }

    }
}
