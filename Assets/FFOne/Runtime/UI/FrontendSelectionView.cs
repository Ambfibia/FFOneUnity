using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        void BuildSelection()
        {
            selectionRoot = FFView.Instantiate("ui/views/CharacterSelection", "character-selection");
            Root.Add(selectionRoot);
            TElement Find<TElement>(string name) where TElement : VisualElement => FFView.Require<TElement>(selectionRoot, name);
            Image Art(string name, string address)
            {
                var image = Find<Image>(name);
                image.image = Texture(address); image.scaleMode = ScaleMode.StretchToFill;
                return image;
            }
            Label Text(string name, string value, bool chalet = false, int size = 12)
            {
                var label = Find<Label>(name); label.text = chalet ? value : FFStyle.Caption(value);
                Font(label, chalet, size); AutoFit(label); return label;
            }
            Button ActionButton(string name, string value, string normal, string hover, Action action, int size = 12)
            {
                var button = Find<Button>(name); button.text = FFStyle.Caption(value);
                Font(button, false, size); Skin(button, normal, hover); AutoFit(button);
                button.clicked += action; return button;
            }
            for (int i = 0; i < backgrounds.Length; i++) backgrounds[i] = Art("selection-background-" + i,
                "ui/en/character/selection/backgrounds/future/CSFutureBG_" + (i + 1));
            chrome = Art("selection-chrome", "ui/en/character/selection/background/CSBG");
            avatarGroup = Find<VisualElement>("avatar-group");
            PreviewImage = Art("selected-avatar-preview", null);
            avatarName = Text("selected-avatar-name", "", true, 14);
            Hold(ActionButton("rotate-left", "", "ui/en/character/creation/camera/CCRotateLeftNormal",
                "ui/en/character/creation/camera/CCRotateLeftOver", () => {}), delta => RotatePreview?.Invoke(-100 * delta));
            Hold(ActionButton("rotate-right", "", "ui/en/character/creation/camera/CCRotateRightNormal",
                "ui/en/character/creation/camera/CCRoatateRightOver", () => {}), delta => RotatePreview?.Invoke(100 * delta));
            selectionGroup = Find<VisualElement>("selection-group");
            Text("selection-title", T("ui.character_select.title", "SELECT A CHARACTER"));
            for (int i = 0; i < 4; i++)
            {
                int slot = i + 1;
                slotButtons[i] = ActionButton("character-slot-" + slot, "", Controls + "CSCharButtonEmpty",
                    Controls + "CSCharButtonOver", () => SelectSlot(slot));
                slotButtons[i].RegisterCallback<PointerEnterEvent>(_ => HighlightSlot(slot - 1, true));
                slotButtons[i].RegisterCallback<PointerLeaveEvent>(_ => HighlightSlot(slot - 1, false));
                emptyLabels[i] = Text("empty-slot-" + slot, T("ui.character_select.empty", "EMPTY"));
                disksBack[i] = Art("disk-back-" + slot, "ui/en/gameplay/player/DiskBack");
                SlotPreviewImages[i] = Art("slot-preview-" + slot, null);
                disksFront[i] = Art("disk-front-" + slot, "ui/en/gameplay/player/DiskFront");
                slotNames[i] = Text("character-name-" + slot, "", true);
                slotLevels[i] = Text("character-level-" + slot, "", true);
                slotLocations[i] = Text("character-location-" + slot, "", true);
                slotLevels[i].style.color = slotLocations[i].style.color = new Color(0, .8f, 1);
            }
            delete = ActionButton("delete-character", T("ui.character_select.delete", "DELETE CHARACTER"),
                Controls + "red_button_normal", Controls + "red_button_over", DeleteDialog);
            create = ActionButton("create-character", T("ui.character_select.create", "CREATE CHARACTER"),
                Controls + "blue_button_normal", Controls + "blue_button_over", () => RequestCreate());
            enter = ActionButton("enter-world", T("ui.character_select.enter", "ENTER THE GAME"),
                Controls + "CSEnterButton", Controls + "CSEnterGameOver", EnterWorld, 14);
            musicButton = ActionButton("music-toggle", "", MusicTexture(), MusicTexture(), ToggleMusic);
            quit = ActionButton("quit", T("ui.common.quit", "QUIT"), Controls + "red_button_normal", Controls + "red_button_over", Quit);
            fullscreen = ActionButton("fullscreen", "", Controls + "CSFullScreenButton", Controls + "FullscreenModeButton_Over", ToggleFullscreen);
        }

        string MusicTexture()
        {
            string path = "ui/" + language + "/character/selection/controls/" + (playMusic ? "musicoff" : "musicon");
            return Texture(path) != null ? path : Controls + (playMusic ? "musicoff" : "musicon");
        }

        public void SetCreationPresentationActive(bool active)
        {
            SetSelectionVisible(!active && Session.IsLoggedIn && !Session.IsInWorld);
            if (music == null) return;
            if (active) music.Pause();
            else if (playMusic && !Session.IsInWorld) music.UnPause();
        }

        public void SelectRosterSlot(int slot) => SelectSlot(slot);

        void ToggleMusic()
        {
            playMusic = !playMusic;
            PlayerPrefs.SetInt("ffone.music", playMusic ? 1 : 0);
            PlayerPrefs.Save();
            if (playMusic) music.Play(); else music.Pause();
            Skin(musicButton, MusicTexture(), MusicTexture());
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
            string texture = Controls + (nextFullscreen ? "CSFullScreenButton" : "CSFullToWindow");
            Skin(fullscreen, texture, Controls + "FullscreenModeButton_Over");
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
