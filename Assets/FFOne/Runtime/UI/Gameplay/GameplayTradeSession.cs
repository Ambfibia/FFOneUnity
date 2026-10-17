using System;
using FFOne.Network;

namespace FFOne.UI
{
    /// <summary>Participant-scoped server-driven trade reducer. Outgoing offer/cancel flags describe intent, never server success.</summary>
    public sealed class GameplayTradeSession
    {
        public GameplayTradeState Snapshot { get; private set; }
        public event Action<GameplayTradeState> Changed;
        public event Action<GameplayTradeState> Completed;
        GameplayTradeSession cancellingTrade;

        public GameplayTradeSession(int localPlayerId)
        {
            if (localPlayerId <= 0) throw new ArgumentOutOfRangeException(nameof(localPlayerId));
            Snapshot = new GameplayTradeState(localPlayerId);
        }

        public void BeginOutgoingOffer(int peerId)
        {
            if (peerId <= 0 || peerId == Snapshot.LocalPlayerId) throw new ArgumentOutOfRangeException(nameof(peerId));
            if (Snapshot.IsTrading || Snapshot.Phase == GameplayTradePhase.IncomingOffer || Snapshot.Phase == GameplayTradePhase.OutgoingOffer)
                throw new InvalidOperationException("A trade interaction is already open.");
            // Cancellation has no self-ack on the current server. A new local offer may be sent,
            // but preserve the previous acknowledged offers for a settlement already in flight.
            if (Snapshot.Phase == GameplayTradePhase.CancellationPending)
                cancellingTrade = new GameplayTradeSession(Snapshot.LocalPlayerId) { Snapshot = Snapshot };
            Publish(new GameplayTradeState(Snapshot.LocalPlayerId) { FromId = Snapshot.LocalPlayerId, ToId = peerId, Phase = GameplayTradePhase.OutgoingOffer });
        }

        public void MarkRefusalSent()
        {
            if (Snapshot.Phase != GameplayTradePhase.IncomingOffer) throw new InvalidOperationException("There is no incoming offer.");
            var next = Snapshot.Copy(); next.Phase = GameplayTradePhase.RefusalPending; Publish(next);
        }

        public void MarkCancellationSent()
        {
            if (!Snapshot.IsTrading && Snapshot.Phase != GameplayTradePhase.OutgoingOffer) throw new InvalidOperationException("There is no cancellable trade.");
            var next = Snapshot.Copy(); next.Phase = GameplayTradePhase.CancellationPending; Publish(next);
        }

        public void Clear() { cancellingTrade = null; Publish(new GameplayTradeState(Snapshot.LocalPlayerId)); }

