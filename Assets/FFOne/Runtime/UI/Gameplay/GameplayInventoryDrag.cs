using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        readonly Dictionary<VisualElement, (ItemContainer container, int slot)> itemSlots = new();
        Func<ItemContainer, int, ItemContainer, int, Task> moveItem;
        public Func<int, Task> DeleteItem { private get; set; }
        int deletingSlot = -1;
        (ItemContainer container, int slot)? dragSource;
        Button dragOwner;
        int dragPointer;
        Vector2 dragStart;
        Image dragImage;
        bool inventoryPending;
        float inventoryDeadline;
        (ItemContainer from, int fromSlot, ItemContainer to, int toSlot)? pendingMove;
        internal bool WeaponChangePending => pendingMove is { } move &&
            (move.from == ItemContainer.Equipment && move.fromSlot == 0 || move.to == ItemContainer.Equipment && move.toSlot == 0);

        public void SetItemMover(Func<ItemContainer, int, ItemContainer, int, Task> mover) => moveItem = mover;

        void BindItemDrag(Button slot, ItemContainer container, int index)
        {
            itemSlots[slot] = (container, index);
            BindItemUse(slot, container, index);
            slot.RegisterCallback<NavigationSubmitEvent>(evt => {
                if (ShowInventoryItem(slot, container, index)) evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerDownEvent>(evt => {
                if (interactionSuspended || prompt != null || MailBusy || TravelBusy || VehicleBusy || evt.button != 0 || moveItem == null || inventoryPending || servicePending != 0 || serviceClosing || ItemAt(container, index).IsEmpty) return;
                if (container != ItemContainer.Bank) playSound?.Invoke("button");
                dragSource = (container, index); dragOwner = slot; dragPointer = evt.pointerId; dragStart = evt.position;
                slot.CapturePointer(evt.pointerId);
            }, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerMoveEvent>(evt => {
                if (dragOwner != slot || !dragSource.HasValue || Vector2.Distance(dragStart, evt.position) < 4) return;
                if (dragImage == null)
                {
                    dragImage = new Image { pickingMode = PickingMode.Ignore, image = slot.Q<Image>()?.image };
                    FFWidgets.Rect(dragImage, 0, 0, slot.worldBound.width, slot.worldBound.height);
                    layers.Add(dragImage, UiLayer.Notification);
                }
                var point = parent.WorldToLocal(evt.position);
                dragImage.style.left = point.x - dragImage.resolvedStyle.width / 2;
                dragImage.style.top = point.y - dragImage.resolvedStyle.height / 2;
            }, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerUpEvent>(evt => {
                if (dragOwner != slot || !dragSource.HasValue) return;
                var from = dragSource.Value;
                bool dragged = Vector2.Distance(dragStart, evt.position) >= 4;
                VisualElement target = parent.panel?.Pick(evt.position);
                CancelItemDrag();
                if (!dragged) { ShowInventoryItem(slot, container, index); return; }
                if (TryTradeDrop(target, from.container, from.slot)) return;
                var trash = Windows.View?.Q<Button>(className: "inventory-trash");
                if (from.container == ItemContainer.Inventory && trash?.enabledInHierarchy == true && (target == trash || (target != null && trash.Contains(target))))
                { ConfirmInventoryDelete(from.slot); return; }
                while (target != null && !itemSlots.ContainsKey(target)) target = target.parent;
                if (target != null)
                {
                    var to = itemSlots[target];
                    _ = RequestItemMove(from.container, from.slot, to.container, to.slot);
                }
            }, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragOwner == slot) CancelItemDrag(); });
        }

        GameplayItemState ItemAt(ItemContainer container, int index) => session.Items(container)[index];

        bool CanDeleteItem => DeleteItem != null && !disposed && !interactionSuspended && !inventoryPending && !itemUsePending &&
            !MailBusy && !TravelBusy && !VehicleBusy && !TradeWindowActive && servicePending == 0 && !serviceClosing && !serviceFaulted &&
            (Windows.Current == GameplayWindow.Inventory || Windows.Current == GameplayWindow.Nanos || Windows.Current == GameplayWindow.Service);

        internal void ConfirmInventoryDelete(int index)
        {
            if (!CanDeleteItem || prompt != null || index < 0 || index >= session.Snapshot.Inventory.Count) return;
            var expected = ItemAt(ItemContainer.Inventory, index);
            if (expected.IsEmpty || !expected.MetadataComplete) return;
            var window = Windows.View; long owner = session.Snapshot.CharacterUid;
            var item = GameplayUiCatalog.Load().Item(expected.ItemType, expected.ItemId, language);
            ShowPrompt(LocalizationCatalog.Lookup("ui.inventory.popup.confirm_delete", language), "", null, async _ => {
                if (!CanDeleteItem || Windows.View != window || session.Snapshot.CharacterUid != owner || !expected.Equals(ItemAt(ItemContainer.Inventory, index))) return;
                deletingSlot = index; inventoryPending = true; inventoryDeadline = Time.unscaledTime + 10;
                try { await DeleteItem(index); }
                catch { deletingSlot = -1; inventoryPending = false; throw; }
            }, null, item?.IconAddress, deleteItem: true, quantity: expected.ItemType == 7 ? expected.Quantity : 0);
        }

        public async Task RequestItemMove(ItemContainer from, int fromSlot, ItemContainer to, int toSlot)
        {
            if (disposed || interactionSuspended || prompt != null || MailBusy || TravelBusy || VehicleBusy || moveItem == null || inventoryPending || servicePending != 0 || serviceClosing || serviceFaulted || TradeWindowActive || from == to && fromSlot == toSlot) return;
            if ((from == ItemContainer.Bank || to == ItemContainer.Bank) && (!serviceBank || session.Snapshot.BankNumber < 0)) return;
            ProtocolInventory.ValidateSlot(from, fromSlot); ProtocolInventory.ValidateSlot(to, toSlot);
            var first = ItemAt(from, fromSlot); var second = ItemAt(to, toSlot);
            if (first.IsEmpty || !ProtocolInventory.Fits(to, toSlot, first.ItemType, first.ItemId) ||
                !ProtocolInventory.Fits(from, fromSlot, second.ItemType, second.ItemId)) return;
            inventoryPending = true;
            pendingMove = (from, fromSlot, to, toSlot);
            inventoryDeadline = Time.unscaledTime + 10;
            try { await moveItem(from, fromSlot, to, toSlot); }
            catch (Exception error)
            {
                inventoryPending = false; pendingMove = null;
                if (!disposed) Error?.Invoke(error.Message);
            }
        }

        public void ReceiveInventory(ProtocolFrame frame)
        {
            if (!ProtocolInventory.TryDecode(frame, out var update)) return;
            if (update.Kind == InventoryUpdateKind.Delete && update.FromSlot != deletingSlot) throw new ProtocolException("Item deletion reply does not match the requested slot.");
            session.ApplyFrame(frame);
            if (update.Kind == InventoryUpdateKind.Delete) { deletingSlot = -1; inventoryPending = false; RefreshService(); }
            if (pendingMove.HasValue && update.Kind == InventoryUpdateKind.Move)
            {
                var pending = pendingMove.Value;
                if (update.FromContainer == pending.from && update.FromSlot == pending.fromSlot &&
                    update.ToContainer == pending.to && update.ToSlot == pending.toSlot)
                { pendingMove = null; inventoryPending = false; RefreshService(); }
            }
        }

        void TickInventory()
        {
            if (!inventoryPending || Time.unscaledTime < inventoryDeadline) return;
            if (openingChest >= 0)
            {
                inventoryDeadline = float.PositiveInfinity;
                Error?.Invoke(T("Crate opening is awaiting the server. Re-enter the world to synchronize.", "Открытие ящика ожидает сервера. Войдите в мир заново для синхронизации."));
                return;
            }
            if (deletingSlot >= 0)
            {
                inventoryDeadline = float.PositiveInfinity;
                Error?.Invoke(T("Item deletion is awaiting the server. Re-enter the world to synchronize.", "Удаление ожидает ответа сервера. Войдите в мир заново для синхронизации."));
                return;
            }
            inventoryPending = false; pendingMove = null;
            Error?.Invoke(language == "ru" ? "Сервер не подтвердил перемещение предмета." : "The server did not confirm the item move.");
        }

        void CancelItemDrag()
        {
            var owner = dragOwner;
            dragOwner = null; dragSource = null;
            if (owner != null && owner.HasPointerCapture(dragPointer)) owner.ReleasePointer(dragPointer);
            dragImage?.RemoveFromHierarchy(); dragImage = null;
        }

        void RefreshInventoryItems()
        {
            var root = Windows.View;
            if (Windows.Current != GameplayWindow.Inventory && Windows.Current != GameplayWindow.Nanos && Windows.Current != GameplayWindow.Trade && Windows.Current != GameplayWindow.Service) return;
            var slots = FindInventorySlots(root);
            if (slots == null) return;
            var catalog = GameplayUiCatalog.Load();
            foreach (var item in session.Snapshot.Inventory) BindItem(slots.Bag[item.Slot], item, catalog, ItemContainer.Inventory, true);
            foreach (var item in session.Snapshot.Equipment) BindItem(slots.Equipment[item.Slot], item, catalog, ItemContainer.Equipment, true);
            RefreshService();
        }
    }
}
