using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string EquipArt = "ui/en/user-equip/";
        static readonly string[] InventoryGuides = { "ben-tennyson", "dexter", "mojo-jojo", "edd", "computress" };
        public event System.Action<float> InventoryRotationRequested;

        sealed class InventoryNanoCard
        {
            public readonly Label Number, Name, Power;
            public readonly Image Portrait, Skill;
            public readonly VisualElement Stamina;

            public InventoryNanoCard(VisualElement card, Image portrait)
            {
                Number = FFView.RequireClass<Label>(card, "inventory-nano-number");
                Name = FFView.RequireClass<Label>(card, "inventory-nano-name");
                Power = FFView.RequireClass<Label>(card, "inventory-nano-power");
                Skill = FFView.RequireClass<Image>(card, "inventory-nano-skill");
                Stamina = FFView.RequireClass<VisualElement>(card, "inventory-nano-stamina");
                Portrait = portrait;
            }
        }

        sealed class InventoryPreview
        {
            public GameplayNanoRenderStage Stage;
            public readonly InventoryNanoCard[] Cards = new InventoryNanoCard[3];
            public readonly Label Name, Level, Health, Fusion, Vehicle, Guide;
            public readonly VisualElement HealthFill, FusionFill;
            public readonly Image GuideIcon;

            public InventoryPreview(VisualElement character)
            {
                Label Text(string role) => FFView.RequireClass<Label>(character, "inventory-" + role);
                Name = Text("player-name");
                Level = Text("player-level");
                Health = Text("health-value");
                Fusion = Text("fusion-value");
                Vehicle = Text("vehicle-name");
                Guide = Text("guide-name");
                HealthFill = FFView.RequireClass<VisualElement>(character, "inventory-health-fill");
                FusionFill = FFView.RequireClass<VisualElement>(character, "inventory-fusion-fill");
                GuideIcon = FFView.RequireClass<Image>(character, "inventory-guide-icon");
                for (int i = 0; i < Cards.Length; i++)
                    Cards[i] = new InventoryNanoCard(
                        FFView.RequireClass<VisualElement>(character, "inventory-nano-card-" + i),
                        FFView.RequireClass<Image>(character, "inventory-nano-portrait-" + i));
            }
        }

        // Panel_UserClothes draws the live Nano cameras after the authored stat cards.
        void BuildInventoryPresentation(VisualElement root, bool readOnly)
        {
            var redeem = FFView.RequireClass<Button>(root, "inventory-redeem");
            redeem.text = T("REDEEM CODE", "ВВЕСТИ КОД");
            redeem.clicked += OpenRedeemCode;
            redeem.SetEnabled(!readOnly && sendChat != null && session.Snapshot.IsActive && !session.Snapshot.IsTutorial);

            Label LabelFor(string role) => FFView.RequireClass<Label>(root, role);
            LabelFor("boost-caption").text = T("BOOSTS", "БАТАРЕИ");
            LabelFor("potion-caption").text = T("POTIONS", "НАНО-БАТ.");
            LabelFor("inventory-vehicle-heading").text = T("VEHICLE", "ТРАНСПОРТ");
            LabelFor("inventory-guide-heading").text = T("GUIDE:", "ГИД:");
            LabelFor("inventory-health-caption").text = T("HEALTH", "ЗДОРОВЬЕ");
            LabelFor("inventory-fusion-caption").text = T("FUSION MATTER", "ФУЗИОННАЯ МАТЕРИЯ");

            var character = FFView.RequireClass<VisualElement>(root, "inventory-character");
            var preview = new InventoryPreview(character);
            character.userData = preview;
            character.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (!Application.isPlaying || preview.Stage) return;
                var owner = new GameObject("Inventory Nano render stage");
                owner.transform.position = new Vector3(6000, 1000, 0);
                preview.Stage = owner.AddComponent<GameplayNanoRenderStage>();
                RefreshInventoryPresentation(root);
            });
            character.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (preview.Stage) preview.Stage.Dispose();
                preview.Stage = null;
            });
            FFWidgets.Hold(FFView.RequireClass<Button>(character, "inventory-turn-left"), seconds => InventoryRotationRequested?.Invoke(-seconds * 90));
            FFWidgets.Hold(FFView.RequireClass<Button>(character, "inventory-turn-right"), seconds => InventoryRotationRequested?.Invoke(seconds * 90));
        }

        void RefreshNanoCollection(VisualElement root)
        {
            var container = root.Q(className: "nano-inventory-slots");
            if (container == null) return;
            container.Clear();
            var owned = session.Snapshot.Nanos.Where(nano => nano.IsOwned).Select(nano => nano.Id).ToHashSet();
            foreach (var presentation in GameplayUiCatalog.Load().Nanos(language).OrderBy(nano => nano.NanoSortOrder).ThenBy(nano => nano.ItemId))
            {
                int id = presentation.ItemId;
                bool acquired = owned.Contains(id);
                var slot = (Button)FFView.InstantiateClass("ui/views/Retrobution/NanoInventorySlot", "inventory-nano-collection-slot");
                slot.name = "inventory-nano-" + id;
                slot.tooltip = presentation.Name + (acquired ? "" : T("\nNot acquired", "\nЕщё не получено"));
                FFView.RequireClass<Image>(slot, "nano-slot-image").image =
                    FFWidgets.Texture(acquired ? presentation.IconAddress : presentation.NanoReadyIconAddress);
                slot.EnableInClassList("nano-unowned", !acquired);
                slot.clicked += () => { playSound?.Invoke("button"); ShowNanoCard(root, id); };
                container.Add(slot);
            }
        }

        void RefreshInventoryPresentation(VisualElement root)
        {
            if (FindInventorySlots(root) == null) return;
            var state = session.Snapshot;
            string digits = state.Taros.ToString("D9", CultureInfo.InvariantCulture);
            void Text(string role, string value)
            {
                var label = root.Q<Label>(className: role);
                if (label != null) label.text = value;
            }
            Text("inventory-taros", digits);
            // Nine native cells; retain the least significant nine digits at the server's wider cap.
            for (int i = 0; i < 9; i++) Text("inventory-taros-digit-" + i, digits[digits.Length - 9 + i].ToString());
            Text("boost-count", state.WeaponBattery.ToString());
            Text("potion-count", state.NanoBattery.ToString());
            if (root.Q(className: "inventory-character")?.userData is not InventoryPreview preview) return;

            preview.Name.text = state.DisplayName;
            preview.Level.text = T("LEVEL :  ", "УРОВЕНЬ :  ") + state.Level;
            preview.Health.text = state.Hp + "/" + (state.MaxHp?.ToString() ?? "—");
            preview.Fusion.text = state.FusionMatter + "/" + (state.MaxFusionMatter?.ToString() ?? "—");
            preview.HealthFill.style.width = 470 * (state.HealthFraction ?? 0);
            preview.FusionFill.style.width = 470 * (state.FusionMatterFraction ?? 0);
            var catalog = GameplayUiCatalog.Load();
            var vehicle = state.Equipment.FirstOrDefault(item => item.Slot == 8);
            preview.Vehicle.text = vehicle.IsEmpty ? T("EMPTY", "ПУСТО") : catalog.Item(vehicle.ItemType, vehicle.ItemId, language)?.Name ?? "";
            int guideNpc = GuideMailNpc(state.Guide);
            preview.Guide.text = guideNpc > 0 ? FFOne.World.WorldNpcCatalog.Load().Find(guideNpc)?.Name ?? "" : "—";
            preview.GuideIcon.image = FFWidgets.Texture(state.Guide >= 1 && state.Guide <= InventoryGuides.Length
                ? EquipArt + "guide-" + InventoryGuides[state.Guide - 1] : null);
            for (int i = 0; i < preview.Cards.Length; i++)
            {
                var card = preview.Cards[i];
                int id = i < state.NanoSlots.Count ? state.NanoSlots[i] : 0;
                var nano = state.Nanos.FirstOrDefault(n => n.IsOwned && n.Id == id);
                var content = nano.IsOwned ? catalog.Nano(id, language) : null;
                card.Number.text = content == null ? T("EMPTY", "ПУСТО") : "NANO " + (i + 1);
                card.Name.text = content?.Name ?? "";
                card.Power.text = content == null ? "" : catalog.NanoPowerName(id, nano.SkillId, language);
                if (preview.Stage) preview.Stage.Bind(i, card.Portrait, content?.NanoModelResource, 0);
                card.Skill.image = FFWidgets.Texture(content == null ? null : catalog.SkillIcon(nano.SkillId));
                card.Stamina.style.width = content?.NanoMaxStamina > 0 ? 88 * Mathf.Clamp01((float)nano.Stamina / content.NanoMaxStamina.Value) : 0;
            }
        }
    }
}
