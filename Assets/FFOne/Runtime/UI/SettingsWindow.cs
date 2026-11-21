using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Editable OptionMode window. Gameplay adapters consume Preferences.Committed.</summary>
    public sealed partial class SettingsWindow : IDisposable
    {
        readonly VisualElement host, overlay, window, popup, message;
        readonly Label messageText;
        readonly Button messageOk;
        readonly Button[] tabButtons = new Button[4];
        readonly VisualElement[] pages = new VisualElement[4];
        VisualElement dropdown, popupFocus;
        readonly Dictionary<TextElement, string> captions = new();
        readonly Dictionary<string, string> labelKeys;
        readonly List<Action> refreshers = new();
        readonly string[] tabs = { "graphics", "game-ui", "social", "controls" };
        public SettingsPreferences Preferences { get; }
        public bool Visible => overlay.ClassListContains("option-overlay--open");
        public event Action<string, string> LocaleChanged;
        public event Action Closed;
        public event Action<int> UnignoreRequested;
        int blockedSelection = -1;
        bool modalOpen;

        public SettingsWindow(VisualElement host, SettingsPreferences preferences)
        {
            this.host = host; Preferences = preferences;
            overlay = FFView.InstantiateClass("ui/views/Settings", "option-overlay");
            window = FFView.RequireClass<VisualElement>(overlay, "option-window");
            popup = FFView.RequireClass<VisualElement>(window, "option-popup-layer");
            message = FFView.RequireClass<VisualElement>(popup, "option-popup-message");
            messageText = FFView.RequireClass<Label>(message, "option-message-text");
            messageOk = FFView.RequireClass<Button>(message, "option-message-ok");
            messageOk.clicked += DismissPopup;
            host.Add(overlay);
            labelKeys = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,string>>(NativeData.Read("ui/option-labels"));
            window.Query<TextElement>().ForEach(e => { if (!string.IsNullOrEmpty(e.text)) captions[e] = e.text; });
            for (int i = 0; i < tabs.Length; ++i)
            {
                int tab = i;
                tabButtons[i] = FFView.RequireClass<Button>(window, "tab-" + tabs[i]);
                pages[i] = FFView.RequireClass<VisualElement>(window, "option-page-" + tabs[i]);
                tabButtons[i].clicked += () => SelectTab(tab);
            }
            B("settings-close").clicked += Close;
            B("settings-apply").clicked += () => Execute(() => { Preferences.Apply(); Refresh(); });
            B("settings-save").clicked += () => Execute(() => { Preferences.Apply(); Hide(); });
            foreach (string group in new[] { "graphics", "sound", "display", "colors", "social", "input" })
            { string captured = group; B("default-" + group).clicked += () => Execute(() => { Preferences.Reset(captured); Refresh(); }); }
            window.Query<Button>(className: "option-radio").ForEach(BindRadio);
            window.Query<Slider>(className: "option-slider").ForEach(BindSlider);
            BindChoice("detail", () => new[] { "BestQuality", "GoodQuality", "Balanced", "GoodPerformance", "BestPerformance" },
                () => Preferences.Get<int>("graphics.detail"), i => Preferences.Preset(i), "Custom");
            BindChoice("shadow", () => new[] { "PlayerOnly", "AllCharacters" }, () => Preferences.Get<int>("graphics.shadow"), i => SetGraphics("graphics.shadow", i));
            BindChoice("texture", () => new[] { "High", "Medium", "Low" }, () => Preferences.Get<int>("graphics.texture"), i => SetGraphics("graphics.texture", i));
            BindChoice("pad-profile", () => new[] { "XBox 360", "RumblePad 2", "PS2" }, () => Preferences.Get<int>("input.pad_profile"), i => Preferences.Set("input.pad_profile", i));
            foreach (var pair in new[] { ("text-locale", "text_locale"), ("voice-locale", "voice_locale") })
            {
                var key = pair.Item2;
                BindChoice(pair.Item1, () => new[] { "ENGLISH", "RUSSIAN" }, () => Preferences.Get<string>(key) == "ru" ? 1 : 0,
                    i => { var locale = i == 1 ? "ru" : "en"; Preferences.SetLocale(key, locale); LocaleChanged?.Invoke(key, locale); });
            }
            BindResolution(); BindPalettes(); BindMappings();
            B("unignore").SetEnabled(false);
            B("unignore").clicked += () => { if (blockedSelection >= 0) UnignoreRequested?.Invoke(blockedSelection); };
            host.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            overlay.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            SelectTab(0); Refresh();
        }
        public void Open()
        {
            Preferences.Open(); overlay.AddToClassList("option-overlay--open"); overlay.BringToFront();
            SelectTab(0); Refresh(); Layout(); overlay.Focus();
        }
        public void Close() { if (!Visible) return; Execute(() => { Preferences.Cancel(); Hide(); }); }
        void Hide() { DismissPopup(); EndCapture(); overlay.RemoveFromClassList("option-overlay--open"); Closed?.Invoke(); }
        public void Dispose()
        {
            EndCapture();
            host.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            overlay.RemoveFromHierarchy();
        }
        void OnGeometry(GeometryChangedEvent _) => Layout();
        internal void Layout()
        {
            float width = host.resolvedStyle.width, height = host.resolvedStyle.height;
            if (!(width > 0 && height > 0)) return;
            float scale = Mathf.Min(FrontendLayout.UiScale(height), width / 1020, height / 638);
            window.style.scale = new Scale(new Vector3(scale, scale, 1));
            window.style.left = (width - 1020 * scale) / 2; window.style.top = (height - 638 * scale) / 2;
        }
        void SelectTab(int selectedTab)
        {
            if (modalOpen) return; EndCapture();
            var frame = FFView.RequireClass<VisualElement>(window, "option-frame");
            for (int i = 0; i < tabs.Length; i++)
            {
                bool active = i == selectedTab;
                tabButtons[i].EnableInClassList("selected", active);
                pages[i].EnableInClassList("option-page--selected", active);
                tabButtons[i].PlaceBehind(frame);
            }
            tabButtons[selectedTab].PlaceBehind(popup);
        }
        void BindRadio(Button button)
        {
            bool enabled = button.name.EndsWith("-on", StringComparison.Ordinal);
            string key = button.name.Substring(0, button.name.Length - (enabled ? 3 : 4));
            button.clicked += () => Execute(() =>
            {
                if (key.StartsWith("graphics.")) SetGraphics(key, enabled); else Preferences.Set(key, enabled);
                Refresh();
            });
            refreshers.Add(() => button.EnableInClassList("selected", Preferences.Get<bool>(key) == enabled));
        }
        void BindSlider(Slider slider)
        {
            string key = slider.name;
            slider.RegisterValueChangedCallback(e => Execute(() =>
            {
                int step = Mathf.RoundToInt(e.newValue);
                if (key.StartsWith("graphics.")) SetGraphics(key, step); else Preferences.Set(key, step);
                Refresh();
            }));
            refreshers.Add(() => slider.SetValueWithoutNotify(Preferences.Get<float>(key)));
        }
        void SetGraphics(string key, object value) { Preferences.Set(key, value); Preferences.Set("graphics.detail", 5); }
        void BindChoice(string name, Func<string[]> choices, Func<int> selection, Action<int> changed, string fallback = "")
        {
            var button = B(name);
            refreshers.Add(() => { var list = choices(); int index = selection(); button.text = Local(index >= 0 && index < list.Length ? list[index] : fallback); });
            button.clicked += () => Choose(button, choices(), i => Execute(() => { changed(i); Refresh(); }));
        }
        void BindResolution()
        {
            var modes = Screen.resolutions.Select(r => (r.width, r.height))
                .Concat(new[] { (1920, 1080), (1920, 1200),
                    (Preferences.Get<int>("graphics.width"), Preferences.Get<int>("graphics.height")) })
                .Distinct().OrderBy(m => m.Item1).ThenBy(m => m.Item2).ToArray();
            var button = B("resolution");
            refreshers.Add(() => button.text = $"{Preferences.Get<int>("graphics.width")} x {Preferences.Get<int>("graphics.height")}" +
                (Preferences.Get<bool>("graphics.windowed") ? " (" + Local("Browser") + ")" : ""));
            button.clicked += () => Choose(button, new[] { Local("Browser") }
                .Concat(modes.Select(m => $"{m.Item1} x {m.Item2}")).ToArray(), i =>
            {
                Preferences.Set("graphics.windowed", i == 0);
                if (i > 0) { Preferences.Set("graphics.width", modes[i-1].Item1); Preferences.Set("graphics.height", modes[i-1].Item2); }
                Refresh();
            });
        }
        void Choose(Button button, string[] choices, Action<int> choose)
        {
            if (modalOpen) return; EndCapture(); modalOpen = true;
            popupFocus = button;
            var shield = dropdown = new VisualElement();
            shield.AddToClassList("option-modal-shield");
            shield.RegisterCallback<PointerDownEvent>(e => { if (e.target == shield) DismissPopup(); });
            popup.Add(shield);
            var panel = new VisualElement(); panel.AddToClassList("option-choices");
            var position = window.WorldToLocal(button.worldBound.position);
            float listHeight = Mathf.Min(322, choices.Length * 23);
            panel.style.left = position.x; panel.style.top = Mathf.Max(0, Mathf.Min(position.y + button.resolvedStyle.height, 638 - listHeight - 8));
            panel.style.width = button.resolvedStyle.width; shield.Add(panel);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("option-choices-list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(list);
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i; var option = new Button(() => { DismissPopup(); choose(index); }) { text = Local(choices[i]) };
                option.AddToClassList("ff-button"); option.AddToClassList("option-choice");
                list.Add(option);
            }
        }
        void DismissPopup()
        {
            dropdown?.RemoveFromHierarchy();
            dropdown = null;
            message.RemoveFromClassList("option-popup-message--open");
            modalOpen = false;
            popupFocus?.Focus();
            popupFocus = null;
        }
        void Message(string text)
        {
            DismissPopup();
            popupFocus = window.panel?.focusController.focusedElement as VisualElement;
            modalOpen = true;
            messageText.text = text;
            message.AddToClassList("option-popup-message--open");
            messageOk.Focus();
        }
        void Execute(Action action) { try { action(); } catch (Exception e) { Message(e.Message); Debug.LogException(e); } }
        void Refresh()
        {
            overlay.EnableInClassList("option-ru", Preferences.Get<string>("text_locale") == "ru");
            foreach (var caption in captions) caption.Key.text = Local(caption.Value);
            foreach (var refresh in refreshers) refresh();
            B("settings-apply").SetEnabled(!Preferences.Clean);
        }
        string Local(string source) => labelKeys.TryGetValue(source, out var key) ? T(key, source) : source;
        string T(string key, string fallback) { var value = LocalizationCatalog.Lookup(key, Preferences.Get<string>("text_locale")); return value == key ? fallback : value; }
        Button B(string role) => FFView.RequireClass<Button>(window, "option-role-" + role);
        public void SetBlockedPlayers(IEnumerable<(int Slot, string Name)> players)
        {
            var list = FFView.RequireClass<ScrollView>(window, "option-role-blocked-players");
            list.Clear();
            blockedSelection = -1;
            B("unignore").SetEnabled(false);
            foreach (var player in players.Where(p => p.Slot >= 0 && p.Slot < 50).Take(50))
            {
                int slot = player.Slot;
                var row = new Button { text = player.Name, enableRichText = false };
                row.AddToClassList("ff-button");
                row.AddToClassList("option-binding");
                row.AddToClassList("option-blocked-player");
                row.clicked += () => {
                    blockedSelection = slot;
                    B("unignore").SetEnabled(true);
                    list.Query<Button>(className: "option-blocked-player")
                        .ForEach(button => button.EnableInClassList("option-blocked-player--selected", button == row));
                };
                list.Add(row);
            }
        }
    }
}
