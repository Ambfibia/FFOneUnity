using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string EquipArt = "ui/en/user-equip/";
        public event System.Action<float> InventoryRotationRequested;

        sealed class InventoryNanoPreview { public GameplayNanoRenderStage Stage; }

        static Image InventoryArt(VisualElement parent, string name, string address, float x, float y, float width, float height)
        {
            var image = new Image { name = name, image = FFWidgets.Texture(address), pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            FFWidgets.Rect(image, x, y, width, height); parent.Add(image); return image;
        }

        static Label InventoryText(VisualElement parent, string name, string text, float x, float y, float width, float height, int size = 10, bool body = false)
        {
            var label = FFWidgets.Label(parent, name, text, x, y, width, height, body, size, false);
            label.enableRichText = false; label.pickingMode = PickingMode.Ignore;
            label.AddToClassList("inventory-copy"); label.style.color = new Color(.8f, 1, 1);
            return label;
        }

        // Source authority: primary Retrobution Panel_UserClothes/Panel_PCStuffScript.
        // The backdrop is 1036x653; the interior is centered independently at 1020x638.
        // Keep cameras and foreground in their original draw order, within one left group.
        void BuildInventoryPresentation(VisualElement root)
        {
            root.AddToClassList("inventory-screen");
            var backdrop = InventoryArt(root, "inventory-backdrop", EquipArt + "backdrop", 0, 0, 1920, 1440);
            root.Insert(0, backdrop);
            var stuff = root.Q("inventory-stuff");
            InventoryArt(stuff, "inventory-grid-shadow", EquipArt + "inventory-shadow", 3, 34, 350, 503);
            InventoryArt(stuff, "inventory-taros-art", EquipArt + "taros", 20, 560, 149, 32);
            InventoryText(stuff, "inventory-taros", "", 0, 0, 1, 1).style.display = DisplayStyle.None;
            for (int i = 0; i < 9; i++)
                InventoryText(stuff, "inventory-taros-digit-" + i, "0", 22 + i * 12, 564, 12, 20, 10).style.unityTextAlign = TextAnchor.UpperRight;
            InventoryArt(stuff, "inventory-dexlabs", EquipArt + "dexlabs", 170, 561, 203, 67);
            var redeem = new Button { name = "inventory-redeem", text = T("REDEEM CODE", "ВВЕСТИ КОД") };
            redeem.AddToClassList("inventory-redeem"); FFWidgets.Rect(redeem, 15, 598, 148, 25);
            FFWidgets.Font(redeem, false, 12); redeem.SetEnabled(false); stuff.Add(redeem);

            var equipment = root.Q("equipment-panel");
            int[] types = { 4, 5, 6, 1, 2, 3, 0, 7, 10 };
            string[] en = { "HEAD", "FACE", "BACK", "CHEST", "LEGS", "FEET", "WEAPON 1", "WEAPON 2", "VEHICLE" };
            string[] ru = { "ГОЛОВА", "ЛИЦО", "СПИНА", "КОРПУС", "НОГИ", "ОБУВЬ", "ОРУЖИЕ 1", "ОРУЖИЕ 2", "ТРАНСПОРТ" };
            for (int i = 0; i < types.Length; i++)
            {
                var slot = root.Q<Button>("equipment-type-" + types[i]);
                InventoryText(slot, "equipment-caption-" + types[i], language == "ru" ? ru[i] : en[i], 1, 0, 61, 10, 8).style.unityTextAlign = TextAnchor.UpperRight;
                slot.Q<Image>().style.top = 0;
            }
            for (int i = 0; i < 2; i++)
            {
                string kind = i == 0 ? "boost" : "potion"; int y = 572 + i * 30;
                InventoryArt(equipment, kind + "-back", EquipArt + "slot-empty", 0, y, 64, 30);
                InventoryArt(equipment, kind + "-icon", EquipArt + kind + "-icon", 32, y + 5, 30, 28);
                InventoryText(equipment, kind + "-caption", i == 0 ? T("BOOSTS", "БАТАРЕИ") : T("POTIONS", "НАНО-БАТ."), 0, y - 2, 60, 19, 8).style.unityTextAlign = TextAnchor.UpperRight;
                InventoryText(equipment, kind + "-count", "", 4, y + 8, 30, 20, 11);
            }
            var character = root.Q("inventory-character");
            character.userData = new InventoryNanoPreview();
            character.RegisterCallback<AttachToPanelEvent>(_ => {
                var preview = (InventoryNanoPreview)character.userData;
                if (Application.isPlaying && !preview.Stage) {
                    var owner = new GameObject("Inventory Nano render stage"); owner.transform.position = new Vector3(6000, 1000, 0);
                    preview.Stage = owner.AddComponent<GameplayNanoRenderStage>();
                    RefreshInventoryPresentation(root);
                }
            });
            character.RegisterCallback<DetachFromPanelEvent>(_ => {
                var preview = (InventoryNanoPreview)character.userData;
                if (preview.Stage) preview.Stage.Dispose(); preview.Stage = null;
            });
            string[] stats = { "stat-vs-one", "stat-vs-many", "stat-defense" }; int[] statsX = { 100, 210, 310 };
            for (int i = 0; i < 3; i++) {
                InventoryArt(character, "inventory-combat-stat-" + i, EquipArt + stats[i], statsX[i], 0, 92, 92);
                InventoryText(character, "inventory-combat-value-" + i, "—", statsX[i], 85, 92, 20, 12).style.unityTextAlign = TextAnchor.MiddleCenter;
            }
            int[] cardX = { 24, 335, 348 }, cardY = { 215, 175, 350 };
            for (int i = 0; i < 3; i++)
            {
                var card = new VisualElement { name = "inventory-nano-card-" + i, pickingMode = PickingMode.Ignore };
                FFWidgets.Rect(card, cardX[i], cardY[i], 126, 69); character.Add(card);
                InventoryArt(card, "inventory-nano-dialog-" + i, EquipArt + "nano-dialog", 0, 0, 126, 69);
                InventoryText(card, "inventory-nano-number-" + i, "", 5, 17, 110, 14, 8);
                InventoryText(card, "inventory-nano-name-" + i, "", 5, 31, 110, 16, 9);
                InventoryText(card, "inventory-nano-power-" + i, "", 5, 44, 100, 12, 8).style.color = Color.red;
                InventoryArt(card, "inventory-nano-stamina-" + i, EquipArt + "fusion-matter-bar", 6, 60, 88, 4);
                InventoryArt(card, "inventory-nano-skill-" + i, null, 103, 47, 18, 18).scaleMode = ScaleMode.ScaleToFit;
            }
            // DrawCamera follows the cards in RenderClothe; these images must be above them.
            int[] nanoX = { 45, 355, 368 }, nanoY = { 110, 69, 244 };
            for (int i = 0; i < 3; i++) InventoryArt(character, "inventory-nano-portrait-" + i, null, nanoX[i], nanoY[i], 128, 128);
            InventoryArt(character, "inventory-vehicle-back", EquipArt + "vehicle-dialog", 0, 375, 167, 52);
            InventoryText(character, "inventory-vehicle-heading", T("VEHICLE", "ТРАНСПОРТ"), 5, 379, 151, 15, 8);
            InventoryText(character, "inventory-vehicle-name", "", 5, 399, 151, 22, 10);
            BuildInventoryStatus(character);
            for (int i = 0; i < 2; i++) {
                int sign = i == 0 ? -1 : 1; string side = i == 0 ? "left" : "right";
                var turn = new Button { name = "inventory-turn-" + side };
                FFWidgets.Rect(turn, i == 0 ? 120 : 340, 440, 43, 78); turn.AddToClassList("retro-control");
                FFStyle.Button(turn, EquipArt + "turn-" + side, EquipArt + "turn-" + side + "-hover");
                FFWidgets.Hold(turn, seconds => InventoryRotationRequested?.Invoke(sign * seconds * 90)); character.Add(turn);
            }
        }

        void BuildInventoryStatus(VisualElement character)
        {
            var status = new VisualElement { name = "inventory-status", pickingMode = PickingMode.Ignore };
            FFWidgets.Rect(status, 0, 517, 498, 115); character.Add(status);
            FFStyle.Background(status, EquipArt + "user-status-panel", new Vector4(7, 7, 7, 7));
            InventoryText(status, "inventory-player-name", "", 13, 12, 308, 40, 12);
            InventoryText(status, "inventory-player-level", "", 18, 43, 180, 20, 12).style.color = Color.yellow;
            InventoryArt(status, "inventory-guide-box", EquipArt + "guide-box", 350, 7, 139, 57);
            InventoryText(status, "inventory-guide-heading", T("GUIDE:", "ГИД:"), 360, 16, 100, 20, 8);
            InventoryText(status, "inventory-guide-name", "", 360, 36, 118, 20, 8);
            InventoryArt(status, "inventory-guide-icon", null, 440, 16, 36, 36);
            InventoryArt(status, "inventory-health-back", EquipArt + "hp-back", 13, 68, 472, 17);
            InventoryArt(status, "inventory-health-fill", EquipArt + "hp-bar", 14, 69, 470, 15);
            InventoryArt(status, "inventory-fusion-back", EquipArt + "hp-back", 13, 90, 472, 17);
            InventoryArt(status, "inventory-fusion-fill", EquipArt + "fusion-matter-bar", 14, 91, 470, 15);
            InventoryText(status, "inventory-health-caption", T("HEALTH", "ЗДОРОВЬЕ"), 17, 66, 450, 20, 8);
            InventoryText(status, "inventory-fusion-caption", T("FUSION MATTER", "ФУЗИОННАЯ МАТЕРИЯ"), 17, 88, 450, 20, 8);
            InventoryText(status, "inventory-health-value", "", 17, 66, 450, 20, 8).style.unityTextAlign = TextAnchor.MiddleRight;
            InventoryText(status, "inventory-fusion-value", "", 17, 88, 450, 20, 8).style.unityTextAlign = TextAnchor.MiddleRight;
        }

        void RefreshNanoCollection(VisualElement root)
        {
            var container = root.Q("nano-inventory-slots"); if (container == null) return;
            container.Clear(); int index = 0;
            foreach (var presentation in GameplayUiCatalog.Load().Nanos(language).OrderBy(nano => nano.NanoSortOrder).ThenBy(nano => nano.ItemId))
            {
                int id = presentation.ItemId; bool owned = session.Snapshot.Nanos.Any(n => n.IsOwned && n.Id == id);
                var slot = FFView.Instantiate("ui/views/Retrobution/NanoInventorySlot", "nano-slot");
                slot.name = "inventory-nano-" + id; slot.tooltip = presentation.Name + (owned ? "" : T("\nNot acquired", "\nЕщё не получено"));
                slot.Q<Image>("nano-slot-image").image = FFWidgets.Texture(owned ? presentation.IconAddress : presentation.NanoReadyIconAddress);
                slot.EnableInClassList("nano-unowned", !owned);
                slot.style.left = index % 5 * 69; slot.style.top = index / 5 * 69;
                container.Add(slot); index++;
            }
            container.style.height = Mathf.Ceil(index / 5f) * 69;
        }

        void RefreshInventoryPresentation(VisualElement root)
        {
            var state = session.Snapshot;
            void Text(string name, string value) { var label = root.Q<Label>(name); if (label != null) label.text = value; }
            string digits = state.Taros.ToString("D9", CultureInfo.InvariantCulture);
            Text("inventory-taros", digits);
            // Nine native cells; retain the least significant nine digits at the server's wider cap.
            for (int i = 0; i < 9; i++) Text("inventory-taros-digit-" + i, digits[digits.Length - 9 + i].ToString());
            Text("inventory-player-name", state.DisplayName);
            Text("inventory-player-level", T("LEVEL :  ", "УРОВЕНЬ :  ") + state.Level);
            Text("inventory-health-value", state.Hp + "/" + (state.MaxHp?.ToString() ?? "—"));
            Text("inventory-fusion-value", state.FusionMatter + "/" + (state.MaxFusionMatter?.ToString() ?? "—"));
            Text("boost-count", state.WeaponBattery.ToString()); Text("potion-count", state.NanoBattery.ToString());
            var hp = root.Q("inventory-health-fill"); if (hp != null) hp.style.width = 470 * (state.HealthFraction ?? 0);
            var fm = root.Q("inventory-fusion-fill"); if (fm != null) fm.style.width = 470 * (state.FusionMatterFraction ?? 0);
            var catalog = GameplayUiCatalog.Load(); var vehicle = state.Equipment.FirstOrDefault(item => item.Slot == 8);
            Text("inventory-vehicle-name", vehicle.IsEmpty ? T("EMPTY", "ПУСТО") : catalog.Item(vehicle.ItemType, vehicle.ItemId, language)?.Name ?? "");
            int guideNpc = GuideMailNpc(state.Guide);
            Text("inventory-guide-name", guideNpc > 0 ? FFOne.World.WorldNpcCatalog.Load().Find(guideNpc)?.Name ?? "" : "—");
            var guideIcon = root.Q<Image>("inventory-guide-icon");
            string[] guides = { "ben-tennyson", "dexter", "mojo-jojo", "edd", "computress" };
            if (guideIcon != null) guideIcon.image = FFWidgets.Texture(state.Guide >= 1 && state.Guide <= 5 ? EquipArt + "guide-" + guides[state.Guide - 1] : null);
            var preview = root.Q("inventory-character")?.userData as InventoryNanoPreview;
            for (int i = 0; i < 3; i++)
            {
                int id = i < state.NanoSlots.Count ? state.NanoSlots[i] : 0;
                var nano = state.Nanos.FirstOrDefault(n => n.IsOwned && n.Id == id);
                var content = nano.IsOwned ? catalog.Nano(id, language) : null;
                Text("inventory-nano-number-" + i, content == null ? T("EMPTY", "ПУСТО") : "NANO " + (i + 1));
                Text("inventory-nano-name-" + i, content?.Name ?? "");
                Text("inventory-nano-power-" + i, content == null ? "" : catalog.SkillName(nano.SkillId, language));
                var portrait = root.Q<Image>("inventory-nano-portrait-" + i);
                if (portrait != null && preview?.Stage) preview.Stage.Bind(i, portrait, content?.NanoModelResource, 0);
                var skill = root.Q<Image>("inventory-nano-skill-" + i); if (skill != null) skill.image = FFWidgets.Texture(content == null ? null : catalog.SkillIcon(nano.SkillId));
                var bar = root.Q("inventory-nano-stamina-" + i); if (bar != null) bar.style.width = content?.NanoMaxStamina > 0 ? 88 * Mathf.Clamp01((float)nano.Stamina / content.NanoMaxStamina.Value) : 0;
            }
        }
    }
}
