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
        readonly VisualElement host, overlay, window, popup;
        readonly Dictionary<TextElement, string> captions = new();
        readonly Dictionary<string, string> labelKeys;
        readonly List<Action> refreshers = new();
        readonly string[] tabs = { "graphics", "game-ui", "social", "controls" };
        readonly Rect[] normal = { new(8,19,275,44), new(330,21,188,32), new(560,21,189,33), new(798,19,196,45) };
        readonly Rect[] selected = { new(8,17,271,49), new(329,19,192,44), new(557,20,197,44), new(799,3,194,61) };
        public SettingsPreferences Preferences { get; }
        public bool Visible => overlay.style.display != DisplayStyle.None;
        public event Action<string, string> LocaleChanged;
        public event Action Closed;
        public event Action<int> UnignoreRequested;
        int blockedSelection = -1;
        bool modalOpen;

        public SettingsWindow(VisualElement host, SettingsPreferences preferences)
        {
            this.host = host; Preferences = preferences;
            var template = FFOne.Content.ContentAssets.Load<VisualTreeAsset>("ui/views/Settings");
            if (!template) throw new InvalidOperationException("Missing native Settings.uxml.");
            var tree = template.CloneTree();
            overlay = tree.Q("settings-overlay"); overlay.RemoveFromHierarchy(); host.Add(overlay);
            // Style sheets live on TemplateContainer; retain them when attaching the standalone modal.
            overlay.styleSheets.Add(FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/FFTheme"));
            overlay.styleSheets.Add(FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/Settings"));
            window = overlay.Q("settings-window"); popup = window.Q("settings-popup-layer");
            labelKeys = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,string>>(NativeData.Read("ui/option-labels"));
            window.Query<TextElement>().ForEach(e => { if (!string.IsNullOrEmpty(e.text)) captions[e] = e.text; });
            for (int i = 0; i < tabs.Length; ++i) { int tab = i; B("tab-" + tabs[i]).clicked += () => SelectTab(tab); }
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
            B("unignore").clicked += () => { if (blockedSelection >= 0) UnignoreRequested?.Invoke(blockedSelection); };
            host.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            overlay.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            overlay.RegisterCallback<PointerDownEvent>(OnPointer, TrickleDown.TrickleDown);
            overlay.focusable = true;
            overlay.style.display = DisplayStyle.None;
            SelectTab(0); Refresh();
        }
        public void Open()
        {
            Preferences.Open(); overlay.style.display = DisplayStyle.Flex; overlay.BringToFront();
            SelectTab(0); Refresh(); Layout(); overlay.Focus();
        }
        public void Close() { if (!Visible) return; Execute(() => { Preferences.Cancel(); Hide(); }); }
        void Hide() { DismissPopup(); EndCapture(); overlay.style.display = DisplayStyle.None; Closed?.Invoke(); }
        public void Dispose() { host.UnregisterCallback<GeometryChangedEvent>(OnGeometry); overlay.RemoveFromHierarchy(); }
        void OnGeometry(GeometryChangedEvent _) => Layout();
        void Layout()
        {
            float width = host.resolvedStyle.width, height = host.resolvedStyle.height;
            if (!(width > 0 && height > 0)) return;
            float scale = Mathf.Min(1, width / 1020, height / 638);
            window.style.transformOrigin = new TransformOrigin(0, 0, 0);
            window.style.scale = new Scale(new Vector3(scale, scale, 1));
            window.style.left = (width - 1020 * scale) / 2; window.style.top = (height - 638 * scale) / 2;
        }
        void SelectTab(int selectedTab)
        {
            if (modalOpen) return; EndCapture();
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = B("tab-" + tabs[i]); tab.EnableInClassList("selected", i == selectedTab);
                SetRect(tab, i == selectedTab ? selected[i] : normal[i]);
                window.Q("page-" + tabs[i]).style.display = i == selectedTab ? DisplayStyle.Flex : DisplayStyle.None;
                tab.PlaceBehind(window.Q("settings-frame"));
            }
            B("tab-" + tabs[selectedTab]).BringToFront(); popup.BringToFront();
        }
        void BindRadio(Button button)
        {
            bool enabled = button.name.EndsWith("-on", StringComparison.Ordinal);
            string key = button.name.Substring(0, button.name.Length - (enabled ? 3 : 4));
            bool inverse = key == "display.new_chat";
            button.clicked += () => Execute(() =>
            {
                bool value = inverse ? !enabled : enabled;
                if (key.StartsWith("graphics.")) SetGraphics(key, value); else Preferences.Set(key, value);
                Refresh();
                if (inverse) Message(T("ui.option.popup.restart_required", "Restart the game to apply the chat change."));
            });
            refreshers.Add(() => button.EnableInClassList("selected", (inverse ? !Preferences.Get<bool>(key) : Preferences.Get<bool>(key)) == enabled));
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
            var shield = new VisualElement(); shield.style.position = Position.Absolute;
            shield.style.left = shield.style.top = shield.style.right = shield.style.bottom = 0;
            shield.RegisterCallback<PointerDownEvent>(e => { if (e.target == shield) DismissPopup(); }); popup.Add(shield);
            var panel = new VisualElement(); panel.AddToClassList("option-choices");
            var position = window.WorldToLocal(button.worldBound.position);
            float listHeight = Mathf.Min(322, choices.Length * 23);
            panel.style.left = position.x; panel.style.top = Mathf.Max(0, Mathf.Min(position.y + button.resolvedStyle.height, 638 - listHeight - 8));
            panel.style.width = button.resolvedStyle.width; shield.Add(panel);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.maxHeight = listHeight;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(list);
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i; var option = new Button(() => { DismissPopup(); choose(index); }) { text = Local(choices[i]) };
                option.AddToClassList("ff-button"); option.AddToClassList("option-choice");
                option.style.flexShrink = 0; list.Add(option);
            }
        }
        void DismissPopup() { popup.Clear(); modalOpen = false; }
        void Message(string message)
        {
            DismissPopup(); modalOpen = true;
            var shield = new VisualElement(); SetRect(shield, new Rect(0,0,1020,638)); popup.Add(shield);
            var box = new VisualElement(); box.AddToClassList("option-small"); SetRect(box,new Rect(285,210,450,210)); shield.Add(box);
            var label = new Label(message); label.AddToClassList("option-description"); SetRect(label,new Rect(35,35,380,100)); box.Add(label);
            var ok = new Button(DismissPopup) { text = "OK" }; ok.AddToClassList("ff-button"); ok.AddToClassList("option-button"); SetRect(ok,new Rect(135,155,180,25)); box.Add(ok);
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
        Button B(string name) => window.Q<Button>(name);
        static void SetRect(VisualElement e, Rect rect) { e.style.position = Position.Absolute; e.style.left = rect.x; e.style.top = rect.y; e.style.width = rect.width; e.style.height = rect.height; }
        public void SetBlockedPlayers(IEnumerable<(int Slot, string Name)> players)
        {
            var list = window.Q<ScrollView>("blocked-players"); list.Clear(); blockedSelection = -1;
            foreach (var player in players.Where(p => p.Slot >= 0 && p.Slot < 50).Take(50))
            {
                int slot = player.Slot; var row = new Button { text = player.Name }; row.AddToClassList("ff-button"); row.AddToClassList("option-binding"); row.style.height = 16;
                row.clicked += () => { blockedSelection = slot; list.Query<Button>().ForEach(b => b.style.backgroundImage = new StyleBackground((Texture2D)null)); row.style.backgroundImage = new StyleBackground(FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/option/row-selected")); };
                list.Add(row);
            }
        }
    }
}
