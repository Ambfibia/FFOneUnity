using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Binds the source-authored HUD without folding its distinct pivots into one transform.</summary>
    public sealed class GameplayHudController : IDisposable
    {
        public VisualElement View { get; }
        public Image Portrait { get; }
        readonly VisualElement parent, topLeft, center, health, damage, expFrame, expFill;
        readonly Label name, level;
        readonly EventCallback<GeometryChangedEvent> geometry;
        GameplayUiState state;
        float fullHealthWidth, healthFraction, delayedHealth, hold;
        float? experienceFraction;
        bool healthInitialized;

        public GameplayHudController(VisualElement parent)
        {
            this.parent = parent;
            View = FFView.Instantiate("ui/views/Retrobution/GameplayHud", "hud-root");
            topLeft = FFView.Require<VisualElement>(View, "hud-top-left");
            center = FFView.Require<VisualElement>(View, "hud-crosshair-center");
            health = FFView.Require<VisualElement>(View, "hud-hp");
            damage = FFView.Require<VisualElement>(View, "hud-hp-damage");
            expFrame = FFView.Require<VisualElement>(View, "hud-exp-frame");
            expFill = FFView.Require<VisualElement>(View, "hud-exp-fill");
            Portrait = FFView.Require<Image>(View, "hud-portrait");
            name = FFView.Require<Label>(View, "hud-name");
            level = FFView.Require<Label>(View, "hud-level");
            name.enableRichText = level.enableRichText = false;
            FFWidgets.Font(name, false, 12); FFWidgets.Font(level, false, 12);
            geometry = _ => Layout();
            View.RegisterCallback(geometry);
            parent.Add(View);
            SetExperienceVisible(false); // Primary localized.expBartype default is zero.
            center.style.display = DisplayStyle.None; // Requires the weapon/aim controller's drawCrosshair state.
            var combat = View.Q("hud-combat-disabled");
            if (combat != null) combat.style.display = DisplayStyle.None;
        }

        public void Bind(GameplayUiState snapshot)
        {
            state = snapshot;
            name.text = snapshot.DisplayName;
            // CnGuiCharacter_info.Update formats the source level with Int32.ToString("00").
            level.text = snapshot.Level.ToString("00", CultureInfo.InvariantCulture);
            health.tooltip = snapshot.MaxHp.HasValue ? $"{snapshot.Hp} / {snapshot.MaxHp.Value}" : snapshot.Hp.ToString();
            SetHealth(snapshot.HealthFraction);
            SetExperience(snapshot.FusionMatterFraction);
        }

        public void SetHealth(float? fraction)
        {
            bool known = fraction.HasValue && float.IsFinite(fraction.Value);
            health.style.visibility = damage.style.visibility = known ? Visibility.Visible : Visibility.Hidden;
            if (!known) { healthInitialized = false; return; }
            healthFraction = Mathf.Clamp01(fraction.Value);
            if (!healthInitialized) { delayedHealth = healthFraction; hold = .4f; healthInitialized = true; }
            DrawHealth();
        }

        public void Tick(float delta)
        {
            if (!healthInitialized) return;
            if (healthFraction >= delayedHealth) { delayedHealth = healthFraction; hold = .4f; }
            else if (hold > 0) hold -= Mathf.Max(0, delta);
            else
            {
                delayedHealth = Mathf.Lerp(delayedHealth, healthFraction, Mathf.Max(0, delta) * 5);
                if (delayedHealth - healthFraction < .001f) delayedHealth = healthFraction;
            }
            DrawHealth();
        }

        void DrawHealth()
        {
            if (fullHealthWidth <= 0) return;
            health.style.width = fullHealthWidth * healthFraction;
            damage.style.width = fullHealthWidth * delayedHealth;
        }

        public void SetExperience(float? fraction)
        {
            // A current value without the original level threshold is not a percentage.
            experienceFraction = fraction.HasValue && float.IsFinite(fraction.Value) ? Mathf.Clamp01(fraction.Value) : (float?)null;
            expFill.style.visibility = experienceFraction.HasValue ? Visibility.Visible : Visibility.Hidden;
            DrawExperience();
        }

        void DrawExperience()
        {
            // Binding occurs before the first panel layout; resolved width is then NaN.
            // Preserve the model value and calculate the source width after attachment.
            float width = parent.resolvedStyle.width;
            expFill.style.width = float.IsFinite(width) && width > 0
                ? Mathf.Max(0, width - 14) * (experienceFraction ?? 0)
                : 0;
        }

        public void SetExperienceVisible(bool visible)
        {
            expFrame.style.display = expFill.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetVisible(bool visible) => View.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public void Layout()
        {
            float width = parent.resolvedStyle.width, height = parent.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            foreach (var group in new[] { topLeft, center })
            {
                group.style.transformOrigin = new TransformOrigin(0, 0);
                group.style.scale = new Scale(new Vector3(scale, scale, 1));
            }
            center.style.left = (int)width / 2; center.style.top = (int)height / 2;
            expFrame.style.width = width;
            if (fullHealthWidth <= 0 && float.IsFinite(health.resolvedStyle.width) && health.resolvedStyle.width > 0)
            {
                fullHealthWidth = health.resolvedStyle.width;
                if (state != null) SetHealth(state.HealthFraction);
            }
            DrawExperience();
        }

        public void Dispose()
        {
            View.UnregisterCallback(geometry);
            Portrait.image = null;
            View.RemoveFromHierarchy();
        }
    }
}
