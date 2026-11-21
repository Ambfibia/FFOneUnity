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
        readonly VisualElement projectileTrajectory;
        readonly Vector2[] trajectoryPoints = new Vector2[33];
        readonly bool[] trajectoryInFront = new bool[33];
        int trajectoryCount;
        readonly VisualElement playerBackground;
        readonly StyleBackground englishPlayerBackground;
        readonly Label name, level;
        readonly EventCallback<GeometryChangedEvent> geometry;
        GameplayUiState state;
        float fullHealthWidth, healthFraction, delayedHealth, hold;
        float? experienceFraction;
        bool healthInitialized;

        public GameplayHudController(VisualElement parent, string language = "en")
        {
            this.parent = parent;
            View = FFView.Instantiate("ui/views/Retrobution/GameplayHud", "hud-root");
            playerBackground = FFView.Require<VisualElement>(View, "hud-player-background");
            englishPlayerBackground = playerBackground.style.backgroundImage;
            SetLanguage(language);
            topLeft = FFView.Require<VisualElement>(View, "hud-top-left");
            center = FFView.RequireClass<VisualElement>(View, "hud-projectile-crosshair");
            projectileTrajectory = FFView.RequireClass<VisualElement>(View, "hud-projectile-trajectory");
            projectileTrajectory.generateVisualContent += DrawProjectileTrajectory;
            health = FFView.Require<VisualElement>(View, "hud-hp");
            damage = FFView.Require<VisualElement>(View, "hud-hp-damage");
            expFrame = FFView.Require<VisualElement>(View, "hud-exp-frame");
            expFill = FFView.Require<VisualElement>(View, "hud-exp-fill");
            Portrait = FFView.Require<Image>(View, "hud-portrait");
            name = FFView.Require<Label>(View, "hud-name");
            level = FFView.Require<Label>(View, "hud-level");
            name.enableRichText = level.enableRichText = false;
            // CnGuiCharacter_info uses pMenuSkin.label (Chalet), including the
            // level digits. Keep its accepted Cyrillic-capable replacement.
            FFWidgets.Font(name, true, 12); FFWidgets.Font(level, true, 12);
            geometry = _ => Layout();
            View.RegisterCallback(geometry);
            parent.Add(View);
            SetExperienceVisible(false); // Primary localized.expBartype default is zero.
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

        public void SetLanguage(string language) => playerBackground.style.backgroundImage = language == "ru"
            ? new StyleBackground(FFWidgets.Texture("ui/ru/gameplay/player/character_info")) : englishPlayerBackground;

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

        internal void SetProjectileAim(bool rocket, Camera view = null, Vector3[] points = null, int count = 0)
        {
            center.EnableInClassList("ui-suppressed", !rocket);
            trajectoryCount = projectileTrajectory.panel == null ? 0 : Mathf.Min(count, trajectoryPoints.Length);
            projectileTrajectory.EnableInClassList("ui-suppressed", trajectoryCount < 2);
            if (trajectoryCount < 2) return;

            for (int i = 0; i < trajectoryCount; i++)
            {
                trajectoryInFront[i] = view.WorldToViewportPoint(points[i]).z > view.nearClipPlane;
                if (trajectoryInFront[i])
                    trajectoryPoints[i] = projectileTrajectory.WorldToLocal(
                        RuntimePanelUtils.CameraTransformWorldToPanel(projectileTrajectory.panel, points[i], view));
            }
            projectileTrajectory.MarkDirtyRepaint();
        }

        void DrawProjectileTrajectory(MeshGenerationContext context)
        {
            if (trajectoryCount < 2) return;
            var painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            for (int i = 1; i < trajectoryCount; i += 2)
            {
                if (!trajectoryInFront[i - 1] || !trajectoryInFront[i]) continue;
                painter.MoveTo(trajectoryPoints[i - 1]);
                painter.LineTo(trajectoryPoints[i]);
            }
            if (trajectoryInFront[trajectoryCount - 1])
            {
                var landing = trajectoryPoints[trajectoryCount - 1];
                painter.MoveTo(landing + new Vector2(0, -7));
                painter.LineTo(landing + new Vector2(9, 0));
                painter.LineTo(landing + new Vector2(0, 7));
                painter.LineTo(landing + new Vector2(-9, 0));
                painter.ClosePath();
            }
            painter.lineWidth = 5;
            painter.strokeColor = new Color(.12f, .08f, .04f, .8f);
            painter.Stroke();
            painter.lineWidth = 2;
            painter.strokeColor = new Color(1, .78f, .24f);
            painter.Stroke();
        }

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
            projectileTrajectory.generateVisualContent -= DrawProjectileTrajectory;
            View.UnregisterCallback(geometry);
            Portrait.image = null;
            View.RemoveFromHierarchy();
        }
    }
}
