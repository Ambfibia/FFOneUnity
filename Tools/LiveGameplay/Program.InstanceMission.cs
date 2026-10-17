using System.Diagnostics;
using FFOne.Network;

static partial class Program
{
    sealed class InstanceMissionAction
    {
        public string kind;
        public int task, npcType, npcX, npcY, npcZ, escortType, nextTask, item, slot;
    }

    static Task BeforeInstanceWarp(Probe p, InstanceCase item, InstanceEdge edge)
        => InstanceMissionActions(p, item, edge.beforeMission);
    static Task AfterInstanceWarp(Probe p, InstanceCase item, InstanceEdge edge)
        => InstanceMissionActions(p, item, edge.afterMission);

    static async Task<WorldEntity> InstanceMissionNpc(Probe p, InstanceCase item, InstanceMissionAction action)
    {
        await p.Client.SendMovementAsync(action.npcX, action.npcY, action.npcZ, 0, 0, 0, 0, 0, 0, true);
        WorldEntity npc = null;
        await Until(() => (npc = p.World.Entities.FirstOrDefault(e => e.Kind == WorldEntityKind.Npc && e.NpcType == action.npcType && e.Hp > 0 &&
            Math.Abs(e.X - action.npcX) < 1000 && Math.Abs(e.Y - action.npcY) < 1000)) != null,
            $"instance {item.map}: mission {action.task} admits live actor {action.npcType}");
        return npc;
    }

    static async Task InstanceMissionActions(Probe p, InstanceCase item, InstanceMissionAction[] actions)
    {
        foreach (var action in actions ?? Array.Empty<InstanceMissionAction>())
        {
            bool Reply(uint type) => p.Frames.Any(f => f.PacketType == type && ProtocolGameplay.I(f.Payload, 0) == action.task);
            switch (action.kind)
            {
                case "equip":
                    if (ProtocolInventory.ReadItem(p.Entry.RawLoadData, 136).ItemId == action.item) break;
                    Check(ProtocolInventory.ReadItem(p.Entry.RawLoadData, 244 + action.slot * 12).ItemId == action.item,
                        $"instance {item.map}: accepted level-36 melee weapon {action.item} is saved in ordinary inventory");
                    p.Frames.Clear(); await p.Client.SendItemMoveAsync(ItemContainer.Inventory, action.slot, ItemContainer.Equipment, 0);
                    await Until(() => p.Frames.Any(f => ProtocolInventory.TryDecode(f, out var update) && update.Kind == InventoryUpdateKind.Move &&
                        update.ToContainer == ItemContainer.Equipment && update.ToSlot == 0 && update.ToItem.ItemId == action.item),
                        $"instance {item.map}: normal equipment move admits melee weapon {action.item} through its level gate");
                    break;
                case "failure":
                    await Until(() => p.Frames.Any(f => f.PacketType == 0x3100001f && ProtocolGameplay.I(f.Payload, 0) == action.task &&
                        ProtocolGameplay.I(f.Payload, 4) == 12), $"instance {item.map}: map departure commits task {action.task} failure edge to {action.nextTask}");
                    break;
                case "start":
                    var escort = action.escortType > 0 ? await InstanceMissionNpc(p, item, action) : null;
                    p.Frames.Clear(); await p.Client.StartTaskAsync(action.task, 0, escort?.Id ?? 0);
                    await Until(() => Reply(0x3100001c) || Reply(0x3100001d), $"instance {item.map}: successor task {action.task} receives a server decision");
                    Check(Reply(0x3100001c) && !Reply(0x3100001d), $"instance {item.map}: task {action.task} passes normal sequence, level, map and escort gates");
                    break;
                case "complete":
                    var target = action.npcType > 0 ? await InstanceMissionNpc(p, item, action) : null;
                    p.Frames.Clear(); await p.Client.CompleteTaskAsync(action.task, target?.Id ?? 0);
                    await Until(() => Reply(0x3100001e) || Reply(0x3100001f), $"instance {item.map}: task {action.task} completion receives a server decision");
                    Check(Reply(0x3100001e) && !Reply(0x3100001f), $"instance {item.map}: actual objective completes task {action.task}; decisions " +
                        string.Join(",", p.Frames.Where(f => (f.PacketType == 0x3100001e || f.PacketType == 0x3100001f) &&
                            ProtocolGameplay.I(f.Payload, 0) == action.task).Select(f => f.PacketType == 0x3100001e ? "success" : "failure:" + ProtocolGameplay.I(f.Payload, 4))));
                    break;
                case "defeat":
                    var enemy = await InstanceMissionNpc(p, item, action);
                    p.Frames.Clear();
                    // A premature finish must not manufacture the kill counter.
                    await p.Client.CompleteTaskAsync(action.task, 0);
                    await Until(() => Reply(0x3100001f), $"instance {item.map}: unfinished escort/defeat stage {action.task} rejects hand-in");
                    Check(!Reply(0x3100001e), $"instance {item.map}: missing defeat cannot advance the mission");
                    var timer = Stopwatch.StartNew();
                    while (!p.Frames.Any(f => f.PacketType == 0x31000034 && ProtocolGameplay.I(f.Payload, 0) == action.npcType))
                    {
                        if (timer.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Actual mission enemy was not defeated.");
                        if (p.Hp <= 0) throw new Exception("Mission combat character died.");
                        if (!p.World.TryGet(WorldEntityKind.Npc, enemy.Id, out var current) || current.Hp <= 0)
                        {
                            // HP is published by the attack immediately; death rewards
                            // arrive when the mob's AI coroutine reaches begin_death.
                            await Until(() => p.Frames.Any(f => f.PacketType == 0x31000034 && ProtocolGameplay.I(f.Payload, 0) == action.npcType),
                                $"instance {item.map}: dead mission enemy publishes authoritative kill credit", 5);
                            break;
                        }
                        await p.Client.SendMovementAsync(current.X - 100, current.Y, current.Z, 0, 0, 0, 0, 0, 0, true);
                        int first = p.Frames.Count;
                        await p.Client.AttackNpcsAsync(new[] { enemy.Id });
                        await Until(() => p.Frames.Skip(first).Any(f => f.PacketType == ProtocolGameplay.AttackSuccess),
                            $"instance {item.map}: ordinary attack on mission enemy {enemy.Id} is acknowledged");
                        var hits = p.Frames.Skip(first).Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => {
                            ProtocolGameplay.TryCombat(f, out var result); return result.Hits; }).Where(h => h.Id == enemy.Id).ToArray();
                        // Corruption windup makes the original mob temporarily
                        // invulnerable. A zero-damage acknowledged hit is valid;
                        // the final assertions still require actual damage and death.
                        Check(hits.Length > 0, $"instance {item.map}: mission attack reports target damage {hits.LastOrDefault().Damage}; enemy HP {hits.LastOrDefault().Hp}");
                        await Task.Delay(1000);
                    }
                    Check(p.Frames.Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => { ProtocolGameplay.TryCombat(f, out var hit); return hit.Hits; })
                        .Any(h => h.Id == enemy.Id && h.Damage > 0 && h.Hp == 0), $"instance {item.map}: real enemy {action.npcType} reaches zero HP before its kill credit");
                    Check(p.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 3338 && e.Hp > 0),
                        $"instance {item.map}: assigned escort survives the actual encounter");
                    break;
                default: throw new InvalidOperationException("Unknown mission acceptance action: " + action.kind);
            }
        }
    }
}
