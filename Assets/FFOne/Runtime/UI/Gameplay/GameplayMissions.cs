using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        Func<int, int, Task> startMission, endMission;
        Func<int, Task> stopMission, trackMission;
        WorldEntity missionNpc;
        bool missionSending;
        bool showCompletedMissions;
        int selectedMissionTask;
        float nextMissionClockRefresh;
        public Func<int, int?> MissionTimeRemaining { get; set; }
        void TickMissionClock()
        {
            if (Windows.Current == GameplayWindow.Journal && UnityEngine.Time.unscaledTime >= nextMissionClockRefresh &&
                session.Snapshot.RunningQuests.Any(quest => quest.TaskId == selectedMissionTask && quest.GrantedTimeSeconds > 0))
            { nextMissionClockRefresh = UnityEngine.Time.unscaledTime + 1; RefreshJournal(); }
        }
        public void OpenMissionReward(int task, WorldEntity npc)
        {
            showCompletedMissions = false; selectedMissionTask = task; missionNpc = npc;
            if (Windows.Current == GameplayWindow.Journal) RefreshJournal(); else Windows.Open(GameplayWindow.Journal);
        }
        public void ConfigureMissions(Func<int, int, Task> start, Func<int, int, Task> end, Func<int, Task> stop, Func<int, Task> track)
        { startMission = start; endMission = end; stopMission = stop; trackMission = track; }
        public void OpenNpcMissions(WorldEntity npc)
        {
            if (npc == null || npc.Kind != WorldEntityKind.Npc || npc.Hp <= 0) return;
            showCompletedMissions = false; missionNpc = npc;
            if (Windows.Current == GameplayWindow.Journal) RefreshJournal(); else Windows.Open(GameplayWindow.Journal);
        }
        void RefreshJournal()
        {
            if (Windows.Current != GameplayWindow.Journal || Windows.View == null) return;
            var root = Windows.View; var list = FFView.Require<ScrollView>(root, "journal-list"); list.Clear();
            var rules = WorldMissionCatalog.Load(); var catalog = GameplayUiCatalog.Load(); var state = session.Snapshot;
            FFView.Require<VisualElement>(root, "journal-details").style.display = DisplayStyle.None;
            FFView.Require<VisualElement>(root, "journal-empty").style.display = DisplayStyle.Flex;
            if (showCompletedMissions)
            {
                foreach (var rule in rules.Tasks.Where(rule => rule.IsFinal && state.CompletedMissions.Contains(rule.MissionId)))
                    Add(rule, null, false, true);
                return;
            }
            foreach (var quest in state.RunningQuests.Where(q => q.TaskId > 0))
            {
                var rule = rules.Find(quest.TaskId); if (rule == null) continue;
                Add(rule, quest, false);
            }
            if (missionNpc != null)
                foreach (var rule in rules.Tasks.Where(r => r.GiverNpc == missionNpc.NpcType && r.MinimumLevel <= state.Level &&
                    (r.MapNumber == 0 || r.MapNumber == state.MapNumber) && (r.RequiredGuide == 0 || r.RequiredGuide == state.Guide) &&
                    !state.CompletedMissions.Contains(r.MissionId) && !state.RunningQuests.Any(q => rules.Find(q.TaskId)?.MissionId == r.MissionId) &&
                    r.RequiredMissions.All(id => id == 0 || state.CompletedMissions.Contains(id)) &&
                    r.RequiredNanos.All(id => id == 0 || state.Nanos.Any(n => n.Id == id)) && r.IsFirst))
                    Add(rule, null, true);

            void Add(WorldMissionDefinition rule, GameplayQuestState quest, bool offered, bool completed = false)
            {
                var row = FFView.Instantiate("ui/views/Retrobution/MissionRow", "mission-row");
                var content = catalog.Mission(rule.TaskId, language);
                FFView.Require<Label>(row, "row-title-label").text = content?.Title ?? rule.TaskId.ToString();
                FFView.Require<Label>(row, "row-objective-label").text = completed ? content?.CompletedSummary ?? "" : content?.Objective ?? "";
                var track = FFView.Require<Button>(row, "track-mission"); track.SetEnabled(!offered && !completed && trackMission != null);
                track.clicked += () => _ = MissionCommand(() => trackMission(rule.MissionId));
                void Select() {
                    selectedMissionTask = rule.TaskId;
                    FFView.Require<VisualElement>(root, "journal-empty").style.display = DisplayStyle.None;
                    var details = FFView.Require<VisualElement>(root, "journal-details"); details.style.display = DisplayStyle.Flex;
                    FFView.Require<Label>(root, "mission-title-label").text = content?.Title ?? rule.TaskId.ToString();
                    string objectives = "";
                    if (quest != null)
                        for (int i = 0; i < 3; i++) if (quest.KillNpcIds[i] > 0 && rule.KillCounts[i] > 0)
                            objectives += $"\n{quest.KillNpcIds[i]}: {Math.Max(0, rule.KillCounts[i] - quest.RemainingKillCounts[i])}/{rule.KillCounts[i]}";
                    bool objectivesReady = GameplayMissionProgression.ObjectivesReady(quest, state.QuestInventory);
                    string description = completed ? content?.CompletionDescription : offered ? content?.OfferDescription : objectivesReady && rule.RewardId > 0 ? content?.CompletionDescription : content?.ActiveDescription;
                    int? remainingTime = MissionTimeRemaining?.Invoke(rule.TaskId);
                    if (remainingTime.HasValue) objectives += "\n" + (language == "ru" ? "Осталось: " : "Time remaining: ") + remainingTime + (language == "ru" ? " с" : " s");
                    FFView.Require<Label>(root, "mission-description-label").text = (string.IsNullOrEmpty(description) ? content?.Objective ?? "" : description) + objectives +
                        (completed || string.IsNullOrEmpty(content?.RewardDescription) ? "" : "\n\n" + content.RewardDescription);
                    details.Q("mission-actions")?.RemoveFromHierarchy();
                    if (completed) return;
                    var actions = new VisualElement { name = "mission-actions" }; actions.style.position = Position.Absolute;
                    actions.style.left = 50; actions.style.top = 300; actions.style.width = 450;
                    void Button(string caption, Func<Task> action)
                    { var button = new Button(() => _ = MissionCommand(action)) { text = caption }; FFWidgets.Font(button, false, 14); actions.Add(button); }
                    int npc = missionNpc?.Id ?? 0;
                    if (offered && startMission != null) Button(language == "ru" ? "ПРИНЯТЬ" : "ACCEPT", () => startMission(rule.TaskId, npc));
                    else
                    {
                        bool correctNpc = rule.EndNpc == 0 || missionNpc?.NpcType == rule.EndNpc;
                        if (endMission != null && correctNpc && objectivesReady) Button(language == "ru" ? "ЗАВЕРШИТЬ" : "COMPLETE", () => endMission(rule.TaskId, npc));
                        if (stopMission != null && rule.MissionType != 2) Button(language == "ru" ? "ОТКАЗАТЬСЯ" : "ABANDON", () => stopMission(rule.TaskId));
                    }
                    details.Add(actions);
                }
                FFView.Require<Button>(row, "select-mission").clicked += Select;
                row.Query<TextElement>().ForEach(text => FFWidgets.Font(text, false, 12)); list.Add(row);
                if (selectedMissionTask == rule.TaskId) Select();
            }
        }
        async Task MissionCommand(Func<Task> action)
        {
            if (missionSending || interactionSuspended || disposed) return;
            missionSending = true;
            try { await action(); }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
            finally { missionSending = false; }
        }
    }
}
