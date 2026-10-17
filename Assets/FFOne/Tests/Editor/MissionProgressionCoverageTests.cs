using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using FFOne.UI;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class MissionProgressionCoverageTests
    {
        static WorldEntity Npc(int id, int type, int x = 0, int hp = 100)
        {
            var p = new byte[36]; Protocol0104.Write32(p, 0, id); Protocol0104.Write32(p, 4, type);
            Protocol0104.Write32(p, 8, hp); Protocol0104.Write32(p, 16, x);
            ProtocolWorld.TryDecode(new ProtocolFrame(ProtocolWorld.NpcEnter, p), out var values); return values[0].Entity;
        }
        static GameplayQuestState Quest(WorldMissionDefinition rule, int remaining = 0)
            => new(0, rule.TaskId, rule.KillTypes, rule.KillCounts.Select(count => count > 0 ? remaining : 0).ToArray(), rule.ItemIds, rule.ItemCounts);
        static GameplayItemState[] Items(WorldMissionDefinition rule)
            => rule.ItemIds.Select((id, index) => new GameplayItemState(index, 8, id, rule.ItemCounts[index], 0)).ToArray();
        [Test] public void EveryAuthoredTaskUsesItsObjectiveAndNpcCompletionContract()
        {
            var rules = WorldMissionCatalog.Load().Tasks.ToArray(); var counts = new Dictionary<int, int>();
            foreach (var rule in rules)
            {
                var npcs = new List<WorldEntity>();
                if (rule.EndNpc > 0) npcs.Add(Npc(100, rule.EndNpc));
                if (rule.EscortNpc > 0) npcs.Add(Npc(101, rule.EscortNpc));
                var intent = GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, npcs,
                    type => WorldNpcCatalog.Load().Find(type)?.SightRange ?? 0);
                if (intent != null)
                {
                    counts[rule.Type] = counts.GetValueOrDefault(rule.Type) + 1;
                    Assert.AreEqual(rule.TaskId, intent.Task);
                    Assert.AreEqual(rule.EndNpc > 0 ? 100 : 0, intent.Npc);
                    Assert.AreEqual(rule.RewardId > 0, intent.RequiresRewardConfirmation);
                }
                if (rule.KillCounts.Any(count => count > 0))
                    Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule, 1), Items(rule), Vector3.zero, npcs, _ => 1500), "unfinished kills: " + rule.TaskId);
                if (rule.ItemCounts.Any(count => count > 0))
                    Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Array.Empty<GameplayItemState>(), Vector3.zero, npcs, _ => 1500), "missing quest items: " + rule.TaskId);
            }
            Assert.AreEqual(2891, rules.Length);
            Assert.AreEqual(672, counts.GetValueOrDefault(2), "every authored goto stage has a usable sight trigger");
            Assert.AreEqual(651, counts.GetValueOrDefault(5), "every authored defeat stage reaches the normal completion/reward intent");
            Assert.AreEqual(8, counts.GetValueOrDefault(6), "escort and defence objectives include the delivery variant");
            Assert.AreEqual(0, counts.GetValueOrDefault(1), "talk stages retain explicit NPC interaction");
            Assert.AreEqual(0, counts.GetValueOrDefault(3), "item-use stages retain explicit interaction");
            Assert.AreEqual(0, counts.GetValueOrDefault(4), "delivery stages retain explicit interaction");
        }
        [Test] public void TriggerRemainsInsideBothAuthoredSightAndServerRangeAndRequiresLivingEscort()
        {
            var rule = new WorldMissionDefinition { TaskId = 10, Type = 2, EndNpc = 20, KillTypes = new int[3], KillCounts = new int[3], ItemIds = new int[3], ItemCounts = new int[3] };
            Assert.IsNotNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20, 1499) }, _ => 1500));
            Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20, 1501) }, _ => 1500));
            Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20, 1700) }, _ => 5000));
            rule.Type = 6; rule.EscortNpc = 21;
            Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20), Npc(101, 21, 0, 0) }, _ => 1500));
            Assert.IsNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20), Npc(101, 21, 501) }, _ => 1500));
            Assert.IsNotNull(GameplayMissionProgression.Automatic(rule, Quest(rule), Items(rule), Vector3.zero, new[] { Npc(100, 20), Npc(101, 21, 499) }, _ => 1500));
        }
        [Test] public void DialogueAndJournalCopyUsesExactTableStringsRatherThanObjectiveFallback()
        {
            var catalog = GameplayUiCatalog.Load();
            var task = WorldMissionCatalog.Load().Find(5216);
            Assert.IsNotEmpty(catalog.MissionLine(task.StartDialogue.MessageString));
            Assert.AreEqual(720, task.StartDialogue.MessageNpc);
            Assert.IsNotEmpty(catalog.Mission(5216).OfferDescription);
            Assert.IsNotEmpty(catalog.Mission(5216).ActiveDescription);
        }
        [Test] public void CompletedJournalUsesAcceptedMissionFlagsAndOffersNoRepeatReward()
        {
            var rule = WorldMissionCatalog.Load().Tasks.First(task => task.IsFinal && task.NextTask == 0 &&
                !string.IsNullOrEmpty(GameplayUiCatalog.Load().Mission(task.TaskId).CompletionDescription));
            var load = new byte[2700]; Protocol0104.Write32(load, 0, 51); Protocol0104.Write64(load, 8, 123);
            Protocol0104.Write32(load, 96, 100); Protocol0104.Write16(load, 4 + 1672, -1);
            var state = new GameplayUiSession(_ => new GameplayLevelLimits(200, 1000));
            state.SetWorld(Protocol0104.DecodeWorldEntry(load, 123));
            var end = new byte[4]; Protocol0104.Write32(end, 0, rule.TaskId);
            state.ApplyFrame(new ProtocolFrame(0x3100001e, end));
            var root = new VisualElement(); using var ui = new GameplayUiController(root, state, "en", null, null);
            ui.Windows.Open(GameplayWindow.Journal);
            void Submit(Button button) { using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt); }
            Assert.IsTrue(root.Q<Button>("tab-completed").enabledSelf);
            Submit(root.Q<Button>("tab-completed"));
            Assert.AreEqual(1, root.Query<Button>("select-mission").ToList().Count);
            Submit(root.Q<Button>("select-mission"));
            Assert.AreEqual(GameplayUiCatalog.Load().Mission(rule.TaskId).CompletionDescription,
                root.Q<Label>("mission-description-label").text);
            Assert.IsNull(root.Q("mission-actions"), "completed missions cannot pay rewards or be abandoned again");
            Assert.IsFalse(root.Q<Button>("track-mission").enabledSelf);
            Submit(root.Q<Button>("tab-active"));
            Assert.AreEqual(0, root.Query<Button>("select-mission").ToList().Count);
        }
    }
}
