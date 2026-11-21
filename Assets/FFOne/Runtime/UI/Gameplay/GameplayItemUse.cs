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
            if (container == ItemContainer.Inventory || container == ItemContainer.Equipment)
                slot.RegisterCallback<PointerDownEvent>(evt => {
                    if (evt.button != 1 || evt.shiftKey || Windows.Current != GameplayWindow.Inventory) return;
                    var item = ItemAt(container, index);
                    if (item.IsEmpty || item.ItemType > 6 && item.ItemType != 10) return;
                    var target = container == ItemContainer.Inventory ? ItemContainer.Equipment : ItemContainer.Inventory;
                    int position = target == ItemContainer.Inventory ? FreeSlot() : item.ItemType == 10 ? 8 : item.ItemType;
                    if (position >= 0) _ = RequestItemMove(container, index, target, position);
                    evt.StopImmediatePropagation();
                }, TrickleDown.TrickleDown);
            if (container != ItemContainer.Inventory) return;
            slot.AddManipulator(new ContextualMenuManipulator(evt => {
                var item = ItemAt(container, index);
                if (item.IsEmpty) return;
                if (DeleteItem != null && item.MetadataComplete && Windows.Current != GameplayWindow.Trade && Windows.Current != GameplayWindow.Mail)
                    evt.menu.AppendAction(T("Delete", "Удалить"), action => ConfirmInventoryDelete(index),
                        CanDeleteItem && prompt == null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                if (item.ItemType != 7 || useInventoryItem == null) return;
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
        int openingChest = -1;
        VisualElement chestDetail;

        async Task OpenInventoryChest(int slot)
        {
            if (inventoryPending || serviceClient == null || openingChest >= 0) return;
            var item = ItemAt(ItemContainer.Inventory, slot);
            if (item.ItemType != 9 || item.IsEmpty || !item.MetadataComplete) return;
            openingChest = slot;
            chestDetail = ActiveInventoryDetail;
            inventoryPending = true; inventoryDeadline = Time.unscaledTime + 10;
            try
            {
                await serviceClient.OpenChestAsync(slot, new EquipmentItem {
                    ItemType = item.ItemType, ItemId = item.ItemId, Option = item.Option, TimeLimit = item.TimeLimit
                }, serviceToken);
            }
            catch (Exception error)
            {
                openingChest = -1; chestDetail = null; inventoryPending = false;
                if (!disposed) Error?.Invoke(error.Message);
            }
        }

        public void ReceiveItemUse(ProtocolFrame frame)
        {
            if (frame.PacketType == ProtocolItemUse.ChestOpened || frame.PacketType == ProtocolItemUse.ChestFailed)
            {
                bool success = frame.PacketType == ProtocolItemUse.ChestOpened;
                Protocol0104.RequireSize(frame.Payload, success ? 4 : 8);
                if (openingChest < 0 || openingChest != Protocol0104.Read32(frame.Payload, 0)) return;
                bool sameDetail = chestDetail != null && ReferenceEquals(ActiveInventoryDetail, chestDetail);
                openingChest = -1; chestDetail = null; inventoryPending = false;
                if (success) { if (sameDetail) CloseInventoryDetail(); }
                else Error?.Invoke(T("Unable to open this crate.", "Не удалось открыть ящик."));
                return;
            }
            if (!ProtocolItemUse.TryDecode(frame, out var value) || (!value.Own && !value.Failed)) return;
            itemUsePending = false;
            if (value.Failed) Error?.Invoke(language == "ru" ? "Предмет сейчас нельзя использовать." : "This item cannot be used now.");
        }
    }
}
