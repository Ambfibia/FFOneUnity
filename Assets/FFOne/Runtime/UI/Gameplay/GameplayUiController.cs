using System;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Gameplay view lifetime, source pivot placement, navigation and data binding.</summary>
    public sealed partial class GameplayUiController : IDisposable
    {
        readonly VisualElement parent;
        readonly UiLayers layers;
        readonly GameplayUiSession session;
        readonly Action settings, quit;
        readonly Action<string> playSound;
        GameplayWindow? previousWindow;
        readonly GameplayMinimap minimap;
        readonly GameplayRewardCounters rewardCounters;
        Image mailAlarm;
        float mailAlarmUntil, minimapRatio = 8;
        CharacterLocations.Location lastOverworldLocation, minimapLocation;
        Label locationCaption;
        float locationCaptionUntil;
        string minimapLanguage;
        string objectiveLanguage;
        int objectiveMissionId;
        object objectiveQuests;
        GameplayMissionPresentation trackedObjective;
        float cameraHeading;
        readonly EventCallback<GeometryChangedEvent> geometry;
        readonly StyleSheet gameplayStyle;
        string language;
        bool disposed, interactionSuspended;
        GameplayUiState boundState = GameplayUiState.Empty;
        int lastWindowChangeFrame = -1;
        public GameplayHudController Hud { get; }
        public GameplayNanoHudController NanoHud { get; }
        public GameplayWindowHost Windows { get; }
        public event Action InputGateChanged;
        public event Action<Image> InventoryAvatarChanged;
        internal bool AllowsMenuAutoRun => Windows.Current == GameplayWindow.NanoCom && prompt == null &&
            gameGuide == null && !mentorDeparture && !expandedInvitation;
        public bool InputBlocked => rewardPreview || Windows.IsOpen || prompt != null || gameGuide != null || mentorDeparture || expandedInvitation;
        bool allowGroupInvites=true;
        bool showNpcMessagesInChat = true;

        public GameplayUiController(VisualElement parent, GameplayUiSession session, string language, Action settings, Action quit, Action<string> playSound = null)
        {
            this.parent = parent; this.session = session; this.language = language; this.settings = settings; this.quit = quit;
            this.playSound = playSound;
            layers = UiLayers.For(parent);
            gameplayStyle = ContentAssets.Load<StyleSheet>("ui/styles/Gameplay");
            if (gameplayStyle) parent.styleSheets.Add(gameplayStyle);
            Hud = new GameplayHudController(layers.Host(UiLayer.Hud), language);
            rewardCounters = new GameplayRewardCounters(Hud.View, layers.Host(UiLayer.Notification), language, playSound);
            NanoHud = new GameplayNanoHudController(layers.Host(UiLayer.Hud));
            hudStatus = new GameplayHudStatus(Hud.View, "hud", language);
            targetHud = new GameplayTargetHud(Hud.View, "hud");
            Hud.View.RegisterCallback<GeometryChangedEvent>(_ => {
                targetHud.Layout();
                hudStatus.Layout(Hud.View.resolvedStyle.width, Hud.View.resolvedStyle.height);
            });
            minimap = new GameplayMinimap(Hud.View, "hud", "");
            mailAlarm = FFWidgets.Image(minimap.View, "hud-new-mail", GameplayMinimap.Art + "new-mail", 145.5f, 158, 19, 14);
            mailAlarm.style.display = DisplayStyle.None;
            Windows = new GameplayWindowHost(layers.Host(UiLayer.Window),
                window => window != GameplayWindow.Map || CurrentMapZone() != RetrobutionMapLayout.Unavailable);
            chatView = FFView.InstantiateClass("ui/views/Retrobution/Chat", "ff-chat-root");
            BindChat(chatView); layers.Add(chatView, UiLayer.Hud);
            Windows.Register(GameplayWindow.NanoCom, () => {
                var root = GameplayNanoComView.Instantiate();
                BindNanoCom(root);
                return root;
            });
            Register(GameplayWindow.Journal, "MissionJournal", "ff-journal-root", BindJournal);
            Register(GameplayWindow.Inventory, "Inventory", "inventory-screen", BindInventory);
            Register(GameplayWindow.Nanos, "Inventory", "inventory-screen", root => BindInventory(root, true));
            Register(GameplayWindow.Trade, "Trade", "ff-trade-root", BindTrade);
            Register(GameplayWindow.Map, "WorldMap", "ff-world-map", BindMap);
            Windows.Register(GameplayWindow.Service, BuildServiceView);
            Windows.Register(GameplayWindow.Contacts, BuildSocialView);
            Windows.Register(GameplayWindow.Mail, BuildMailView);
            Windows.Register(GameplayWindow.Travel, BuildTravelView);
            Windows.Register(GameplayWindow.Mentor, BuildMentorView);
            Windows.Register(GameplayWindow.CashMall, BuildCashMall);
            Windows.Register(GameplayWindow.Race, BuildRaceWindow);
            Windows.Register(GameplayWindow.NpcServices, BuildNpcServices);
            Windows.Register(GameplayWindow.Barber, BuildBarber);
            Windows.Register(GameplayWindow.NpcMissions, BuildNpcMissionMenu);
            Windows.Changed += WindowChanged;
            session.Changed += Bind;
            session.RewardReceived += rewardCounters.Receive;
            session.QuestLootReceived += rewardCounters.QueueQuestLoot;
            session.RewardReceived += ShowRewardChat;
            geometry = _ => RefreshViewport();
            parent.RegisterCallback(geometry);
            parent.RegisterCallback<KeyDownEvent>(OnInvitationKey, TrickleDown.TrickleDown);
            // The persistent frontend root can have its final size before this HUD exists.
            Hud.View.RegisterCallback<GeometryChangedEvent>(_ => PlaceMinimap());
            PlaceMinimap();
            rewardCounters.Layout(parent.resolvedStyle.height);
            chatView.RegisterCallback<GeometryChangedEvent>(_ => PlaceChat());
            SetChatActive(false);
            Bind(session.Snapshot);
        }

        internal void RefreshViewport()
        {
            Hud.Layout(); NanoHud.Layout();
            rewardCounters.Layout(parent.resolvedStyle.height);
            targetHud.Layout();
            hudStatus.Layout(parent.resolvedStyle.width, parent.resolvedStyle.height);
            PlaceMinimap(); PlaceWindow(); PlaceChat();
        }

        void Register(GameplayWindow id, string asset, string role, Action<VisualElement> bind)
        {
            Windows.Register(id, () => {
                string address = "ui/views/Retrobution/" + asset;
                var root = FFView.InstantiateClass(address, role);
                bind(root);
                root.Query<ScrollView>().ForEach(scroll => scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden);
                root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
                return root;
            });
        }

        void WindowChanged()
        {
            if (previousWindow == GameplayWindow.Barber && Windows.Current != GameplayWindow.Barber) barberPreview?.EndDraft();
            UpdateNpcConversation();
            if (gameGuide != null) CloseGuide(false);
            if (!disposed && session.Snapshot.IsActive && previousWindow != Windows.Current)
            {
                if (previousWindow is GameplayWindow.NanoCom or GameplayWindow.Mail or GameplayWindow.Service or GameplayWindow.Travel or GameplayWindow.Trade)
                    playSound?.Invoke("close_screen");
                if (Windows.Current is GameplayWindow.NanoCom or GameplayWindow.Mail or GameplayWindow.Service or GameplayWindow.Trade)
                    playSound?.Invoke("open_screen");
            }
            if (Windows.Current != previousWindow)
            {
                int tip = Windows.Current switch {
                    GameplayWindow.Inventory => 3, GameplayWindow.Map => 15, GameplayWindow.Nanos => 39,
                    GameplayWindow.Mail => 11, GameplayWindow.Trade => 34, _ => 0
                };
                if (tip > 0) CheckFirstUse(tip);
            }
            previousWindow = Windows.Current;
            ServiceWindowChanged();
            lastWindowChangeFrame = Time.frameCount;
            CancelItemDrag();
            foreach (var slot in itemSlots.Keys.Where(slot => Windows.View == null || !Windows.View.Contains(slot)).ToArray()) itemSlots.Remove(slot);
            if (Windows.Current == GameplayWindow.NanoCom)
            {
                GameplayNanoComView.AttachMinimap(Windows.View, minimap.View);
            }
            else { Hud.View.Add(minimap.View); PlaceMinimap(); }
            PlaceWindow();
            UpdateMinimap();
            SetChatActive(Windows.Current == GameplayWindow.NanoCom);
            Windows.View?.SetEnabled(!interactionSuspended);
            if (Windows.Current == GameplayWindow.Mail) RefreshMail();
            if (Windows.Current == GameplayWindow.Journal) RefreshJournal(); else missionNpc = null;
            InventoryAvatarChanged?.Invoke(Windows.Current == GameplayWindow.Trade ? null : Windows.View?.Q<Image>(className: "inventory-avatar"));
            InputGateChanged?.Invoke();
        }

        public void SetLanguage(string value)
        {
            if (language == value) return;
            language = value;
            Hud.SetLanguage(language);
            rewardCounters.SetLanguage(language);
            hudStatus.SetLanguage(language);
            BindActiveNano(session.Snapshot);
            hudStatus.Layout(Hud.View.resolvedStyle.width, Hud.View.resolvedStyle.height);
            UpdateMinimap();
            LocalizeChat();
            if (ServiceActive) { _ = CloseService(); return; }
            Windows.Refresh();
        }

        internal void ApplyDisplayPreferences(JObject preferences)
        {
            showNpcMessagesInChat = (bool?)preferences?["display"]?["npc_messages_in_chat"] ?? true;
            firstUseHintsEnabled = (bool?)preferences?["display"]?["game_hint"] ?? true;
            ApplyWorldDisplayPreferences(preferences);
        }

        public void Tick(float delta, JObject preferences, bool externalModal)
        {
            SetInteractionSuspended(externalModal);
            TickNanoReward();
            Hud.Tick(delta);
            rewardCounters.Tick(delta, Windows.Current != GameplayWindow.NanoCom);
            UpdateMinimap();
            TickMissionClock();
            TickInventory();
            TickServices();
            TickSocial();
            allowBuddyInvites=(bool?)preferences?["social"]?["buddy"]??true;
            allowGroupInvites=(bool?)preferences?["social"]?["group"]??true;
            TickInvitations();
            TickTravel();
            TickBarber();
            TickRace();
            TickVehicle();
            if (Windows.Current == GameplayWindow.Barber || rewardPreview || expandedInvitation || gameGuide != null || buddyEntry != null || Windows.Current == GameplayWindow.Mentor || externalModal || disposed || Time.frameCount == lastWindowChangeFrame || !session.Snapshot.IsActive || prompt != null || TradeWindowActive || ServiceActive) return;
            if (UnityEngine.InputSystem.Gamepad.current?.startButton.wasPressedThisFrame == true && GameplayInput.Pressed(GameplayAction.Menu))
            {
                Windows.Toggle(GameplayWindow.NanoCom);
                return;
            }
            if (Windows.IsOpen && UnityEngine.InputSystem.Gamepad.current?.dpad.ReadValue().sqrMagnitude > .2f) return;
            for (var focus = parent.panel?.focusController.focusedElement as VisualElement; focus != null; focus = focus.parent)
                if (focus is TextField) return;
            if (GameplayInput.Pressed(GameplayAction.Menu)) Windows.Toggle(GameplayWindow.NanoCom);
            else if (GameplayInput.Pressed(GameplayAction.Inventory)) Windows.Toggle(GameplayWindow.Inventory);
            else if (GameplayInput.Pressed(GameplayAction.Journal)) Windows.Toggle(GameplayWindow.Journal);
            else if (GameplayInput.Pressed(GameplayAction.Email))
            {
                if (Windows.Current == GameplayWindow.Mail) CloseWindow();
                else OpenMail();
            }
            else if (GameplayInput.Pressed(GameplayAction.WorldMap)) Windows.Toggle(GameplayWindow.Map);
            else if (GameplayInput.Pressed(GameplayAction.NanoBook)) Windows.Toggle(GameplayWindow.Nanos);
        }

        internal void UnignorePlayer(int slot)
        {
            // Settings is an external modal. The normal social gate would block its own action.
            if (socialClient == null || disposed || !session.Snapshot.IsActive) return;
            var friends = social.Friends;
            if (slot < 0 || slot >= friends.Count || friends[slot]?.Blocked != true) return;
            var friend = friends[slot];
            _ = SocialSend(() => socialClient.RemoveFriendAsync(friend.Slot, friend.Uid, false, socialToken));
        }

        public void SetInteractionSuspended(bool value)
        {
            if (interactionSuspended == value) return;
            interactionSuspended = value;
            if (value) CancelItemDrag();
            gameGuide?.SetEnabled(!value);
            Windows.View?.SetEnabled(!value && gameGuide == null);
            chatView?.SetEnabled(!value && gameGuide == null);
            prompt?.View.SetEnabled(!value);
            SetChatActive(Windows.Current == GameplayWindow.NanoCom);
        }

        public bool CloseWindow()
        {
            if (Windows.Current == GameplayWindow.Barber && barberPending == FFOne.Network.ProtocolBarber.Result) return true;
            if (rewardPreview) return true;
            if (expandedInvitation) { _=AnswerInvitation(false,true); return true; }
            if (buddyEntry != null) { CloseBuddyEntry(); return true; }
            if (prompt != null) { prompt.Cancel(); return true; }
            if (gameGuide != null) { CloseGuide(true); return true; }
            if (quickMenu != null) { CloseQuickChat(); return true; }
            if (CloseInventoryDetail()) return true;
            if (Windows.Current == GameplayWindow.Mentor && mentorPending != 0 && !travelFaulted) return true;
            if (ServiceActive) { _ = CloseService(); return true; }
            if (Windows.Current == GameplayWindow.Trade && TradeWindowActive) { _ = CancelTrade(); return true; }
            return Windows.Close();
        }

        void Bind(GameplayUiState state)
        {
            Hud.Bind(state);
            if (!state.IsActive || boundState.PlayerId != state.PlayerId || boundState.CharacterUid != state.CharacterUid)
                lastOverworldLocation = null;
            UpdateMinimap();
            NanoHud.Bind(state);
            BindActiveNano(state);
            if (Windows.View != null) {
                RefreshInventoryPresentation(Windows.View);
                if (!ReferenceEquals(boundState.Nanos, state.Nanos)) RefreshNanoCollection(Windows.View);
            }
            Hud.SetVisible(state.IsActive);
            if (!state.IsActive || (boundState.IsActive && boundState.PlayerId != state.PlayerId)) rewardCounters.Reset();
            if (!ReferenceEquals(boundState.Inventory, state.Inventory))
                rewardCounters.SetInventoryFull(state.IsActive && state.Inventory.Count != 0 && state.Inventory.All(item => !item.IsEmpty));
            chatView.style.display = state.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ReferenceEquals(boundState.Inventory, state.Inventory) || !ReferenceEquals(boundState.Equipment, state.Equipment)) RefreshInventoryItems();
            if (!ReferenceEquals(boundState.Inventory, state.Inventory) || boundState.Taros != state.Taros) RefreshCrocInventory(boundState, state);
            if (!ReferenceEquals(boundState.Bank, state.Bank) || boundState.Taros != state.Taros || boundState.WeaponBattery != state.WeaponBattery || boundState.NanoBattery != state.NanoBattery) RefreshService();
            if (!ReferenceEquals(boundState.RunningQuests, state.RunningQuests) || !ReferenceEquals(boundState.QuestInventory, state.QuestInventory) ||
                !ReferenceEquals(boundState.CompletedMissions, state.CompletedMissions) || !ReferenceEquals(boundState.Nanos, state.Nanos) ||
                boundState.Level != state.Level || boundState.Guide != state.Guide || boundState.MapNumber != state.MapNumber ||
                boundState.HasBuff(13) != state.HasBuff(13)) mapContentsDirty = true;
            if (Windows.Current == GameplayWindow.Map && !Windows.Supports(GameplayWindow.Map)) Windows.Close();
            RefreshMap();
            if (!ReferenceEquals(boundState.RunningQuests, state.RunningQuests) || !ReferenceEquals(boundState.QuestInventory, state.QuestInventory) ||
                !ReferenceEquals(boundState.CompletedMissions, state.CompletedMissions) || !ReferenceEquals(boundState.Nanos, state.Nanos) ||
                boundState.Guide != state.Guide || boundState.Level != state.Level || boundState.MapNumber != state.MapNumber ||
                boundState.CurrentMissionId != state.CurrentMissionId) RefreshJournal();
            if (!ReferenceEquals(boundState.RunningQuests, state.RunningQuests) || !ReferenceEquals(boundState.CompletedMissions, state.CompletedMissions) ||
                !ReferenceEquals(boundState.QuestInventory, state.QuestInventory) || !ReferenceEquals(boundState.Inventory, state.Inventory) ||
                !ReferenceEquals(boundState.Nanos, state.Nanos) || boundState.Guide != state.Guide || boundState.Level != state.Level ||
                boundState.MapNumber != state.MapNumber || boundState.Taros != state.Taros)
            {
                if (socialClient != null) UpdateGuideMailNotices();
                RefreshMail();
            }
            if (boundState.IsActive != state.IsActive || boundState.IsTutorial != state.IsTutorial) SetChatActive(Windows.Current == GameplayWindow.NanoCom);
            boundState = state;
            if (!state.IsActive) Windows.Close();
        }

        void PlaceWindow()
        {
            var view = Windows.View;
            if (view == null) return;
            if (view.ClassListContains("player-interaction")) return;
            if (Windows.Current is GameplayWindow.NpcMissions or GameplayWindow.Barber) return;
            if (view.ClassListContains("npc-service-root"))
            {
                ScaleServiceMenu(serviceMenuPanel, parent.resolvedStyle.height);
                return;
            }
            if (Windows.Current == GameplayWindow.Mentor) return; // Owns the window, backdrop and confirmation transforms.
            if (Windows.Current == GameplayWindow.Mail || Windows.Current == GameplayWindow.CashMall || Windows.Current == GameplayWindow.Race || Windows.Current == GameplayWindow.Travel && (crocView || transportHub != null))
            {
                PlaceInventory(view, parent.resolvedStyle.width, parent.resolvedStyle.height);
                return;
            }
            if (Windows.Current == GameplayWindow.Contacts || Windows.Current == GameplayWindow.Travel)
            {
                var socialPanel = Windows.Current == GameplayWindow.Contacts ? view.Q(className: "social-window") : view.Q("travel-window");
                const float sw = 880, sh = 630;
                float pw = parent.resolvedStyle.width, ph = parent.resolvedStyle.height;
                if (float.IsFinite(pw) && float.IsFinite(ph) && pw > 0 && ph > 0)
                    FFWidgets.ScaledRect(socialPanel, new Rect(((int)pw - sw) / 2, ((int)ph - sh) / 2, sw, sh), RetrobutionLayout.Scale(ph), new Vector2((int)pw / 2, (int)ph / 2));
                return;
            }
            if (Windows.Current == GameplayWindow.Trade)
            {
                var tradePanel = FFView.Require<VisualElement>(view, "trade-window");
                float tradeWidth = parent.resolvedStyle.width, tradeHeight = parent.resolvedStyle.height;
                if (!float.IsFinite(tradeWidth) || !float.IsFinite(tradeHeight) || tradeWidth <= 0 || tradeHeight <= 0) return;
                PlaceInventory(view, tradeWidth, tradeHeight);
                FFWidgets.ScaledRect(tradePanel, new Rect(Mathf.Max(0, ((int)tradeWidth - 1020) / 2), Mathf.Max(0, ((int)tradeHeight - 638) / 2), 550, 700),
                    RetrobutionLayout.Scale(tradeHeight), new Vector2((int)tradeWidth / 2, (int)tradeHeight / 2));
                return;
            }
            float width = parent.resolvedStyle.width, height = parent.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            if (Windows.Current == GameplayWindow.NanoCom) GameplayNanoComView.Place(view, width, height);
            else if (Windows.Current is GameplayWindow.Inventory or GameplayWindow.Nanos or GameplayWindow.Service)
                PlaceInventory(view, width, height);
            else
            {
                var panel = FFView.RequireClass<VisualElement>(view,
                    Windows.Current == GameplayWindow.Map ? "ff-world-map-window" : "ff-journal-window");
                float w = panel.resolvedStyle.width, h = panel.resolvedStyle.height;
                if (!float.IsFinite(w) || !float.IsFinite(h) || w <= 0 || h <= 0) return;
                float scale = Mathf.Min(RetrobutionLayout.Scale(height), width / w);
                FFWidgets.ScaledRect(panel, new Rect((width - w) / 2, (height - h) / 2, w, h), scale,
                    new Vector2((int)width / 2, (int)height / 2));
            }
        }

        static void PlaceInventory(VisualElement view, float width, float height)
        {
            var panel = view.Q(className: "transport-window") ?? view.Q(className: "inventory-window") ?? view.Q("inventory-window");
            if (panel == null || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float x = ((int)width - 1036) / 2, y = ((int)height - 653) / 2;
            float scale = Mathf.Min(RetrobutionLayout.Scale(height), width / 1060f);
            FFWidgets.ScaledRect(panel, new Rect(x,y,1036,653), scale, new Vector2((int)width/2,(int)height/2));
            var backdrop = view.Q(className: "transport-backdrop") ?? view.Q(className: "inventory-backdrop") ?? view.Q("inventory-backdrop");
            if (backdrop != null) FFWidgets.ScaledRect(backdrop, new Rect((width - 1920) / 2, (height - 1440) / 2, 1920, 1440), scale, new Vector2((int)width/2,(int)height/2));
        }

        void PlaceMinimap()
        {
            if (minimap.View.parent == Hud.View) GameplayMinimap.Layout(minimap.View, parent.resolvedStyle.width, parent.resolvedStyle.height);
        }

        public void SetCameraHeading(float yaw) => cameraHeading = yaw;
        void UpdateMinimap()
        {
            var state = session.Snapshot;
            bool visible = state.IsActive && !state.IsTutorial;
            minimap.View.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            var position = state.WorldPosition;
            minimap.Update(position, minimapRatio, state.FusionMatterFraction ?? 0, mapHeading, cameraHeading);
            bool inInstance = state.MapNumber != 0;
            var location = inInstance ? lastOverworldLocation : CharacterLocations.ResolveWorld(position);
            if (!inInstance && location != null)
            {
                if (lastOverworldLocation != null && lastOverworldLocation.District != location.District)
                {
                    if (locationCaption == null)
                    {
                        locationCaption = FFView.OptionalClass("ui/views/Retrobution/GameplayHud", "world-location-caption") as Label;
                        if (locationCaption != null) Hud.View.Add(locationCaption);
                    }
                    if (locationCaption != null) locationCaption.text = CharacterLocations.District(location, language);
                    locationCaptionUntil = Time.unscaledTime + 3;
                }
                lastOverworldLocation = location;
            }
            if (locationCaption != null)
            {
                float left = locationCaptionUntil - Time.unscaledTime;
                locationCaption.style.display = !inInstance && left > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                locationCaption.style.opacity = Mathf.Clamp01(left * 2);
            }
            if (location != minimapLocation || language != minimapLanguage)
            {
                minimapLocation = location;
                minimapLanguage = language;
                minimap.PlaceName.text = CharacterLocations.District(location, language);
            }
            if (objectiveMissionId != state.CurrentMissionId || !ReferenceEquals(objectiveQuests, state.RunningQuests) || objectiveLanguage != language)
            {
                objectiveMissionId = state.CurrentMissionId;
                objectiveQuests = state.RunningQuests;
                objectiveLanguage = language;
                var quest = objectiveMissionId > 0
                    ? state.RunningQuests.FirstOrDefault(q => FFOne.World.WorldMissionCatalog.Load().Find(q.TaskId)?.MissionId == objectiveMissionId)
                    : null;
                trackedObjective = quest == null ? null : GameplayUiCatalog.Load().Mission(quest.TaskId, language);
            }
            minimap.SetObjective(trackedObjective, !Windows.IsOpen);
            mailAlarm.style.display = Time.unscaledTime < mailAlarmUntil && Windows.Current != GameplayWindow.NanoCom ? DisplayStyle.Flex : DisplayStyle.None;
            if (Time.unscaledTime < mailAlarmUntil) mailAlarm.tintColor = new Color(1, 1, 1, Mathf.Abs(Mathf.Cos(Time.time * Mathf.PI * 2)));
        }

        void BindNanoCom(VisualElement root)
        {
            GameplayNanoComView.Localize(root, language);
            GameplayNanoComView.BindZoom(root, delta => {
                minimapRatio = Mathf.Clamp(minimapRatio + delta, 4, 16);
                playSound?.Invoke("button");
                UpdateMinimap();
            });
            Navigate("inventory", GameplayWindow.Inventory);
            Navigate("journal", GameplayWindow.Journal);
            Navigate("mail", GameplayWindow.Mail);
            Navigate("map", GameplayWindow.Map);
            Bind("guide", () => OpenGuide());
            Bind("settings", () => { Windows.Close(); settings?.Invoke(); });
            Bind("quit", () => quit?.Invoke());
            Bind("close", () => Windows.Close());

            void Navigate(string role, GameplayWindow id)
            {
                Bind(role, () => { if (id == GameplayWindow.Mail) OpenMail(); else Windows.Open(id); }).SetEnabled(Windows.Supports(id));
            }

            Button Bind(string role, Action run)
            {
                var button = GameplayNanoComView.Action(root, role);
                button.clicked += () => { playSound?.Invoke("button"); run(); };
                return button;
            }
        }

        Button ActionButton(VisualElement root, string name, Action action, string cue = null)
        {
            var button = FFView.Require<Button>(root, name);
            if (cue != null) button.clicked += () => playSound?.Invoke(cue);
            button.clicked += action;
            var label = button.Q<Label>(name + "-label");
            if (label != null)
            {
                FFWidgets.Font(label, false, 12);
                if (!string.IsNullOrEmpty(label.text)) label.text = Caption(name, label.text);
            }
            return button;
        }

        string Caption(string name, string fallback)
        {
            if (language != "ru") return fallback;
            return name switch {
                "close" => "ЗАКРЫТЬ",
                "tab-active" => "АКТИВНЫЕ", "tab-completed" => "ЗАВЕРШЁННЫЕ", _ => fallback
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseGuide(false);
            DisposeTrade();
            DisposeServices();
            DisposeSocial();
            ReleaseRace();
            DisposeVehicle();
            EndNanoReward();
            DisposeMissionDialogue();
            DisposeWorldAnnotations();
            ShowLauncher(false, 0);
            targetHud.Hide();
            foreach (var texture in waypointArt.Values) UnityEngine.Object.Destroy(texture);
            waypointArt.Clear();
            travelClient = null;
            CancelItemDrag(); itemSlots.Clear();
            parent.UnregisterCallback(geometry);
            parent.UnregisterCallback<KeyDownEvent>(OnInvitationKey, TrickleDown.TrickleDown);
            session.Changed -= Bind;
            session.RewardReceived -= rewardCounters.Receive;
            session.QuestLootReceived -= rewardCounters.QueueQuestLoot;
            session.RewardReceived -= ShowRewardChat;
            rewardCounters.Remove();
            Windows.Changed -= WindowChanged;
            Windows.Dispose(); Hud.Dispose(); NanoHud.Dispose(); chatView.RemoveFromHierarchy();
            if (gameplayStyle) parent.styleSheets.Remove(gameplayStyle);
            InputGateChanged = null; InventoryAvatarChanged = null; Error = null;
            InventoryRotationRequested = null;
        }
    }
}
