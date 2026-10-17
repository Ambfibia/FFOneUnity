using System;
using System.Globalization;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed class GameplayTradeActions
    {
        public Func<int, Task> Offer;
        public Func<int, int, Task> Accept, Refuse, CancelOffer, Confirm, Cancel;
        public Func<int, int, int, Task> Cash;
        public Func<int, int, TradeItem, Task> AddItem, RemoveItem;
        public Func<int, int, string, Task> Chat;
    }

    public sealed partial class GameplayUiController
    {
        GameplayTradeSession trade;
        GameplayTradeActions tradeActions;
        GameplayPrompt prompt;
        bool tradeSending;
        bool TradeWindowActive => trade?.Snapshot.Phase == GameplayTradePhase.Active || trade?.Snapshot.Phase == GameplayTradePhase.AwaitingSettlement;

        public void ConfigureTrade(GameplayTradeSession value, GameplayTradeActions actions)
        {
            if (trade != null) { trade.Changed -= TradeChanged; trade.Completed -= TradeCompleted; }
            trade = value; tradeActions = actions;
            tradeChatHistory.Clear();
            if (trade != null) { trade.Changed += TradeChanged; trade.Completed += TradeCompleted; }
        }

        public async Task OfferTrade(int peerId)
        {
            if (trade == null || tradeActions?.Offer == null || disposed || interactionSuspended || ServiceActive || serviceFaulted || MailBusy || TravelBusy) return;
            trade.BeginOutgoingOffer(peerId);
            try { await tradeActions.Offer(peerId); }
            catch (Exception error) { trade.Clear(); if (!disposed) Error?.Invoke(error.Message); }
        }

        public void ReceiveTrade(ProtocolFrame frame)
        {
            if (ReceiveTradeChat(frame)) return;
            if (trade?.ApplyFrame(frame) == true && frame.PacketType == ProtocolTrade.Accepted)
            { tradeChatHistory.Clear(); RefreshTradeChat(); }
        }

        void TradeChanged(GameplayTradeState state)
        {
            if (disposed) return;
            if (state.Phase == GameplayTradePhase.IncomingOffer)
            {
                if (ServiceActive || serviceFaulted || MailBusy || TravelBusy || prompt != null)
                {
                    _ = RunTrade(async () => { await tradeActions.Refuse(state.FromId, state.ToId); trade.MarkRefusalSent(); });
                    return;
                }
                ShowPrompt(language == "ru" ? "ОБМЕН" : "TRADE",
                    (language == "ru" ? "Игрок предлагает обмен: " : "Trade request from player ") + state.PeerId, null,
                    _ => tradeActions.Accept(state.FromId, state.ToId),
                    async () => { await tradeActions.Refuse(state.FromId, state.ToId); trade.MarkRefusalSent(); });
                return;
            }
            if (state.Phase == GameplayTradePhase.Active || state.Phase == GameplayTradePhase.AwaitingSettlement)
            {
                prompt?.Dispose(); prompt = null;
                if (Windows.Current != GameplayWindow.Trade) Windows.Open(GameplayWindow.Trade);
                RefreshTrade();
            }
            else if (state.Phase != GameplayTradePhase.OutgoingOffer)
            {
                prompt?.Dispose(); prompt = null;
                if (Windows.Current == GameplayWindow.Trade) Windows.Close();
            }
            if (state.LastFailure.HasValue) Error?.Invoke((language == "ru" ? "Ошибка обмена: " : "Trade error: ") + state.LastErrorCode);
        }

        void TradeCompleted(GameplayTradeState state)
        {
            session.ApplyTradeReceipt(state.Receipt, state.OwnItems);
        }

        void ShowPrompt(string title, string body, string value, Func<string, Task> yes, Func<Task> no)
        {
            prompt?.Dispose();
            var next = new GameplayPrompt(parent, title, body, language == "ru", value, yes, no, message => Error?.Invoke(message));
            prompt = next;
            next.View.SetEnabled(!interactionSuspended);
            next.Closed += () => { if (prompt == next) prompt = null; InputGateChanged?.Invoke(); };
            InputGateChanged?.Invoke();
        }

        async Task CancelTrade()
        {
            if (trade == null || tradeSending) return;
            var state = trade.Snapshot;
            await RunTrade(async () => {
                if (state.Phase == GameplayTradePhase.OutgoingOffer) await tradeActions.CancelOffer(state.FromId, state.ToId);
                else await tradeActions.Cancel(state.FromId, state.ToId);
                // This server acknowledges cancellation only to the peer. Keep local intent distinct from server success.
                trade.MarkCancellationSent();
            });
        }

        async Task RunTrade(Func<Task> operation)
        {
            if (disposed || interactionSuspended || tradeSending || operation == null) return;
            tradeSending = true; RefreshTrade();
            try { await operation(); }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
            finally { tradeSending = false; if (!disposed) RefreshTrade(); }
        }

        void BindTrade(VisualElement root)
        {
            var bag = FFView.Instantiate("ui/views/Retrobution/Inventory", "inventory-root");
            BindInventory(bag, false, () => _ = CancelTrade());
            bag.Q("inventory-character")?.SetEnabled(false);
            if (bag.Q("inventory-character") != null) bag.Q("inventory-character").style.display = DisplayStyle.None;
            bag.Q("inventory-character-background").style.display = DisplayStyle.None;
            bag.Q("equipment-panel").style.display = DisplayStyle.None;
            bag.Q<Button>("tab-nanos").SetEnabled(false);
            root.Insert(0, bag);
            for (int i = 0; i < ProtocolTrade.OfferSlots; i++)
            {
                int slot = i;
                FFView.Require<Button>(root, "trade-own-slot-" + i).clicked += () => {
                    var state = trade?.Snapshot;
                    if (state == null || state.OwnItems[slot].IsEmpty || state.Phase != GameplayTradePhase.Active) return;
                    _ = RunTrade(() => tradeActions.RemoveItem(state.FromId, state.ToId, state.OwnItems[slot]));
                };
                FFView.Require<Button>(root, "trade-other-slot-" + i).SetEnabled(false);
            }
            FFView.Require<Button>(root, "trade-submit").clicked += () => {
                var state = trade?.Snapshot;
                if (state?.Phase == GameplayTradePhase.Active && !state.OwnConfirmed)
                    _ = RunTrade(() => tradeActions.Confirm(state.FromId, state.ToId));
            };
            FFView.Require<Button>(root, "trade-add-taros").clicked += () => {
                var state = trade?.Snapshot;
                if (state?.Phase != GameplayTradePhase.Active) return;
                ShowPrompt("TAROS", language == "ru" ? "Сумма для обмена" : "Amount to offer", state.OwnTaros.ToString(), async text => {
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount < 0 || amount > session.Snapshot.Taros)
                        throw new ArgumentException(language == "ru" ? "Недостаточно Taros или неверная сумма." : "Invalid amount or insufficient Taros.");
                    await tradeActions.Cash(state.FromId, state.ToId, amount);
                }, () => Task.CompletedTask);
            };
            BindTradeChat(root);
            root.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12));
        }

        void RefreshTrade()
        {
            if (Windows.Current != GameplayWindow.Trade || trade == null) return;
            var root = Windows.View; var state = trade.Snapshot;
            FFView.Require<Label>(root, "trade-own-name-label").text = session.Snapshot.DisplayName;
            FFView.Require<Image>(root, "trade-own-portrait").image = Hud.Portrait.image;
            FFView.Require<Label>(root, "trade-other-name-label").text = state.PeerId.ToString();
            FFView.Require<Label>(root, "trade-own-taros-label").text = state.OwnTaros.ToString();
            FFView.Require<Label>(root, "trade-other-taros-label").text = state.OtherTaros.ToString();
            FFView.Require<Label>(root, "trade-status-name-label").text = state.OtherConfirmed ? (language == "ru" ? "ПАРТНЁР ПОДТВЕРДИЛ" : "PARTNER CONFIRMED") : "";
            FFView.Require<Label>(root, "trade-status-detail-label").text = state.OwnConfirmed ? (language == "ru" ? "ОЖИДАНИЕ ОБМЕНА" : "WAITING FOR TRADE") : "";
            FFView.Require<Label>(root, "trade-submit-label").text = state.OtherConfirmed ? (language == "ru" ? "ПРИНЯТЬ" : "ACCEPT") : (language == "ru" ? "ПОДТВЕРДИТЬ" : "SUBMIT");
            bool active = state.Phase == GameplayTradePhase.Active && !tradeSending;
            FFView.Require<Button>(root, "trade-submit").SetEnabled(active && !state.OwnConfirmed && tradeActions?.Confirm != null);
            FFView.Require<Button>(root, "trade-add-taros").SetEnabled(active && tradeActions?.Cash != null);
            var catalog = GameplayUiCatalog.Load();
            foreach (string side in new[] { "own", "other" })
                for (int i = 0; i < ProtocolTrade.OfferSlots; i++)
                {
                    var item = (side == "own" ? state.OwnItems : state.OtherItems)[i];
                    string name = "trade-" + side + "-slot-" + i;
                    var button = FFView.Require<Button>(root, name);
                    var info = item.IsEmpty ? null : catalog.Item(item.ItemType, item.ItemId, language);
                    button.tooltip = info?.Name ?? "";
                    FFView.Require<Image>(button, name + "-image").image = info?.IconAddress == null ? null : ContentAssets.Load<Texture2D>(info.IconAddress);
                    FFView.Require<Label>(button, name + "-count").text = info?.MaxStack > 1 && item.Quantity > 1 ? item.Quantity.ToString() : "";
                    if (side == "own") button.SetEnabled(active && tradeActions?.AddItem != null);
                }
        }

        bool TryTradeDrop(VisualElement target, ItemContainer from, int sourceSlot)
        {
            if (Windows.Current != GameplayWindow.Trade || from != ItemContainer.Inventory || trade?.Snapshot.Phase != GameplayTradePhase.Active) return false;
            while (target != null && !target.name.StartsWith("trade-own-slot-", StringComparison.Ordinal)) target = target.parent;
            if (target == null) return false;
            if (!int.TryParse(target.name.Substring("trade-own-slot-".Length), out int slot) || slot < 0 || slot >= 5) return false;
            var state = trade.Snapshot; var item = session.Snapshot.Inventory[sourceSlot];
            if (!state.OwnItems[slot].IsEmpty || item.IsEmpty || tradeActions?.AddItem == null) return true;
            var offer = new TradeItem(item.ItemType, item.ItemId, item.Quantity, sourceSlot, slot);
            _ = RunTrade(() => tradeActions.AddItem(state.FromId, state.ToId, offer));
            return true;
        }

        void DisposeTrade()
        {
            if (trade != null) { trade.Changed -= TradeChanged; trade.Completed -= TradeCompleted; }
            prompt?.Dispose(); prompt = null;
        }
    }
}
