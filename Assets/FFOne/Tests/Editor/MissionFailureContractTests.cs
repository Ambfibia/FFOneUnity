using System;
using System.Linq;
using FFOne.Network;
using FFOne.UI;
using FFOne.World;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class MissionFailureContractTests
    {
        static ProtocolFrame Frame(uint type, int task, int value = 0)
        {
            var p = new byte[type == 0x3100001e ? 4 : 8]; Protocol0104.Write32(p, 0, task);
            if (p.Length == 8) Protocol0104.Write32(p, 4, value);
            return new ProtocolFrame(type, p);
        }
        static GameplayUiSession Session()
        {
            var p = new byte[2700]; Protocol0104.Write32(p, 0, 51); Protocol0104.Write64(p, 8, 123);
            Protocol0104.Write16(p, 1676, -1);
            var s = new GameplayUiSession(_ => new GameplayLevelLimits(200, 1000));
            s.MissionDefinition = task => new WorldMissionDefinition { TaskId = task, MissionId = 842, MapNumber = task == 5226 ? 145 : 146,
                FailureTask = task == 5226 ? 5227 : 0, NextTask = task == 5226 ? 5227 : 5228,
                KillTypes = new int[3], KillCounts = new int[3], ItemIds = new int[3], ItemCounts = new int[3] };
            s.SetWorld(Protocol0104.DecodeWorldEntry(p, 123)); s.ApplyFrame(Frame(0x3100001c, 5226)); return s;
        }
        [TestCase(1)] [TestCase(11)] [TestCase(12)]
        public void TerminalFailureFollowsAuthoredFailureWithoutCompletingMissionAndDeduplicatesTick(int reason)
        {
            var s = Session(); int followup = 0, calls = 0; s.FollowUpTask += id => { followup = id; calls++; };
            s.ApplyFrame(Frame(0x3100001f, 5226, reason)); s.ApplyFrame(Frame(0x3100001f, 5226, reason));
            Assert.AreEqual(5227, followup); Assert.AreEqual(1, calls);
            Assert.False(s.Snapshot.RunningQuests.Any(q => q.TaskId == 5226));
            Assert.False(s.Snapshot.RunningQuests.Any(q => q.TaskId == 5227));
            Assert.False(s.Snapshot.CompletedMissions.Contains(842));
            s.ApplyFrame(Frame(0x3100001c, 5227)); Assert.AreEqual(5227, s.Snapshot.RunningQuests.Single(q => q.TaskId > 0).TaskId);
        }
        [TestCase(0)] [TestCase(13)]
        public void RejectedHandInPreservesRunningTaskWithoutFollowingFailureEdge(int reason)
        {
            var s = Session(); var before = s.Snapshot; int calls = 0; string message = null;
            s.FollowUpTask += _ => calls++; s.ServerError += error => message = error;
            s.ApplyFrame(Frame(0x3100001f, 5226, reason));
            Assert.AreSame(before, s.Snapshot); Assert.AreEqual(0, calls); Assert.IsNotNull(message);
        }
        [Test] public void PublishedMissionCatalogKeepsTheOriginalCrossMapAndEscortContracts()
        {
            var catalog = WorldMissionCatalog.Load();
            foreach (var edge in new[] { (5226, 5227, 145), (5228, 5233, 146), (5230, 5231, 147), (5231, 5222, 148) })
            { Assert.AreEqual(edge.Item2, catalog.Find(edge.Item1).FailureTask); Assert.AreEqual(edge.Item3, catalog.Find(edge.Item1).MapNumber); }
            Assert.AreEqual(3338, catalog.Find(5229).EscortNpc);
            Assert.AreEqual(3337, catalog.Find(5229).KillTypes[0]); Assert.AreEqual(1, catalog.Find(5229).KillCounts[0]);
        }
        [Test] public void LastSerializedRowWithOutgoingTaskDoesNotCompleteOrRewardTheNanoMission()
        {
            var s = Session();
            s.MissionDefinition = task => new WorldMissionDefinition { TaskId = task, MissionId = 842, IsFinal = task == 5233,
                NextTask = task == 5233 ? 5229 : 5230, KillTypes = new int[3], KillCounts = new int[3], ItemIds = new int[3], ItemCounts = new int[3] };
            s.ApplyFrame(Frame(0x3100001c, 5233)); int followup = 0; s.FollowUpTask += id => followup = id;
            s.ApplyFrame(Frame(0x3100001e, 5233));
            Assert.AreEqual(5229, followup); Assert.False(s.Snapshot.CompletedMissions.Contains(842));
        }
    }
}
