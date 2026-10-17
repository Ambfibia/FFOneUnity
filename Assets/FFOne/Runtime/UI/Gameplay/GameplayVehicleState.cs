using System.Collections.Generic;
using System.Linq;
using FFOne.Network;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        public bool VehicleMounted { get; private set; }
        internal GameplayUiState WithVehicle(bool mounted)
        {
            if (mounted == VehicleMounted) return this;
            var next = (GameplayUiState)MemberwiseClone(); next.VehicleMounted = mounted; return next;
        }
        internal GameplayUiState WithExpiredItems(IReadOnlyList<ExpiredItemSlot> slots)
        {
            var next = (GameplayUiState)MemberwiseClone();
            var equipment = Equipment.ToArray(); var inventory = Inventory.ToArray(); var bank = Bank.ToArray();
            foreach (var item in slots)
            {
                var empty = new GameplayItemState(item.Slot, 0, 0, 0, 0);
                if (item.Container == ItemContainer.Equipment)
                { equipment[item.Slot] = empty; if (item.Slot == ProtocolVehicle.EquipmentSlot) next.VehicleMounted = false; }
                else if (item.Container == ItemContainer.Inventory) inventory[item.Slot] = empty;
                // Expiry may include an unopened bank. Its contents will arrive on the next open.
                else if (BankNumber >= 0 && bank.Length == ProtocolServices.BankCapacity) bank[item.Slot] = empty;
            }
            next.Equipment = System.Array.AsReadOnly(equipment); next.Inventory = System.Array.AsReadOnly(inventory); next.Bank = System.Array.AsReadOnly(bank);
            return next;
        }
    }
    public sealed partial class GameplayUiSession
    {
        bool ApplyVehicleFrame(ProtocolFrame frame)
        {
            if (ProtocolVehicle.TryDecode(frame, out var v))
            {
                if (!v.Failed && (v.Type != ProtocolVehicle.StateChanged || v.PlayerId == Snapshot.PlayerId)) Publish(Snapshot.WithVehicle(v.Mounted));
                return true;
            }
            if (!ProtocolVehicle.TryDecodeExpired(frame, out var slots)) return false;
            Publish(Snapshot.WithExpiredItems(slots)); return true;
        }
    }
}
