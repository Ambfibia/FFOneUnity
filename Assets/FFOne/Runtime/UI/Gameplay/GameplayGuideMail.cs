using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.World;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        sealed class GuideMailEntry
        {
            public long Index;
            public int Npc;
            public string Sender, Subject, Body;
        }

        int guideMailPage;
        IReadOnlyList<GuideMailEntry> guideMails = Array.Empty<GuideMailEntry>();

        void UpdateGuideMailNotices()
        {
            var next = BuildGuideMail();
            if (next.Any(mail => !guideMails.Any(previous => previous.Index == mail.Index && previous.Npc == mail.Npc)))
                SignalNewMail();
            guideMails = next;
        }

        IReadOnlyList<GuideMailEntry> BuildGuideMail()
        {
            var state = session.Snapshot; var rules = WorldMissionCatalog.Load(); var text = GameplayUiCatalog.Load();
            var result = new List<GuideMailEntry>();
            var active = state.RunningQuests.Select(quest => rules.Find(quest.TaskId)).Where(rule => rule != null).ToArray();
            int mentorIndex = state.Guide - 1;
            foreach (var rule in active)
            {
                // cnEmailSystemManager.AddEmail(MissionElement), 0x06000249:
                // the email is a mission message of type4/6, independent of player mail packets.
                if (rule.StartDialogue == null || (rule.StartDialogue.MessageType != 4 && rule.StartDialogue.MessageType != 6)) continue;
                int body = rule.StartDialogue.MessageString;
                // ClickComEmail's active-message fallback is restricted to guides1..4.
                if (body <= 0 && mentorIndex >= 0 && mentorIndex < 4 && mentorIndex < (rule.MentorEmailIds?.Length ?? 0)) body = rule.MentorEmailIds[mentorIndex];
                Add(rule, rule.StartDialogue.MessageNpc, body);
            }
            AddAvailableGuideMail();
            return result;

            void Add(WorldMissionDefinition rule, int npc, int body)
            {
                if (result.Any(mail => mail.Index == -rule.TaskId)) return;
                result.Add(new GuideMailEntry { Index = -rule.TaskId, Npc = npc,
                    Sender = WorldNpcCatalog.Load().Find(npc)?.Name ?? "",
                    Subject = text.MissionLine(rule.MissionNameString, language), Body = text.MissionLine(body, language) });
            }

            void AddAvailableGuideMail()
            {
                int guideNpc = GuideMailNpc(state.Guide);
                if (guideNpc <= 0 || mentorIndex < 0 || mentorIndex >= 5) return;
                int typeOne = active.Count(rule => rule.MissionType == 1), typeTwo = active.Count(rule => rule.MissionType == 2);
                foreach (var rule in rules.Tasks)
                {
                    // CheckToStartTaskCondition0x06000477 supplies level, mission,
                    // Nano, quest-item and trigger conditions. Repeat timing is not
                    // present in this snapshot, so pending repeat mail is not inferred.
                    if (!rule.IsFirst || rule.MentorEmailIds == null || rule.MentorEmailIds.All(id => id <= 0) ||
                        rule.RepeatFlag != 0 || Math.Max(rule.MinimumLevel, rule.StartLevel) > state.Level ||
                        // Primary IL_0221 is clt for m_iCTRReqLvMax as well.
                        rule.MaximumLevel > 0 && state.Level < rule.MaximumLevel ||
                        rule.MapNumber != 0 && rule.MapNumber != state.MapNumber ||
                        rule.RequiredGuide != 0 && rule.RequiredGuide != state.Guide ||
                        state.CompletedMissions.Contains(rule.MissionId) || active.Any(running => running.MissionId == rule.MissionId) ||
                        rule.RequiredMissions.Any(id => id > 0 && !state.CompletedMissions.Contains(id)) ||
                        rule.RequiredNanos.Any(id => id > 0 && !state.Nanos.Any(nano => nano.Id == id)) ||
                        rule.StartTrigger > 0 && !active.Any(running => running.TaskId == rule.StartTrigger)) continue;
                    if (Enumerable.Range(0, rule.StartItemIds?.Length ?? 0).Any(i => rule.StartItemIds[i] > 0 &&
                        state.QuestInventory.Where(item => item.ItemId == rule.StartItemIds[i]).Sum(item => item.Quantity) < rule.StartItemCounts[i])) continue;
                    if (rule.MissionType == 1 && typeOne > 0 || rule.MissionType == 2 && typeTwo > 0) continue;
                    int body = rule.MentorEmailIds[mentorIndex];
                    if (body <= 0) continue;
                    Add(rule, guideNpc, body);
                    if (rule.MissionType == 1) typeOne++;
                    if (rule.MissionType == 2) typeTwo++;
                }
            }
        }

        // Panel_EmailList::.ctor0x06000a84 loads FieldRVA0x04002116:
        // [707,728,731,732,1171]. Guide identity and NPC identity remain separate.
        static int GuideMailNpc(int guide) => guide switch { 1 => 707, 2 => 728, 3 => 731, 4 => 732, 5 => 1171, _ => 0 };
    }
}
