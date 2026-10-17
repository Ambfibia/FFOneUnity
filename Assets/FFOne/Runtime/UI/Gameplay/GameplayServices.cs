using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        FusionClient serviceClient;
        CancellationToken serviceToken;
        WorldEntity serviceNpc;
        WorldNpcDefinition serviceDefinition;
        bool serviceBank, serviceClosing, serviceFaulted, serviceBuyback;
        uint servicePending;
        float serviceDeadline;
        EquipmentItem servicePendingItem;
        int servicePendingSlot;
        readonly List<EquipmentItem> buyback = new();
        IReadOnlyList<VendorListing> vendorListings = Array.Empty<VendorListing>();
        string serviceMessage = "";
        Button nearbyServiceButton;
        WorldEntity nearbyServiceNpc;
        WorldNpcDefinition nearbyServiceDefinition;
        bool ServiceActive => serviceNpc != null;
        public void ConfigureServices(FusionClient client, CancellationToken token)
        { serviceClient = client; serviceToken = token; }
        public void SetNearbyService(WorldEntity npc, WorldNpcDefinition definition)
        {
            nearbyServiceNpc = npc; nearbyServiceDefinition = definition;
            if (nearbyServiceButton == null && npc != null)
            {
                nearbyServiceButton = ServiceButton(parent, "nearby-bank", "", 0, 0, 260,
                    () => OpenNpcService(nearbyServiceNpc, nearbyServiceDefinition));
                nearbyServiceButton.style.left = new Length(50, LengthUnit.Percent); nearbyServiceButton.style.marginLeft = -130;
                nearbyServiceButton.style.top = StyleKeyword.Auto; nearbyServiceButton.style.bottom = 140;
            }
            if (nearbyServiceButton == null) return;
            nearbyServiceButton.text = language == "ru" ? "ОТКРЫТЬ БАНК" : "OPEN BANK";
            nearbyServiceButton.style.display = npc != null && session.Snapshot.Hp > 0 && !Windows.IsOpen && !interactionSuspended && prompt == null
                ? DisplayStyle.Flex : DisplayStyle.None;
        }
        public void OpenNpcService(WorldEntity npc, WorldNpcDefinition definition)
        {
            if (serviceClient == null || ServiceActive || serviceFaulted || MailBusy || TravelBusy || inventoryPending || TradeWindowActive ||
                trade?.Snapshot.Phase == GameplayTradePhase.OutgoingOffer || trade?.Snapshot.Phase == GameplayTradePhase.IncomingOffer || disposed || interactionSuspended || session.Snapshot.Hp <= 0) return;
            if (npc == null || definition == null || npc.Hidden || !npc.Interactable || (!definition.IsBank && !definition.IsVendor)) return;
            serviceNpc = npc; serviceDefinition = definition; serviceBank = definition.IsBank; serviceClosing = serviceBuyback = false;
            vendorListings = Array.Empty<VendorListing>(); serviceMessage = language == "ru" ? "Ожидание сервера…" : "Waiting for server…";
            Windows.Open(GameplayWindow.Service);
            _ = BeginService(serviceBank ? ProtocolServices.BankOpened : ProtocolServices.Started,
                () => serviceBank ? serviceClient.OpenBankAsync(npc.Id, serviceToken) : serviceClient.StartVendorAsync(npc.Id, npc.NpcType, serviceToken));
        }
        async Task BeginService(uint expected, Func<Task> send)
        {
            if (servicePending != 0 || serviceFaulted || disposed || inventoryPending) return;
            servicePending = expected; serviceDeadline = Time.unscaledTime + 12; RefreshService();
            try { await send(); }
            catch (OperationCanceledException) { if (!disposed) FaultService("Service connection closed."); }
            catch (Exception error) { if (!disposed) FaultService(error.Message); }
        }
        void FaultService(string message)
        {
            // These replies have no transaction IDs. After an unknown outcome, prevent a late
            // reply from being correlated with a new financial operation until a new world session.
            serviceFaulted = true; servicePending = 0; serviceMessage = message;
            RefreshService(); Error?.Invoke(message);
        }
        void TickServices()
        {
            if (servicePending != 0 && Time.unscaledTime >= serviceDeadline)
                FaultService(language == "ru" ? "Нет подтверждения операции. Для синхронизации войдите в мир заново." : "Operation not acknowledged. Re-enter the world to synchronize.");
            if (ServiceActive && session.Snapshot.Hp <= 0 && !serviceClosing) _ = CloseService();
            if (serviceClosing && servicePending == 0 && !inventoryPending) _ = CloseService();
        }
        public void ReceiveServices(ProtocolFrame frame)
        {
            if (!ProtocolServices.TryDecode(frame, out var u) || servicePending == 0 || serviceFaulted) return;
            if (u.Type != servicePending && u.Type != servicePending + 1) return;
            uint expected = servicePending;
            if (!u.Failed && expected == ProtocolServices.Started && (u.NpcId != serviceNpc?.Id || u.VendorId != serviceNpc?.NpcType))
                throw new ProtocolException("Vendor start does not match the requested NPC.");
            if (!u.Failed && expected == ProtocolServices.Listed && u.Listings.Any(row => row.VendorId != serviceNpc?.NpcType))
                throw new ProtocolException("Vendor table belongs to another NPC type.");
            if (!u.Failed && (expected == ProtocolServices.Bought || expected == ProtocolServices.Restored) &&
                (u.Slot != servicePendingSlot || u.Item.ItemType != servicePendingItem.ItemType || u.Item.ItemId != servicePendingItem.ItemId))
                throw new ProtocolException("Purchase does not match the pending item/destination.");
            servicePending = 0;
            if (u.Failed)
            {
                serviceMessage = expected == ProtocolServices.BankOpened && u.Error == 2
                    ? language == "ru" ? "Для этого банка нужна карта доступа." : "A membership card is required for this bank."
                    : language == "ru" ? "Сервер отклонил операцию." : "The server rejected the operation.";
                if (expected == ProtocolServices.BankClosed) serviceClosing = false;
            }
            else
            {
                serviceMessage = "";
                if (expected == ProtocolServices.Started && !serviceClosing)
                { _ = BeginService(ProtocolServices.Listed, () => serviceClient.RequestVendorTableAsync(serviceNpc.Id, serviceNpc.NpcType, serviceToken)); return; }
                if (expected == ProtocolServices.Listed) vendorListings = u.Listings;
                if (expected == ProtocolServices.Sold) { buyback.Add(u.Item); if (buyback.Count > 5) buyback.RemoveAt(0); }
                if (expected == ProtocolServices.Restored)
                { int index = buyback.FindIndex(item => item.Equals(servicePendingItem)); if (index >= 0) buyback.RemoveAt(index); }
                if (expected == ProtocolServices.BankClosed) { FinishService(); return; }
            }
            RefreshService();
            if (serviceClosing) _ = CloseService();
        }
        void ServiceWindowChanged()
        {
            if (ServiceActive && Windows.Current != GameplayWindow.Service && !disposed) _ = CloseService();
        }
        async Task CloseService()
        {
            if (!ServiceActive) return;
            serviceClosing = true;
            if (servicePending != 0 || inventoryPending) return;
            if (serviceBank && session.Snapshot.BankNumber >= 0 && !serviceFaulted)
            { await BeginService(ProtocolServices.BankClosed, () => serviceClient.CloseBankAsync(serviceToken)); return; }
            FinishService();
        }
        void FinishService()
        {
            serviceNpc = null; serviceDefinition = null; serviceClosing = false; servicePending = 0;
            if (Windows.Current == GameplayWindow.Service) Windows.Close();
        }
        void DisposeServices()
        { serviceNpc = null; serviceClient = null; servicePending = 0; buyback.Clear(); nearbyServiceButton?.RemoveFromHierarchy(); nearbyServiceButton = null; }

        VisualElement BuildServiceView()
        {
            var root = FFView.Instantiate("ui/views/Retrobution/Inventory", "inventory-root");
            BindInventory(root, false, () => _ = CloseService());
            root.Q("inventory-character")?.RemoveFromHierarchy();
            root.Q<Button>("tab-nanos")?.SetEnabled(false);
            var panel = new VisualElement { name = "service-panel" }; FFWidgets.Rect(panel, 0, 0, 498, 638);
            if (serviceBank)
            {
                panel.style.backgroundColor = new Color(.01f, .06f, .1f, .98f);
                var info = new VisualElement { pickingMode = PickingMode.Ignore }; FFWidgets.Rect(info, 3, 9, 481, 87);
                FFStyle.Background(info, "ui/en/bank-mode/bank-info"); panel.Add(info);
                var frame = new VisualElement { pickingMode = PickingMode.Ignore }; FFWidgets.Rect(frame, 11, 105, 482, 490);
                // Exact Panel_BankScript source insets, converted to Unity left/bottom/right/top.
                FFStyle.Background(frame, "ui/en/bank-mode/bank-panel", new Vector4(136, 51, 10, 33)); panel.Add(frame);
            }
            else FFStyle.Background(panel, "ui/en/vendor/vendor-panel");
            root.Q("inventory-window").Add(panel);
            FFWidgets.Label(panel, "service-title", serviceDefinition?.Name ?? "", 18, 12, 420, 24, true, 16, false);
            FFWidgets.Label(panel, "service-status", "", 18, 42, 450, 44, true, 12, false);
            var list = new ScrollView { name = "service-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            FFWidgets.Rect(list, 28, serviceBank ? 139 : 110, 450, serviceBank ? 406 : 438); panel.Add(list);
            FFWidgets.Label(panel, "service-balance", "", 18, 554, 460, 25, true, 14, false);
            if (!serviceBank)
            {
                ServiceButton(panel, "service-tab-buy", language == "ru" ? "КУПИТЬ" : "BUY", 20, 85, 210, () => { serviceBuyback = false; RefreshService(); });
                ServiceButton(panel, "service-tab-restore", language == "ru" ? "ВЫКУПИТЬ" : "BUY BACK", 246, 85, 210, () => { serviceBuyback = true; RefreshService(); });
                ServiceButton(panel, "service-weapon-battery", language == "ru" ? "БОЕВАЯ БАТАРЕЯ +100" : "WEAPON BATTERY +100", 18, 588, 218, () => PromptBattery(false));
                ServiceButton(panel, "service-nano-battery", language == "ru" ? "НАНО-БАТАРЕЯ +100" : "NANO BATTERY +100", 250, 588, 218, () => PromptBattery(true));
            }
            for (int i = 0; i < 50; i++)
            {
                int slot = i; root.Q<Button>("inventory-slot-" + slot).clicked += () => ServiceInventoryClick(slot);
            }
            root.RegisterCallback<AttachToPanelEvent>(_ => RefreshService());
            root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
            return root;
        }
        Button ServiceButton(VisualElement parentView, string name, string text, float x, float y, float width, Action action) =>
            FFWidgets.Button(parentView, name, text, x, y, width, 25,
                "ui/en/character/selection/controls/blue_button_normal", "ui/en/character/selection/controls/blue_button_over", action);
        void RefreshService()
        {
            if (Windows.Current != GameplayWindow.Service || Windows.View == null) return;
            var root = Windows.View; var list = root.Q<ScrollView>("service-list"); if (list == null) return;
            bool ready = servicePending == 0 && !serviceClosing && !serviceFaulted && !inventoryPending && session.Snapshot.Hp > 0;
            root.Q<Label>("service-status").text = serviceMessage.Length > 0 ? serviceMessage : servicePending != 0
                ? language == "ru" ? "Ожидание сервера…" : "Waiting for server…" : serviceBank
                ? language == "ru" ? "Щелчок переносит предмет. Можно перетаскивать." : "Click an item to transfer it, or drag between slots."
                : language == "ru" ? "Выберите товар или предмет для продажи." : "Choose goods or an inventory item to sell.";
            root.Q<Label>("service-balance").text = $"Taros: {session.Snapshot.Taros:N0}   {session.Snapshot.WeaponBattery} / {session.Snapshot.NanoBattery}";
            foreach (var button in root.Query<Button>().ToList())
                if (button.name.StartsWith("service-")) button.SetEnabled(ready);
            // Remove previous bank button bindings as their visual lifetime ends on refresh.
            foreach (var old in itemSlots.Keys.Where(slot => list.Contains(slot)).ToArray()) itemSlots.Remove(old);
            list.Clear();
            if (serviceBank)
            {
                if (session.Snapshot.BankNumber < 0) return;
                var grid = new VisualElement { name = "bank-slots" }; grid.style.height = 40 * 57; list.Add(grid);
                foreach (var item in session.Snapshot.Bank)
                {
                    int slot = item.Slot; var button = new Button(() => _ = WithdrawBank(slot)) { name = "bank-slot-" + slot };
                    FFWidgets.Rect(button, slot % 5 * 79, slot / 5 * 57, 75, 54);
                    button.style.backgroundColor = new Color(.01f, .08f, .14f, .95f); button.style.borderBottomWidth = button.style.borderTopWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 1;
                    button.style.borderBottomColor = button.style.borderTopColor = button.style.borderLeftColor = button.style.borderRightColor = Color.cyan;
                    PaintServiceItem(button, item); BindItemDrag(button, ItemContainer.Bank, slot); button.SetEnabled(ready); grid.Add(button);
                }
            }
            else if (serviceBuyback)
            {
                for (int i = 0; i < buyback.Count; i++)
                {
                    int index = i; var item = buyback[i]; var content = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language);
                    AddVendorRow(list, "buyback-row-" + i, item, (content?.SellPrice ?? 0) * Math.Max(1, item.Option & 0xffff),
                        () => PromptRestore(index), ready);
                }
            }
            else foreach (var row in vendorListings)
                AddVendorRow(list, "vendor-row-" + row.SourceSlot, row.Item, row.Price, () => PromptBuy(row), ready);
        }
        void PaintServiceItem(Button button, GameplayItemState item)
        {
            if (item.IsEmpty) return;
            var content = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language);
            button.tooltip = content?.Name ?? $"{item.ItemType}:{item.ItemId}";
            var image = new Image { image = ContentAssets.Load<Texture2D>(content?.IconAddress), pickingMode = PickingMode.Ignore };
            FFWidgets.Rect(image, 8, 2, 48, 48); button.Add(image);
            if (item.Quantity > 1) FFWidgets.Label(button, "count", item.Quantity.ToString(), 32, 32, 40, 20, false, 12);
        }
        void AddVendorRow(ScrollView list, string name, EquipmentItem item, float price, Action action, bool ready)
        {
            var button = new Button(action) { name = name }; button.style.height = 75; button.style.flexShrink = 0;
            FFStyle.Background(button, "ui/en/vendor/item-row");
            PaintServiceItem(button, new GameplayItemState(0, item.ItemType, item.ItemId, item.Option, item.TimeLimit));
            var content = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language);
            FFWidgets.Label(button, "name", content?.Name ?? $"{item.ItemType}:{item.ItemId}", 76, 7, 310, 34, true, 14, false);
            FFWidgets.Label(button, "price", price.ToString("N0", CultureInfo.CurrentCulture) + " Taros", 76, 43, 310, 24, true, 12, false);
            button.SetEnabled(ready); list.Add(button);
        }
        int FreeSlot() => session.Snapshot.Inventory.Where(item => item.IsEmpty).Select(item => item.Slot).DefaultIfEmpty(-1).First();
        bool ServiceCanAct => ServiceActive && !disposed && !interactionSuspended && !serviceFaulted && !serviceClosing && servicePending == 0 && !inventoryPending && session.Snapshot.Hp > 0;
        void PromptBuy(VendorListing row)
        {
            if (!ServiceCanAct) return;
            var content = GameplayUiCatalog.Load().Item(row.Item.ItemType, row.Item.ItemId, language);
            ShowPrompt(content?.Name ?? "BUY", row.Price.ToString("N0") + " Taros / 1", "1", async text => {
                int quantity = Quantity(text, Math.Max(1, content?.MaxStack ?? 1)); int slot = FreeSlot(); if (slot < 0) throw new InvalidOperationException("Inventory is full.");
                var item = row.Item; item.Option = quantity; item.TimeLimit = 0; servicePendingItem = item; servicePendingSlot = slot;
                await BeginService(ProtocolServices.Bought, () => serviceClient.BuyVendorItemAsync(serviceNpc.Id, serviceNpc.NpcType, row.SourceSlot, item, slot, token: serviceToken));
            }, null);
        }
        void ServiceInventoryClick(int slot)
        {
            if (!ServiceCanAct) return;
            if (serviceBank)
            {
                int destination = session.Snapshot.Bank.Where(item => item.IsEmpty).Select(item => item.Slot).DefaultIfEmpty(-1).First();
                if (destination >= 0) _ = RequestItemMove(ItemContainer.Inventory, slot, ItemContainer.Bank, destination);
                return;
            }
            var item = session.Snapshot.Inventory[slot]; if (item.IsEmpty) return;
            var content = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language);
            if (content?.Sellable != true || (item.Option & unchecked((int)0xffff0000)) != 0) return;
            ShowPrompt(content.Name, (language == "ru" ? "Продать: " : "Sell: ") + content.SellPrice + " Taros / 1", "1",
                text => { int quantity = Quantity(text, item.Quantity); return BeginService(ProtocolServices.Sold, () => serviceClient.SellVendorItemAsync(slot, quantity, serviceToken)); }, null);
        }
        async Task WithdrawBank(int slot)
        { if (!ServiceCanAct) return; int destination = FreeSlot(); if (destination >= 0) await RequestItemMove(ItemContainer.Bank, slot, ItemContainer.Inventory, destination); }
        void PromptRestore(int index)
        {
            if (!ServiceCanAct) return; var item = buyback[index];
            ShowPrompt(language == "ru" ? "ОБРАТНЫЙ ВЫКУП" : "BUY BACK", GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.Name ?? "", null,
                async _ => { int slot = FreeSlot(); if (slot < 0) throw new InvalidOperationException("Inventory is full.");
                    servicePendingItem = item; servicePendingSlot = slot;
                    await BeginService(ProtocolServices.Restored, () => serviceClient.BuyVendorItemAsync(serviceNpc.Id, serviceNpc.NpcType, index + 1, item, slot, true, serviceToken)); }, null);
        }
        void PromptBattery(bool nano)
        {
            if (!ServiceCanAct) return;
            ShowPrompt(language == "ru" ? "БАТАРЕЯ" : "BATTERY", "100 Taros / +100", "1",
                text => { int packs = Quantity(text, 100); return BeginService(ProtocolServices.BatteryBought, () => serviceClient.BuyBatteryAsync(serviceNpc.Id, serviceNpc.NpcType, nano, packs, serviceToken)); }, null);
        }
        static int Quantity(string text, int maximum)
        { if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > maximum) throw new ArgumentException("Quantity must be between 1 and " + maximum); return n; }
    }
}
