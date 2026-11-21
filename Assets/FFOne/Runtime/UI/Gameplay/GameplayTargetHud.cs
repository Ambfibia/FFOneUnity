using System.Globalization;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal sealed class GameplayTargetHud
    {
        static readonly string[] AffinityBacks = { "blue_icon", "red_icon", "yellow_icon" };
        static readonly string[] AffinityIcons = { "a_icon", "b_icon", "c_icon" };
        static readonly string[] MatchIcons = { "nano_win", "nano_draw", "nano_lose" };

        internal const string Art = "ui/en/gameplay/shared/";
        internal const string AffinityArt = "ui/en/gameplay/nano/affinity/";
        readonly VisualElement info, hpBar, damage, hostileDisk, friendlyDisk;
        readonly Image frame, portraitImage, affinityBack, affinityFront, affinityIcon, matchup, reticle;
        readonly Label name, levelLabel;
        readonly VisualElement root;
        object owner;
        NetworkEntityView playerActor;
        GameplayNpcPortrait playerPortrait;
        string language;
        internal bool Visible => info.style.display.value == DisplayStyle.Flex;
        float delayedHealth, hold;
        bool targetHostile;
        int tickFrame = -1;

        internal GameplayTargetHud(VisualElement root, string prefix)
        {
            this.root = root;
            var template = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "ff-target-hud");
            root.Add(template);
            info = Bind<VisualElement>("info");
            frame = Bind<Image>("frame");
            hostileDisk = Bind<VisualElement>("hostile-disk");
            affinityBack = Bind<Image>("affinity-back");
            portraitImage = Bind<Image>("portrait");
            affinityFront = Bind<Image>("affinity-front");
            affinityIcon = Bind<Image>("affinity-icon");
            damage = Bind<VisualElement>("hp-damage");
            hpBar = Bind<VisualElement>("hp");
            name = Bind<Label>("name");
            levelLabel = Bind<Label>("level");
            friendlyDisk = Bind<VisualElement>("friendly-disk");
            matchup = Bind<Image>("matchup");
            reticle = Bind<Image>("reticle");
            T Bind<T>(string role) where T : VisualElement
            {
                var control = FFView.RequireClass<T>(template, "ff-target-" + role);
                control.name = prefix + "-target-" + role;
                return control;
            }
        }

        internal void Hide()
        {
            info.style.display = reticle.style.display = DisplayStyle.None;
            owner = null;
            SetPlayerPortrait(null);
        }

        internal void Bind(object actor, string displayName, int? level, bool hostile, float? hp, int style,
            int? nanoStyle, Texture portrait, string locale)
        {
            info.style.display = DisplayStyle.Flex;
            reticle.style.display = DisplayStyle.None;

            float health = hp ?? 0;
            if (!ReferenceEquals(owner, actor) || language != locale)
            {
                owner = actor;
                language = locale;
                delayedHealth = health; hold = .4f;
                targetHostile = hostile;
                hostileDisk.style.display = hostile ? DisplayStyle.Flex : DisplayStyle.None;
                friendlyDisk.style.display = hostile ? DisplayStyle.None : DisplayStyle.Flex;
                frame.image = FFWidgets.Texture(hostile ? "ui/" + locale + "/gameplay/shared/mop_info" : Art + "npc_info");
                bool affinity = hostile && style >= 0 && style < 3;
                affinityBack.image = affinityFront.image = affinity ? FFWidgets.Texture(AffinityArt + AffinityBacks[style]) : null;
                affinityIcon.image = affinity ? FFWidgets.Texture(AffinityArt + AffinityIcons[style]) : null;
                affinityBack.style.display = affinityFront.style.display = affinityIcon.style.display = affinity ? DisplayStyle.Flex : DisplayStyle.None;
                Layout();
            }
            if (!playerActor) portraitImage.image = portrait;
            portraitImage.style.display = portraitImage.image ? DisplayStyle.Flex : DisplayStyle.None;
            name.text = displayName;
            levelLabel.text = level?.ToString("00", CultureInfo.InvariantCulture) ?? "";
            levelLabel.style.display = level.HasValue && (hostile || playerActor) ? DisplayStyle.Flex : DisplayStyle.None;
            hpBar.style.display = damage.style.display = hostile && hp.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            // TickHud is also called before input. Animate the damage trail only once
            // per rendered frame, using the same hold and decay as the player panel.
            if (tickFrame != Time.frameCount)
            {
                tickFrame = Time.frameCount;
                if (health >= delayedHealth) { delayedHealth = health; hold = .4f; }
                else if (hold > 0) hold -= Time.unscaledDeltaTime;
                else
                {
                    delayedHealth = Mathf.Lerp(delayedHealth, health, Time.unscaledDeltaTime * 5);
                    if (delayedHealth - health < .001f) delayedHealth = health;
                }
            }
            hpBar.style.width = 199 * health;
            damage.style.width = 199 * delayedHealth;
            int affinityResult = FFOne.Tutorial.TutorialTargetHudLayout.Matchup(hostile ? style : -1, nanoStyle);
            matchup.style.display = affinityResult >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (affinityResult >= 0)
            {
                matchup.image = FFWidgets.Texture(Art + MatchIcons[affinityResult]);
                if (matchup.image) FFWidgets.Rect(matchup, 0, 0, matchup.image.width, matchup.image.height);
            }
        }

        internal void SetPlayerPortrait(NetworkEntityView actor)
        {
            if (ReferenceEquals(playerActor, actor)) return;
            playerActor = actor;
            if (!actor)
            {
                playerPortrait?.Dispose();
                playerPortrait = null;
                return;
            }
            playerPortrait ??= new GameplayNpcPortrait(portraitImage, () => playerActor, 128, 128, 1.4f, 1.4f, 0, true);
        }

        internal void AimAt(Camera camera, Vector3 position, bool hostile)
        {
            if (!camera || !Visible) return;
            Vector3 point = camera.WorldToViewportPoint(position);
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (point.z <= 0 || !float.IsFinite(width) || !float.IsFinite(height)) return;
            reticle.image = FFWidgets.Texture(Art + (hostile ? "target_icon1a" : "target_icon2"));
            FFWidgets.Rect(reticle, FFOne.Tutorial.TutorialTargetHudLayout.Reticle(new Vector2(point.x * width, (1 - point.y) * height), hostile));
            float pulse = .5f + (Mathf.Sin(Time.time * 5) + 1) * .25f;
            reticle.tintColor = new Color(pulse, pulse, pulse, pulse);
            reticle.style.display = DisplayStyle.Flex;
        }

        internal void Layout()
        {
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height), panelWidth = targetHostile ? 290 : 255;
            FFWidgets.Rect(info, width / 2 - panelWidth * scale / 2, 5 * scale, panelWidth, targetHostile ? 76 : 66);
            frame.style.width = panelWidth;
            info.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

    }
}
