using System;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string CrocArt = "ui/en/combi/";
        int crocCarried = -1, crocResult = -1;
        bool crocWaiting;
        string CrocText(string key) => LocalizationCatalog.Lookup("ui.combi." + key, language);

        sealed class CrocSide
        {
            public readonly Image Icon;
            public readonly Label Empty, Name, Level, Description;
            public readonly Button Slot, Drop;

            public CrocSide(VisualElement panel, string id)
            {
                var root = FFView.RequireClass<VisualElement>(panel, "croc-" + id + "-selection");
                Icon = FFView.RequireClass<Image>(root, id + "-selected-icon");
                Empty = FFView.RequireClass<Label>(root, id + "-empty");
                Name = FFView.RequireClass<Label>(root, id + "-name");
                Level = FFView.RequireClass<Label>(root, id + "-level");
                Description = FFView.RequireClass<Label>(root, id + "-description");
                Slot = FFView.RequireClass<Button>(root, "croc-" + id);
                Drop = FFView.RequireClass<Button>(root, "croc-" + id + "-drop");
            }
        }

        sealed class CrocView
        {
            public readonly CrocSide Looks, Stats;
            public readonly VisualElement Overlay, Wait, Success;
            public readonly Label Cost, Chance, ResultName, ResultDescription;
            public readonly Image ResultIcon, WaitingNpc;
            public readonly Button Clear, Combine;
            public GameplayNpcPortrait WaitingPortrait;

            public CrocView(VisualElement panel, VisualElement overlay)
            {
                Looks = new CrocSide(panel, "looks");
                Stats = new CrocSide(panel, "stats");
                Overlay = overlay;
                Wait = FFView.RequireClass<VisualElement>(overlay, "croc-wait");
                Success = FFView.RequireClass<VisualElement>(overlay, "croc-success");
                WaitingNpc = FFView.RequireClass<Image>(Wait, "croc-waiting-npc");
                Cost = FFView.RequireClass<Label>(panel, "croc-cost");
                Chance = FFView.RequireClass<Label>(panel, "croc-chance");
                ResultName = FFView.RequireClass<Label>(Success, "result-name");
                ResultDescription = FFView.RequireClass<Label>(Success, "croc-result-description");
                ResultIcon = FFView.RequireClass<Image>(Success, "result-icon");
                Clear = FFView.RequireClass<Button>(panel, "croc-clear");
                Combine = FFView.RequireClass<Button>(panel, "croc-combine");
            }
        }

        VisualElement BuildCrocView()
        {
            var root = FFView.InstantiateClass("ui/views/Retrobution/Inventory", "inventory-screen");
            root.AddToClassList("croc-view");
            BindInventory(root, false, () => { if (!TravelBusy) Windows.Close(); }, true);
            FFView.RequireClass<VisualElement>(root, "inventory-character").RemoveFromHierarchy();
            FFView.RequireClass<Button>(root, "inventory-nanos-tab").SetEnabled(false);

            var shell = FFView.RequireClass<VisualElement>(root, "inventory-window");
            var template = FFView.RequireClass<TemplateContainer>(root, "croc-workshop-template");
            var panel = FFView.RequireClass<VisualElement>(template, "croc-panel");
            var overlay = FFView.RequireClass<VisualElement>(template, "croc-overlay");
            shell.Add(panel);
            shell.Add(overlay);
            template.RemoveFromHierarchy();
            var view = new CrocView(panel, overlay);
            panel.userData = view;
            BindServicePortrait(FFView.RequireClass<Image>(panel, "croc-npc"), 143, 128, 1.3f, .55f, -20);

            void Localize(VisualElement parent, string role, string key, bool upper = true)
            {
                var label = FFView.RequireClass<Label>(parent, role);
                var value = CrocText(key);
                label.text = upper ? FFStyle.Caption(value) : value;
                AutoFit(label);
            }

            Localize(panel, "croc-title", "title");
            Localize(panel, "croc-intro", "intro", false);
            Localize(panel, "croc-style-caption", "style");
            Localize(panel, "croc-stats-caption", "stats");
            Localize(panel, "croc-new", "new_combined_item");
            Localize(panel, "croc-taros", "taros");
            Localize(panel, "croc-chance-title", "chance.title");
            view.Looks.Empty.text = CrocText("look.empty");
            view.Stats.Empty.text = CrocText("stats.empty");
            Localize(overlay, "croc-result-title", "success.hooray");
            Localize(overlay, "croc-result-message", "success.message", false);

            void ApplySelection(bool style)
            {
                if (interactionSuspended || prompt != null || TravelBusy || crocResult != -1) return;
                if (crocCarried >= 0 && !CanSelectForCroc(session.Snapshot.Inventory[crocCarried])) return;
                if (style) { looksSlot = crocCarried; if (looksSlot >= 0 && statsSlot == looksSlot) statsSlot = -1; }
                else { statsSlot = crocCarried; if (statsSlot >= 0 && looksSlot == statsSlot) looksSlot = -1; }
                crocCarried = -1;
                playSound?.Invoke("button");
                RefreshCroc(root);
            }

            void BindDrop(Button button, bool style)
            {
                button.userData = style;
                button.clicked += () => ApplySelection(style);
            }
            BindDrop(view.Looks.Slot, true);
            BindDrop(view.Looks.Drop, true);
            BindDrop(view.Stats.Slot, false);
            BindDrop(view.Stats.Drop, false);

            void BindAction(Button button, string key, Action clicked)
            {
                button.text = FFStyle.Caption(CrocText(key));
                button.clicked += clicked;
                AutoFit(button);
            }
            BindAction(view.Clear, "clear_all", () =>
            {
                looksSlot = statsSlot = crocCarried = -1;
                playSound?.Invoke("button");
                RefreshCroc(root);
            });
            BindAction(view.Combine, "combine", ConfirmCroc);
            BindAction(FFView.RequireClass<Button>(overlay, "combine-more"), "success.combine_more", () => ResetCrocResult(root));
            BindAction(FFView.RequireClass<Button>(overlay, "my-stuff"), "success.go_to_my_stuff", () => Windows.Open(GameplayWindow.Inventory));
            foreach (var bagItem in session.Snapshot.Inventory)
            {
                int index = bagItem.Slot;
                var button = InventoryBagSlot(root, index);
                void Carry()
                {
                    if (!interactionSuspended && prompt == null && !TravelBusy && crocResult == -1 &&
                        CanSelectForCroc(session.Snapshot.Inventory[index])) crocCarried = index;
                }
                button.clicked += Carry;
                button.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0 && button.enabledInHierarchy) Carry();
                }, TrickleDown.TrickleDown);
                button.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0 || crocCarried != index || !button.HasPointerCapture(e.pointerId)) return;
                    for (var target = root.panel?.Pick(e.position); target != null && root.Contains(target); target = target.parent)
                    {
                        if (target == button) return;
                        if (target is Button dest && dest.userData is bool style) { ApplySelection(style); return; }
                    }
                    crocCarried = -1;
                }, TrickleDown.TrickleDown);
            }
            RefreshCroc(root);
            return root;
        }

        static bool CanSelectForCroc(GameplayItemState item) =>
            !item.IsEmpty && item.MetadataComplete && item.ItemType is >= 0 and <= 3;

        Texture2D CrocAppearanceIcon(GameplayUiCatalog catalog, GameplayItemState item)
        {
            int appearance = (int)((uint)item.Option >> 16);
            return !item.IsEmpty && item.ItemType is >= 0 and <= 3 && appearance != 0
                ? Texture(catalog.Item(item.ItemType, appearance, language)?.IconAddress) : null;
        }

        void RefreshCrocInventory(GameplayUiState before, GameplayUiState after)
        {
            if (!crocView || Windows.Current != GameplayWindow.Travel) return;
            bool inventoryChanged = !ReferenceEquals(before.Inventory, after.Inventory);
            if (!inventoryChanged && before.Taros == after.Taros) return;
            if (!TravelBusy && inventoryChanged)
            {
                bool Replaced(int slot) => slot >= 0 && !before.Inventory[slot].Equals(after.Inventory[slot]);
                if (Replaced(looksSlot)) looksSlot = -1;
                if (Replaced(statsSlot)) statsSlot = -1;
                if (Replaced(crocCarried)) crocCarried = -1;
            }
            RefreshTravel();
        }

        bool CrocRecipe(out long cost, out float chance)
        {
            cost = 0; chance = 0;
            if (looksSlot < 0 || statsSlot < 0 || looksSlot == statsSlot) return false;
            var a = session.Snapshot.Inventory[looksSlot]; var b = session.Snapshot.Inventory[statsSlot];
            if (!CanSelectForCroc(a) || !CanSelectForCroc(b) || a.ItemType != b.ItemType) return false;
            var catalog = GameplayUiCatalog.Load();
            var look = catalog.Item(a.ItemType, a.ItemId); var stat = catalog.Item(b.ItemType, b.ItemId);
            if (look == null || stat == null || !travelCatalog.Croc.TryGetValue(Math.Abs(look.RequiredLevel - stat.RequiredLevel), out var odds)) return false;
            int gap = Math.Abs(look.Rarity - stat.Rarity);
            if (gap >= odds.Rarity.Length) return false;
            cost = (long)look.BuyPrice * odds.LooksCost + (long)stat.BuyPrice * odds.StatsCost;
            chance = odds.Base * odds.Rarity[gap] * 100; return true;
        }

        void ConfirmCroc()
        {
            if (!TravelAvailable || !CrocRecipe(out var cost, out _) || session.Snapshot.Taros < cost) return;
            int style = looksSlot, stats = statsSlot;
            var a = session.Snapshot.Inventory[style]; var b = session.Snapshot.Inventory[stats];
            string[] lines = CrocText("modal.attempt_confirmation").Split(new[] {'\n'}, 2);
            ShowPrompt(lines[0], lines.Length == 2 ? lines[1] : "", null, unused => {
                if (Windows.Current != GameplayWindow.Travel || !crocView || looksSlot != style || statsSlot != stats ||
                    !a.Equals(session.Snapshot.Inventory[style]) || !b.Equals(session.Snapshot.Inventory[stats]) ||
                    !CrocRecipe(out var updated, out _) || updated > session.Snapshot.Taros) return Task.CompletedTask;
                return WaitForCroc(style, stats, a, b);
            }, null, CrocArt + "npc-icon");
        }

        GameplayNpcPortrait BindServicePortrait(Image surface, int width, int height, float distance, float elevation, float angle)
        {
            int id = travelNpc?.Id ?? 0;
            return id == 0 ? null : new GameplayNpcPortrait(surface, () => ResolveServiceNpc?.Invoke(id), width, height, distance, elevation, angle);
        }

        async Task WaitForCroc(int style, int stats, GameplayItemState first, GameplayItemState second)
        {
            crocWaiting = true; RefreshTravel();
            try
            {
                float end = Time.time + 4;
                while (!disposed && Windows.Current == GameplayWindow.Travel && Time.time <= end)
                { if (travelToken.IsCancellationRequested) return; await Task.Yield(); }
            }
            finally { crocWaiting = false; }
            if (disposed || Windows.Current != GameplayWindow.Travel || !crocView) return;
            if (first.Equals(session.Snapshot.Inventory[style]) && second.Equals(session.Snapshot.Inventory[stats]) &&
                CrocRecipe(out var cost, out _) && cost <= session.Snapshot.Taros)
                await BeginTravel(ProtocolCroc.Ok, () => travelClient.CombineItemsAsync(style, stats, travelToken));
            RefreshTravel();
        }

        void ResetCrocResult(VisualElement root)
        {
            crocResult = -1;
            RefreshCroc(root);
        }

        void RefreshCroc(VisualElement root)
        {
            if (root.Q(className: "croc-panel")?.userData is not CrocView view) return;
            var catalog = GameplayUiCatalog.Load();
            void Detail(CrocSide side, int slot, bool looks)
            {
                var state = slot < 0 ? default : session.Snapshot.Inventory[slot];
                var item = state.IsEmpty ? null : catalog.Item(state.ItemType, state.ItemId, language);
                side.Icon.image = (looks ? CrocAppearanceIcon(catalog, state) : null) ?? Texture(item?.IconAddress);
                side.Empty.EnableInClassList("croc-hidden", item != null);
                side.Name.text = item?.Name ?? "";
                side.Level.text = item == null ? "" : CrocText("item.level").Replace("{level}", item.RequiredLevel.ToString());
                side.Description.text = item?.Description ?? "";
            }
            Detail(view.Looks, looksSlot, true);
            Detail(view.Stats, statsSlot, false);
            bool recipe = CrocRecipe(out long cost, out float chance);
            view.Cost.text = cost.ToString();
            string rank = !recipe ? looksSlot >= 0 && statsSlot >= 0 ? "not_possible" : "not_ready" :
                chance < 10 ? "very_low" : chance < 30 ? "low" : chance < 70 ? "medium" : chance < 90 ? "good" : "very_good";
            view.Chance.text = CrocText("chance." + rank);
            view.Combine.SetEnabled(!TravelBusy && recipe && session.Snapshot.Taros >= cost);
            view.Clear.SetEnabled(!TravelBusy && (looksSlot >= 0 || statsSlot >= 0));
            var bag = FindInventorySlots(root).Bag;
            foreach (var item in session.Snapshot.Inventory)
            {
                BindInventoryBagItem(root, item, catalog, false);
                var slot = bag[item.Slot];
                var appearance = CrocAppearanceIcon(catalog, item);
                if (appearance) slot.Icon.image = appearance;
                if (!item.IsEmpty && !slot.Icon.image)
                    slot.Count.text = item.Quantity > 1 ? $"#{item.ItemId} ×{item.Quantity}" : $"#{item.ItemId}";
                slot.Button.SetEnabled(!TravelBusy && CanSelectForCroc(item));
            }

            bool waiting = crocWaiting || travelPending == ProtocolCroc.Ok;
            view.Overlay.EnableInClassList("croc-hidden", !waiting && crocResult == -1);
            view.Wait.EnableInClassList("croc-hidden", !waiting);
            view.Success.EnableInClassList("croc-hidden", waiting || crocResult < 0);
            if (waiting)
            {
                view.WaitingPortrait ??= BindServicePortrait(view.WaitingNpc, 357, 384, 2.2f, .42f, 0);
                return;
            }
            view.WaitingPortrait?.Dispose();
            view.WaitingPortrait = null;
            if (crocResult == -1) return;
            if (crocResult == -2)
            {
                string[] lines = CrocText("modal.combination_failed").Split(new[] {'\n'}, 2);
                ShowPrompt(lines[0], lines.Length == 2 ? lines[1] : "", null,
                    _ => { ResetCrocResult(root); return Task.CompletedTask; },
                    () => { ResetCrocResult(root); return Task.CompletedTask; }, CrocArt + "npc-icon");
                return;
            }
            var post = session.Snapshot.Inventory[crocResult];
            var row = catalog.Item(post.ItemType, post.ItemId, language);
            view.ResultIcon.image = CrocAppearanceIcon(catalog, post) ?? Texture(row?.IconAddress);
            view.ResultName.text = row?.Name ?? "";
            view.ResultDescription.text = row?.Description ?? "";
        }
    }
}
