using System;
using System.Globalization;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Source cnNanoWheel animated camera path, bound to the authoritative snapshot.</summary>
    public sealed class GameplayNanoHudController : IDisposable
    {
        public VisualElement View { get; }
        readonly VisualElement parent, pivot;
        readonly EventCallback<GeometryChangedEvent> geometry;
        readonly GameplayUiCatalog catalog;
        readonly Slot[] slots = new Slot[3];
        readonly GameplayNanoRenderStage renderStage;
        bool disposed, animated = true;
        GameplayUiState lastState;
        sealed class Slot
        {
            public VisualElement Root, Clip, StaminaBack;
            public Image Portrait, Skill, SkillBack, Stamina;
            public readonly Image[] Back = new Image[3], Front = new Image[3], Style = new Image[3];
        }

        public GameplayNanoHudController(VisualElement parent)
        {
            this.parent = parent;
            catalog = GameplayUiCatalog.Load();
            var stage = new GameObject("Nano HUD render stage");
            stage.transform.position = new Vector3(5000, 1000, 0);
            renderStage = stage.AddComponent<GameplayNanoRenderStage>();
            View = FFView.Instantiate("ui/views/Retrobution/GameplayNanoHud", "nano-hud-root");
            RetrobutionImage.BindAll(View);
            pivot = FFView.Require<VisualElement>(View, "nano-hud-pivot");
            for (int i = 0; i < slots.Length; i++)
            {
                string prefix = "nano-" + i;
                var slot = slots[i] = new Slot {
                    Root = FFView.Require<VisualElement>(View, "nano-slot-" + i),
                    Clip = FFView.Require<VisualElement>(View, prefix + "-portrait-clip"),
                    Portrait = FFView.Require<Image>(View, prefix + "-portrait"),
                    Skill = FFView.Require<Image>(View, prefix + "-skill"),
                    SkillBack = FFView.Require<Image>(View, prefix + "-skill-back"),
                    StaminaBack = FFView.Require<VisualElement>(View, prefix + "-stamina-back"),
                    Stamina = FFView.Require<Image>(View, prefix + "-stamina")
                };
                for (int style = 0; style < 3; style++)
                {
                    slot.Back[style] = FFView.Require<Image>(View, prefix + "-back-" + style);
                    slot.Front[style] = FFView.Require<Image>(View, prefix + "-front-" + style);
                    slot.Style[style] = FFView.Require<Image>(View, prefix + "-style-" + style);
                }
            }
            geometry = _ => Layout();
            parent.RegisterCallback(geometry);
            parent.Add(View);
            Layout();
        }

        public void Bind(GameplayUiState state)
        {
            if (disposed) return;
            lastState = state;
            View.style.display = state.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                int id = i < state.NanoSlots.Count ? state.NanoSlots[i] : 0;
                GameplayNanoState? value = null;
                foreach (var nano in state.Nanos) if (nano.IsOwned && nano.Id == id) { value = nano; break; }
                var content = value.HasValue ? catalog.Nano(value.Value.Id) : null;
                slot.Root.style.display = (animated ? content?.NanoModelResource != null : content?.IconAddress != null) ? DisplayStyle.Flex : DisplayStyle.None;
                slot.Root.tooltip = content?.Name ?? "";
                bool active = i == state.ActiveNanoSlot;
                float? remaining = value.HasValue && content?.NanoMaxStamina > 0
                    ? Mathf.Clamp01((float)value.Value.Stamina / content.NanoMaxStamina.Value) : (float?)null;
                for (int style = 0; style < 3; style++)
                    slot.Back[style].style.display = slot.Front[style].style.display = slot.Style[style].style.display
                        = content?.NanoStyle == style ? DisplayStyle.Flex : DisplayStyle.None;
                renderStage.Bind(i, slot.Portrait, animated ? content?.NanoModelResource : null, GameplayNanoRenderStage.StaminaCameraOffset(remaining, active));
                if (!animated) slot.Portrait.image = Load(content?.IconAddress);
                bool bar = remaining.HasValue && !active && remaining.Value < .99f;
                slot.StaminaBack.style.display = slot.Stamina.style.display = bar ? DisplayStyle.Flex : DisplayStyle.None;
                slot.Stamina.style.width = 40 * (remaining ?? 0);
                slot.Skill.image = value.HasValue ? Load(catalog.SkillIcon(value.Value.SkillId)) : null;
                slot.SkillBack.style.display = slot.Skill.image ? DisplayStyle.Flex : DisplayStyle.None;
                // Cooldown and gumball overlays require states that the snapshot does not contain.
            }
            BindBattery("nano", state.NanoBattery);
            BindBattery("weapon", state.WeaponBattery);
        }

        internal void SetAnimated(bool value)
        {
            if (animated == value) return;
            animated = value;
            if (animated)
                foreach (var slot in slots) slot.Portrait.image = null;
            if (lastState != null) Bind(lastState);
        }

        void BindBattery(string kind, int amount)
        {
            FFView.Require<VisualElement>(View, kind + "-battery").style.display = amount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            FFView.Require<Label>(View, kind + "-battery-count").text = amount.ToString("0000", CultureInfo.InvariantCulture);
        }
        static Texture2D Load(string address) => string.IsNullOrEmpty(address) ? null : ContentAssets.Load<Texture2D>(address);
        public void Layout()
        {
            float width = parent.resolvedStyle.width, height = parent.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            pivot.style.left = width; pivot.style.top = height;
            float scale = RetrobutionLayout.Scale(height);
            pivot.style.scale = new Scale(new Vector3(scale, scale, 1));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            renderStage.Dispose();
            parent.UnregisterCallback(geometry);
            foreach (var slot in slots)
            {
                slot.Portrait.image = null; slot.Skill.image = null;
            }
            View.RemoveFromHierarchy();
        }
    }
}
