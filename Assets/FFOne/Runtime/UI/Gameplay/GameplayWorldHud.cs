using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        GameplayTargetHud targetHud;
        Image useTrigger;
        VisualElement launcherHud, launcherBar;

        internal void ShowLauncher(bool active, float charge)
        {
            if (!active)
            {
                if (launcherHud != null) { launcherHud.RemoveFromHierarchy(); launcherHud = launcherBar = null; }
                return;
            }
            if (launcherHud == null)
            {
                launcherHud = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "world-launcher");
                launcherBar = FFView.RequireClass<VisualElement>(launcherHud, "world-launcher-power");
                layers.Add(launcherHud, UiLayer.Cinematic);
            }
            launcherBar.style.top = 30 + (1 - Mathf.Clamp01(charge)) * (500 - 31);
        }
        GameplayHudStatus hudStatus;
        NetworkEntityView targetActor;
        float lastCombatResult = float.NegativeInfinity;

        internal void ReceiveCombat(ProtocolFrame frame)
        {
            int player = session.Snapshot.PlayerId;
            if (!ProtocolGameplay.TryCombat(frame, out var combat)) return;
            bool outgoing = combat.WeaponBattery.HasValue || combat.AttackerKind == WorldEntityKind.Player && combat.AttackerId == player;
            if (outgoing || combat.Hits.Any(hit => hit.Kind == WorldEntityKind.Player && hit.Id == player))
                lastCombatResult = Time.realtimeSinceStartup;
            RecordCombatPresentation(combat, outgoing);
        }

        internal void UpdateTarget(NetworkEntityView aim, Transform player, Camera view, bool blocked, bool hasUseTarget = false)
        {
            if (hasUseTarget && !blocked)
            {
                if (useTrigger == null)
                {
                    useTrigger = (Image)FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "world-use-trigger");
                    useTrigger.image = FFWidgets.Texture("ui/en/gameplay/interaction/icons/use-trigger");
                    Hud.View.Add(useTrigger);
                }
                useTrigger.style.display = DisplayStyle.Flex;
                float pulse = .75f + Mathf.Sin(Time.time * 5) * .25f;
                useTrigger.tintColor = new Color(pulse, pulse, pulse, pulse);
            }
            else if (useTrigger != null) useTrigger.style.display = DisplayStyle.None;
            var state = session.Snapshot;
            if (blocked || InputBlocked || !player || !view || !state.IsActive || state.Hp <= 0)
                targetActor = null;
            else if (IsVisible(aim)) targetActor = aim;
            else if (!IsVisible(targetActor) || !targetActor.Hostile) targetActor = null;

            if (targetActor)
            {
                var actor = targetActor;
                bool playerTarget = actor.Entity.Kind == WorldEntityKind.Player;
                float distance = Vector3.Distance(player.position, actor.transform.position);
                if (!actor.Hostile && distance >= 10 || actor.Hostile && distance > 25)
                    targetActor = null;
                else
                {
                    targetHud.SetPlayerPortrait(playerTarget ? actor : null);
                    if (playerTarget)
                    {
                        var character = actor.Entity.Character;
                        targetHud.Bind(actor, character.DisplayName, character.Level > 0 ? character.Level : null,
                            false, null, -1, null, null, language);
                        if (actor == aim) targetHud.AimAt(view, actor.transform.position + Vector3.up * 1.44f, false);
                    }
                    else BindNpcTarget(actor, aim, view, state);
                }
            }
            if (!targetActor) targetHud.Hide();
            hudStatus.ShowCombat(!blocked && !InputBlocked && state.Hp > 0 && GameplayHudStatus.CombatActive(lastCombatResult, Time.realtimeSinceStartup),
                targetHud.Visible, false);
        }

        void BindNpcTarget(NetworkEntityView actor, NetworkEntityView aim, Camera view, GameplayUiState state)
        {
            var npc = actor.Definition;
            int type = actor.Entity.NpcType;
            var catalog = GameplayUiCatalog.Load();
            string key = "content.npc." + type + ".name";
            string title = LocalizationCatalog.Lookup(key, language);
            float? hp = npc.MaxHp >= actor.Entity.Hp && npc.MaxHp > 0 ? (float)actor.Entity.Hp / npc.MaxHp : null;
            int? nanoStyle = state.ActiveNanoSlot >= 0 && state.ActiveNanoSlot < state.NanoSlots.Count
                ? catalog.Nano(state.NanoSlots[state.ActiveNanoSlot])?.NanoStyle : null;
            targetHud.Bind(actor, title == key ? npc.Name : title, catalog.NpcLevel(type), actor.Hostile,
                hp, npc.Style, nanoStyle, FFWidgets.Texture(catalog.NpcPortrait(type)), language);
            if (actor == aim)
                targetHud.AimAt(view, actor.transform.position + Vector3.up * npc.Height * (actor.Hostile ? .5f : .8f), actor.Hostile);
        }

        static bool IsVisible(NetworkEntityView actor) => actor && actor.isActiveAndEnabled && actor.Entity != null &&
            !actor.Entity.Hidden && actor.Entity.Hp > 0 &&
            (actor.Entity.Kind == WorldEntityKind.Npc && actor.Definition != null ||
                actor.Entity.Kind == WorldEntityKind.Player && actor.Entity.Character != null && actor.Avatar && actor.Avatar.IsReady);

        void BindActiveNano(GameplayUiState state)
        {
            var nano = state.ActiveNanoSlot >= 0 && state.ActiveNanoSlot < state.NanoSlots.Count
                ? state.Nanos.FirstOrDefault(value => value.Id == state.NanoSlots[state.ActiveNanoSlot]) : default;
            int? max = nano.IsOwned ? GameplayUiCatalog.Load().Nano(nano.Id)?.NanoMaxStamina : null;
            hudStatus.BindNano(nano.Id, nano.SkillId, max > 0 ? (float)nano.Stamina / max.Value : 0);
        }
    }
}
