using System;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task StartVendorAsync(int npc, int vendor, CancellationToken token = default)
            => SendGameplayAsync(ProtocolServices.Start, ProtocolServices.Pair(npc, vendor), token);
        public Task RequestVendorTableAsync(int npc, int vendor, CancellationToken token = default)
            => SendGameplayAsync(ProtocolServices.Table, ProtocolServices.Pair(npc, vendor), token);
        public Task BuyVendorItemAsync(int npc, int vendor, int list, EquipmentItem item, int slot, bool restore = false, CancellationToken token = default)
            => SendGameplayAsync(restore ? ProtocolServices.Restore : ProtocolServices.Buy, ProtocolServices.Purchase(npc, vendor, list, item, slot), token);
        public Task SellVendorItemAsync(int slot, int quantity, CancellationToken token = default)
        {
            ProtocolInventory.ValidateSlot(ItemContainer.Inventory, slot);
            if (quantity <= 0 || quantity > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(quantity));
            return SendGameplayAsync(ProtocolServices.Sell, ProtocolServices.Pair(slot, quantity), token);
        }
        public Task BuyBatteryAsync(int npc, int vendor, bool nano, int packs, CancellationToken token = default)
        {
            if (packs <= 0 || packs > 100) throw new ArgumentOutOfRangeException(nameof(packs));
            return SendGameplayAsync(ProtocolServices.Battery, ProtocolServices.Purchase(npc, vendor, 0,
                new EquipmentItem { ItemType = 7, ItemId = nano ? 4 : 3, Option = packs }, 0, true), token);
        }
        public Task OpenBankAsync(int npc = 0, CancellationToken token = default)
            => SendGameplayAsync(ProtocolServices.BankOpen, ProtocolServices.Pair(World?.PlayerId ?? 0, npc), token);
        public Task CloseBankAsync(CancellationToken token = default)
        { var p = new byte[4]; Protocol0104.Write32(p, 0, World?.PlayerId ?? 0); return SendGameplayAsync(ProtocolServices.BankClose, p, token); }
        public Task InteractNpcAsync(int npc, bool active, CancellationToken token = default)
            => SendGameplayAsync(ProtocolServices.Interaction, ProtocolServices.Pair(npc, active ? 1 : 0), token);
    }
}
