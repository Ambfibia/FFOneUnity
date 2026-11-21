using System;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        const string BuffHudAddress = "ui/views/Retrobution/GameplayHud";
        readonly Label[] buffTimers = new Label[26];
        BuffState worldBuffs;
        VisualElement buffStrip;
        int displayedBuffFlags = -1;
        readonly System.Collections.Generic.Dictionary<int, ShinyInterest> detectorShinies = new();
        NativeWorldShinies worldShinies;
        VisualElement detectorPanel;
        Label detectorText;
        bool itemUserConfigured;
        void StartBuffEffects(WorldEntry entry)
        {
            worldBuffs = ProtocolBuff.FromEntry(entry, Time.unscaledTimeAsDouble);
            buffStrip = FFView.InstantiateClass(BuffHudAddress, "gameplay-buffs");
            detectorPanel = FFView.InstantiateClass(BuffHudAddress, "gameplay-detectors");
            detectorText = FFView.RequireClass<Label>(detectorPanel, "gameplay-detector-copy");
            var layers = UiLayers.For(Root);
            layers.Add(buffStrip, UiLayer.Hud);
            layers.Add(detectorPanel, UiLayer.Hud);
            detectorShinies.Clear();
            networkWorld?.SetBuffState(worldBuffs); displayedBuffFlags = -1; TickBuffEffects();
            if (entry.BootstrapPackets != null) foreach (var frame in entry.BootstrapPackets) ApplyBuffPacket(frame);
        }
        void StopBuffEffects()
        {
            if (worldShinies) { worldShinies.Clear(); Destroy(worldShinies.gameObject); worldShinies = null; }
            buffStrip?.RemoveFromHierarchy(); buffStrip = null; worldBuffs = null; displayedBuffFlags = -1; itemUserConfigured = false;
            detectorPanel?.RemoveFromHierarchy(); detectorPanel = null; detectorText = null; detectorShinies.Clear();
            Array.Clear(buffTimers, 0, buffTimers.Length);
            networkWorld?.SetBuffState(null);
        }
        void TickBuffEffects()
        {
            if (worldBuffs == null || buffStrip == null) return;
            if (!worldShinies && networkWorld && networkWorld.Player)
            {
                var holder = new GameObject("Network E.G.G. interests");
                holder.transform.SetParent(networkWorld.transform, false);
                worldShinies = holder.AddComponent<NativeWorldShinies>();
                worldShinies.Initialize(networkWorld);
                foreach (var value in detectorShinies.Values) worldShinies.Apply(value);
            }
            if (worldShinies) worldShinies.Tick(gameplayState.Snapshot.Hp > 0, worldBuffs.Has(14), RequestShiny);
            if (!itemUserConfigured && gameplayUi != null)
            { gameplayUi.SetItemUser((slot, nano) => Session.UseItemAsync(slot, nano, worldLifetime.Token)); itemUserConfigured = true; }
            gameplayUi?.TickItemUse();
            if (displayedBuffFlags != worldBuffs.Condition)
            {
                buffStrip.Clear();
                Array.Clear(buffTimers, 0, buffTimers.Length);
                displayedBuffFlags = worldBuffs.Condition;
                var catalog = GameplayUiCatalog.Load();
                for (int id = 1; id < buffTimers.Length; id++)
                {
                    if (!worldBuffs.Has(id)) continue;
                    var cell = FFView.InstantiateClass(BuffHudAddress, "gameplay-buff-cell");
                    cell.name = "buff-" + id;
                    string address = catalog.BuffIcon(id);
                    FFView.RequireClass<Image>(cell, "gameplay-buff-icon").image = FFWidgets.Texture(address);
                    buffTimers[id] = FFView.RequireClass<Label>(cell, "gameplay-buff-timer");
                    buffStrip.Add(cell);
                }
            }
            networkWorld?.RefreshBuffPresentation();
            RefreshBuffDetectors();
            for (int id = 1; id < buffTimers.Length; id++)
            {
                var timer = buffTimers[id];
                if (timer == null) continue;
                var remaining = worldBuffs.RemainingSeconds(id, Time.unscaledTimeAsDouble);
                timer.text = remaining.HasValue ? Math.Ceiling(remaining.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            }
        }
        System.Threading.Tasks.Task RequestShiny(int id) => Session.PickupShinyAsync(id, worldLifetime.Token);
        void ApplyBuffPacket(ProtocolFrame frame)
        {
            if (worldBuffs == null) return;
            if (frame.PacketType == ProtocolTravel.WarpOk) { detectorShinies.Clear(); if (worldShinies) worldShinies.Clear(); }
            if (ProtocolShiny.TryDecode(frame, out var shinyUpdates))
                foreach (var shiny in shinyUpdates)
                {
                    if (shiny.Removed) detectorShinies.Remove(shiny.Id); else detectorShinies[shiny.Id] = shiny;
                    if (worldShinies) worldShinies.Apply(shiny);
                }
            if (worldShinies && frame.PacketType is ProtocolShiny.PickupOk or ProtocolShiny.PickupFail)
                worldShinies.Acknowledged(frame.PacketType == ProtocolShiny.PickupOk, PlayUiSound, () => gameplayUi?.ShowEggPickupChat());
            gameplayUi?.ReceiveItemUse(frame);
            if (ProtocolItemUse.TryDecode(frame, out var item)) ApplyBuffConditions(item.Effects);
            if (ProtocolBuff.TryNanoCondition(frame, gameplayState.Snapshot.PlayerId, out int nanoCondition))
                worldBuffs.SetCondition(nanoCondition);
            if (ProtocolBuff.TryUpdate(frame, out var update)) worldBuffs.Apply(update, Time.unscaledTimeAsDouble);
            if (ProtocolBuff.TryEffects(frame, out var periodic)) ApplyBuffConditions(periodic);
            if (ProtocolNpcSkill.TryDecode(frame, out var npc)) ApplyBuffConditions(npc);
            if (ProtocolNanoSkill.TryDecode(frame, out var nano))
            {
                ApplyBuffConditions(nano.Effects);
                if (nano.Effects.Any(e => e.Kind == WorldEntityKind.Player && e.Id == gameplayState.Snapshot.PlayerId && e.Resurrected)
                    && gameplayState.Snapshot.Hp == 0) _ = ReviveByGroupBuff();
            }
            TickBuffEffects();
        }
        // Functional detector continuation: only ordinary server interests are
        // exposed, with no invented detection range or hidden world table scan.
        void RefreshBuffDetectors()
        {
            if (detectorPanel == null || detectorText == null || !networkWorld || !networkWorld.Player) return;
            bool enemies = worldBuffs.Has(13), treasures = worldBuffs.Has(14);
            detectorPanel.EnableInClassList("ui-suppressed", !enemies && !treasures);
            if (!enemies && !treasures) return;
            var origin = networkWorld.Player.position;
            var lines = new System.Collections.Generic.List<(float distance, string text)>();
            string Bearing(Vector3 point)
            {
                var delta = point - origin;
                float angle = Mathf.Repeat(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg + 22.5f, 360);
                string[] directions = language == "ru" ? new[] { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" } : new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                return directions[Mathf.FloorToInt(angle / 45) % 8];
            }
            if (enemies && networkWorld.Replicas != null)
                foreach (var actor in networkWorld.Replicas.Actors)
                    if (actor.Hostile && actor.Entity.Hp > 0 && !actor.Entity.Hidden)
                    {
                        float distance = Vector3.Distance(origin, actor.transform.position);
                        lines.Add((distance, actor.Definition.Name + "  " + Mathf.RoundToInt(distance) + "m " + Bearing(actor.transform.position)));
                    }
            if (treasures)
                foreach (var shiny in detectorShinies.Values)
                    if (shiny.Map == gameplayState.Snapshot.MapNumber)
                    {
                        var point = new Vector3(shiny.X, shiny.Z, shiny.Y) / 100f; float distance = Vector3.Distance(origin, point);
                        lines.Add((distance, "E.G.G.  " + Mathf.RoundToInt(distance) + "m " + Bearing(point)));
                    }
            detectorText.text = string.Join("\n", lines.OrderBy(line => line.distance).Select(line => line.text));
        }
        void ApplyBuffConditions(System.Collections.Generic.IReadOnlyList<NanoSkillEffect> effects)
        {
            foreach (var effect in effects)
                if (effect.Kind == WorldEntityKind.Player && effect.Id == gameplayState.Snapshot.PlayerId && effect.Condition.HasValue)
                    worldBuffs.SetCondition(effect.Condition.Value);
        }
        async System.Threading.Tasks.Task ReviveByGroupBuff()
        {
            try { await Session.RevivePhoenixAsync(true, worldLifetime.Token); }
            catch (Exception error) { if (!(error is OperationCanceledException)) ShowError(error.Message); }
        }
    }
}
