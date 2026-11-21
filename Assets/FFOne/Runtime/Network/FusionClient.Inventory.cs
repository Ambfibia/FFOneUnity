using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        /// <summary>
        /// Requests a move/swap. Completion confirms transmission only; apply the server's two post-state
        /// records on MoveSuccess. Server validation can silently reject a move, so no success is synthesized.
        /// </summary>
        public Task DeleteInventoryItemAsync(int slot, CancellationToken cancellation = default)
            => SendGameplayAsync(ProtocolInventory.DeleteRequest, ProtocolInventory.DeletePayload(slot), cancellation);

        public Task SendItemMoveAsync(ItemContainer from, int fromSlot, ItemContainer to, int toSlot,
            CancellationToken cancellationToken = default)
        {
            byte[] payload = ProtocolInventory.MovePayload(from, fromSlot, to, toSlot);
            return SendGameplayAsync(ProtocolInventory.MoveRequest, payload, cancellationToken);
        }
    }
}
