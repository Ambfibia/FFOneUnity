using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        VisualElement BuildCashMall()
        {
            const string art = "ui/en/cashmall/", vendor = "ui/en/vendor/";
            const string inventoryView = "ui/views/Retrobution/Inventory";
            var root = FFView.InstantiateClass(inventoryView, "inventory-screen");
            BindInventory(root, false, () => Windows.Close(), true);
            FFView.RequireClass<VisualElement>(root, "inventory-character").RemoveFromHierarchy();
            var shell = FFView.RequireClass<VisualElement>(root, "inventory-window");
            var panel = Group(shell, "cashmall-panel"); Rect(panel, 0, 0, 498, 653);
            string Copy(string key) => LocalizationCatalog.Lookup("ui.cashmall." + key, language);
            Image(panel, "cashmall-frame", vendor + "vendor-panel", 0, 0, 498, 653);
            Image(panel, "cash-info", vendor + "info", 3, 9, 481, 87);
            Label(panel, "cash-title", Copy("title"), 14, 10, 150, 20, false, 12);
            Image(panel, "cash-meter", art + "cash", 300, 50, 147, 31);
            for (int i = 0; i < 9; i++) Label(panel, "cash-digit-" + i, "0", 301+i*12, 53, 12, 20, false, 12).style.color = new Color(.79435f,1,1);
            var list = Group(panel, "cash-list"); Rect(list, 11, 132, 486, 528);
            list.Add(FFView.InstantiateClass(inventoryView, "ff-cashmall-list-skin"));
            Image(list, "cash-divider", art + "back-bar", 0, 20, 486, 13);
            Image(list, "cash-scroll", vendor + "scroll-track", 456, 54, 18, 364);
            Image(list, "cash-up", vendor + "scroll-up", 456, 42, 17, 12);
            Image(list, "cash-down", vendor + "scroll-down", 456, 418, 17, 12);
            string[] keys = { "new", "scroll", "potion", "equipment", "etc" };
            int[] x = {1,40,120,200,300}, w = {80,130,130,150,130}, hits = {10,76,156,236,336};
            var tabs = new Image[keys.Length];
            int selected = 0;
            void Select(int index)
            {
                selected = index;
                for (int i = 0; i < tabs.Length; i++)
                    tabs[i].image = Texture(art + (i == 0 ? "first-tab" : "second-tab") + (i == index ? "" : "-button"));
                tabs[index].BringToFront();
                for (int i = 0; i < tabs.Length; i++) list.Q("cash-tab-" + i).BringToFront();
            }
            for (int i = 0; i < tabs.Length; i++)
                tabs[i] = Image(list, "cash-tab-art-" + i, null, x[i], 2, w[i], 27);
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                var hit = Button(list, "cash-tab-" + i, Copy("tab."+keys[i]), hits[i], 5, i == 0 ? 30 : 100, 15,
                    null, null, () => { if (selected != index) { Select(index); playSound?.Invoke("tab_click01"); } });
                hit.style.backgroundColor = Color.clear;
                hit.RegisterCallback<PointerEnterEvent>(_ => { if (selected != index) tabs[index].image = Texture(art + (index == 0 ? "first" : "second") + "-tab-button-hover"); });
                hit.RegisterCallback<PointerLeaveEvent>(_ => { if (selected != index) tabs[index].image = Texture(art + (index == 0 ? "first" : "second") + "-tab-button"); });
            }
            Select(0);
            Button(panel, "cash-stuff", Copy("go_to_my_stuff"), 313, 595, 161, 25, vendor + "button-normal", vendor + "button-hover",
                () => { playSound?.Invoke("button"); Windows.Open(GameplayWindow.Inventory); });
            float elapsed = 0;
            var animation = panel.schedule.Execute(() => {
                elapsed = Mathf.Min(1, elapsed + Time.unscaledDeltaTime);
                float offset = (shell.worldBound.xMin / Mathf.Max(.1f, shell.resolvedStyle.scale.value.x) + 498);
                panel.style.left = -offset * (1-elapsed) * (1-elapsed);
            }).Every(16);
            panel.schedule.Execute(() => { animation.Pause(); panel.style.left = 0; }).StartingIn(1000);
            return root;
        }
    }
}
