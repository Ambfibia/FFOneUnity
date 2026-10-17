using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        readonly List<ChatMessage> chatHistory = new();
        VisualElement chatView;
        Func<string, Task> sendChat;
        bool chatPending;
        public event Action<string> Error;

        public void SetChatSender(Func<string, Task> sender) => sendChat = sender;

        public void ReceiveChat(ProtocolFrame frame)
        {
            if (!ProtocolChat.TryDecode(frame, out var message)) return;
            if (message.Kind == ChatMessageKind.Rejected)
            {
                Error?.Invoke(language == "ru" ? "Сервер отклонил сообщение: " + message.ErrorCode : "Chat rejected: " + message.ErrorCode);
                return;
            }
            chatHistory.Add(message);
            if (chatHistory.Count > 50) chatHistory.RemoveAt(0);
            RefreshChat(chatView);
        }

        void BindChat(VisualElement root)
        {
            FFView.Require<ScrollView>(root, "chat-history").horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var input = FFView.Require<TextField>(root, "chat-input");
            input.maxLength = ProtocolChat.MaximumTextUnits;
            FFWidgets.Font(input, true, 12);
            var send = FFView.Require<Button>(root, "chat-send");
            foreach (string name in new[] { "chat-group", "chat-friend", "chat-menu", "chat-emotes" })
                FFView.Require<Button>(root, name).SetEnabled(false);
            root.Query<TextElement>().ForEach(text => FFWidgets.Font(text, true, 12));
            void Enabled() => send.SetEnabled(sendChat != null && !chatPending && !string.IsNullOrWhiteSpace(input.value));
            input.RegisterValueChangedCallback(_ => Enabled());
            send.clicked += () => _ = SubmitChat(input, send);
            input.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode != UnityEngine.KeyCode.Return && evt.keyCode != UnityEngine.KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                if (string.IsNullOrWhiteSpace(input.value)) Windows.Close();
                else if (send.enabledInHierarchy) _ = SubmitChat(input, send);
            });
            Enabled(); RefreshChat(root);
        }

        async Task SubmitChat(TextField input, Button send)
        {
            if (chatPending || sendChat == null || disposed || interactionSuspended || session.Snapshot.IsTutorial || Windows.Current != GameplayWindow.NanoCom) return;
            string submitted = input.value;
            chatPending = true; send.SetEnabled(false);
            try
            {
                await sendChat(submitted);
                if (disposed) return;
                // Do not remove a newer draft entered while the write was pending.
                if (input.value == submitted) input.SetValueWithoutNotify("");
                if (Windows.Current == GameplayWindow.NanoCom) Windows.Close();
            }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
            finally
            {
                chatPending = false;
                if (!disposed) send.SetEnabled(!string.IsNullOrWhiteSpace(input.value));
            }
        }

        void SetChatActive(bool active)
        {
            if (chatView == null) return;
            active = active && !interactionSuspended && !session.Snapshot.IsTutorial;
            if (session.Snapshot.IsTutorial) chatView.Q<TextField>("chat-input").SetValueWithoutNotify("");
            LocalizeChat();
            foreach (string name in new[] { "chat-input", "chat-send", "chat-emotes" })
                FFView.Require<VisualElement>(chatView, name).style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            var hint = chatView.Q("chat-inactive-input");
            if (hint != null) hint.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            if (active) chatView.Q<TextField>("chat-input").schedule.Execute(() => {
                if (!disposed && !interactionSuspended && !session.Snapshot.IsTutorial && Windows.Current == GameplayWindow.NanoCom) chatView.Q<TextField>("chat-input").Focus();
            });
        }

        void LocalizeChat()
        {
            if (chatView == null) return;
            void Label(string name, string en, string ru)
            {
                var label = chatView.Q<Label>(name);
                if (label != null) label.text = language == "ru" ? ru : en;
            }
            Label("chat-all-label", "ALL", "ВСЕ");
            Label("chat-friend-label", "FRIEND", "ДРУЗЬЯ");
            Label("chat-group-label", "GROUP", "ГРУППА");
            Label("chat-active-tab-label", "ALL", "ВСЕ");
            Label("chat-inactive-hint", "Press ENTER to access chat and menus.", "Нажмите ENTER для чата и меню.");
        }

        void PlaceChat()
        {
            if (chatView == null) return;
            var panel = FFView.Require<VisualElement>(chatView, "chat-window");
            float height = parent.resolvedStyle.height, h = panel.resolvedStyle.height;
            if (!float.IsFinite(height) || !float.IsFinite(h) || height <= 0 || h <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            panel.style.left = 0; panel.style.top = height - h * scale;
            panel.style.transformOrigin = new TransformOrigin(0, 0);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        void RefreshChat(VisualElement root)
        {
            var list = FFView.Require<ScrollView>(root, "chat-history");
            list.Clear();
            foreach (var message in chatHistory)
            {
                var row = FFView.Instantiate("ui/views/Retrobution/ChatRow", "chat-row");
                var label = FFView.Require<Label>(row, "chat-message");
                label.enableRichText = false;
                string sender = message.PlayerId == session.Snapshot.PlayerId ? session.Snapshot.DisplayName : message.PlayerId.ToString();
                label.text = sender + ": " + message.Text;
                FFWidgets.Font(label, true, 12);
                list.Add(row);
            }
            if (list.contentContainer.childCount > 0)
                list.schedule.Execute(() => {
                    if (list.contentContainer.childCount > 0) list.ScrollTo(list.contentContainer[list.contentContainer.childCount - 1]);
                });
        }
    }
}
