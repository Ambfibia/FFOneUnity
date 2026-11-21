using System;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        static readonly int[] EquipmentKinds = { 4, 5, 6, 1, 2, 3, 0, 7, 10 };

        sealed class InventorySlotView
        {
            public readonly Button Button;
            public readonly Image Icon;
            public readonly Label Count, Caption;

            public InventorySlotView(VisualTreeAsset template, string id, string mode)
            {
                Button = FFView.RequireClass<Button>(template.Instantiate(), "inventory-item-button");
                Button.RemoveFromHierarchy();
                Button.name = id;
                Button.AddToClassList(mode);
                Icon = FFView.RequireClass<Image>(Button, "inventory-item-image");
                Count = FFView.RequireClass<Label>(Button, "inventory-item-count");
                Caption = FFView.RequireClass<Label>(Button, "inventory-item-caption");
                Icon.name = id + "-image";
                Count.name = id + "-count";
            }
        }

        sealed class InventorySlots
        {
            public readonly InventorySlotView[] Bag = new InventorySlotView[50];
            public readonly InventorySlotView[] Equipment = new InventorySlotView[EquipmentKinds.Length];
        }

        static InventorySlots FindInventorySlots(VisualElement root) =>
            (root?.ClassListContains("inventory-screen") == true ? root : root?.Q(className: "inventory-screen"))?.userData as InventorySlots;

        Button InventoryBagSlot(VisualElement root, int index) => FindInventorySlots(root).Bag[index].Button;

        void BindInventory(VisualElement root) => BindInventory(root, false);

        void BindInventory(VisualElement root, bool showNanos, Action close = null, bool readOnly = false)
        {
            var slotSource = FFView.RequireClass<TemplateContainer>(root, "inventory-slot-template");
            var slotTemplate = slotSource.templateSource;
            slotSource.RemoveFromHierarchy();
            var slots = new InventorySlots();
            root.userData = slots;
            root.EnableInClassList("inventory-read-only", readOnly);
            var bag = FFView.RequireClass<VisualElement>(root, "inventory-slots");
            var equipment = FFView.RequireClass<VisualElement>(root, "equipment-panel");
            var catalog = GameplayUiCatalog.Load();
            for (int i = 0; i < slots.Bag.Length; i++)
            {
                var slot = slots.Bag[i] = new InventorySlotView(slotTemplate, "inventory-slot-" + i, "inventory-bag-slot");
                bag.Add(slot.Button);
                if (i < session.Snapshot.Inventory.Count)
                    BindItem(slot, session.Snapshot.Inventory[i], catalog, FFOne.Network.ItemContainer.Inventory, !readOnly);
            }

            string[] captions = language == "ru"
                ? new[] { "ГОЛОВА", "ЛИЦО", "СПИНА", "КОРПУС", "НОГИ", "ОБУВЬ", "ОРУЖИЕ 1", "ОРУЖИЕ 2", "ТРАНСПОРТ" }
                : new[] { "HEAD", "FACE", "BACK", "CHEST", "LEGS", "FEET", "WEAPON 1", "WEAPON 2", "VEHICLE" };
            for (int i = 0; i < EquipmentKinds.Length; i++)
            {
                int kind = EquipmentKinds[i];
                int index = kind == 10 ? 8 : kind;
                var id = "equipment-type-" + kind;
                var slot = slots.Equipment[index] = new InventorySlotView(slotTemplate, id, "inventory-equipment-slot");
                slot.Button.AddToClassList(id);
                slot.Caption.text = captions[i];
                // Match the original draw order: the battery panels sit over equipment.
                equipment.Insert(i + 1, slot.Button);
                if (index < session.Snapshot.Equipment.Count)
                    BindItem(slot, session.Snapshot.Equipment[index], catalog, FFOne.Network.ItemContainer.Equipment, !readOnly);
            }

            var closeButton = FFView.RequireClass<Button>(root, "inventory-close");
            closeButton.clicked += close ?? (() => Windows.Close());
            FFView.RequireClass<Button>(root, "inventory-trash").SetEnabled(!readOnly && DeleteItem != null);
            var equipmentTab = FFView.RequireClass<Button>(root, "inventory-equipment-tab");
            var nanoTab = FFView.RequireClass<Button>(root, "inventory-nanos-tab");
            if (language == "ru")
            {
                FFView.RequireClass<Label>(equipmentTab, "inventory-equipment-caption").text = "ЭКИПИРОВКА";
                FFView.RequireClass<Label>(nanoTab, "inventory-nanos-caption").text = "НАНО";
                FFView.RequireClass<Label>(equipment, "equipment-title-caption").text = "НАДЕТО";
            }

            bool nanoSelected = showNanos;
            void Tab(bool nano)
            {
                if (nano != nanoSelected) playSound?.Invoke("tab_click01");
                nanoSelected = nano;
                root.EnableInClassList("inventory-show-nanos", nano);
                equipmentTab.EnableInClassList("inventory-tab-selected", !nano);
                nanoTab.EnableInClassList("inventory-tab-selected", nano);
            }

            FFView.RequireClass<Button>(root, "inventory-help").clicked += () =>
            {
                playSound?.Invoke("button");
                OpenGuide(Windows.Current switch
                {
                    GameplayWindow.Trade => 34,
                    GameplayWindow.Mail => 11,
                    GameplayWindow.Service => serviceBank ? 3 : 24,
                    GameplayWindow.Travel when crocView => 67,
                    _ => nanoSelected ? 3 : 1
                });
            };
            equipmentTab.clicked += () => Tab(false);
            nanoTab.clicked += () => Tab(true);
            var detail = root.Q(className: "inventory-item-detail");
            if (detail != null)
            {
                FFView.RequireClass<Button>(detail, "inventory-detail-close").clicked += () => CloseInventoryDetail();
                FFView.RequireClass<Button>(detail, "inventory-detail-action").clicked += () => (detail.userData as Action)?.Invoke();
            }
            BuildInventoryPresentation(root, readOnly);
            RefreshNanoCollection(root);
            RefreshInventoryPresentation(root);
            Tab(showNanos);
        }

        VisualElement detailOpener, returnFromDetail;
        internal VisualElement ActiveInventoryDetail => Windows.View?.Q(className: "inventory-item-detail--open") ?? Windows.View?.Q("nano-card-modal");

        internal VisualElement TakeDetailFocus(VisualElement scope)
        {
            var previous = returnFromDetail;
            returnFromDetail = null;
            return previous?.panel != null && scope.Contains(previous) ? previous : null;
        }

        bool CloseInventoryDetail()
        {
            var detail = ActiveInventoryDetail;
            if (detail == null) return false;
            if (detail.ClassListContains("inventory-item-detail"))
            {
                detail.RemoveFromClassList("inventory-item-detail--open");
                detail.userData = null;
            }
            else { detail.RemoveFromHierarchy(); selectedStationNano = 0; }
            returnFromDetail = detailOpener;
            if (detailOpener?.panel != null) detailOpener.Focus();
            detailOpener = null;
            return true;
        }

        bool ShowInventoryItem(Button opener, FFOne.Network.ItemContainer container, int index)
        {
            if (Windows.Current != GameplayWindow.Inventory || container == FFOne.Network.ItemContainer.Bank || prompt != null) return false;
            var item = ItemAt(container, index);
            if (item.IsEmpty) return false;
            var detail = Windows.View.Q(className: "inventory-item-detail");
            var catalog = GameplayUiCatalog.Load();
            var content = catalog.Item(item.ItemType, item.ItemId, language);
            if (detail == null || content == null) return false;
            bool equipped = container == FFOne.Network.ItemContainer.Equipment;
            detailOpener = opener;
            detail.AddToClassList("inventory-item-detail--open");
            detail.EnableInClassList("inventory-item-detail--equipped", equipped);
            var title = FFView.RequireClass<Label>(detail, "inventory-detail-title");
            title.text = content.Name;
            FFWidgets.AutoFit(title);
            FFView.RequireClass<Image>(detail, "inventory-detail-icon").image = opener.Q<Image>(className: "inventory-item-image").image;
            FFView.RequireClass<Label>(detail, "inventory-detail-level").text = T("Level", "Уровень") + " " + content.RequiredLevel;
            FFView.RequireClass<Label>(detail, "inventory-detail-description").text = content.Description;
            var ratings = FFView.RequireClass<VisualElement>(detail, "inventory-detail-ratings");
            ratings.style.display = item.ItemType is >= 0 and <= 3 ? DisplayStyle.Flex : DisplayStyle.None;
            if (item.ItemType is >= 0 and <= 3)
            {
                var old = session.Snapshot.Equipment[item.ItemType];
                var baseline = !equipped && !old.IsEmpty ? catalog.Item(old.ItemType, old.ItemId, language) : null;
                SetRating(ratings, "point", T("Point", "Урон"), content.PointRating, baseline?.PointRating ?? 0);
                SetRating(ratings, "group", T("Group", "По группе"), content.GroupRating, baseline?.GroupRating ?? 0);
                SetRating(ratings, "defense", T("Defense", "Защита"), content.DefenseRating, baseline?.DefenseRating ?? 0);
            }
            var action = FFView.RequireClass<Button>(detail, "inventory-detail-action");
            if (!equipped && item.ItemType == 9)
            {
                action.style.display = DisplayStyle.Flex;
                action.text = T("OPEN", "ОТКРЫТЬ");
                action.SetEnabled(serviceClient != null && !inventoryPending && item.MetadataComplete);
                detail.userData = (Action)(() => _ = OpenInventoryChest(index));
                return true;
            }
            bool wearable = item.ItemType is >= 0 and <= 6 or 10;
            action.style.display = wearable ? DisplayStyle.Flex : DisplayStyle.None;
            action.text = equipped ? T("UNEQUIP", "СНЯТЬ") : T("EQUIP", "НАДЕТЬ");
            int destination = equipped ? FreeSlot() : item.ItemType == 10 ? 8 : item.ItemType;
            action.SetEnabled(moveItem != null && !inventoryPending && destination >= 0 && (equipped || !content.UnavailableTo(session.Snapshot)));
            detail.userData = (Action)(() => {
                CloseInventoryDetail();
                _ = RequestItemMove(container, index, equipped ? FFOne.Network.ItemContainer.Inventory : FFOne.Network.ItemContainer.Equipment, destination);
            });
            return true;
        }

        static void SetRating(VisualElement ratings, string kind, string caption, int value, int baseline)
        {
            FFView.RequireClass<Label>(ratings, "inventory-rating-" + kind + "-caption").text = caption;
            var label = FFView.RequireClass<Label>(ratings, "inventory-rating-" + kind);
            label.text = value.ToString();
            label.EnableInClassList("inventory-rating--better", value > baseline);
            label.EnableInClassList("inventory-rating--worse", value < baseline);
        }

        void BindInventoryBagItem(VisualElement root, GameplayItemState item, GameplayUiCatalog catalog, bool draggable = true) =>
            BindItem(FindInventorySlots(root).Bag[item.Slot], item, catalog, FFOne.Network.ItemContainer.Inventory, draggable);

        void BindItem(InventorySlotView view, GameplayItemState item, GameplayUiCatalog catalog, FFOne.Network.ItemContainer container, bool draggable)
        {
            var slot = view.Button;
            slot.userData = item;
            if (draggable && !itemSlots.ContainsKey(slot)) BindItemDrag(slot, container, item.Slot);
            slot.SetEnabled(moveItem != null || !item.IsEmpty);
            view.Count.text = "";
            view.Icon.image = null;
            slot.tooltip = "";
            var content = item.IsEmpty ? null : catalog.Item(item.ItemType, item.ItemId, language);
            slot.EnableInClassList("inventory-item-restricted", content?.UnavailableTo(session.Snapshot) == true);
            if (item.IsEmpty) return;
            if (content == null) { slot.tooltip = $"{item.ItemType}:{item.ItemId}"; return; }
            slot.tooltip = content.Name + "\n" + content.Description;
            view.Icon.image = string.IsNullOrEmpty(content.IconAddress) ? null : ContentAssets.Load<Texture2D>(content.IconAddress);
            if (content.MaxStack > 1 && item.Quantity > 1) view.Count.text = item.Quantity.ToString();
        }
    }
}
