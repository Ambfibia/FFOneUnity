using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFOne.Content;

namespace FFOne.World
{
    public enum GameplayMissionEdge { Start, Success, Failure, Complete }
    public sealed class WorldMissionDialogue
    {
        public int MessageNpc, MessageType, MessageString, BubbleNpc, BubbleString;
    }
    public sealed class WorldMissionDefinition
    {
        public int TaskId, MissionId, Type, MissionType, GiverNpc, EndNpc, NextTask, FailureTask, EscortNpc, MinimumLevel, MapNumber, RequiredGuide;
        public int RewardId, GrantTimer, CompletionTimer, WaypointNpc, JournalNpc, JournalRow, MissionNameString;
        public int MaximumLevel, StartLevel, StartTrigger, RepeatFlag;
        public WorldMissionDialogue StartDialogue, SuccessDialogue, FailureDialogue;
        public bool IsFinal, IsFirst;
        public int[] BarkerTextIds, KillTypes, KillCounts, ItemIds, ItemCounts, RequiredMissions, RequiredNanos, MentorEmailIds, StartItemIds, StartItemCounts;
    }
    public sealed class WorldMissionCatalog
    {
        readonly Dictionary<int, WorldMissionDefinition> tasks = new();
        static WorldMissionCatalog cached;
        public static WorldMissionCatalog Load() => cached ??= ContentAssets.Read<NativeDataDocument, WorldMissionCatalog>(NativeData.Resource("data/tables/xdt"), Read);
        public WorldMissionDefinition Find(int task) => tasks.TryGetValue(task, out var definition) ? definition : null;
        public IEnumerable<WorldMissionDefinition> Tasks => tasks.Values;
        public static WorldMissionCatalog Read(NativeDataDocument d)
        {
            var result = new WorldMissionCatalog(); var final = new Dictionary<int, WorldMissionDefinition>();
            var first = new HashSet<int>();
            int table = Child(d, Child(d, 0, "m_pMissionTable"), "m_pMissionData");
            if (table < 0) return result;
            foreach (int row in d.Children(table))
            {
                int id = Number(d, row, "m_iHTaskID"); if (id <= 0) continue;
                var value = new WorldMissionDefinition { TaskId = id, JournalNpc = Number(d, row, "m_iHJournalNPCID"), MissionId = Number(d, row, "m_iHMissionID"),
                    Type = Number(d, row, "m_iHTaskType"), MissionType = Number(d, row, "m_iHMissionType"), GiverNpc = Number(d, row, "m_iHNPCID"), EndNpc = Number(d, row, "m_iHTerminatorNPCID"),
                    NextTask = Number(d, row, "m_iSUOutgoingTask"), FailureTask = Number(d, row, "m_iFOutgoingTask"),
                    EscortNpc = Number(d, row, "m_iCSUDEFNPCID"), MinimumLevel = Number(d, row, "m_iCTRReqLvMin"),
                    RewardId = Number(d, row, "m_iSUReward"), GrantTimer = Number(d, row, "m_iSTGrantTimer"),
                    CompletionTimer = Number(d, row, "m_iCSUCheckTimer"), WaypointNpc = Number(d, row, "m_iSTGrantWayPoint"),
                    JournalRow = Number(d, row, "m_iSTJournalIDAdd"), MissionNameString = Number(d, row, "m_iHMissionName"),
                    MentorEmailIds = Array(d, row, "m_iMentorEmailID", 5), StartDialogue = Dialogue(d, row, "ST", "m_iSTMessageTextID"),
                    MaximumLevel = Number(d, row, "m_iCTRReqLvMax"), StartLevel = Number(d, row, "m_iKorStReqLvlMin"),
                    StartTrigger = Number(d, row, "m_iCSTTrigger"), RepeatFlag = Number(d, row, "m_iRepeatflag"),
                    StartItemIds = Array(d, row, "m_iCSTItemID", 3), StartItemCounts = Array(d, row, "m_iCSTItemNumNeeded", 3),
                    SuccessDialogue = Dialogue(d, row, "SU", "m_iSUMessagetextID"), FailureDialogue = Dialogue(d, row, "F", "m_iFMessageTextID"),
                    MapNumber = Number(d, row, "m_iRequireInstanceID"), RequiredGuide = Number(d, row, "m_iCSTReqGuide"),
                    BarkerTextIds = Array(d, row, "m_iHBarkerTextID", 4), KillTypes = Array(d, row, "m_iCSUEnemyID", 3), KillCounts = Array(d, row, "m_iCSUNumToKill", 3),
                    ItemIds = Array(d, row, "m_iCSUItemID", 3), ItemCounts = Array(d, row, "m_iCSUItemNumNeeded", 3),
                    RequiredMissions = Array(d, row, "m_iCSTReqMission", 2), RequiredNanos = Array(d, row, "m_iCSTRReqNano", 5) };
                value.IsFirst = first.Add(value.MissionId);
                result.tasks.Add(id, value); final[value.MissionId] = value;
            }
            // Retrobution IsFinalTask follows serialized row order, not numeric task ordering.
            foreach (var value in final.Values) value.IsFinal = true;
            return result;
        }
        static WorldMissionDialogue Dialogue(NativeDataDocument d, int row, string phase, string message)
            => new() { MessageNpc = Number(d, row, "m_i" + phase + "MessageSendNPC"), MessageType = Number(d, row, "m_i" + phase + "MessageType"),
                MessageString = Number(d, row, message), BubbleNpc = Number(d, row, "m_i" + phase + "DialogBubbleNPCID"), BubbleString = Number(d, row, "m_i" + phase + "DialogBubble") };
        static int Child(NativeDataDocument d, int p, string key)
        { if (p < 0) return -1; foreach (int i in d.Children(p)) if (d.Nodes[i].Key == key) return i; return -1; }
        static int Number(NativeDataDocument d, int p, string key)
        { int i = Child(d, p, key); return i >= 0 && int.TryParse(d.Nodes[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0; }
        static int[] Array(NativeDataDocument d, int p, string key, int count)
        {
            var values = new int[count]; int node = Child(d, p, key), at = 0;
            if (node >= 0) foreach (int i in d.Children(node))
            { if (at >= count || !int.TryParse(d.Nodes[i].Value, out values[at++])) throw new InvalidOperationException("Invalid mission objective array: " + key); }
            return values;
        }
    }
}