        public bool ApplyFrame(ProtocolFrame frame)
        {
            if (!ProtocolTrade.TryDecode(frame, out var update)) return false;
            int local = Snapshot.LocalPlayerId;
            if (update.FromId != local && update.ToId != local) return false;
            if (cancellingTrade != null && update.FromId == cancellingTrade.Snapshot.FromId && update.ToId == cancellingTrade.Snapshot.ToId &&
                update.Kind != TradeUpdateKind.Offer && update.Kind != TradeUpdateKind.Accepted && update.Kind != TradeUpdateKind.OfferAborted &&
                update.Kind != TradeUpdateKind.Refused && update.Kind != TradeUpdateKind.OfferCancelled)
            {
                if (!cancellingTrade.ApplyFrame(frame)) return false;
                var previous = cancellingTrade.Snapshot;
                if (previous.Phase == GameplayTradePhase.Completed)
                { cancellingTrade = null; Completed?.Invoke(previous); }
                else if (previous.Phase == GameplayTradePhase.Cancelled || previous.Phase == GameplayTradePhase.Aborted) cancellingTrade = null;
                return true;
            }
            if (update.Kind == TradeUpdateKind.Offer)
            {
                if (update.ToId != local || Snapshot.IsTrading || Snapshot.Phase == GameplayTradePhase.OutgoingOffer) return false;
                cancellingTrade = null;
                Publish(new GameplayTradeState(local) { FromId = update.FromId, ToId = update.ToId, Phase = GameplayTradePhase.IncomingOffer });
                return true;
            }
            if (Snapshot.FromId != update.FromId || Snapshot.ToId != update.ToId) return false;
            var next = Snapshot.Copy();
            bool live = Snapshot.IsTrading || Snapshot.Phase == GameplayTradePhase.CancellationPending;
            switch (update.Kind)
            {
                case TradeUpdateKind.Accepted:
                    if (Snapshot.Phase != GameplayTradePhase.IncomingOffer && Snapshot.Phase != GameplayTradePhase.OutgoingOffer &&
                        Snapshot.Phase != GameplayTradePhase.RefusalPending && Snapshot.Phase != GameplayTradePhase.CancellationPending) return false;
                    next = new GameplayTradeState(local) { FromId = update.FromId, ToId = update.ToId, Phase = GameplayTradePhase.Active };
                    cancellingTrade = null;
                    break;
                case TradeUpdateKind.Refused:
                    if (live || Snapshot.Phase == GameplayTradePhase.Completed) return false;
                    next.Phase = GameplayTradePhase.Refused; break;
                case TradeUpdateKind.OfferCancelled:
                    if (live && Snapshot.Phase != GameplayTradePhase.CancellationPending) return false;
                    next.Phase = GameplayTradePhase.Cancelled; break;
                case TradeUpdateKind.OfferAborted:
                    if (live) return false;
                    next.Phase = GameplayTradePhase.Aborted; next.LastFailure = update.Kind; next.LastErrorCode = update.ErrorCode; break;
                case TradeUpdateKind.Cancelled:
                    if (!live) return false; next.Phase = GameplayTradePhase.Cancelled; break;
                case TradeUpdateKind.Aborted:
                case TradeUpdateKind.CompletionFailed:
                    if (!live) return false;
                    next.Phase = GameplayTradePhase.Aborted; next.LastFailure = update.Kind; next.LastErrorCode = update.ErrorCode; break;
                case TradeUpdateKind.ItemRegistrationFailed:
                case TradeUpdateKind.ItemUnregistrationFailed:
                case TradeUpdateKind.CashRegistrationFailed:
                    if (!live || update.RequesterId != local) return false;
                    next.LastFailure = update.Kind; next.LastErrorCode = update.ErrorCode; break;
                case TradeUpdateKind.CashRegistered:
                    if (!live) return false;
                    if (update.RequesterId == local) next.OwnTaros = update.Taros; else next.OtherTaros = update.Taros;
                    ResetConfirmations(next); break;
                case TradeUpdateKind.ItemRegistered:
                case TradeUpdateKind.ItemUnregistered:
                    if (!live) return false;
                    var source = update.RequesterId == local ? next.OwnItems : next.OtherItems;
                    var items = new TradeItem[ProtocolTrade.OfferSlots];
                    for (int i = 0; i < items.Length; i++) items[i] = source[i];
                    items[update.Item.TradeSlot] = update.Kind == TradeUpdateKind.ItemRegistered ? update.Item : default;
                    if (update.RequesterId == local) next.OwnItems = Array.AsReadOnly(items); else next.OtherItems = Array.AsReadOnly(items);
                    ResetConfirmations(next); break;
                case TradeUpdateKind.Confirmed:
                    if (!live) return false;
                    if (update.RequesterId == local) next.OwnConfirmed = true; else next.OtherConfirmed = true;
                    if (next.OwnConfirmed && next.OtherConfirmed && next.Phase != GameplayTradePhase.CancellationPending) next.Phase = GameplayTradePhase.AwaitingSettlement;
                    break;
                case TradeUpdateKind.Completed:
                    if (!live) return false;
                    next.Receipt = update; next.Phase = GameplayTradePhase.Completed; next.LastFailure = null;
                    break;
                default: return false;
            }
            Publish(next);
            if (next.Phase == GameplayTradePhase.Completed) Completed?.Invoke(next);
            return true;
        }

        static void ResetConfirmations(GameplayTradeState state)
        {
            state.OwnConfirmed = false; state.OtherConfirmed = false; state.LastFailure = null; state.LastErrorCode = 0;
            if (state.Phase != GameplayTradePhase.CancellationPending) state.Phase = GameplayTradePhase.Active;
        }
        void Publish(GameplayTradeState state) { Snapshot = state; Changed?.Invoke(state); }
    }
}
