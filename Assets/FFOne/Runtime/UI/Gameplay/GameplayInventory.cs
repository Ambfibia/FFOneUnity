using FFOne.Content;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        void BindInventory(VisualElement root) => BindInventory(root, false);

        void BindInventory(VisualElement root, bool showNanos, System.Action close = null, bool readOnly = false)
        {
            ActionButton(root, "close", close ?? (() => Windows.Close()));
            foreach (string name in new[] { "trash", "hammer", "help" })
                FFView.Require<Button>(root, name).SetEnabled(false);
            root.Q<Button>("hammer").style.display = DisplayStyle.None;
            root.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12));
            if (language == "ru")
            {
                FFView.Require<Label>(root, "tab-equipment-label").text = "ЭКИПИРОВКА";
                FFView.Require<Label>(root, "tab-nanos-label").text = "НАНО";
                FFView.Require<Label>(root, "equipment-title-label").text = "НАДЕТО";
            }
            var catalog = GameplayUiCatalog.Load();
            foreach (var item in session.Snapshot.Inventory)
                BindItem(root, "inventory-slot-" + item.Slot, item, catalog, !readOnly);
            foreach (var item in session.Snapshot.Equipment)
            {
                int type = item.Slot == 8 ? 10 : item.Slot;
                BindItem(root, "equipment-type-" + type, item, catalog, !readOnly);
            }
            var inventory = FFView.Require<ScrollView>(root, "inventory-grid");
            var nanos = FFView.Require<ScrollView>(root, "nano-inventory-grid");
            var frame = root.Q("inventory-frame");
            var inactiveTab = InventoryArt(root.Q("inventory-stuff"), "inventory-inactive-tab", null, 0, 0, 142, 29);
            inactiveTab.PlaceBehind(frame);
            if (readOnly) inactiveTab.style.display = DisplayStyle.None;
            var equipmentTab = root.Q<Button>("tab-equipment");
            var nanoTab = root.Q<Button>("tab-nanos");
            equipmentTab.style.backgroundImage = nanoTab.style.backgroundImage = new StyleBackground((Texture2D)null);
            void Tab(bool nano)
            {
                inventory.style.display = nano ? DisplayStyle.None : DisplayStyle.Flex;
                nanos.style.display = nano ? DisplayStyle.Flex : DisplayStyle.None;
                root.Q<Button>("tab-equipment").EnableInClassList("inventory-tab-selected", !nano);
                root.Q<Button>("tab-nanos").EnableInClassList("inventory-tab-selected", nano);
                // The selected tab belongs to the cabinet frame. Its inactive neighbour
                // is drawn behind that frame in Panel_PCStuffScript.OnGUI.
                FFStyle.Background(frame.Q(className: "retro-background"), EquipArt + (nano ? "nano-back" : "itemslotback"),
                    nano ? new Vector4(219, 77, 10, 34) : new Vector4(136, 77, 10, 33));
                FFWidgets.Rect(inactiveTab, nano ? 0 : 96, 0, nano ? 142 : 129, 29);
                inactiveTab.image = FFWidgets.Texture(EquipArt + (nano ? "item-tab" : "nano-tab"));
            }
            ActionButton(root, "tab-equipment", () => Tab(false));
            ActionButton(root, "tab-nanos", () => Tab(true));
            BuildInventoryPresentation(root);
            RefreshNanoCollection(root);
            RefreshInventoryPresentation(root);
            Tab(showNanos);
        }

        void BindItem(VisualElement root, string prefix, GameplayItemState item, GameplayUiCatalog catalog, bool draggable = true)
        {
            var slot = FFView.Require<Button>(root, prefix);
            var image = FFView.Require<Image>(slot, prefix + "-image");
            var count = FFView.Require<Label>(slot, prefix + "-count");
            slot.userData = item;
            if (draggable && !itemSlots.ContainsKey(slot)) BindItemDrag(slot, prefix.StartsWith("inventory-slot-") ? FFOne.Network.ItemContainer.Inventory : FFOne.Network.ItemContainer.Equipment, item.Slot);
            slot.SetEnabled(moveItem != null || !item.IsEmpty);
            count.text = "";
            image.image = null; slot.tooltip = "";
            if (item.IsEmpty) return;
            var content = catalog.Item(item.ItemType, item.ItemId, language);
            if (content == null) { slot.tooltip = $"{item.ItemType}:{item.ItemId}"; return; }
            slot.tooltip = content.Name + "\n" + content.Description;
            image.image = string.IsNullOrEmpty(content.IconAddress) ? null : ContentAssets.Load<Texture2D>(content.IconAddress);
            if (content.MaxStack > 1 && item.Quantity > 1) count.text = item.Quantity.ToString();
        }

        void BindJournal(VisualElement root)
        {
            root.RegisterCallback<AttachToPanelEvent>(_ => RefreshJournal());
            ActionButton(root, "close", () => Windows.Close());
            ActionButton(root, "tab-active", () => { showCompletedMissions = false; selectedMissionTask = 0; RefreshJournal(); });
            ActionButton(root, "tab-completed", () => { showCompletedMissions = true; selectedMissionTask = 0; RefreshJournal(); });
            FFView.Require<Button>(root, "help").SetEnabled(false);
            root.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12));
            if (language == "ru")
            {
                FFView.Require<Label>(root, "journal-frame-label").text = "ЖУРНАЛ ЗАДАНИЙ";
                FFView.Require<Label>(root, "journal-empty-label").text = "Задание не выбрано";
                FFView.Require<Label>(root, "mission-details-heading-label").text = "ОПИСАНИЕ ЗАДАНИЯ:";
                FFView.Require<Label>(root, "journal-active-background-label").text = "Активные";
                FFView.Require<Label>(root, "journal-completed-background-label").text = "Завершённые";
            }
            var list = FFView.Require<ScrollView>(root, "journal-list");
            var catalog = GameplayUiCatalog.Load();
            foreach (var quest in session.Snapshot.RunningQuests)
            {
                if (quest.TaskId <= 0) continue;
                var row = FFView.Instantiate("ui/views/Retrobution/MissionRow", "mission-row");
                var content = catalog.Mission(quest.TaskId, language);
                string title = content?.Title ?? quest.TaskId.ToString();
                string objective = content?.Objective ?? "";
                FFView.Require<Label>(row, "row-title-label").text = title;
                FFView.Require<Label>(row, "row-objective-label").text = objective;
                row.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12));
                FFView.Require<Button>(row, "track-mission").SetEnabled(false);
                FFView.Require<Button>(row, "select-mission").clicked += () => {
                    FFView.Require<VisualElement>(root, "journal-empty").style.display = DisplayStyle.None;
                    FFView.Require<VisualElement>(root, "journal-details").style.display = DisplayStyle.Flex;
                    FFView.Require<Label>(root, "mission-title-label").text = title;
                    FFView.Require<Label>(root, "mission-description-label").text = objective;
                };
                list.Add(row);
            }
        }
    }
}
