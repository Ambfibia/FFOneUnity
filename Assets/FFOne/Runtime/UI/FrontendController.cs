using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
        [SerializeField] UIDocument document;

        [Header("Frontend preferences")]
        [SerializeField] string language = "ru";
        [SerializeField] bool playMusic = true;

        public FusionClient Session { get; private set; }
        public string Language => language;
        public string VoiceLanguage => (string)preferences?.Committed["voice_locale"] ?? language;
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
        RetrobutionAudioController audio;
        PanelSettings ownedPanel;
        VisualElement loginRoot, selectionRoot, selectionGroup, avatarGroup, loginPanel, modal;
        VisualElement loginBackground;
        VisualElement chrome;
        readonly Image[] backgrounds = new Image[10];
        readonly Button[] slotButtons = new Button[4];
        readonly VisualElement[] slotRows = new VisualElement[4];
        readonly Label[] slotNames = new Label[4], slotLevels = new Label[4], slotLocations = new Label[4], emptyLabels = new Label[4];
        TextField username, password;
        Button submit, create, delete, enter, musicButton, fullscreen, quit;
        Label avatarName, avatarStatus, status;
        bool busy, showingSelection;
        float selectionTime;
        float viewportWidth, uiScale = 1, characterScale = 1;
        string statusText = "";
        string connectionErrorServerKey;
        string currentBackground = "future/CSFutureBG_";

        void Awake()
        {
            GameplayInput.TrackGamepads();
            lifetime = new CancellationTokenSource();
            Session = new FusionClient();
            InitializeProfiles();
        }

        // Addressables may need the scene's asynchronous activation to finish.
        // Start runs after every Awake; dependent frontend components start later.
        async void Start()
        {
            language = PlayerPrefs.GetString("ffone.text-language", language);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            language = UiSmokeLaunch.Locale ?? language;
#endif
            playMusic = PlayerPrefs.GetInt("ffone.music", playMusic ? 1 : 0) == 1;
            if (!document) TryGetComponent(out document);
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
            try { await InitializeFrontendAsync(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                frontendInitialized = false;
                Debug.LogException(error, this);
                if (this) ShowError(error.Message);
            }
        }

        void Update()
        {
            if (!frontendInitialized) return;
            if (UnityEngine.InputSystem.Keyboard.current?.f10Key.wasPressedThisFrame == true && !networkWorld) OpenSettings();
            TickServerHealth();
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
                if (networkEvent.Kind == NetworkEventKind.Disconnected)
                {
                    bool loginDisconnected = !networkEvent.IsShard || !Session.IsLoggedIn;
                    if (loginDisconnected) MarkServerOffline(profiles.Current);
                    if (!busy || selectionPreparing)
                    {
                        ReturnToLoginAfterDisconnect(networkEvent.Message, loginDisconnected ? profiles.Current.Key : null);
                        if (!loginDisconnected) ProbeServer(profiles.Current);
                    }
                }
            }
            TickWorldFlow();
            TickGameplayUi();
            TickGamepadNavigation();
            if (!showingSelection) return;
            if (selectionRoot.style.display != DisplayStyle.None) RefreshSelectionPreviewStatus();
            selectionTime += Time.unscaledDeltaTime;
            for (int i = 0; i < backgrounds.Length; i++) backgrounds[i].style.left = viewportWidth / 2 + (FrontendLayout.BackgroundX(i, selectionTime) - viewportWidth / 2) * characterScale;
        }

        void OnDestroy()
        {
            selectionLoading?.Cancel(); selectionLoading?.Dispose(); selectionLoading = null;
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
            audio?.SetMode(RetrobutionAudioController.Mode.Lobby);
            selectionPresentationVersion++; selectionPreparing = false;
            selectionLoading?.Cancel(); selectionLoading?.Dispose(); selectionLoading = null;
            HideLoading();
            showingSelection = false;
            loginRoot.style.display = DisplayStyle.Flex;
            selectionRoot.style.display = DisplayStyle.None;
            SelectedCharacter = null;
            CharacterSelected?.Invoke(null);
            RosterChanged?.Invoke(Array.Empty<CharacterInfo>());
            CloseModal();
            SetStatus("");
            Layout();
            BuildAccountControls();
            if (savedAccount == null) username.Focus();
            else submit.Focus();
        }

        public async void ShowSelection()
        {
            try { await ShowSelectionAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (this) ShowError(error.Message); }
        }

        public void SetSelectionVisible(bool visible) => selectionRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        public string Text(string key, string fallback) => T(key, fallback);
        public void RefreshLayout() => Layout();

        public void RefreshRoster()
        {
            var characters = Session.Characters;
            long selectedUid = SelectedCharacter?.Uid ?? 0;
            SelectedCharacter = characters.FirstOrDefault(c => selectedUid != 0 && c.Uid == selectedUid)
                ?? characters.FirstOrDefault(c => c.Slot == Session.SelectedSlot)
                ?? characters.FirstOrDefault();
            for (int slot = 0; slot < 4; slot++)
            {
                var character = characters.FirstOrDefault(c => c.Slot == slot + 1);
                bool occupied = character != null;
                bool selected = character == SelectedCharacter && occupied;
                slotRows[slot].EnableInClassList("selection-slot-occupied", occupied);
                slotRows[slot].EnableInClassList("selection-slot-selected", selected);
                slotRows[slot].EnableInClassList("selection-slot-highlight", selected);
                slotButtons[slot].pickingMode = occupied ? PickingMode.Position : PickingMode.Ignore;
                slotButtons[slot].focusable = occupied;
                slotNames[slot].text = occupied ? character.DisplayName : "";
                slotLevels[slot].text = occupied ? T("ui.character_select.level", "LEVEL {level}").Replace("{level}", character.Level.ToString()) : "";
                slotLocations[slot].text = occupied ? CharacterLocations.Describe(character, language, selected) : "";
            }
            avatarName.text = SelectedCharacter?.DisplayName ?? "";
            string background = SelectedCharacter == null ? "future/CSFutureBG_" : CharacterLocations.RosterLocation(SelectedCharacter)?.Background ?? "future/CSFutureBG_";
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
            slotRows[slot].EnableInClassList("selection-slot-highlight", selected);
            slotLocations[slot].text = CharacterLocations.Describe(character, language, selected);
        }

        void SelectSlot(int slot)
        {
            if (busy) return;
            var character = Session.Characters.FirstOrDefault(c => c.Slot == slot);
            if (character == null || character == SelectedCharacter) return;
            PlayUiSound("button");
            SelectedCharacter = character;
            RefreshRoster();
            SetStatus("");
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
                try
                {
                    if (server.Api != null)
                    {
                        var accepted = await Session.LoginWithLauncherCredentialAsync(server.Api, user, secret, saved, lifetime.Token);
                        user = accepted.Username; secret = accepted.RefreshToken;
                    }
                    else await Session.LoginAsync(server.Endpoint.Host, server.Endpoint.Port, user, secret, lifetime.Token);
                }
                catch (Exception error) when (IsLoginTransportFailure(error))
                {
                    if (this)
                    {
                        MarkServerOffline(server);
                        SetStatus(error.Message, server.Key);
                    }
                    return;
                }
                if (this == null || lifetime.IsCancellationRequested) return;
                if (remember)
                {
                    try { profiles.RememberAccepted(server, user, secret); savedAccount = user; browserMessage = ""; }
                    catch (Exception error) { browserMessage = error.Message; }
                }
                password.value = "";
                BuildAccountControls();
                UpdateServerBrowserMessage();
                await ShowSelectionAsync();
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
            var session = Session;
            bool needsAuthenticatedSession = session.IsLoggedIn || showingSelection;
            Exception failure = null;
            SetBusy(true);
            SetStatus(progress);
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { failure = error; }
            finally
            {
                if (this != null && Session == session)
                {
                    // A failed transaction can close the socket before the reader reports it.
                    if (needsAuthenticatedSession && !session.IsLoggedIn && !session.IsInWorld)
                    {
                        var server = profiles.Current;
                        bool connectionLost = failure == null || IsLoginTransportFailure(failure);
                        if (connectionLost) MarkServerOffline(server);
                        ReturnToLoginAfterDisconnect(failure?.Message ?? (language == "ru" ? "Сервер закрыл соединение." : "Server closed the connection."),
                            connectionLost ? server.Key : null);
                    }
                    else
                    {
                        if (failure != null) ShowError(failure.Message);
                        SetBusy(false);
                    }
                }
            }
        }

        static bool IsLoginTransportFailure(Exception error) => error is SocketException or EndOfStreamException or TimeoutException
            or IOException { InnerException: SocketException };

        void ReturnToLoginAfterDisconnect(string message, string serverKey)
        {
            Session.Dispose();
            Session = new FusionClient();
            ShowLogin();
            SetBusy(false);
            SetStatus(message, serverKey);
        }

        void SetBusy(bool value)
        {
            value |= selectionPreparing;
            busy = value;
            submit?.SetEnabled(!value);
            username?.SetEnabled(!value);
            password?.SetEnabled(!value);
            browserPanel?.SetEnabled(!value);
            accountControls?.SetEnabled(!value);
            loginLanguage?.SetEnabled(!value);
            for (int i = 0; i < 4; i++) slotButtons[i]?.SetEnabled(!value);
            create?.SetEnabled(!value && Session.Characters.Count < 4 && !Session.IsInWorld);
            delete?.SetEnabled(!value && SelectedCharacter != null && !Session.IsInWorld);
            enter?.SetEnabled(!value && SelectedCharacter != null && !Session.IsInWorld);
            if (create != null) create.style.display = Session.Characters.Count < 4 ? DisplayStyle.Flex : DisplayStyle.None;
            if (delete != null) delete.style.display = SelectedCharacter != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (enter != null) enter.style.display = SelectedCharacter != null ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyWorldInputGate();
        }

        public void ShowError(string message) => SetStatus(message);
        void SetStatus(string message, string serverKey = null)
        {
            statusText = message ?? "";
            connectionErrorServerKey = serverKey;
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
            selectionTitle.text = FFStyle.Caption(T("ui.character_select.title", "SELECT A CHARACTER"));
            for (int i = 0; i < 4; i++) emptyLabels[i].text = FFStyle.Caption(T("ui.character_select.empty", "EMPTY"));
            create.text = FFStyle.Caption(T("ui.character_select.create", "CREATE CHARACTER"));
            delete.text = FFStyle.Caption(T("ui.character_select.delete", "DELETE CHARACTER"));
            enter.text = FFStyle.Caption(T("ui.character_select.enter", "ENTER THE GAME"));
            quit.text = FFStyle.Caption(T("ui.common.quit", "QUIT"));
            RefreshSelectionControls();
            if (showingSelection) RefreshRoster();
            Layout();
        }

        void OnKey(KeyDownEvent evt)
        {
            if (settings?.Visible == true || worldMenu != null) return;
            if (modal != null)
            {
                if (evt.keyCode == KeyCode.Escape) { CloseModal(); evt.StopPropagation(); }
                return;
            }
            if (busy) return;
            bool submitKey = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            var target = evt.target as VisualElement;
            var targetButton = target as Button ?? target?.GetFirstAncestorOfType<Button>();
            if (loginRoot.style.display != DisplayStyle.None)
            {
                if (!submitKey || addingServer || targetButton != null) return;
                Login();
                evt.StopPropagation();
            }
            else if (selectionRoot.style.display != DisplayStyle.None && !Session.IsInWorld)
            {
                int slot = (int)evt.keyCode - (int)KeyCode.Alpha1 + 1;
                if (slot >= 1 && slot <= slotButtons.Length) SelectSlot(slot);
                else if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
                {
                    var roster = Session.Characters;
                    int count = roster.Count;
                    if (count == 0) return;
                    int current = SelectedCharacter?.Slot ?? 0;
                    var next = evt.keyCode == KeyCode.UpArrow
                        ? roster.LastOrDefault(character => character.Slot < current) ?? roster[count - 1]
                        : roster.FirstOrDefault(character => character.Slot > current) ?? roster[0];
                    SelectSlot(next.Slot);
                }
                else if (submitKey && (targetButton == null || slotButtons.Contains(targetButton)))
                {
                    if (SelectedCharacter != null) PlayUiSound("button");
                    EnterWorld();
                }
                else return;
                evt.StopPropagation();
            }
        }

        void Layout()
        {
            if (!frontendInitialized || Root == null) return;
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
            LayoutFrontendDialog(width, height);
            FrontendLayoutOverrides.Apply(Root);
            FFWidgets.FitTree(Root);
        }
    }
}
