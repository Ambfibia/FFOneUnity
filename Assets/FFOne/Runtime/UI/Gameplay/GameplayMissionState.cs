using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using FFOne.World;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        public int Guide { get; private set; }
        public int GuideCount { get; private set; }
        public IReadOnlyList<int> CompletedMissions { get; private set; } = Array.Empty<int>();
        internal GameplayUiState WithMissionFlags(byte[] load)
        {
            var next = (GameplayUiState)MemberwiseClone(); var completed = new List<int>();
            for (int i = 0; i < 2048; i++) if ((load[1712 + i / 8] & (1 << (i % 8))) != 0) completed.Add(i + 1);
            next.CompletedMissions = completed.AsReadOnly(); next.Guide = Protocol0104.Read16(load, 86);
            next.GuideCount = Protocol0104.Read16(load, 88); return next;
        }
        internal GameplayUiState WithMentor(int mentor, int count, int fusionMatter)
        {
            var next = (GameplayUiState)MemberwiseClone();
            next.Guide = mentor; next.GuideCount = count; next.FusionMatter = fusionMatter;
            return next;
        }
        internal GameplayUiState WithQuests(IEnumerable<GameplayQuestState> quests, int? tracked = null, int? completed = null)
        {
            var next = (GameplayUiState)MemberwiseClone(); next.RunningQuests = Array.AsReadOnly(quests.ToArray());
            if (tracked.HasValue) next.CurrentMissionId = tracked.Value;
            if (completed > 0) next.CompletedMissions = Array.AsReadOnly(CompletedMissions.Concat(new[] { completed.Value }).Distinct().ToArray());
            return next;
        }
        internal GameplayUiState WithNanoCreation(byte[] p, GameplayLevelLimits? limits)
        {
            var nano = GameplayUiSession.LiveNano(p, 20); int level = Protocol0104.Read16(p, 26), fm = ProtocolGameplay.I(p, 0), slot = ProtocolGameplay.I(p, 4);
            if (!nano.IsOwned || level <= 0 || fm < 0 || slot < -1 || slot >= QuestInventory.Count) throw new ProtocolException("Invalid Nano acquisition post-state.");
            var next = WithNanos(new[] { nano }); next.Level = level; next.FusionMatter = fm;
            next.MaxHp = limits?.MaxHp; next.MaxFusionMatter = limits?.MaxFusionMatter;
            if (slot >= 0)
            {
                var quest = QuestInventory.ToArray();
                quest[slot] = new GameplayItemState(slot, Protocol0104.Read16(p, 8), Protocol0104.Read16(p, 10), ProtocolGameplay.I(p, 12), ProtocolGameplay.I(p, 16));
                next.QuestInventory = Array.AsReadOnly(quest);
            }
            return next;
        }
        internal GameplayUiState WithoutItem(int location, int slot)
        {
            var next = (GameplayUiState)MemberwiseClone();
            var target = (location == 0 ? Equipment : location == 1 ? Inventory : location == 2 ? QuestInventory : null)?.ToArray();
            if (target == null || slot < 0 || slot >= target.Length) throw new ProtocolException("Invalid deleted item slot.");
            target[slot] = new GameplayItemState(slot, 0, 0, 0, 0);
            if (location == 0) next.Equipment = Array.AsReadOnly(target);
            else if (location == 1) next.Inventory = Array.AsReadOnly(target);
            else next.QuestInventory = Array.AsReadOnly(target);
            return next;
        }
    }

    public sealed partial class GameplayUiSession
    {
        internal void ConfirmMentor(int mentor, int count, int fusionMatter) => Publish(Snapshot.WithMentor(mentor, count, fusionMatter));
        readonly Dictionary<int, int> completionBranches = new();
        readonly HashSet<int> automaticMissionRequests = new();
        public Func<int, WorldMissionDefinition> MissionDefinition { get; set; } = id => WorldMissionCatalog.Load().Find(id);
        public event Action<int> FollowUpTask;
        public event Action<int, GameplayMissionEdge> MissionEdge;
        public event Action<int, int> MissionRejected;
        public event Action<string> ServerError;
        public bool BeginAutomaticMissionEnd(int task) => Snapshot.RunningQuests.Any(q => q.TaskId == task) && automaticMissionRequests.Add(task);
        bool ApplyMissionFrame(ProtocolFrame frame)
        {
            var p = frame.Payload;
            switch (frame.PacketType)
            {
                case 0x3100001c: // TASK_START_SUCC
                    Protocol0104.RequireSize(p, 8); int task = ProtocolGameplay.I(p, 0); var start = RequireMission(task);
                    var quests = Snapshot.RunningQuests.Where(q => q.TaskId > 0 && MissionDefinition(q.TaskId)?.MissionId != start.MissionId).ToList();
                    int slot = Enumerable.Range(0, 9).FirstOrDefault(i => quests.All(q => q.Slot != i));
                    if (quests.Count >= 9) throw new ProtocolException("Server started more than nine running tasks.");
                    quests.Add(new GameplayQuestState(slot, task, start.KillTypes, start.KillCounts, start.ItemIds, start.ItemCounts, ProtocolGameplay.I(p, 4)));
                    Publish(Snapshot.WithQuests(quests)); MissionEdge?.Invoke(task, GameplayMissionEdge.Start); return true;
                case 0x3100013f: // NativeCompletionBranch, supported by the current RustyFusion checkout
                    Protocol0104.RequireSize(p, 16); int branchTask = ProtocolGameplay.I(p, 0), next = ProtocolGameplay.I(p, 12);
                    RequireMission(branchTask); if (next < 0) throw new ProtocolException("Invalid mission completion branch.");
                    completionBranches[branchTask] = next; return true;
                case 0x3100001e: // TASK_END_SUCC
                    Protocol0104.RequireSize(p, 4); int ended = ProtocolGameplay.I(p, 0); var definition = RequireMission(ended);
                    bool branched = completionBranches.Remove(ended, out var branch);
                    int outgoing = branched ? branch : definition.NextTask;
                    // RustyFusion commits the mission only when its accepted
                    // completion has no outgoing task. Serialized row order is
                    // not authoritative for nonnumeric chains (842: 5233 -> 5229).
                    bool final = outgoing == 0;
                    automaticMissionRequests.Remove(ended);
                    Publish(Snapshot.WithQuests(Snapshot.RunningQuests.Where(q => q.TaskId > 0 && MissionDefinition(q.TaskId)?.MissionId != definition.MissionId),
                        completed: final ? definition.MissionId : null));
                    MissionEdge?.Invoke(ended, final ? GameplayMissionEdge.Complete : GameplayMissionEdge.Success);
                    if (outgoing > 0 && MissionDefinition(outgoing) != null) FollowUpTask?.Invoke(outgoing);
                    return true;
                case 0x3100002d: // TASK_STOP_SUCC
                    Protocol0104.RequireSize(p, 4); int stopped = ProtocolGameplay.I(p, 0);
                    Publish(Snapshot.WithQuests(Snapshot.RunningQuests.Where(q => q.TaskId != stopped))); return true;
                case 0x31000034: // KILL_QUEST_NPCS_SUCC carries a TYPE, not a live NPC ID
                    Protocol0104.RequireSize(p, 4); int type = ProtocolGameplay.I(p, 0);
                    Publish(Snapshot.WithQuests(Snapshot.RunningQuests.Select(q => {
                        var remaining = q.RemainingKillCounts.ToArray();
                        for (int i = 0; i < 3; i++) if (q.KillNpcIds[i] == type && remaining[i] > 0) remaining[i]--;
                        return new GameplayQuestState(q.Slot, q.TaskId, q.KillNpcIds.ToArray(), remaining, q.NeededItemIds.ToArray(), q.NeededItemCounts.ToArray(), q.GrantedTimeSeconds);
                    }))); return true;
                case 0x310000ef:
                    Protocol0104.RequireSize(p, 4); Publish(Snapshot.WithQuests(Snapshot.RunningQuests, ProtocolGameplay.I(p, 0))); return true;
                case 0x31000039:
                    Protocol0104.RequireSize(p, 8); Publish(Snapshot.WithoutItem(ProtocolGameplay.I(p, 0), ProtocolGameplay.I(p, 4))); return true;
                case 0x3100001f: // TASK_END_FAIL: terminal failures commit their authored failure edge.
                    Protocol0104.RequireSize(p, 8);
                    int failed = ProtocolGameplay.I(p, 0), reason = ProtocolGameplay.I(p, 4);
                    var failedDefinition = RequireMission(failed);
                    bool automatic = automaticMissionRequests.Remove(failed);
                    MissionRejected?.Invoke(failed, reason);
                    // NotComplete (0) and InventoryFull (13) reject only the command.
                    // InstanceLeft (12) is emitted by RustyFusion's mission tick after
                    // fail_task(), and its successor must pass normal TASK_START gates.
                    bool terminal = reason == 1 || reason == 11 || reason == 12;
                    if (terminal &&
                        Snapshot.RunningQuests.Any(q => q.TaskId == failed))
                    {
                        var successor = failedDefinition.FailureTask > 0 ? MissionDefinition(failedDefinition.FailureTask) : null;
                        completionBranches.Remove(failed);
                        Publish(Snapshot.WithQuests(Snapshot.RunningQuests.Where(q => q.TaskId != failed)));
                        MissionEdge?.Invoke(failed, GameplayMissionEdge.Failure);
                        // Source-owned dangling IDs fail closed; no guessed stage.
                        if (successor != null) FollowUpTask?.Invoke(successor.TaskId);
                        return true;
                    }
                    // Repeated tick notifications can arrive before TASK_START_SUCC.
                    if (terminal) return true;
                    if (automatic && reason == 0) return true;
                    ServerError?.Invoke($"Task {failed} rejected: {reason}"); return true;
                case 0x3100001d:
                    Protocol0104.RequireSize(p, 8); ServerError?.Invoke($"Task {ProtocolGameplay.I(p, 0)} rejected: {ProtocolGameplay.I(p, 4)}"); return true;
                case 0x3100002e:
                    Protocol0104.RequireSize(p, 4); ServerError?.Invoke("Task cancellation rejected: " + ProtocolGameplay.I(p, 0)); return true;
                default: return false;
            }
        }
        WorldMissionDefinition RequireMission(int task) => MissionDefinition(task) ?? throw new ProtocolException("Unknown server task: " + task);
    }
}
