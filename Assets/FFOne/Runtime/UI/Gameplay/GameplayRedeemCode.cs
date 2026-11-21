using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string RedeemSuccessMessage = "You have redeemed code items";
        TaskCompletionSource<string> redeemReply;

        bool CanRedeem => !disposed && sendChat != null && session.Snapshot.IsActive && !session.Snapshot.IsTutorial &&
            !interactionSuspended && !inventoryPending && !TradeWindowActive && servicePending == 0 && !serviceClosing && !serviceFaulted &&
            Windows.Current is GameplayWindow.Inventory or GameplayWindow.Nanos or GameplayWindow.Service;

        void UpdateRedeemButton()
        {
            var button = Windows.View?.Q<Button>(className: "inventory-redeem");
            button?.SetEnabled(CanRedeem);
        }

        void OpenRedeemCode()
        {
            if (!CanRedeem || prompt != null || redeemReply != null) return;

            playSound?.Invoke("button");
            ShowPrompt(T("REDEEM CODE", "ВВЕСТИ КОД"),
                T("Have a code? Enter it to receive an exclusive item.", "Введите код, чтобы получить особый предмет."),
                "", SubmitRedeemCode, null);
            var field = FFView.RequireClass<TextField>(prompt.View, "gameplay-prompt-value");
            field.maxLength = 32;
            FFView.RequireClass<Button>(prompt.View, "gameplay-prompt-confirm").text = T("REDEEM", "ПРИМЕНИТЬ");
        }

        async Task SubmitRedeemCode(string code)
        {
            if (code.Length < 3 || code.Length > 32)
                throw new ArgumentException(T("Enter a code of 3 to 32 characters.", "Введите код длиной от 3 до 32 символов."));
            if (code.Any(char.IsWhiteSpace))
                throw new ArgumentException(T("Codes cannot contain spaces.", "Код не должен содержать пробелы."));
            if (redeemReply != null || !CanRedeem) return;

            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            redeemReply = reply;
            try
            {
                await sendChat("/redeem " + code);
                if (await Task.WhenAny(reply.Task, Task.Delay(TimeSpan.FromSeconds(10))) != reply.Task)
                    throw new TimeoutException(T("The server did not respond to the code.", "Сервер не ответил на запрос активации кода."));
                string result = await reply.Task;
                if (result != RedeemSuccessMessage) throw new InvalidOperationException(RedeemMessage(result));
            }
            finally { if (redeemReply == reply) redeemReply = null; }
        }

        string RedeemMessage(string message) => message switch
        {
            RedeemSuccessMessage => T("Code redeemed. Check your inventory.", "Код активирован. Награда в инвентаре."),
            "/redeem: Unknown code" => T("Unknown code.", "Неизвестный код."),
            "/redeem: You have already redeemed this code item" => T("You have already redeemed this code.", "Вы уже активировали этот код."),
            "/redeem: Not enough space in inventory" => T("Not enough space in your inventory.", "В инвентаре недостаточно места."),
            "/redeem: Finish trading before redeeming a code" => T("Close the trade before redeeming a code.", "Завершите обмен перед активацией кода."),
            "/redeem: Could not save code rewards; try again later" => T("The server could not save the reward. Try again later.", "Сервер не смог сохранить награду. Попробуйте позже."),
            _ => message
        };

        bool ReceiveServerChatMessage(ProtocolFrame frame)
        {
            if (!ProtocolChat.TryDecodeServerMessage(frame, out var message)) return false;
            if (string.IsNullOrWhiteSpace(message)) return true;
            if (message == RedeemSuccessMessage || message.StartsWith("/redeem: ", StringComparison.Ordinal))
            {
                redeemReply?.TrySetResult(message);
                message = RedeemMessage(message);
            }
            AppendChat(ChatChannel.All, message);
            RefreshChat(chatView);
            return true;
        }
    }
}
