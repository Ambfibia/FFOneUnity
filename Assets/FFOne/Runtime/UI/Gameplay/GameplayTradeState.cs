using System;
using System.Collections.Generic;
using FFOne.Network;

namespace FFOne.UI
{
    public enum GameplayTradePhase
    {
        Idle, OutgoingOffer, IncomingOffer, Active, AwaitingSettlement,
        RefusalPending, CancellationPending, Refused, Cancelled, Aborted, Completed
    }

    public sealed class GameplayTradeState
    {
        public int LocalPlayerId { get; internal set; }
        public int FromId { get; internal set; }
        public int ToId { get; internal set; }
        public int PeerId => FromId == LocalPlayerId ? ToId : FromId;
        public GameplayTradePhase Phase { get; internal set; }
        public bool IsTrading => Phase == GameplayTradePhase.Active || Phase == GameplayTradePhase.AwaitingSettlement;
        public IReadOnlyList<TradeItem> OwnItems { get; internal set; }
        public IReadOnlyList<TradeItem> OtherItems { get; internal set; }
        public int OwnTaros { get; internal set; }
        public int OtherTaros { get; internal set; }
        public bool OwnConfirmed { get; internal set; }
        public bool OtherConfirmed { get; internal set; }
        public TradeUpdateKind? LastFailure { get; internal set; }
        public int LastErrorCode { get; internal set; }
        public TradeUpdate Receipt { get; internal set; }
        /// <summary>Completion records omit expiry/full option metadata; a fresh inventory snapshot is required for those fields.</summary>
        public bool InventoryMetadataRefreshRequired => Phase == GameplayTradePhase.Completed;

        internal GameplayTradeState(int localPlayerId)
        {
            LocalPlayerId = localPlayerId;
            OwnItems = Array.AsReadOnly(new TradeItem[ProtocolTrade.OfferSlots]);
            OtherItems = Array.AsReadOnly(new TradeItem[ProtocolTrade.OfferSlots]);
        }
        internal GameplayTradeState Copy() => (GameplayTradeState)MemberwiseClone();
    }
}
