using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal sealed class GameplayHudStatus
    {
        readonly VisualElement activeNanoInfo;
        readonly Label activeNanoName;
        readonly Image activeNanoSkill, combatFrame, combatDanger;
        readonly VisualElement stamina;
        int nanoId, skillId;
        string language;

        internal GameplayHudStatus(VisualElement root, string prefix, string language)
        {
            this.language = language;
            var template = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "ff-combat-hud");
            combatFrame = Bind<Image>("combat-frame");
            combatFrame.image = FFWidgets.Texture(GameplayTargetHud.Art + "combat-frame");
            combatDanger = Bind<Image>("combat-danger");
            combatDanger.image = FFWidgets.Texture("ui/" + language + "/gameplay/shared/danger");
            activeNanoInfo = Bind<VisualElement>("active-nano-info");
            activeNanoName = Bind<Label>("active-nano-name");
            stamina = Bind<VisualElement>("active-nano-stamina");
            activeNanoSkill = Bind<Image>("active-nano-skill");
            combatFrame.RemoveFromHierarchy();
            root.Insert(0, combatFrame);
            root.Add(template);

            T Bind<T>(string role) where T : VisualElement
            {
                var control = FFView.RequireClass<T>(template, "ff-" + role);
                control.name = prefix + "-" + role;
                return control;
            }
        }

        internal void ShowCombat(bool combat, bool targetVisible, bool suppressFrame)
        {
            combatFrame.style.display = combat && !suppressFrame ? DisplayStyle.Flex : DisplayStyle.None;
            combatFrame.tintColor = new Color(1, 1, 1, FrameAlpha(Time.realtimeSinceStartup));
            combatDanger.style.display = combat && !targetVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void BindNano(int id, int skill, float fraction)
        {
            activeNanoInfo.style.display = id > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (id <= 0) return;
            var catalog = GameplayUiCatalog.Load();
            if (nanoId != id || skillId != skill)
            {
                nanoId = id; skillId = skill;
                activeNanoName.text = catalog.Nano(id, language)?.Name ?? "";
                activeNanoSkill.image = FFWidgets.Texture(catalog.SkillIcon(skill));
            }
            stamina.style.width = 104 * Mathf.Clamp01(fraction);
        }

        internal void SetLanguage(string value)
        {
            if (language == value) return;
            language = value;
            nanoId = 0;
            combatDanger.image = FFWidgets.Texture("ui/" + language + "/gameplay/shared/danger");
        }

        internal void Layout(float width, float height)
        {
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float scale = RetrobutionLayout.Scale(height);
            if (combatDanger.image)
                FFWidgets.Rect(combatDanger, (width - combatDanger.image.width * scale) / 2, 0,
                    combatDanger.image.width * scale, combatDanger.image.height * scale);
            activeNanoInfo.style.left = 101 * scale; activeNanoInfo.style.top = 42 * scale;
            activeNanoInfo.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        internal static bool CombatActive(float lastResult, float now) => float.IsFinite(lastResult) && float.IsFinite(now)
            && now >= lastResult && now - lastResult <= 5;

        internal static float FrameAlpha(float realtime) => float.IsFinite(realtime) ? (Mathf.Sin(realtime * Mathf.PI * 2) + 1) * .5f : 0;
    }
}
