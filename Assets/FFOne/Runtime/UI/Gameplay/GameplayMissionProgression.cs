using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed class GameplayMissionCompletion
    {
        public int Task, Npc, Escort;
        public bool RequiresRewardConfirmation;
    }

    /// <summary>Client completion intent. Normal TASK_END remains the authority for every gate.</summary>
    public static class GameplayMissionProgression
    {
        public static bool ObjectivesReady(GameplayQuestState quest, IReadOnlyList<GameplayItemState> inventory)
            => quest != null && quest.RemainingKillCounts.All(count => count <= 0) && Enumerable.Range(0, 3).All(i =>
                quest.NeededItemIds[i] <= 0 || inventory.Where(item => item.ItemId == quest.NeededItemIds[i]).Sum(item => item.Quantity) >= quest.NeededItemCounts[i]);

        public static GameplayMissionCompletion Automatic(WorldMissionDefinition rule, GameplayQuestState quest,
            IReadOnlyList<GameplayItemState> inventory, Vector3 player, IEnumerable<WorldEntity> entities, Func<int, int> sightRange)
        {
            if (rule == null || quest == null || quest.TaskId != rule.TaskId || !ObjectivesReady(quest, inventory)) return null;
            var npcs = entities.Where(npc => npc.Kind == WorldEntityKind.Npc && npc.Hp > 0 && !npc.Hidden).ToArray();
            WorldEntity target = null, escort = null;
            if (rule.EndNpc > 0)
            {
                if (rule.Type != 2 && rule.Type != 6) return null;
                int radius = Math.Min(1600, sightRange(rule.EndNpc));
                if (radius <= 0) return null;
                target = npcs.Where(npc => npc.NpcType == rule.EndNpc && Distance(player, npc) < radius / 100f).OrderBy(npc => Distance(player, npc)).FirstOrDefault();
                if (target == null) return null;
            }
            else if (!(rule.KillCounts?.Any(count => count > 0) == true || rule.ItemCounts?.Any(count => count > 0) == true)) return null;
            if (rule.EscortNpc > 0)
            {
                escort = npcs.FirstOrDefault(npc => npc.NpcType == rule.EscortNpc);
                if (escort == null || target != null && Vector3.Distance(Position(escort), Position(target)) >= 5) return null;
            }
            return new GameplayMissionCompletion { Task = rule.TaskId, Npc = target?.Id ?? 0, Escort = escort?.Id ?? 0, RequiresRewardConfirmation = rule.RewardId > 0 };
        }
        static float Distance(Vector3 point, WorldEntity npc) => Vector3.Distance(point, Position(npc));
        static Vector3 Position(WorldEntity npc) => WorldCoordinates.FromServer(npc.X, npc.Y, npc.Z);
    }
}
