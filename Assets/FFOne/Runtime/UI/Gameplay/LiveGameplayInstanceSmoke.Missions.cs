#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayInstanceSmoke
    {
        sealed class MissionAction
        {
            public string kind;
            public int task, npcType, npcX, npcY, npcZ, escortType, nextTask, item, slot;
        }
        readonly List<ProtocolFrame> missionFrames = new();
        bool observingMissions;

        async Task BeforeMissionWarp(Case item, Edge edge)
        {
            if (!observingMissions && (edge.beforeMission?.Length > 0 || edge.afterMission?.Length > 0))
            { frontend.NetworkEventReceived += ObserveMission; observingMissions = true; }
            await MissionActions(item, edge.beforeMission);
            missionFrames.Clear();
        }
        Task AfterMissionWarp(Case item, Edge edge) => MissionActions(item, edge.afterMission);
        void ObserveMission(NetworkEvent value)
        { if (value.IsShard && value.Kind == NetworkEventKind.Packet) missionFrames.Add(value.Frame); }

        async Task<WorldEntity> MissionActor(Case item, MissionAction action)
        {
            frontend.LiveWorld.MovementEnabled = false;
            await frontend.LiveMoveTo(action.npcX, action.npcY, action.npcZ);
            WorldEntity npc = null;
            await Wait(() => (npc = frontend.LiveWorld.Replicas.Session.Entities.FirstOrDefault(e => e.Kind == WorldEntityKind.Npc &&
                e.NpcType == action.npcType && e.Hp > 0 && Math.Abs(e.X - action.npcX) < 1000 && Math.Abs(e.Y - action.npcY) < 1000)) != null,
                $"map {item.map}: live mission actor {action.npcType} enters native interest");
            return npc;
        }

        async Task MissionActions(Case item, MissionAction[] actions)
        {
            foreach (var action in actions ?? Array.Empty<MissionAction>())
            {
                bool Reply(uint type) => missionFrames.Any(f => f.PacketType == type && ProtocolGameplay.I(f.Payload, 0) == action.task);
                switch (action.kind)
                {
                    case "equip":
                        if (frontend.LiveState.Snapshot.Equipment[0].ItemId == action.item) break;
                        Check(frontend.LiveState.Snapshot.Inventory[action.slot].ItemId == action.item,
                            $"map {item.map}: accepted melee weapon {action.item} is loaded from ordinary saved inventory");
                        await frontend.LiveUi.RequestItemMove(ItemContainer.Inventory, action.slot, ItemContainer.Equipment, 0);
                        await Wait(() => frontend.LiveState.Snapshot.Equipment[0].ItemId == action.item,
                            $"map {item.map}: native inventory equips weapon {action.item} through the server level gate");
                        break;
                    case "failure":
                        await Wait(() => missionFrames.Concat(frontend.Session.World.BootstrapPackets ?? Array.Empty<ProtocolFrame>())
                            .Any(f => f.PacketType == 0x3100001f && ProtocolGameplay.I(f.Payload, 0) == action.task &&
                            ProtocolGameplay.I(f.Payload, 4) == 12), $"map {item.map}: server commits InstanceLeft failure {action.task} to {action.nextTask}");
                        Check(!frontend.LiveState.Snapshot.CompletedMissions.Contains(842), $"map {item.map}: route transition does not complete or reward Nano mission 842");
                        break;
                    case "start":
                        if (action.escortType > 0) await MissionActor(item, action);
                        // The production frontend starts the successor, including its
                        // real escort ID. Acceptance waits for its server acknowledgment.
                        await Wait(() => frontend.LiveState.Snapshot.RunningQuests.Any(q => q.TaskId == action.task),
                            $"map {item.map}: native mission lifecycle starts task {action.task} through normal server gates");
                        break;
                    case "complete":
                        var target = action.npcType > 0 ? await MissionActor(item, action) : null;
                        var rule = WorldMissionCatalog.Load().Find(action.task);
                        if (rule.Type == 2 || rule.Type == 6)
                        {
                            await Wait(() => Reply(0x3100001e), $"map {item.map}: native mission checker completes actual objective {action.task}");
                            break;
                        }
                        missionFrames.Clear(); await frontend.Session.CompleteTaskAsync(action.task, target?.Id ?? 0);
                        await Wait(() => Reply(0x3100001e) || Reply(0x3100001f), $"map {item.map}: real objective {action.task} receives a server decision");
                        Check(Reply(0x3100001e) && !Reply(0x3100001f), $"map {item.map}: actual objective completes task {action.task}");
                        break;
                    case "defeat":
                        var enemy = await MissionActor(item, action);
                        missionFrames.Clear(); float deadline = Time.realtimeSinceStartup + 90;
                        while (!missionFrames.Any(f => f.PacketType == 0x31000034 && ProtocolGameplay.I(f.Payload, 0) == action.npcType))
                        {
                            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("Native mission enemy was not defeated.");
                            if (frontend.LiveState.Snapshot.Hp <= 0) throw new InvalidOperationException("Mission character died during the actual encounter.");
                            if (!frontend.LiveWorld.Replicas.Session.TryGet(WorldEntityKind.Npc, enemy.Id, out var current) || current.Hp <= 0)
                            {
                                float rewardDeadline = Time.realtimeSinceStartup + 5;
                                await Wait(() => missionFrames.Any(f => f.PacketType == 0x31000034 && ProtocolGameplay.I(f.Payload, 0) == action.npcType),
                                    $"map {item.map}: original death lifecycle publishes authoritative kill credit",
                                    () => Time.realtimeSinceStartup >= rewardDeadline ? "Kill credit did not follow the actual death within five seconds." : null);
                                break;
                            }
                            await frontend.LiveMoveTo(current.X - 100, current.Y, current.Z);
                            int first = missionFrames.Count;
                            frontend.LiveWorld.PlayAttack(); await frontend.Session.AttackNpcsAsync(new[] { enemy.Id });
                            await Wait(() => missionFrames.Skip(first).Any(f => f.PacketType == ProtocolGameplay.AttackSuccess),
                                $"map {item.map}: ordinary mission attack is acknowledged");
                            var hits = missionFrames.Skip(first).Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => {
                                ProtocolGameplay.TryCombat(f, out var result); return result.Hits; }).Where(h => h.Id == enemy.Id).ToArray();
                            Check(hits.Length > 0, $"map {item.map}: ordinary weapon attack reports target damage {hits.LastOrDefault().Damage}");
                            float next = Time.realtimeSinceStartup + 1;
                            while (Time.realtimeSinceStartup < next) await Task.Yield();
                        }
                        Check(missionFrames.Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => {
                            ProtocolGameplay.TryCombat(f, out var result); return result.Hits; }).Any(h => h.Id == enemy.Id && h.Hp == 0 && h.Damage > 0),
                            $"map {item.map}: ordinary attacks reduce enemy {action.npcType} to zero HP before kill credit");
                        Check(frontend.LiveState.Snapshot.RunningQuests.Any(q => q.TaskId == action.task && q.RemainingKillCounts.All(n => n == 0)) ||
                            missionFrames.Any(f => f.PacketType == 0x3100001e && ProtocolGameplay.I(f.Payload, 0) == action.task),
                            $"map {item.map}: native journal applies actual mission kill credit");
                        Check(frontend.LiveWorld.Replicas.Session.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 3338 && e.Hp > 0),
                            $"map {item.map}: assigned escort survives the encounter");
                        break;
                    default: throw new InvalidOperationException("Unknown mission acceptance action: " + action.kind);
                }
            }
        }
    }
}
#endif
