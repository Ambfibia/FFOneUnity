using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task SendTradeChatAsync(int fromId, int toId, string text, CancellationToken cancellationToken = default)
        {
            int player = CurrentTradePlayer();
            return SendTradeFrameAsync(ProtocolTradeChat.Request, ProtocolTradeChat.Payload(player, fromId, toId, text), player, cancellationToken);
        }
    }
}
