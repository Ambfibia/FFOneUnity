using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task SendTradeOfferAsync(int peerId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.OfferRequest, CurrentTradePlayer(), peerId, cancellationToken);
        public Task SendTradeAcceptAsync(int fromId, int toId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.AcceptRequest, fromId, toId, cancellationToken);
        public Task SendTradeRefuseAsync(int fromId, int toId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.RefuseRequest, fromId, toId, cancellationToken);
        public Task SendTradeCancelOfferAsync(int fromId, int toId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.CancelOfferRequest, fromId, toId, cancellationToken);
        public Task SendTradeConfirmAsync(int fromId, int toId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.ConfirmRequest, fromId, toId, cancellationToken);
        public Task SendTradeCancelAsync(int fromId, int toId, CancellationToken cancellationToken = default)
            => SendTradeParticipantsAsync(ProtocolTrade.CancelRequest, fromId, toId, cancellationToken);
        public Task SendTradeRegisterCashAsync(int fromId, int toId, int taros, CancellationToken cancellationToken = default)
        {
            int player = CurrentTradePlayer();
            return SendTradeFrameAsync(ProtocolTrade.RegisterCashRequest, ProtocolTrade.CashPayload(player, fromId, toId, taros), player, cancellationToken);
        }
        public Task SendTradeRegisterItemAsync(int fromId, int toId, TradeItem item, CancellationToken cancellationToken = default)
            => SendTradeItemAsync(ProtocolTrade.RegisterItemRequest, fromId, toId, item, cancellationToken);
        public Task SendTradeUnregisterItemAsync(int fromId, int toId, TradeItem item, CancellationToken cancellationToken = default)
            => SendTradeItemAsync(ProtocolTrade.UnregisterItemRequest, fromId, toId, item, cancellationToken);

        int CurrentTradePlayer()
        {
            ThrowIfDisposed(); if (!IsWorldLoaded) throw new InvalidOperationException("The world has not finished loading."); return world.PlayerId;
        }
        Task SendTradeParticipantsAsync(uint type, int from, int to, CancellationToken cancellation)
        {
            int player = CurrentTradePlayer();
            if ((type == ProtocolTrade.AcceptRequest || type == ProtocolTrade.RefuseRequest) && player != to)
                throw new ArgumentException("Only the recipient may answer a trade offer.");
            if (type == ProtocolTrade.CancelOfferRequest && player != from) throw new ArgumentException("Only the sender may cancel a pending offer.");
            return SendTradeFrameAsync(type, ProtocolTrade.Participants(player, from, to), player, cancellation);
        }
        Task SendTradeItemAsync(uint type, int from, int to, TradeItem item, CancellationToken cancellation)
        {
            int player = CurrentTradePlayer(); return SendTradeFrameAsync(type, ProtocolTrade.ItemPayload(player, from, to, item), player, cancellation);
        }
        // Transport completion is not transaction success. Server replies drive GameplayTradeSession.
        async Task SendTradeFrameAsync(uint type, byte[] bytes, int player, CancellationToken cancellation)
        {
            long generation = Interlocked.Read(ref shardGeneration);
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    if (CurrentTradePlayer() != player || generation != Interlocked.Read(ref shardGeneration))
                        throw new InvalidOperationException("The shard changed before the trade command was sent.");
                    await shard.SendAsync(type, bytes, token.Token).ConfigureAwait(false);
                }
                finally { operations.Release(); }
            }
        }
    }
}
