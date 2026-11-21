using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
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
        readonly System.Collections.Generic.HashSet<int> collapsedJournalCategories = new();
        int selectedMissionTask;
        float nextMissionClockRefresh;
        public Func<int, int?> MissionTimeRemaining { get; set; }
        void TickMissionClock()
        {
            if (Windows.Current != GameplayWindow.Journal || Time.unscaledTime < nextMissionClockRefresh) return;
            nextMissionClockRefresh = Time.unscaledTime + 1;
            UpdateMissionClock(Windows.View);
        }
        void UpdateMissionClock(VisualElement root)
        {
            var clock = root.Q<Label>(className: "mission-clock");
            int? seconds = showCompletedMissions || selectedMissionTask <= 0 ? null : MissionTimeRemaining?.Invoke(selectedMissionTask);
            clock.style.display = seconds.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            clock.text = seconds.HasValue ? T("Time remaining: ", "Осталось: ") + seconds.Value + T(" s", " с") : "";
        }
        void BindJournal(VisualElement root)
        {
            GameplayJournalView.Localize(root, language);
            ActionButton(root, "close", () => Windows.Close(), "button");
            ActionButton(root, "tab-active", () => SelectJournalTab(false));
            ActionButton(root, "tab-completed", () => SelectJournalTab(true));
            ActionButton(root, "help", () => OpenGuide(46), "button");
        }
        void SelectJournalTab(bool completed)
        {
            if (showCompletedMissions == completed) return;
            playSound?.Invoke("tab_click01");
            showCompletedMissions = completed;
            selectedMissionTask = 0;
            FFView.Require<ScrollView>(Windows.View, "journal-list").scrollOffset = Vector2.zero;
            RefreshJournal();
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
            if (AdvanceIntermediateMission(npc)) return;
            showCompletedMissions = false; selectedMissionTask = 0; missionNpc = npc;
            if (Windows.Current == GameplayWindow.Journal) RefreshJournal(); else Windows.Open(GameplayWindow.Journal);
        }

        bool AdvanceIntermediateMission(WorldEntity npc)
        {
            var rules = WorldMissionCatalog.Load();
            var state = session.Snapshot;
            var continuations = state.RunningQuests.Where(q => q.TaskId > 0 &&
                GameplayMissionProgression.ObjectivesReady(q, state.QuestInventory)).Select(q => rules.Find(q.TaskId))
                .Where(r => r != null && r.Type == 1 && !r.IsFinal && r.RewardId == 0 && r.EndNpc == npc.NpcType).Take(2).ToArray();
            if (continuations.Length == 1 && endMission != null && !Windows.IsOpen)
            {
                _ = MissionCommand(() => endMission(continuations[0].TaskId, npc.Id));
                return true;
            }
            return false;
        }
        void RefreshJournal()
        {
            if (Windows.Current != GameplayWindow.Journal || Windows.View == null) return;
            var root = Windows.View; var list = FFView.Require<ScrollView>(root, "journal-list"); list.Clear();
            var rules = WorldMissionCatalog.Load(); var catalog = GameplayUiCatalog.Load(); var state = session.Snapshot;
            FFView.Require<VisualElement>(root, "journal-completed-state").style.display = showCompletedMissions ? DisplayStyle.Flex : DisplayStyle.None;
            FFView.Require<Button>(root, "tab-active").EnableInClassList("retro-on", !showCompletedMissions);
            FFView.Require<Button>(root, "tab-completed").EnableInClassList("retro-on", showCompletedMissions);
            FFView.Require<VisualElement>(root, "journal-details").style.display = DisplayStyle.None;
            FFView.Require<VisualElement>(root, "journal-empty").style.display = DisplayStyle.Flex;
            root.Q<Button>(className: "mission-primary").style.display = DisplayStyle.None;
            root.Q("mission-actions")?.RemoveFromHierarchy();
            if (showCompletedMissions)
            {
                var completed = rules.Tasks.Where(rule => rule.IsFinal && state.CompletedMissions.Contains(rule.MissionId)).ToLookup(rule => rule.MissionType);
                foreach (int type in new[] { 2, 1, 3 }.Concat(completed.Select(group => group.Key)).Distinct())
                {
                    var category = type == 2 ? "nano" : type == 1 ? "guide" : "world";
                    var caption = LocalizationCatalog.Lookup("ui.mission.journal.category." + category, language);
                    bool expanded = !collapsedJournalCategories.Contains(type);
                    var header = new Button { name = "journal-category-" + type, text = caption + "  " + completed[type].Count() };
                    header.AddToClassList("ff-journal-category");
                    var indicator = new VisualElement { pickingMode = PickingMode.Ignore };
                    indicator.AddToClassList("ff-journal-category-indicator");
                    indicator.EnableInClassList("ff-journal-category-indicator--expanded", expanded);
                    header.Add(indicator);
                    header.clicked += () => {
                        if (!collapsedJournalCategories.Add(type)) collapsedJournalCategories.Remove(type);
                        RefreshJournal();
                    };
                    list.Add(header);
                    if (expanded)
                        foreach (var rule in completed[type]) Add(rule, null, false, true);
                }
                return;
            }
            foreach (var quest in state.RunningQuests.Where(q => q.TaskId > 0))
            {
                var rule = rules.Find(quest.TaskId); if (rule == null || missionNpc != null && rule.EndNpc != missionNpc.NpcType && rule.GiverNpc != missionNpc.NpcType) continue;
                Add(rule, quest, false);
            }
            if (missionNpc != null)
                foreach (var rule in rules.Tasks.Where(r => r.GiverNpc == missionNpc.NpcType && MissionOffered(r, rules, state)))
                    Add(rule, null, true);

            void Add(WorldMissionDefinition rule, GameplayQuestState quest, bool offered, bool completed = false)
            {
                var content = catalog.Mission(rule.TaskId, language);
                string title = content?.Title ?? rule.TaskId.ToString();
                bool isTracked = quest != null && state.CurrentMissionId == rule.MissionId;
                Action track = quest != null && trackMission != null ? () => _ = MissionCommand(() => trackMission(rule.MissionId)) : null;
                void Select() {
                    selectedMissionTask = rule.TaskId;
                    string objectives = "";
                    if (quest != null)
                        for (int i = 0; i < 3; i++) if (quest.KillNpcIds[i] > 0 && rule.KillCounts[i] > 0)
                        {
                            int type = quest.KillNpcIds[i];
                            string name = LocalizationCatalog.Lookup("content.npc." + type + ".name", language);
                            if (name.StartsWith("content.npc.", StringComparison.Ordinal))
                                name = WorldNpcCatalog.Load().Find(type)?.Name ?? type.ToString();
                            objectives += $"\n{name}: {Math.Max(0, rule.KillCounts[i] - quest.RemainingKillCounts[i])}/{rule.KillCounts[i]}";
                        }
                    bool objectivesReady = GameplayMissionProgression.ObjectivesReady(quest, state.QuestInventory);
                    bool correctNpc = rule.EndNpc == 0 || missionNpc?.NpcType == rule.EndNpc;
                    bool canComplete = !completed && !offered && endMission != null && correctNpc && objectivesReady;
                    string description = completed ? content?.CompletionDescription : offered ? content?.OfferDescription :
                        canComplete ? content?.CompletionDescription : content?.ActiveDescription;
                    string objective = content?.Objective ?? "";
                    string text = string.IsNullOrEmpty(description) ? objective : description;
                    if (!completed && !offered && !canComplete && !string.IsNullOrEmpty(objective) && text != objective)
                        text = objective + "\n\n" + text;
                    GameplayJournalView.ShowDetails(root, rule, title, text + objectives +
                        (completed || string.IsNullOrEmpty(content?.RewardDescription) ? "" : "\n\n" + content.RewardDescription),
                        language, offered, completed);
                    var contact = root.Q(className: "mission-journal-contact");
                    if (contact != null)
                    {
                        int who = rule.JournalNpc;
                        contact.style.display = who > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                        contact.Q<Label>(className: "mission-contact-name").text = NpcSpeaker(who);
                        contact.Q<Label>(className: "mission-contact-location").text = JournalContactDistrict(who);
                        var address = catalog.NpcPortrait(who);
                        contact.Q<Image>(className: "mission-contact-portrait").image = string.IsNullOrEmpty(address) ? null : FFWidgets.Texture(address);
                    }
                    UpdateMissionClock(root);
                    root.Q<Button>(className: "mission-primary").style.display = DisplayStyle.None;
                    root.Q("mission-actions")?.RemoveFromHierarchy();
                    if (completed) return;
                    var window = FFView.Require<VisualElement>(root, "journal-window");
                    int npc = missionNpc?.Id ?? 0;
                    if ((offered && startMission != null) || canComplete)
                    {
                        GameplayJournalView.PrimaryAction(root, offered ? T("ACCEPT MISSION", "ПРИНЯТЬ ЗАДАНИЕ") :
                            T("COMPLETE MISSION", "ЗАВЕРШИТЬ ЗАДАНИЕ"), () => {
                            playSound?.Invoke(offered ? "mission_accepted" : "button");
                            _ = MissionCommand(() => offered ? startMission(rule.TaskId, npc) : endMission(rule.TaskId, npc));
                        }, completion: canComplete);
                    }
                    if (!offered && stopMission != null && rule.MissionType != 2)
                    {
                        var actions = FFWidgets.Group(window, "mission-actions");
                        actions.StretchToParentSize();
                        FFWidgets.Button(actions, "mission-abandon", LocalizationCatalog.Lookup("ui.mission.journal.delete", language),
                            50, 588, 218, 25, FFWidgets.Controls + "blue_button_normal", FFWidgets.Controls + "blue_button_over", () => {
                                playSound?.Invoke("abandon_mission");
                                string kind = LocalizationCatalog.Lookup("ui.mission.type." + (rule.MissionType == 1 ? "guide" : "quest"), language);
                                string name = LocalizationCatalog.Lookup("ui.mission.journal.mission_name", language).Replace("{kind}", kind).Replace("{title}", title);
                                string[] lines = LocalizationCatalog.Lookup("ui.mission.delete.confirm", language).Replace("{mission}", name).Split(new[] { '\n' }, 2);
                                ShowPrompt(lines[0], lines.Length > 1 ? lines[1] : name, null, _ => session.Snapshot.RunningQuests.Any(q => q.TaskId == rule.TaskId)
                                    ? MissionCommand(() => stopMission(rule.TaskId)) : Task.CompletedTask, null, deleteMission: true);
                            });
                    }
                }
                GameplayJournalView.AddRow(root, title, completed ? content?.CompletedSummary ?? "" : content?.Objective ?? "",
                    () => { playSound?.Invoke("button"); Select(); }, track, isTracked);
                if (selectedMissionTask == rule.TaskId) Select();
            }
        }
        static bool MissionOffered(WorldMissionDefinition r, WorldMissionCatalog rules, GameplayUiState state) =>
            r.IsFirst && r.MinimumLevel <= state.Level && (r.MapNumber == 0 || r.MapNumber == state.MapNumber) &&
            (r.RequiredGuide == 0 || r.RequiredGuide == state.Guide) && !state.CompletedMissions.Contains(r.MissionId) &&
            !state.RunningQuests.Any(q => rules.Find(q.TaskId)?.MissionId == r.MissionId) &&
            r.RequiredMissions.All(id => id == 0 || state.CompletedMissions.Contains(id)) &&
            r.RequiredNanos.All(id => id == 0 || state.Nanos.Any(n => n.Id == id));

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
