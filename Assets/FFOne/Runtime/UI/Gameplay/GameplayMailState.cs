using System;
using System.Linq;
using FFOne.Network;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        internal GameplayUiState WithMailBalance(int taros)
        { var next = (GameplayUiState)MemberwiseClone(); next.Taros = taros; return next; }
        internal GameplayUiState WithGivenItem(byte[] p)
        {
            Protocol0104.RequireSize(p, 20); int location = Protocol0104.Read32(p, 0), slot = Protocol0104.Read32(p, 4);
            var source = location == 0 ? Equipment : location == 1 ? Inventory : location == 2 ? QuestInventory : null;
            if (source == null || slot < 0 || slot >= source.Count) throw new ProtocolException("Invalid given-item destination.");
            var item = ProtocolServices.Item(p, 8);
            if (location == 0 && !ProtocolInventory.Fits(ItemContainer.Equipment, slot, item.ItemType, item.ItemId)) throw new ProtocolException("Invalid given equipment type.");
            var items = source.ToArray(); items[slot] = new GameplayItemState(slot, item.ItemType, item.ItemId, item.Option, item.TimeLimit);
            var next = (GameplayUiState)MemberwiseClone();
            if (location == 0) next.Equipment = Array.AsReadOnly(items); else if (location == 1) next.Inventory = Array.AsReadOnly(items); else next.QuestInventory = Array.AsReadOnly(items);
            return next;
        }
    }
}
