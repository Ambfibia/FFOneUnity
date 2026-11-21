using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const int LocationDebugMaxUserLevel = 50;
        enum ChatChannel { All, Group, Friend }
        readonly List<(string Text, string Tone)>[] chatHistory = { new(), new(), new() };
        readonly List<string> sentHistory = new();
        int historyCursor;
        ChatChannel chatChannel;
        VisualElement chatView;
        VisualElement emptyChatChannel;
        Label emptyChatText;
        Image legacyEmptyChatIcon;
        Func<string, Task> sendChat;
        bool chatPending;
        NetworkWorldController locationDebugWorld;
        Label locationDebug;
        bool showLocationDebug;
        public event Action<string> Error;

        public void SetChatSender(Func<string, Task> sender) { sendChat = sender; UpdateChatSend(); UpdateRedeemButton(); }

        internal void ConfigureLocationDebug(int userLevel, NetworkWorldController world)
        {
            if (userLevel > LocationDebugMaxUserLevel || !world) return;
            locationDebugWorld = world;
            locationDebug.schedule.Execute(RefreshLocationDebug).Every(200);
        }

        void RefreshLocationDebug()
        {
            bool visible = showLocationDebug && locationDebugWorld && locationDebugWorld.IsReady && locationDebugWorld.Player;
            locationDebug.EnableInClassList("ff-location-debug--visible", visible);
            if (!visible) return;
            var position = WorldCoordinates.ToServer(locationDebugWorld.Player.position);
            int angle = WorldCoordinates.ToServerHeading(locationDebugWorld.Player.eulerAngles.y);
            locationDebug.text = LocalizationCatalog.Lookup("ui.chat.command.gm.view_location", language)
                .Replace("{map}", locationDebugWorld.MapNumber.ToString())
                .Replace("{x}", position.x.ToString()).Replace("{y}", position.y.ToString())
                .Replace("{z}", position.z.ToString()).Replace("{angle}", angle.ToString());
        }

        public void ReceiveChat(ProtocolFrame frame)
        {
            if (frame.PacketType == 0x31000054)
            {
                Protocol0104.RequireSize(frame.Payload, 8);
                if (Protocol0104.Read32(frame.Payload, 0) != session.Snapshot.PlayerId) return;
                int code = Protocol0104.Read32(frame.Payload, 4);
                string key = code == 1 ? "ui.chat.command.nano.unknown_server" : code == 2 ? "ui.chat.command.nano.already_owned" : "ui.chat.command.nano.rejected";
                AppendChat(ChatChannel.All, LocalizationCatalog.Lookup(key, language).Replace("{error}", code.ToString()));
                RefreshChat(chatView);
                return;
            }
            if (ReceiveServerChatMessage(frame)) return;
            if (!ProtocolChat.TryDecode(frame, out var message)) return;
            if (message.Kind == ChatMessageKind.Rejected)
            {
                Error?.Invoke(language == "ru" ? "Сервер отклонил сообщение: " + message.ErrorCode : "Chat rejected: " + message.ErrorCode);
                return;
            }
            bool own = message.PlayerId == session.Snapshot.PlayerId;
            string name = own ? session.Snapshot.DisplayName : socialPeers?.Invoke()?.FirstOrDefault(p => p.Kind == WorldEntityKind.Player && p.Id == message.PlayerId)?.Character?.DisplayName ?? message.PlayerId.ToString();
            AppendChat(ChatChannel.All, name + ": " + message.Text);
            if (showBalloons && frame.PacketType == ProtocolChat.Success)
                GetWorldAnnotations().ShowPlayerBubble(message.PlayerId, message.Text);
            if (own) playSound?.Invoke("outgoing_chat");
            RefreshChat(chatView);
        }

        void ShowRewardChat(GameplayUiState previous, GameplayUiState reward)
        {
            if (reward.IsTutorial || !showNpcMessagesInChat) return;
            foreach (var item in reward.Inventory)
            {
                if (item.IsEmpty || item.ItemType != 9) continue;
                var old = previous.Inventory[item.Slot];
                if (old.ItemType == item.ItemType && old.ItemId == item.ItemId && old.Quantity >= item.Quantity) continue;
                int? kind = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.ChestKind;
                if (kind is 0 or 1) AppendChat(ChatChannel.All, FFOne.Content.LocalizationCatalog.Lookup(
                    kind == 0 ? "ui.hud.chat.reward.crate" : "ui.hud.chat.reward.egg", language));
            }
            AddCurrency("ui.hud.chat.reward.taros", previous.Taros, reward.Taros);
            AddCurrency("ui.hud.chat.reward.fusion_matter", previous.FusionMatter, reward.FusionMatter);
            RefreshChat(chatView);

            void AddCurrency(string key, int before, int total)
            {
                if (total <= before) return;
                AppendChat(ChatChannel.All, FFOne.Content.LocalizationCatalog.Lookup(key, language)
                    .Replace("{amount}", (total - before).ToString()).Replace("{total}", total.ToString()));
            }
        }

        internal void ShowEggPickupChat()
        {
            if (session.Snapshot.IsTutorial || !showNpcMessagesInChat) return;
            AppendChat(ChatChannel.All, T("Collected a Coco egg.", "Подобрано яйцо Коко."));
            RefreshChat(chatView);
        }

        void AppendChat(ChatChannel channel, string text, string tone = null)
        {
            var lines = chatHistory[(int)channel]; lines.Add((text, tone));
            if (lines.Count > 50) lines.RemoveAt(0);
        }

        FriendInfo SelectedChatFriend => whisperTarget != null ? social.Friends.FirstOrDefault(f => f != null && !f.Blocked && f.Uid == whisperTarget.Uid) : null;
        bool GroupChatAvailable => socialClient != null && social.Members.Count > 1;
        bool CanSendChat => chatChannel switch {
            ChatChannel.Group => GroupChatAvailable,
            ChatChannel.Friend => socialClient != null && SelectedChatFriend != null,
            _ => sendChat != null
        };
        void UpdateChatSend()
        {
            if (chatView == null) return;
            chatView.Q<Button>("chat-send").SetEnabled(CanSendChat && !chatPending && !string.IsNullOrWhiteSpace(chatView.Q<TextField>("chat-input").value));
        }

        void SelectChatChannel(ChatChannel channel)
        {
            if (interactionSuspended || prompt != null || gameGuide != null || Windows.Current != GameplayWindow.NanoCom || session.Snapshot.IsTutorial) return;
            chatChannel = channel;
            LocalizeChat(); RefreshChat(chatView); chatView.Q<TextField>("chat-input").Focus();
        }

        void BindChat(VisualElement root)
        {
            GameplayChatPresentation.Configure(root);
            locationDebug = FFView.RequireClass<Label>(root, "ff-location-debug");
            FFWidgets.Font(locationDebug, true, 12);
            var input = FFView.Require<TextField>(root, "chat-input");
            FFWidgets.BindTextEditing(input);
            input.maxLength = ProtocolChat.MaximumTextUnits;
            FFWidgets.Font(input, true, 12);
            var send = FFView.Require<Button>(root, "chat-send");
            root.Q<Button>("chat-menu").clicked += () => ToggleQuickChat(false);
            root.Q<Button>("chat-emotes").clicked += () => ToggleQuickChat(true);
            root.Q<Button>("chat-group").clicked += () => SelectChatChannel(ChatChannel.Group);
            root.Q<Button>("chat-friend").clicked += () => SelectChatChannel(ChatChannel.Friend);
            root.Q<Button>("chat-all").clicked += () => SelectChatChannel(ChatChannel.All);
            var window = root.Q("chat-window");
            BuildBuddy(root);
            emptyChatChannel = root.Q(className: "ff-chat-empty-channel");
            if (emptyChatChannel != null)
                emptyChatText = FFView.RequireClass<Label>(emptyChatChannel, "ff-chat-empty-text");
            else
            {
                // Code-only builds may still load the previous Chat.uxml from Addressables.
                emptyChatChannel = FFWidgets.Group(window, "chat-empty-channel");
                emptyChatChannel.AddToClassList("ff-chat-empty-channel");
                FFStyle.Background(emptyChatChannel, "ui/en/gameplay/chat/empty-state-background", Vector4.zero);
                legacyEmptyChatIcon = FFWidgets.Image(emptyChatChannel, "chat-channel-icon", "ui/en/gameplay/chat/icons/buddy", 10, 15, 29, 29);
                emptyChatText = new Label { pickingMode = PickingMode.Ignore };
                emptyChatText.AddToClassList("ff-chat-empty-text");
                FFWidgets.Font(emptyChatText, true, 12);
                emptyChatText.style.color = Color.white;
                emptyChatText.style.whiteSpace = WhiteSpace.Normal;
                emptyChatText.style.position = Position.Absolute;
                emptyChatText.style.left = 50;
                emptyChatText.style.top = 7;
                emptyChatText.style.right = 5;
                emptyChatChannel.Add(emptyChatText);
            }
            var resize = FFView.RequireClass<Button>(root, "ff-chat-resize");
            Vector2 initialSize = default, initialPointer = default;
            void LayoutChat()
            {
                float w = window.resolvedStyle.width, h = window.resolvedStyle.height;
                if (!(w > 0 && h > 0)) return;
                root.EnableInClassList("ff-chat-compact", w < 390);
                resize.style.left = w - 29;
                FFWidgets.Rect(emptyChatChannel, w / 16, (h - 53) / 2 - 15, w - w / 8, 60);
                PlaceChat();
            }
            window.RegisterCallback<GeometryChangedEvent>(_ => LayoutChat());
            resize.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 || interactionSuspended || prompt != null || gameGuide != null) return;
                initialPointer = e.position; initialSize = new Vector2(window.resolvedStyle.width, window.resolvedStyle.height);
                resize.CapturePointer(e.pointerId);
            }, TrickleDown.TrickleDown);
            resize.RegisterCallback<PointerMoveEvent>(e => {
                if (!resize.HasPointerCapture(e.pointerId) || e.pressedButtons == 0) return;
                Vector2 delta = ((Vector2)e.position - initialPointer) / RetrobutionLayout.Scale(parent.resolvedStyle.height);
                window.style.width = Mathf.Clamp(initialSize.x + delta.x, 325, 550); window.style.height = Mathf.Clamp(initialSize.y - delta.y, 135, 250);
            });
            resize.RegisterCallback<PointerUpEvent>(e => { if (resize.HasPointerCapture(e.pointerId)) resize.ReleasePointer(e.pointerId); });
            input.RegisterValueChangedCallback(_ => UpdateChatSend());
            send.clicked += () => { playSound?.Invoke("button"); _ = SubmitChat(input, send); };
            input.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
                {
                    historyCursor = Mathf.Clamp(historyCursor + (evt.keyCode == KeyCode.UpArrow ? -1 : 1), 0, sentHistory.Count);
                    if (historyCursor < sentHistory.Count) { input.value = sentHistory[historyCursor]; input.SelectRange(input.value.Length, input.value.Length); }
                    evt.StopPropagation(); return;
                }
                if (evt.keyCode != UnityEngine.KeyCode.Return && evt.keyCode != UnityEngine.KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                if (string.IsNullOrWhiteSpace(input.value)) Windows.Close();
                else if (send.enabledInHierarchy) _ = SubmitChat(input, send);
            });
            UpdateChatSend(); RefreshChat(root);
        }

        async Task SubmitChat(TextField input, Button send)
        {
            if (chatPending || !CanSendChat || disposed || interactionSuspended || gameGuide != null || prompt != null || session.Snapshot.IsTutorial || Windows.Current != GameplayWindow.NanoCom) return;
            string submitted = input.value;
            string command = submitted.Trim();
            if (string.Equals(command, "/viewloc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command, "/viweloc", StringComparison.OrdinalIgnoreCase))
            {
                if (locationDebugWorld)
                {
                    showLocationDebug = !showLocationDebug;
                    RefreshLocationDebug();
                }
                else
                {
                    string feedback = LocalizationCatalog.Lookup("ui.chat.command.gm.access", language)
                        .Replace("{level}", LocationDebugMaxUserLevel.ToString());
                    AppendChat(chatChannel, feedback);
                    RefreshChat(chatView);
                }
                RememberChatSubmission(submitted, input);
                UpdateChatSend();
                return;
            }
            if (chatChannel == ChatChannel.All && submitted == "/cashmall")
            {
                input.SetValueWithoutNotify("");
                Windows.Open(GameplayWindow.CashMall);
                return;
            }
            chatPending = true; send.SetEnabled(false);
            try
            {
                Task request = chatChannel switch {
                    ChatChannel.Friend => socialClient.SendFriendChatAsync(SelectedChatFriend.Slot, SelectedChatFriend.Uid, submitted, socialToken),
                    ChatChannel.Group => socialClient.SendGroupChatAsync(submitted, socialToken),
                    _ => sendChat(submitted)
                };
                await request;
                if (disposed) return;
                RememberChatSubmission(submitted, input);
            }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
            finally
            {
                chatPending = false;
                if (!disposed) UpdateChatSend();
            }
        }

        void RememberChatSubmission(string submitted, TextField input)
        {
            sentHistory.Add(submitted);
            if (sentHistory.Count > 20) sentHistory.RemoveAt(0);
            historyCursor = sentHistory.Count;
            // Preserve a newer draft entered while the write was pending.
            if (input.value == submitted) input.SetValueWithoutNotify("");
            if (Windows.Current == GameplayWindow.NanoCom) Windows.Close();
        }

        void SetChatActive(bool active)
        {
            if (chatView == null) return;
            active = active && gameGuide == null && !interactionSuspended && !session.Snapshot.IsTutorial;
            if (!active) { CloseQuickChat(); CloseBuddyEntry(); }
            RefreshBuddy();
            if (session.Snapshot.IsTutorial) chatView.Q<TextField>("chat-input").SetValueWithoutNotify("");
            LocalizeChat();
            GameplayChatPresentation.SetActive(chatView, active);
            if (active) chatView.Q<TextField>("chat-input").schedule.Execute(() => {
                if (!disposed && gameGuide == null && !interactionSuspended && !session.Snapshot.IsTutorial && Windows.Current == GameplayWindow.NanoCom) chatView.Q<TextField>("chat-input").Focus();
            });
        }

        void LocalizeChat()
        {
            if (chatView == null) return;
            GameplayChatPresentation.Localize(chatView, language);
            bool friend = chatChannel == ChatChannel.Friend, group = chatChannel == ChatChannel.Group;
            chatView.EnableInClassList("ff-chat-channel-friend", friend);
            chatView.EnableInClassList("ff-chat-channel-group", group);
            chatView.EnableInClassList("ff-chat-channel-all", !friend && !group);
            string role = group ? "ff-chat-group-label" : friend ? "ff-chat-friend-label" : "ff-chat-all-label";
            FFView.RequireClass<Label>(chatView, "ff-chat-active-tab-label").text = FFView.RequireClass<Label>(chatView, role).text;
            emptyChatText.text = LocalizationCatalog.Lookup(group ? "ui.hud.chat.empty.group" : "ui.hud.chat.empty.buddy", language);
            if (legacyEmptyChatIcon != null)
                legacyEmptyChatIcon.image = FFWidgets.Texture("ui/en/gameplay/chat/icons/" + (group ? "group" : "buddy"));
        }

        void PlaceChat()
        {
            if (chatView == null) return;
            float height = parent.resolvedStyle.height;
            GameplayChatPresentation.Place(chatView, height);
            if (buddyPanel != null) {
                var panel = FFView.Require<VisualElement>(chatView, "chat-window");
                float scale = RetrobutionLayout.Scale(height);
                if (!float.IsFinite(panel.resolvedStyle.width) || !float.IsFinite(height) || height <= 0) return;
                buddyPanel.style.left = panel.resolvedStyle.width * scale; buddyPanel.style.top = height - 150 * scale;
                buddyPanel.style.transformOrigin = new TransformOrigin(0, 0); buddyPanel.style.scale = new Scale(new Vector3(scale, scale, 1));
            }
        }

        void RefreshChat(VisualElement root)
        {
            RefreshBuddy();
            var list = FFView.Require<ScrollView>(root, "chat-history");
            list.Clear();
            bool empty = chatChannel switch
            {
                ChatChannel.Group => !GroupChatAvailable,
                ChatChannel.Friend => !social.Friends.Any(f => f != null && !f.Blocked),
                _ => false
            };
            root.EnableInClassList("ff-chat-channel-empty", empty);
            if (legacyEmptyChatIcon != null)
            {
                emptyChatChannel.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
                list.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
            }
            UpdateChatSend();
            if (empty) return;
            foreach (var line in chatHistory[(int)chatChannel])
            {
                GameplayChatPresentation.AddLine(list, line.Text, line.Tone);
            }
            GameplayChatPresentation.ScrollToLatest(list);
        }
    }

    public static class GameplayChatPresentation
    {
        public static void SetActive(VisualElement root, bool active)
        {
            // CnGuiChat.DoChatWindow disables only the history box while idle.
            // Match IMGUI's disabled background without fading received text.
            var background = FFView.RequireClass<VisualElement>(root, "ff-chat-history-area").Q(className: "retro-background");
            if (background != null) background.style.opacity = active ? 1 : .5f;
            FFView.RequireClass<ScrollView>(root, "ff-chat-history").verticalScrollerVisibility =
                active ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            root.EnableInClassList("ff-chat-root--active", active);
            foreach (string id in new[] { "chat-input", "chat-send", "chat-emotes" })
                FFView.RequireClass<VisualElement>(root, "ff-" + id).style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            FFView.RequireClass<VisualElement>(root, "ff-chat-inactive-input").style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
        }

        internal static void Place(VisualElement root, float height)
        {
            var panel = FFView.RequireClass<VisualElement>(root, "ff-chat-window");
            float h = panel.resolvedStyle.height;
            if (!float.IsFinite(h)) h = panel.style.height.value.value;
            if (!float.IsFinite(height) || height <= 0 || h <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            panel.style.left = 0;
            panel.style.top = height - h * scale;
            panel.style.transformOrigin = new TransformOrigin(0, 0);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        internal static void Localize(VisualElement root, string language)
        {
            void SetText(string id, string en, string ru) => FFView.RequireClass<Label>(root, "ff-" + id).text = language == "ru" ? ru : en;
            SetText("chat-all-label", "ALL", "ВСЕ");
            SetText("chat-friend-label", "FRIEND", "ДРУЗЬЯ");
            SetText("chat-group-label", "GROUP", "ГРУППА");
            SetText("chat-active-tab-label", "ALL", "ВСЕ");
            SetText("chat-inactive-hint", "Press ENTER to access chat and menus.", "Нажмите ENTER для чата и меню.");
        }

        internal static void AddLine(ScrollView list, string text, string tone = null)
        {
            var row = FFView.InstantiateClass("ui/views/Retrobution/ChatRow", "ff-chat-row");
            var label = FFView.RequireClass<Label>(row, "ff-chat-message");
            label.enableRichText = false;
            label.text = text;
            if (tone != null) label.AddToClassList("ff-chat-message--" + tone);
            FFWidgets.Font(label, true, 12);
            list.Add(row);
        }

        internal static void ScrollToLatest(ScrollView list)
        {
            if (list.contentContainer.childCount == 0) return;
            var last = list.contentContainer[list.contentContainer.childCount - 1];
            last.schedule.Execute(() => { if (list.Contains(last)) list.ScrollTo(last); });
        }

        public static void Configure(VisualElement root)
        {
            // CnGuiChat.Start replaces serialized WindowRect with the display
            // option defaults (440 x 150), before GUILayout places its tabs.
            // Apply after instantiation so existing shipped UXML is corrected too.
            SetActive(root, false);
            var history = FFView.RequireClass<ScrollView>(root, "ff-chat-history");
            history.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Query<TextElement>().ForEach(text => { text.enableRichText = false; FFWidgets.Font(text, true, 12); });
            // The body/input use Chalet, while largeChatTab and largeChatTab_sel
            // explicitly use JEFFE___12 in fusionfallchatskin.
            foreach (string id in new[] { "chat-all-label", "chat-friend-label", "chat-group-label", "chat-active-tab-label" })
                FFWidgets.Font(FFView.RequireClass<Label>(root, "ff-" + id), false, 12);
        }
    }
}
