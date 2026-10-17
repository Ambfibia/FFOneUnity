using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        readonly Dictionary<int, float> automaticMissionRetry = new();
        readonly HashSet<int> presentedMissionRewards = new();
        readonly Queue<(int task, GameplayMissionEdge edge)> pendingMissionDialogues = new();
        readonly Dictionary<int, float> missionDeadlines = new();
        float nextMissionCheck;
        Task StartNpcMission(int task, int giver, FusionClient activeSession, System.Threading.CancellationTokenSource operation)
        {
            var rule = WorldMissionCatalog.Load().Find(task) ?? throw new ProtocolException("Unknown mission task: " + task);
            int escort = 0;
            if (rule.EscortNpc > 0)
            {
                var actor = networkWorld?.Replicas?.Actors.Where(candidate => candidate.Entity.Kind == WorldEntityKind.Npc &&
                    candidate.Entity.NpcType == rule.EscortNpc && candidate.Entity.Hp > 0 && !candidate.Entity.Hidden &&
                    Vector3.Distance(candidate.transform.position, networkWorld.Player.position) <= 8)
                    .OrderBy(candidate => Vector3.Distance(candidate.transform.position, networkWorld.Player.position)).FirstOrDefault();
                escort = actor?.Entity.Id ?? 0;
                if (escort == 0) throw new InvalidOperationException("The mission escort is not nearby: " + rule.EscortNpc);
            }
            return activeSession.StartTaskAsync(task, giver, escort, operation.Token);
        }
        void ConfigureMissionProgression()
        {
            automaticMissionRetry.Clear(); presentedMissionRewards.Clear(); pendingMissionDialogues.Clear(); missionDeadlines.Clear(); nextMissionCheck = 0;
            gameplayState.MissionEdge += (task, edge) => {
                automaticMissionRetry.Remove(task); presentedMissionRewards.Remove(task);
                if (edge == GameplayMissionEdge.Start)
                {
                    int remaining = gameplayState.Snapshot.RunningQuests.FirstOrDefault(quest => quest.TaskId == task)?.GrantedTimeSeconds ?? 0;
                    if (remaining > 0) missionDeadlines[task] = Time.unscaledTime + remaining;
                }
                else missionDeadlines.Remove(task);
                pendingMissionDialogues.Enqueue((task, edge));
            };
            gameplayState.MissionRejected += (task, reason) => automaticMissionRetry[task] = Time.unscaledTime + (reason == 13 ? 10 : 2);
        }
        void TickMissionProgression()
        {
            if (gameplayUi != null)
            {
                gameplayUi.MissionSpeakerAvailable ??= type => networkWorld && networkWorld.Replicas.Session.Entities.Any(npc => npc.Kind == WorldEntityKind.Npc && npc.NpcType == type && npc.Hp > 0 && !npc.Hidden);
                gameplayUi.MissionTimeRemaining ??= task => missionDeadlines.TryGetValue(task, out float end) ? (int?)Mathf.Max(0, Mathf.CeilToInt(end - Time.unscaledTime)) : null;
                while (pendingMissionDialogues.Count > 0) { var dialogue = pendingMissionDialogues.Dequeue(); gameplayUi.QueueMissionDialogue(dialogue.task, dialogue.edge); }
            }
            if (gameplayState == null || gameplayUi == null || !networkWorld || !networkWorld.IsReady || worldTransition ||
                !Session.IsWorldLoaded || gameplayState.Snapshot.Hp <= 0 || Time.unscaledTime < nextMissionCheck) return;
            nextMissionCheck = Time.unscaledTime + .2f;
            var snapshot = gameplayState.Snapshot;
            foreach (var quest in snapshot.RunningQuests.Where(quest => quest.TaskId > 0))
            {
                if (automaticMissionRetry.TryGetValue(quest.TaskId, out float retry) && Time.unscaledTime < retry) continue;
                var intent = GameplayMissionProgression.Automatic(WorldMissionCatalog.Load().Find(quest.TaskId), quest, snapshot.QuestInventory,
                    networkWorld.Player.position, networkWorld.Replicas.Session.Entities, type => WorldNpcCatalog.Load().Find(type)?.SightRange ?? 0);
                if (intent == null) continue;
                if (intent.RequiresRewardConfirmation)
                {
                    if (!busy && !gameplayUi.InputBlocked && presentedMissionRewards.Add(intent.Task))
                        gameplayUi.OpenMissionReward(intent.Task, networkWorld.Replicas.Session.Entities.FirstOrDefault(npc => npc.Kind == WorldEntityKind.Npc && npc.Id == intent.Npc));
                    continue;
                }
                if (gameplayState.BeginAutomaticMissionEnd(intent.Task)) _ = SendAutomaticMissionEnd(intent);
            }
        }
        async Task SendAutomaticMissionEnd(GameplayMissionCompletion intent)
        {
            var operation = worldLifetime;
            try { await Session.CompleteTaskAsync(intent.Task, intent.Npc, escortId: intent.Escort, cancellationToken: operation.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (CurrentWorldOperation(operation)) ShowError(error.Message); }
        }
    }
}
