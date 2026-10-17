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
        readonly GameplayUiSession session;
        readonly Action settings, quit;
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
        public bool InputBlocked => Windows.IsOpen || prompt != null;

        public GameplayUiController(VisualElement parent, GameplayUiSession session, string language, Action settings, Action quit)
        {
            this.parent = parent; this.session = session; this.language = language; this.settings = settings; this.quit = quit;
            gameplayStyle = ContentAssets.Load<StyleSheet>("ui/styles/Gameplay");
            if (gameplayStyle) parent.styleSheets.Add(gameplayStyle);
            Hud = new GameplayHudController(parent);
            NanoHud = new GameplayNanoHudController(parent);
            Windows = new GameplayWindowHost(parent);
            chatView = FFView.Instantiate("ui/views/Retrobution/Chat", "chat-root");
            BindChat(chatView); parent.Add(chatView);
            Register(GameplayWindow.NanoCom, "NanoCom", "nanocom", BindNanoCom);
            Register(GameplayWindow.Journal, "MissionJournal", "journal", BindJournal);
            Register(GameplayWindow.Inventory, "Inventory", "inventory", BindInventory);
            Register(GameplayWindow.Nanos, "Inventory", "inventory", root => BindInventory(root, true));
            Register(GameplayWindow.Trade, "Trade", "trade", BindTrade);
            Register(GameplayWindow.Map, "WorldMap", "map", BindMap);
            Windows.Register(GameplayWindow.Service, BuildServiceView);
            Windows.Register(GameplayWindow.Contacts, BuildSocialView);
            Windows.Register(GameplayWindow.Mail, BuildMailView);
            Windows.Register(GameplayWindow.Travel, BuildTravelView);
            Windows.Changed += WindowChanged;
            session.Changed += Bind;
            geometry = _ => { Hud.Layout(); PlaceWindow(); PlaceChat(); };
            parent.RegisterCallback(geometry);
            chatView.RegisterCallback<GeometryChangedEvent>(_ => PlaceChat());
            SetChatActive(false);
            Bind(session.Snapshot);
        }

        void Register(GameplayWindow id, string asset, string prefix, Action<VisualElement> bind)
        {
            Windows.Register(id, () => {
                var root = FFView.Instantiate("ui/views/Retrobution/" + asset, prefix + "-root");
                bind(root);
                root.Query<ScrollView>().ForEach(scroll => scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden);
                root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
                return root;
            });
        }

        void WindowChanged()
        {
            ServiceWindowChanged();
            lastWindowChangeFrame = Time.frameCount;
            CancelItemDrag();
            foreach (var slot in itemSlots.Keys.Where(slot => Windows.View == null || !Windows.View.Contains(slot)).ToArray()) itemSlots.Remove(slot);
            PlaceWindow();
            SetChatActive(Windows.Current == GameplayWindow.NanoCom);
            Windows.View?.SetEnabled(!interactionSuspended);
            if (Windows.Current == GameplayWindow.Mail) RefreshMail();
            InventoryAvatarChanged?.Invoke(Windows.Current == GameplayWindow.Trade ? null : Windows.View?.Q<Image>("inventory-avatar"));
            InputGateChanged?.Invoke();
        }

        public void SetLanguage(string value)
        {
            if (language == value) return;
            language = value;
            LocalizeChat();
            if (ServiceActive) { _ = CloseService(); return; }
            var current = Windows.Current;
            Windows.Close();
            if (current.HasValue) Windows.Open(current.Value);
        }

        public void Tick(float delta, JObject preferences, bool externalModal)
        {
            SetInteractionSuspended(externalModal);
            Hud.Tick(delta);
            TickMissionClock();
            TickInventory();
            TickServices();
            TickSocial();
            TickTravel();
            TickVehicle();
            if (externalModal || disposed || Time.frameCount == lastWindowChangeFrame || !session.Snapshot.IsActive || prompt != null || TradeWindowActive || ServiceActive) return;
            for (var focus = parent.panel?.focusController.focusedElement as VisualElement; focus != null; focus = focus.parent)
                if (focus is TextField) return;
            if (SettingsInputReader.Pressed("Menu", preferences)) Windows.Toggle(GameplayWindow.NanoCom);
            else if (SettingsInputReader.Pressed("Inventory", preferences)) Windows.Toggle(GameplayWindow.Inventory);
            else if (SettingsInputReader.Pressed("Journal", preferences)) Windows.Toggle(GameplayWindow.Journal);
            else if (SettingsInputReader.Pressed("WorldMap", preferences)) Windows.Toggle(GameplayWindow.Map);
            else if (SettingsInputReader.Pressed("NanoBook", preferences)) Windows.Toggle(GameplayWindow.Nanos);
        }

        public void SetInteractionSuspended(bool value)
        {
            if (interactionSuspended == value) return;
            interactionSuspended = value;
            if (value) CancelItemDrag();
            Windows.View?.SetEnabled(!value);
            chatView?.SetEnabled(!value);
            prompt?.View.SetEnabled(!value);
            SetChatActive(Windows.Current == GameplayWindow.NanoCom);
        }

        public bool CloseWindow()
        {
            if (prompt != null) { prompt.Cancel(); return true; }
            if (ServiceActive) { _ = CloseService(); return true; }
            if (Windows.Current == GameplayWindow.Trade && TradeWindowActive) { _ = CancelTrade(); return true; }
            return Windows.Close();
        }

        void Bind(GameplayUiState state)
        {
            Hud.Bind(state);
            NanoHud.Bind(state);
            if (Windows.View != null) {
                RefreshInventoryPresentation(Windows.View);
                if (!ReferenceEquals(boundState.Nanos, state.Nanos)) RefreshNanoCollection(Windows.View);
            }
            Hud.SetVisible(state.IsActive);
            chatView.style.display = state.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ReferenceEquals(boundState.Inventory, state.Inventory) || !ReferenceEquals(boundState.Equipment, state.Equipment)) RefreshInventoryItems();
            if (!ReferenceEquals(boundState.Bank, state.Bank) || boundState.Taros != state.Taros || boundState.WeaponBattery != state.WeaponBattery || boundState.NanoBattery != state.NanoBattery) RefreshService();
            RefreshMap();
            if (!ReferenceEquals(boundState.RunningQuests, state.RunningQuests) || !ReferenceEquals(boundState.QuestInventory, state.QuestInventory)) RefreshJournal();
            if (!ReferenceEquals(boundState.RunningQuests, state.RunningQuests) || !ReferenceEquals(boundState.CompletedMissions, state.CompletedMissions) ||
                !ReferenceEquals(boundState.QuestInventory, state.QuestInventory) || !ReferenceEquals(boundState.Inventory, state.Inventory) ||
                !ReferenceEquals(boundState.Nanos, state.Nanos) || boundState.Guide != state.Guide || boundState.Level != state.Level ||
                boundState.MapNumber != state.MapNumber || boundState.Taros != state.Taros) RefreshMail();
            if (boundState.IsActive != state.IsActive || boundState.IsTutorial != state.IsTutorial) SetChatActive(Windows.Current == GameplayWindow.NanoCom);
            boundState = state;
            if (!state.IsActive) Windows.Close();
        }

        void PlaceWindow()
        {
            var view = Windows.View;
            if (view == null) return;
            if (Windows.Current == GameplayWindow.Mail)
            {
                PlaceInventory(view, parent.resolvedStyle.width, parent.resolvedStyle.height);
                return;
            }
            if (Windows.Current == GameplayWindow.Contacts || Windows.Current == GameplayWindow.Travel)
            {
                var socialPanel = view.Q(Windows.Current == GameplayWindow.Mail ? "mail-window" : Windows.Current == GameplayWindow.Travel ? "travel-window" : "social-window");
                float sw = Windows.Current == GameplayWindow.Mail ? 1020 : 880, sh = Windows.Current == GameplayWindow.Mail ? 638 : 630;
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
            string prefix = Windows.Current switch { GameplayWindow.NanoCom => "nanocom", GameplayWindow.Inventory or GameplayWindow.Nanos or GameplayWindow.Service => "inventory", GameplayWindow.Map => "map", _ => "journal" };
            var panel = FFView.Require<VisualElement>(view, prefix + "-window");
            float w = panel.resolvedStyle.width, h = panel.resolvedStyle.height;
            float width = parent.resolvedStyle.width, height = parent.resolvedStyle.height;
            if (!float.IsFinite(w) || !float.IsFinite(h) || w <= 0 || h <= 0 || width <= 0 || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            panel.style.transformOrigin = new TransformOrigin(0, 0);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
            if (Windows.Current == GameplayWindow.NanoCom)
            {
                panel.style.left = width - w * scale;
                // Source NanoRect.y is zero in the supported scene.
                panel.style.top = 0;
            }
            else
            {
                Vector2 pivot = new Vector2((int)width / 2, (int)height / 2);
                float x = (width - w) / 2, y = (height - h) / 2;
                if (Windows.Current == GameplayWindow.Inventory || Windows.Current == GameplayWindow.Nanos || Windows.Current == GameplayWindow.Service)
                {
                    PlaceInventory(view, width, height);
                    return;
                }
                panel.style.left = pivot.x + (x - pivot.x) * scale;
                panel.style.top = pivot.y + (y - pivot.y) * scale;
            }
        }

        static void PlaceInventory(VisualElement view, float width, float height)
        {
            var panel = view.Q("inventory-window") ?? view.Q("mail-window");
            if (panel == null || !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float x = ((int)width - 1036) / 2, y = ((int)height - 653) / 2;
            float scale = Mathf.Min(RetrobutionLayout.Scale(height), width / 1060f);
            FFWidgets.ScaledRect(panel, new Rect(x,y,1036,653), scale, new Vector2((int)width/2,(int)height/2));
            var backdrop = view.Q("inventory-backdrop");
            if (backdrop != null) FFWidgets.ScaledRect(backdrop, new Rect((width - 1920) / 2, (height - 1440) / 2, 1920, 1440), scale, new Vector2((int)width/2,(int)height/2));
        }

        void BindNanoCom(VisualElement root)
        {
            Navigate(root, "inventory", GameplayWindow.Inventory);
            Navigate(root, "journal", GameplayWindow.Journal);
            Navigate(root, "mail", GameplayWindow.Mail);
            Navigate(root, "map", GameplayWindow.Map);
            Navigate(root, "guide", GameplayWindow.Help);
            ActionButton(root, "settings", () => { Windows.Close(); settings?.Invoke(); });
            ActionButton(root, "quit", () => { Windows.Close(); quit?.Invoke(); });
            ActionButton(root, "close", () => Windows.Close());
        }

        void Navigate(VisualElement root, string name, GameplayWindow id)
        {
            var button = ActionButton(root, name, () => { if (id == GameplayWindow.Mail) OpenMail(); else Windows.Open(id); });
            // The source contains this action, but absent consumers must not claim a successful action.
            button.SetEnabled(Windows.Supports(id));
        }

        Button ActionButton(VisualElement root, string name, Action action)
        {
            var button = FFView.Require<Button>(root, name);
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
                "inventory" => "МОИ ВЕЩИ", "journal" => "ЖУРНАЛ", "mail" => "ПОЧТА", "map" => "КАРТА",
                "settings" => "НАСТРОЙКИ", "guide" => "РУКОВОДСТВО", "quit" => "ВЫХОД", "close" => "ЗАКРЫТЬ",
                "tab-active" => "АКТИВНЫЕ", "tab-completed" => "ЗАВЕРШЁННЫЕ", _ => fallback
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DisposeTrade();
            DisposeServices();
            DisposeSocial();
            DisposeVehicle();
            DisposeMissionDialogue();
            crocButton?.RemoveFromHierarchy(); travelClient = null;
            CancelItemDrag(); itemSlots.Clear();
            parent.UnregisterCallback(geometry);
            session.Changed -= Bind;
            Windows.Changed -= WindowChanged;
            Windows.Dispose(); Hud.Dispose(); NanoHud.Dispose(); chatView.RemoveFromHierarchy();
            if (gameplayStyle) parent.styleSheets.Remove(gameplayStyle);
            InputGateChanged = null; InventoryAvatarChanged = null; Error = null;
            InventoryRotationRequested = null;
        }
    }
}
