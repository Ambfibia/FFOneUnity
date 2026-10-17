using System;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class NpcSkillProtocolTests
    {
        // NPC3337's accepted Mega176: a player who leaves its ground area is
        // unharmed, but the server still closes the attack with NPC_SKILL_HIT.
        static ProtocolFrame Miss(int count = 0, int type = 1, int extraWords = 0)
        {
            var payload = new byte[28 + extraWords * 4];
            Protocol0104.Write32(payload, 0, 2999); Protocol0104.Write16(payload, 4, 176);
            Protocol0104.Write32(payload, 8, 1083158); Protocol0104.Write32(payload, 12, 1266688);
            Protocol0104.Write32(payload, 16, 4616); Protocol0104.Write32(payload, 20, type);
            Protocol0104.Write32(payload, 24, count);
            return new ProtocolFrame(ProtocolNpcSkill.Hit, payload);
        }

        [Test] public void AvoidedOriginalBossMegaAcceptsExactEmptyNpcHit()
        {
            Assert.IsTrue(ProtocolNpcSkill.TryDecode(Miss(), out var effects));
            Assert.IsEmpty(effects);
            var entry = new byte[2700]; Protocol0104.Write32(entry, 0, 51);
            Protocol0104.Write64(entry, 8, 123); Protocol0104.Write32(entry, 96, 100);
            Protocol0104.Write16(entry, 4 + 1672, -1);
            var session = new GameplayUiSession(_ => new GameplayLevelLimits(200, 1000));
            session.SetWorld(Protocol0104.DecodeWorldEntry(entry, 123));
            var before = session.Snapshot;
            Assert.IsTrue(session.ApplyFrame(Miss()));
            Assert.AreSame(before, session.Snapshot, "An avoided ground attack must not fabricate health, Nano or position changes.");
        }

        [Test] public void EmptyNpcHitStillRejectsTruncatedTrailingNegativeOversizedAndUnknownFrames()
        {
            var truncated = Miss().Payload; Array.Resize(ref truncated, 27);
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(new ProtocolFrame(ProtocolNpcSkill.Hit, truncated), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(Miss(extraWords: 1), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(Miss(count: -1), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(Miss(count: 1), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(Miss(count: int.MaxValue), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(Miss(type: 999), out _));
            var unidentified = Miss().Payload; Protocol0104.Write32(unidentified, 0, 0);
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(new ProtocolFrame(ProtocolNpcSkill.Hit, unidentified), out _));
        }

        [Test] public void EmptyNpcMissDoesNotPermitEmptyNanoUseOrCorruptionReplies()
        {
            var nano = new byte[36]; Protocol0104.Write32(nano, 28, 1);
            Assert.Throws<ProtocolException>(() => ProtocolNanoSkill.TryDecode(new ProtocolFrame(ProtocolNanoSkill.Success, nano), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNanoSkill.TryDecode(new ProtocolFrame(ProtocolNanoSkill.Broadcast, nano), out _));
            Assert.Throws<ProtocolException>(() => ProtocolNpcSkill.TryDecode(new ProtocolFrame(ProtocolNpcSkill.Corruption, new byte[24]), out _));
        }
    }
}
