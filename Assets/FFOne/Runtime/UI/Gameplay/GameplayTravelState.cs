using System;
using System.Linq;
using FFOne.Network;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        internal GameplayUiState WithCroc(CrocUpdate u)
        {
            var next = (GameplayUiState)MemberwiseClone(); var items = Inventory.ToArray();
            if (u.Combined) items[u.StatsSlot] = new GameplayItemState(u.StatsSlot, 0, 0, 0, 0);
            items[u.LooksSlot] = new GameplayItemState(u.LooksSlot, u.Item.ItemType, u.Item.ItemId, u.Item.Option, u.Item.TimeLimit);
            next.Inventory = Array.AsReadOnly(items); next.Taros = u.Taros; return next;
        }
        internal GameplayUiState WithTravel(TravelUpdate u)
        {
            if (u.Type != ProtocolTravel.WarpOk && u.Type != ProtocolTravel.TripOk) return this;
            var next = WithMailBalance(u.Taros);
            // RustyFusion clears vehicle_speed on every NPC warp, including a
            // warp back to map zero. Do not resurrect the previous mount later.
            if (u.Type == ProtocolTravel.WarpOk) next = next.WithVehicle(false);
            if (u.Type == ProtocolTravel.WarpOk && u.Container is >= 0 and <= 2)
            {
                var p = ProtocolTravel.Words(u.Container, u.Slot, 0, 0, 0);
                ProtocolServices.WriteItem(p, 8, u.Item); next = next.WithGivenItem(p);
            }
            return next;
        }
    }
    public sealed partial class GameplayUiSession
    {
        public void Relocate(int map, int x, int y, int z) => Publish(Snapshot.WithServerLocation(map, x, y, z));
    }
}
