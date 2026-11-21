using FFOne.Integration;
using FFOne.Network;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    /// <summary>Copies accepted equipment into the roster identity used by avatar renderers.</summary>
    public static class GameplayEquipmentAppearance
    {
        public static bool Apply(CharacterInfo character, GameplayUiState state)
        {
            if (character == null || state == null || !state.IsActive || character.Uid != state.CharacterUid || state.Equipment.Count != 9)
                return false;
            bool changed = character.Equipment == null || character.Equipment.Length != 9;
            var equipment = new EquipmentItem[9];
            for (int slot = 0; slot < equipment.Length; slot++)
            {
                var item = state.Equipment[slot];
                // Equipment acknowledgement carries full ItemBase metadata. Never erase unknown fields
                // with a partial trade receipt (which belongs to inventory, not equipment).
                if (item.Slot != slot || !item.MetadataComplete) return false;
                equipment[slot] = new EquipmentItem { ItemType = item.ItemType, ItemId = item.ItemId, Option = item.Option, TimeLimit = item.TimeLimit };
                if (!changed)
                {
                    var old = character.Equipment[slot];
                    changed = old.ItemType != item.ItemType || old.ItemId != item.ItemId || old.Option != item.Option || old.TimeLimit != item.TimeLimit;
                }
            }
            if (!changed) return false;
            character.Equipment = equipment;
            return true;
        }
    }

    public sealed partial class FrontendController
    {
        // Call after ReceiveInventory has reduced a server frame, and once after SetWorld. No request
        // intent reaches this path; duplicate/foreign acknowledgements leave the snapshot unchanged.
        void RefreshGameplayEquipmentAppearance()
        {
            var character = SelectedCharacter;
            if (!GameplayEquipmentAppearance.Apply(character, gameplayState?.Snapshot)) return;
            var preview = rosterPreview;
            preview?.RefreshGameplayAppearance(character);
            if (networkWorld && !networkWorld.RefreshAppearance(character, out var failure)) ShowError(failure);
        }
    }
}
