using System;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        Func<int, int, Task> useInventoryItem;
        bool itemUsePending;
        float itemUseDeadline;
        public void SetItemUser(Func<int, int, Task> use) => useInventoryItem = use;
        void BindItemUse(Button slot, ItemContainer container, int index)
        {
            if (container != ItemContainer.Inventory) return;
            slot.AddManipulator(new ContextualMenuManipulator(evt => {
                var item = ItemAt(container, index); if (item.IsEmpty || item.ItemType != 7 || useInventoryItem == null) return;
                var row = GameplayUiCatalog.Load().Item(7, item.ItemId, language);
                var skill = row != null ? WorldSkillCatalog.Load().Find(row.LinkedSkillId) : null;
                if (skill == null || row.GeneralKind != 3 && row.GeneralKind != 4 || skill.Type == 36) return;
                var status = interactionSuspended || prompt != null || itemUsePending || inventoryPending || servicePending != 0 || TradeWindowActive || session.Snapshot.Hp <= 0
                    ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                if (row.GeneralKind == 3)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int nanoSlot = i;
                        if (session.Snapshot.NanoSlots.Count <= i || session.Snapshot.NanoSlots[i] <= 0) continue;
                        string name = GameplayUiCatalog.Load().Nano(session.Snapshot.NanoSlots[i], language)?.Name ?? "Nano " + (i + 1);
                        evt.menu.AppendAction((language == "ru" ? "Использовать/" : "Use/") + name, action => _ = UseInventoryItem(index, nanoSlot), status);
                    }
                }
                else evt.menu.AppendAction(language == "ru" ? "Использовать" : "Use", action => _ = UseInventoryItem(index, 0), status);
            }));
        }
        async Task UseInventoryItem(int slot, int nanoSlot)
        {
            if (itemUsePending || useInventoryItem == null) return;
            itemUsePending = true; itemUseDeadline = Time.unscaledTime + 10;
            try { await useInventoryItem(slot, nanoSlot); }
            catch (Exception error) { itemUsePending = false; if (!disposed) Error?.Invoke(error.Message); }
        }
        public void TickItemUse()
        {
            if (!itemUsePending || Time.unscaledTime < itemUseDeadline) return;
            itemUsePending = false; Error?.Invoke(language == "ru" ? "Сервер не подтвердил использование предмета." : "The server did not confirm item use.");
        }
        public void ReceiveItemUse(ProtocolFrame frame)
        {
            if (!ProtocolItemUse.TryDecode(frame, out var value) || (!value.Own && !value.Failed)) return;
            itemUsePending = false;
            if (value.Failed) Error?.Invoke(language == "ru" ? "Предмет сейчас нельзя использовать." : "This item cannot be used now.");
        }
    }
}
