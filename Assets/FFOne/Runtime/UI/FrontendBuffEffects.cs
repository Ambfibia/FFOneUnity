using System;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        BuffState worldBuffs;
        VisualElement buffStrip;
        int displayedBuffFlags = -1;
        readonly System.Collections.Generic.Dictionary<int, ShinyInterest> detectorShinies = new();
        VisualElement detectorPanel;
        Label detectorText;
        bool itemUserConfigured;
        void StartBuffEffects(WorldEntry entry)
        {
            worldBuffs = ProtocolBuff.FromEntry(entry, Time.unscaledTimeAsDouble);
            buffStrip = new VisualElement { name = "gameplay-buffs", pickingMode = PickingMode.Ignore };
            buffStrip.style.position = Position.Absolute; buffStrip.style.left = 28; buffStrip.style.top = 260;
            buffStrip.style.flexDirection = FlexDirection.Row; Root.Add(buffStrip);
            detectorPanel = new ScrollView { name = "gameplay-detectors", pickingMode = PickingMode.Ignore };
            detectorPanel.style.position = Position.Absolute; detectorPanel.style.right = 24; detectorPanel.style.top = 140;
            detectorPanel.style.width = 230; detectorPanel.style.maxHeight = 240;
            detectorText = new Label(); detectorText.style.whiteSpace = WhiteSpace.Normal; detectorText.style.fontSize = 12;
            detectorPanel.Add(detectorText); Root.Add(detectorPanel); detectorShinies.Clear();
            networkWorld?.SetBuffState(worldBuffs); displayedBuffFlags = -1; TickBuffEffects();
            if (entry.BootstrapPackets != null) foreach (var frame in entry.BootstrapPackets) ApplyBuffPacket(frame);
        }
        void StopBuffEffects()
        {
            buffStrip?.RemoveFromHierarchy(); buffStrip = null; worldBuffs = null; displayedBuffFlags = -1; itemUserConfigured = false;
            detectorPanel?.RemoveFromHierarchy(); detectorPanel = null; detectorText = null; detectorShinies.Clear();
            networkWorld?.SetBuffState(null);
        }
        void TickBuffEffects()
        {
            if (worldBuffs == null || buffStrip == null) return;
            if (!itemUserConfigured && gameplayUi != null)
            { gameplayUi.SetItemUser((slot, nano) => Session.UseItemAsync(slot, nano, worldLifetime.Token)); itemUserConfigured = true; }
            gameplayUi?.TickItemUse();
            if (displayedBuffFlags != worldBuffs.Condition)
            {
                buffStrip.Clear(); displayedBuffFlags = worldBuffs.Condition;
                for (int id = 1; id <= 25; id++) if (worldBuffs.Has(id))
                {
                    var cell = new VisualElement { name = "buff-" + id, pickingMode = PickingMode.Ignore };
                    cell.style.width = 34; cell.style.height = 48; cell.style.marginRight = 3;
                    string address = GameplayUiCatalog.Load().BuffIcon(id);
                    var icon = new Image { name = "icon", image = string.IsNullOrEmpty(address) ? null : ContentAssets.Load<Texture2D>(address), scaleMode = ScaleMode.ScaleToFit };
                    icon.style.width = icon.style.height = 32; cell.Add(icon);
                    var timer = new Label { name = "remaining" }; timer.style.fontSize = 10; timer.style.unityTextAlign = TextAnchor.MiddleCenter;
                    cell.Add(timer); buffStrip.Add(cell);
                }
            }
            networkWorld?.RefreshBuffPresentation();
            RefreshBuffDetectors();
            for (int id = 1; id <= 25; id++)
            {
                var timer = buffStrip.Q<VisualElement>("buff-" + id)?.Q<Label>("remaining");
                if (timer == null) continue;
                var remaining = worldBuffs.RemainingSeconds(id, Time.unscaledTimeAsDouble);
                timer.text = remaining.HasValue ? Math.Ceiling(remaining.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            }
        }
        void ApplyBuffPacket(ProtocolFrame frame)
        {
            if (worldBuffs == null) return;
            if (frame.PacketType == ProtocolTravel.WarpOk) detectorShinies.Clear();
            if (ProtocolShiny.TryDecode(frame, out var shinyUpdates))
                foreach (var shiny in shinyUpdates) { if (shiny.Removed) detectorShinies.Remove(shiny.Id); else detectorShinies[shiny.Id] = shiny; }
            gameplayUi?.ReceiveItemUse(frame);
            if (ProtocolItemUse.TryDecode(frame, out var item)) ApplyBuffConditions(item.Effects);
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
            detectorPanel.style.display = enemies || treasures ? DisplayStyle.Flex : DisplayStyle.None;
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
