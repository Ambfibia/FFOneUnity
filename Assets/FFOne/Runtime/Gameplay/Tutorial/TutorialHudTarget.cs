using FFOne.UI;
using UnityEngine;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        GameplayTargetHud tutorialTargetHud;

        void CreateTutorialTargetHud() => tutorialTargetHud = new GameplayTargetHud(tutorialHudRoot, "tutorial");

        void TickTutorialTargetHud()
        {
            var actor = target;
            if (tutorialTargetHud == null) return;
            if (!actor || actor.Hp <= 0 || !Player ||
                (!actor.Hostile && Vector3.Distance(Player.position, actor.transform.position) >= 10))
            {
                tutorialTargetHud.Hide();
                return;
            }

            var catalog = GameplayUiCatalog.Load();
            string name = Localized("content.npc." + actor.NpcType + ".name");
            tutorialTargetHud.Bind(actor, name.StartsWith("content.npc.") ? actor.DisplayName : name,
                catalog.NpcLevel(actor.NpcType), actor.Hostile,
                actor.MaxHp > 0 ? Mathf.Clamp01((float)actor.Hp / actor.MaxHp) : null, actor.Style,
                nanoActive ? catalog.Nano(1)?.NanoStyle : null,
                FFWidgets.Texture(catalog.NpcPortrait(actor.NpcType)), Language);
            if (!actor.Hostile || Vector3.Distance(Player.position, actor.transform.position) <= 16)
                tutorialTargetHud.AimAt(ViewCamera, actor.transform.position + Vector3.up * actor.Height * (actor.Hostile ? .5f : .8f), actor.Hostile);
        }

        void LayoutTutorialTargetHud() => tutorialTargetHud?.Layout();
        void ReleaseTutorialTargetHud() => tutorialTargetHud = null;
    }


    public static class TutorialTargetHudLayout
    {
        // PrintName target icons are screen-pixel overlays, independently of HUD scale.
        public static Rect Reticle(Vector2 center, bool hostile)
        {
            Vector2 size = hostile ? new Vector2(100, 86) : new Vector2(42, 41);
            return new Rect(center - size / 2, size);
        }

        public static int Matchup(int mobStyle, int? activeNanoStyle)
        {
            if (mobStyle < 0 || mobStyle > 2 || !activeNanoStyle.HasValue || activeNanoStyle < 0 || activeNanoStyle > 2) return -1;
            int difference = mobStyle - activeNanoStyle.Value;
            return difference == 0 ? 1 : difference == 1 || difference == -2 ? 0 : 2;
        }
    }
}
