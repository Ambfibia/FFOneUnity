using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        public int BankNumber { get; private set; } = -1;
        public IReadOnlyList<GameplayItemState> Bank { get; private set; } = Array.Empty<GameplayItemState>();
        internal GameplayUiState WithService(ServiceUpdate u)
        {
            var next = (GameplayUiState)MemberwiseClone();
            if (u.Type == ProtocolServices.BankOpened)
            {
                next.BankNumber = u.BankNumber; next.Bank = Array.AsReadOnly(u.Bank.Select((item, i) =>
                    new GameplayItemState(i, item.ItemType, item.ItemId, item.Option, item.TimeLimit)).ToArray());
            }
            else if (u.Type == ProtocolServices.BankClosed) { next.BankNumber = -1; next.Bank = Array.Empty<GameplayItemState>(); }
            else if (u.Type == ProtocolServices.BatteryBought)
            { next.Taros = u.Taros; next.WeaponBattery = u.WeaponBattery; next.NanoBattery = u.NanoBattery; }
            else if (u.Type == ProtocolServices.Bought || u.Type == ProtocolServices.Restored || u.Type == ProtocolServices.Sold)
            {
                var inventory = Inventory.ToArray(); var item = u.Type == ProtocolServices.Sold ? u.Remaining : u.Item;
                inventory[u.Slot] = new GameplayItemState(u.Slot, item.ItemType, item.ItemId, item.Option, item.TimeLimit);
                next.Inventory = Array.AsReadOnly(inventory); next.Taros = u.Taros;
            }
            return next;
        }
    }
}
