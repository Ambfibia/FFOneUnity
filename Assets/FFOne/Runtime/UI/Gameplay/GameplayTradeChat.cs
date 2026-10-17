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
        readonly List<TradeChatMessage> tradeChatHistory = new();
        bool tradeChatSending;

        bool ReceiveTradeChat(ProtocolFrame frame)
        {
            if (!ProtocolTradeChat.TryDecode(frame, out var message)) return false;
            var state = trade?.Snapshot;
            if (disposed || state == null || !state.IsTrading || state.FromId != message.FromId || state.ToId != message.ToId) return true;
            if (message.Rejected)
            {
                if (message.SenderId == state.LocalPlayerId)
                    Error?.Invoke((language == "ru" ? "Сообщение обмена отклонено: " : "Trade chat rejected: ") + message.ErrorCode);
                return true;
            }
            tradeChatHistory.Add(message);
            if (tradeChatHistory.Count > 50) tradeChatHistory.RemoveAt(0);
            RefreshTradeChat();
            return true;
        }

        void BindTradeChat(VisualElement root)
        {
            var input = FFView.Require<TextField>(root, "trade-chat-input");
            var send = FFView.Require<Button>(root, "trade-chat-send");
            input.maxLength = ProtocolTradeChat.MaximumTextUnits;
            FFView.Require<Label>(root, "trade-chat-send-label").text = language == "ru" ? "ПОСЛАТЬ" : "SEND";
            void Enabled() => send.SetEnabled(tradeActions?.Chat != null && !tradeChatSending && !string.IsNullOrWhiteSpace(input.value));
            input.SetEnabled(tradeActions?.Chat != null);
            input.RegisterValueChangedCallback(_ => Enabled());
            send.clicked += () => _ = SubmitTradeChat(input, send);
            input.RegisterCallback<KeyDownEvent>(evt => {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                if (send.enabledInHierarchy) _ = SubmitTradeChat(input, send);
            });
            Enabled(); RefreshTradeChat(root);
        }

        async Task SubmitTradeChat(TextField input, Button send)
        {
            var state = trade?.Snapshot;
            if (disposed || interactionSuspended || tradeChatSending || state?.IsTrading != true || tradeActions?.Chat == null || Windows.Current != GameplayWindow.Trade) return;
            string submitted = input.value;
            tradeChatSending = true; send.SetEnabled(false);
            try
            {
                await tradeActions.Chat(state.FromId, state.ToId, submitted);
                // History is populated only by server echo. Keep any newer draft entered meanwhile.
                if (!disposed && input.value == submitted) input.SetValueWithoutNotify("");
            }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
            finally
            {
                tradeChatSending = false;
                if (!disposed) send.SetEnabled(tradeActions?.Chat != null && !string.IsNullOrWhiteSpace(input.value));
            }
        }

        void RefreshTradeChat(VisualElement root = null)
        {
            root ??= Windows.Current == GameplayWindow.Trade ? Windows.View : null;
            if (root == null) return;
            var list = FFView.Require<ScrollView>(root, "trade-chat-history"); list.Clear();
            foreach (var message in tradeChatHistory)
            {
                string sender = message.SenderId == session.Snapshot.PlayerId ? session.Snapshot.DisplayName : message.SenderId.ToString();
                var row = FFView.Instantiate("ui/views/Retrobution/ChatRow", "chat-row");
                var label = FFView.Require<Label>(row, "chat-message");
                label.text = sender + ": " + message.Text; label.enableRichText = false;
                FFWidgets.Font(label, true, 12); list.Add(row);
            }
            if (list.contentContainer.childCount > 0) list.schedule.Execute(() => {
                if (list.contentContainer.childCount > 0) list.ScrollTo(list.contentContainer[list.contentContainer.childCount - 1]);
            });
        }
    }
}
