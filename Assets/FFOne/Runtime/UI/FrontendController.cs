using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class FrontendController : MonoBehaviour
    {
        [Header("Frontend preferences")]
        [SerializeField] string language = "ru";
        [SerializeField] bool playMusic = true;

        public FusionClient Session { get; private set; }
        public string Language => language;
        public CharacterInfo SelectedCharacter { get; private set; }
        public VisualElement Root { get; private set; }
        public Image PreviewImage { get; private set; }
        public Image[] SlotPreviewImages { get; } = new Image[4];
        public event Action<CharacterInfo> CharacterSelected;
        public event Action<IReadOnlyList<CharacterInfo>> RosterChanged;
        public event Action<float> RotatePreview;
        public event Action<WorldEntry> WorldEntered;
        public event Action<NetworkEvent> NetworkEventReceived;
        public event Action<int> CreateCharacterRequested;

        CancellationTokenSource lifetime;
        AudioSource music;
        PanelSettings ownedPanel;
        VisualElement loginRoot, selectionRoot, selectionGroup, avatarGroup, loginPanel, modal;
        Image loginBackground, chrome;
        readonly Image[] backgrounds = new Image[10];
        readonly Button[] slotButtons = new Button[4];
        readonly Label[] slotNames = new Label[4], slotLevels = new Label[4], slotLocations = new Label[4], emptyLabels = new Label[4];
        readonly Image[] disksBack = new Image[4], disksFront = new Image[4];
        TextField username, password;
        Button submit, create, delete, enter, musicButton, fullscreen, quit;
        Label avatarName, status;
        bool busy, showingSelection;
        float selectionTime;
        float viewportWidth, uiScale = 1, characterScale = 1;
        string statusText = "";
        string currentBackground = "future/CSFutureBG_";

        void Awake()
        {
            lifetime = new CancellationTokenSource();
            Session = new FusionClient();
            InitializeProfiles();
        }

        // Addressables may need the scene's asynchronous activation to finish.
        // Start runs after every Awake; dependent frontend components start later.
        void Start()
        {
            language = PlayerPrefs.GetString("ffone.text-language", language);
            playMusic = PlayerPrefs.GetInt("ffone.music", playMusic ? 1 : 0) == 1;
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                ownedPanel.name = "FFOne Runtime Panel";
                ownedPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                document.panelSettings = ownedPanel;
            }
            Root = document.rootVisualElement;
            document.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            document.panelSettings.scale = 1;
            Root.Clear();
            Root.name = "ffone-frontend";
            Root.AddToClassList("frontend-root");
            Root.styleSheets.Add(FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/FFTheme"));
            var styles = FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/Frontend");
            if (styles != null) Root.styleSheets.Add(styles);
            FFWidgets.Font(Root);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            BuildLogin();
            BuildSelection();
            status = FFWidgets.Label(Root, "connection-status", "", 0, 0, 580, 60, true, 14);
            status.AddToClassList("frontend-status");
            status.style.display = DisplayStyle.None;
            Root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            music = gameObject.AddComponent<AudioSource>();
            music.clip = FFOne.Content.ContentAssets.Load<AudioClip>("audio/music/00_main_theme");
            music.loop = true;
            music.volume = 0.5f;
            if (playMusic && music.clip != null) music.Play();
            InitializeOptions();
            ShowLogin();
        }

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.f10Key.wasPressedThisFrame == true && !networkWorld) OpenSettings();
            if (!showingSelection && profiles != null && Time.unscaledTime >= nextServerProbe) RefreshServerHealth();
            while (Session != null && Session.TryDequeueEvent(out var networkEvent))
            {
                if (DeferGameplayEvent(networkEvent)) continue;
                NetworkEventReceived?.Invoke(networkEvent);
                if (networkEvent.PlayerPosition != null && networkWorld)
                {
                    var position = networkEvent.PlayerPosition;
                    try { ApplyServerRelocation(gameplayState?.Snapshot.MapNumber ?? 0, position.X, position.Y, position.Z); }
                    catch (ProtocolException failure) { ShowError(failure.Message); }
                }
                HandleWorldDisconnect(networkEvent);
                if (networkEvent.Kind == NetworkEventKind.Disconnected && !busy)
                {
                    Session.Dispose();
                    Session = new FusionClient();
                    ShowLogin();
                    SetBusy(false);
                    ShowError(networkEvent.Message);
                }
            }
            TickWorldFlow();
            TickGameplayUi();
            if (!showingSelection) return;
            selectionTime += Time.unscaledDeltaTime;
            for (int i = 0; i < backgrounds.Length; i++) backgrounds[i].style.left = viewportWidth / 2 + (FrontendLayout.BackgroundX(i, selectionTime) - viewportWidth / 2) * characterScale;
        }

        void OnDestroy()
        {
            if (preferences != null) preferences.Changed -= ApplyOptions;
            settings?.Dispose();
            DisposeWorldFlow();
            lifetime?.Cancel();
            lifetime?.Dispose();
            Session?.Dispose();
            if (ownedPanel != null) Destroy(ownedPanel);
        }

        public void ShowLogin()
        {
            showingSelection = false;
            loginRoot.style.display = DisplayStyle.Flex;
            selectionRoot.style.display = DisplayStyle.None;
            SelectedCharacter = null;
            CharacterSelected?.Invoke(null);
            CloseModal();
            SetStatus("");
            Layout();
            username.Focus();
            BuildAccountControls();
        }

        public void ShowSelection()
        {
            showingSelection = true;
            loginRoot.style.display = DisplayStyle.None;
            selectionRoot.style.display = DisplayStyle.Flex;
            CloseModal();
            SetStatus("");
            RefreshRoster();
            Layout();
        }

        public void SetSelectionVisible(bool visible) => selectionRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        public string Text(string key, string fallback) => T(key, fallback);
        public void RefreshLayout() => Layout();

        public void RefreshRoster()
        {
            var characters = Session.Characters;
            long selectedUid = SelectedCharacter?.Uid ?? 0;
            SelectedCharacter = characters.FirstOrDefault(c => c.Uid == selectedUid) ?? characters.FirstOrDefault();
            for (int slot = 0; slot < 4; slot++)
            {
                var character = characters.FirstOrDefault(c => c.Slot == slot + 1);
                bool occupied = character != null;
                bool selected = character == SelectedCharacter && occupied;
                string image = occupied ? (selected ? "CSCharButtonOver" : "CSCharButtonNormal") : "CSCharButtonEmpty";
                FFStyle.Button(slotButtons[slot], FFWidgets.Controls + image, FFWidgets.Controls + (occupied ? "CSCharButtonOver" : "CSCharButtonEmpty"));
                slotButtons[slot].pickingMode = occupied ? PickingMode.Position : PickingMode.Ignore;
                slotButtons[slot].focusable = occupied;
                slotNames[slot].text = occupied ? character.DisplayName : "";
                slotNames[slot].style.color = selected ? new Color(0, .179f, .558f) : new Color(.898f, .898f, .898f);
                slotLevels[slot].text = occupied ? T("ui.character_select.level", "LEVEL {level}").Replace("{level}", character.Level.ToString()) : "";
                slotLocations[slot].text = occupied ? CharacterLocations.Describe(character, language, selected) : "";
                slotLevels[slot].style.color = slotLocations[slot].style.color = selected ? new Color(0, .179f, .558f) : new Color(0, .8f, 1);
                emptyLabels[slot].style.display = occupied ? DisplayStyle.None : DisplayStyle.Flex;
                disksBack[slot].style.display = disksFront[slot].style.display = DisplayStyle.Flex;
                SlotPreviewImages[slot].style.display = occupied ? DisplayStyle.Flex : DisplayStyle.None;
            }
            avatarName.text = SelectedCharacter?.DisplayName ?? "";
            string background = SelectedCharacter == null ? "future/CSFutureBG_" : CharacterLocations.Resolve(SelectedCharacter.X, SelectedCharacter.Y)?.Background ?? "future/CSFutureBG_";
            if (background != currentBackground)
            {
                currentBackground = background;
                for (int i = 0; i < backgrounds.Length; i++) backgrounds[i].image = FFWidgets.Texture("ui/en/character/selection/backgrounds/" + background + (i + 1));
            }
            SetBusy(busy);
            RosterChanged?.Invoke(characters);
            CharacterSelected?.Invoke(SelectedCharacter);
        }

        void HighlightSlot(int slot, bool hover)
        {
            var character = Session.Characters.FirstOrDefault(c => c.Slot == slot + 1);
            if (character == null) return;
            bool selected = hover || SelectedCharacter == character;
            slotNames[slot].style.color = selected ? new Color(0, .179f, .558f) : new Color(.898f, .898f, .898f);
            slotLevels[slot].style.color = slotLocations[slot].style.color = selected ? new Color(0, .179f, .558f) : new Color(0, .8f, 1);
            slotLocations[slot].text = CharacterLocations.Describe(character, language, selected);
        }

        void SelectSlot(int slot)
        {
            if (busy) return;
            var character = Session.Characters.FirstOrDefault(c => c.Slot == slot);
            if (character == null) return;
            SelectedCharacter = character;
            RefreshRoster();
        }

        void RequestCreate(int slot = 0)
        {
            if (busy) return;
            if (slot == 0) slot = Enumerable.Range(1, 4).FirstOrDefault(i => !Session.Characters.Any(c => c.Slot == i));
            if (slot == 0) return;
            if (CreateCharacterRequested != null) CreateCharacterRequested(slot);
            else ShowError(language == "ru" ? "Редактор персонажа ещё не подключён." : "Character creator is not connected yet.");
        }

        async void Login()
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(username.value) || (savedAccount == null && string.IsNullOrEmpty(password.value)))
            { ShowError(language == "ru" ? "Введите логин и пароль." : "Enter your username and password."); return; }
            string user = username.value.Trim(), secret = password.value;
            var server = profiles.Current;
            bool saved = savedAccount != null, remember = rememberAccount;
            await RunOperation(async () => {
                if (saved) secret = profiles.ReadSecret(user);
                if (server.Api != null)
                {
                    var accepted = await Session.LoginWithLauncherCredentialAsync(server.Api, user, secret, saved, lifetime.Token);
                    user = accepted.Username; secret = accepted.RefreshToken;
                }
                else await Session.LoginAsync(server.Endpoint.Host, server.Endpoint.Port, user, secret, lifetime.Token);
                if (this == null || lifetime.IsCancellationRequested) return;
                if (remember)
                {
                    try { profiles.RememberAccepted(server, user, secret); savedAccount = user; browserMessage = ""; }
                    catch (Exception error) { browserMessage = error.Message; }
                }
                password.value = "";
                BuildAccountControls();
                ShowSelection();
            }, T("ui.login.connecting", "CONNECTING..."));
        }

        async void EnterWorld()
        {
            if (busy || SelectedCharacter == null) return;
            if (!SelectedCharacter.Style.AppearanceComplete)
            {
                if (CreateCharacterRequested != null) CreateCharacterRequested(SelectedCharacter.Slot);
                return;
            }
            await RunOperation(EnterSelectedWorldAsync, language == "ru" ? "ПОДКЛЮЧЕНИЕ К МИРУ..." : "CONNECTING TO WORLD...");
        }

        async Task RunOperation(Func<Task> operation, string progress)
        {
            SetBusy(true);
            SetStatus(progress);
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (this != null) ShowError(error.Message); }
            finally { if (this != null) SetBusy(false); }
        }

        void SetBusy(bool value)
        {
            busy = value;
            submit?.SetEnabled(!value);
            username?.SetEnabled(!value);
            password?.SetEnabled(!value);
            browserPanel?.SetEnabled(!value);
            accountControls?.SetEnabled(!value);
            loginRoot?.Q<Button>("login-language")?.SetEnabled(!value);
            for (int i = 0; i < 4; i++) slotButtons[i]?.SetEnabled(!value);
            create?.SetEnabled(!value && Session.Characters.Count < 4 && !Session.IsInWorld);
            delete?.SetEnabled(!value && SelectedCharacter != null && !Session.IsInWorld);
            enter?.SetEnabled(!value && SelectedCharacter != null && !Session.IsInWorld);
            if (create != null) create.style.display = Session.Characters.Count < 4 ? DisplayStyle.Flex : DisplayStyle.None;
            if (delete != null) delete.style.display = SelectedCharacter != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (enter != null) enter.style.display = SelectedCharacter != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowError(string message) => SetStatus(message);
        void SetStatus(string message)
        {
            statusText = message ?? "";
            if (status == null) return;
            status.text = statusText;
            status.style.display = statusText.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        internal string T(string key, string fallback)
        {
            string value = LocalizationCatalog.Lookup(key, language);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }

        void SwitchLanguage()
        {
            if (busy) return;
            var next = language == "ru" ? "en" : "ru";
            if (preferences != null) preferences.SetLocale("text_locale", next);
            else SetLanguage(next);
        }

        void SetLanguage(string next)
        {
            language = next;
            PlayerPrefs.SetString("ffone.text-language", language);
            PlayerPrefs.Save();
            string user = username.value, secret = password.value;
            loginRoot.RemoveFromHierarchy();
            BuildLogin();
            loginRoot.style.display = showingSelection || worldLifetime != null ? DisplayStyle.None : DisplayStyle.Flex;
            username.value = user;
            password.value = secret;
            selectionGroup.Q<Label>("selection-title").text = FFStyle.Caption(T("ui.character_select.title", "SELECT A CHARACTER"));
            for (int i = 0; i < 4; i++) emptyLabels[i].text = FFStyle.Caption(T("ui.character_select.empty", "EMPTY"));
            create.text = FFStyle.Caption(T("ui.character_select.create", "CREATE CHARACTER"));
            delete.text = FFStyle.Caption(T("ui.character_select.delete", "DELETE CHARACTER"));
            enter.text = FFStyle.Caption(T("ui.character_select.enter", "ENTER THE GAME"));
            quit.text = FFStyle.Caption(T("ui.common.quit", "QUIT"));
            FFStyle.Button(musicButton, MusicTexture(), MusicTexture());
            status.BringToFront();
            if (showingSelection) RefreshRoster();
            Layout();
        }

        void OnKey(KeyDownEvent evt)
        {
            if (settings?.Visible == true || worldMenu != null) return;
            if (evt.keyCode == KeyCode.Escape && modal != null) { CloseModal(); evt.StopPropagation(); }
            else if (evt.keyCode == KeyCode.Return && modal == null && loginRoot.style.display != DisplayStyle.None && !addingServer) { Login(); evt.StopPropagation(); }
        }

        void Layout()
        {
            if (Root == null) return;
            float width = Root.resolvedStyle.width, height = Root.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 0) width = Screen.width;
            if (float.IsNaN(height) || height <= 0) height = Screen.height;
            viewportWidth = width; uiScale = FrontendLayout.UiScale(height); characterScale = FrontendLayout.CharacterScale(width, height);
            var center = new Vector2(width / 2, height / 2);
            FFWidgets.ScaledRect(loginPanel, FrontendLayout.Login(width, height), uiScale, center);
            if (browserPanel != null)
            {
                float left = width / 2 + 185 * uiScale + 16, panelHeight = BrowserHeight();
                float fit = Mathf.Max(.1f, Mathf.Min(uiScale, (width - left - 16) / 280, (height - 24) / panelHeight));
                FFWidgets.Rect(browserPanel, left, (height - 299 * uiScale) / 2, 280, panelHeight);
                browserPanel.style.transformOrigin = new TransformOrigin(0, 0);
                browserPanel.style.scale = new Scale(new Vector3(fit, fit, 1));
            }
            float backgroundScale = Mathf.Max(width / 1920, height / 1080);
            FFWidgets.Rect(loginBackground, (width - 1920 * backgroundScale) / 2,
                (height - 1080 * backgroundScale) / 2, 1920 * backgroundScale, 1080 * backgroundScale);
            if (selectionGroup == null) return;
            FFWidgets.ScaledRect(selectionGroup, FrontendLayout.Selection(width, height), characterScale, center);
            FFWidgets.ScaledRect(avatarGroup, FrontendLayout.Avatar(width, height), characterScale, center);
            FFWidgets.ScaledRect(chrome, FrontendLayout.Chrome(width, height), characterScale, center);
            for (int i = 0; i < backgrounds.Length; i++) FFWidgets.ScaledRect(backgrounds[i],
                new Rect(FrontendLayout.BackgroundX(i, selectionTime), (height - 1440) / 2 + 418, 542, 477), characterScale, center);
            FFWidgets.ScaledRect(quit, new Rect(20, height - 38, 90, 23), characterScale, new Vector2(0, height));
            FFWidgets.ScaledRect(fullscreen, new Rect(width - 45, height - 35, 35, 31), characterScale, new Vector2(width, height));
            if (status != null) FFWidgets.Rect(status, (width - 580) / 2, height - 68, 580, 58);
            if (modal?.Q("modal-panel") is VisualElement dialog) LayoutModal(dialog, width, height);
            FrontendLayoutOverrides.Apply(Root);
            FFWidgets.FitTree(Root);
        }

        void CloseModal() { modal?.RemoveFromHierarchy(); modal = null; }
        VisualElement ModalPanel(float width, float height, string texture = null)
        {
            CloseModal();
            modal = new VisualElement { name = "frontend-modal" };
            modal.AddToClassList("frontend-modal");
            Root.Add(modal);
            var panel = new VisualElement { name = "modal-panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = Length.Percent(50);
            panel.style.top = Length.Percent(50);
            panel.style.marginLeft = -width / 2;
            panel.style.marginTop = -height / 2;
            panel.style.width = width;
            panel.style.height = height;
            if (texture != null) FFStyle.Background(panel, texture);
            else panel.style.backgroundColor = new Color(.03f, .10f, .17f, .98f);
            modal.Add(panel);
            LayoutModal(panel, Root.resolvedStyle.width, Root.resolvedStyle.height);
            return panel;
        }

        static void LayoutModal(VisualElement panel, float width, float height)
        {
            if (!(width > 0 && height > 0)) return;
            float scale = Mathf.Min(FrontendLayout.CharacterScale(width, height),
                (width - 24) / panel.style.width.value.value, (height - 24) / panel.style.height.value.value);
            panel.style.marginLeft = panel.style.marginTop = 0;
            FFWidgets.Rect(panel, FrontendLayout.Centered(width, height,
                new Vector2(panel.style.width.value.value, panel.style.height.value.value), scale));
            panel.style.transformOrigin = new TransformOrigin(0, 0);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
        }
    }
}
