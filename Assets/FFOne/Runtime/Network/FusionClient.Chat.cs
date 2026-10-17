using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        /// <summary>
        /// Sends ordinary local chat, including server slash commands. Completion means the frame was
        /// sent, not that the server accepted it. Observe ProtocolChat replies on NetworkEventReceived.
        /// </summary>
        public async Task SendFreeChatAsync(string text, CancellationToken cancellationToken = default)
        {
            byte[] payload = ProtocolChat.FreeChatPayload(text);
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    if (!IsWorldLoaded) throw new InvalidOperationException("The world has not finished loading.");
                    await shard.SendAsync(ProtocolChat.Request, payload, token.Token).ConfigureAwait(false);
                }
                finally { operations.Release(); }
            }
        }
    }
}
