using System;
using FFOne.Network;
using FFOne.World;
using NUnit.Framework;
namespace FFOne.Tests
{
    public sealed class BuffEffectsTests
    {
        static void I(byte[] p, int o, int v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, p, o, 4);
        static void S(byte[] p, int o, short v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, p, o, 2);
        static void L(byte[] p, int o, long v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, p, o, 8);
        [Test]
        public void BuffUpdateUsesNativeOffsetsAndRetainsAuthoritativeFlagsAfterDisplayTimerEnds()
        {
            var p = new byte[44]; I(p, 0, 1); I(p, 4, 1); I(p, 8, 2); L(p, 12, 2000); L(p, 20, 6000); I(p, 32, 230); I(p, 40, 1);
            Assert.That(ProtocolBuff.TryUpdate(new ProtocolFrame(ProtocolBuff.Update, p), out var update), Is.True);
            var state = new BuffState(); state.Apply(update, 10);
            Assert.That(state.Value(1), Is.EqualTo(230)); Assert.That(state.RemainingSeconds(1, 11), Is.EqualTo(1));
            Assert.That(state.RemainingSeconds(1, 13), Is.Zero); Assert.That(state.Has(1), Is.True);
            I(p, 4, 2); I(p, 40, 0); ProtocolBuff.TryUpdate(new ProtocolFrame(ProtocolBuff.Update, p), out update); state.Apply(update, 14);
            Assert.That(state.Has(1), Is.False); Assert.That(state.Value(1), Is.Zero);
        }
        [Test]
        public void MalformedBuffDoesNotMutateLiveState()
        {
            var state = new BuffState(); state.SetCondition(1);
            var p = new byte[44]; I(p, 0, 26); I(p, 4, 1); I(p, 8, 1);
            Assert.Throws<ProtocolException>(() => { ProtocolBuff.TryUpdate(new ProtocolFrame(ProtocolBuff.Update, p), out var update); state.Apply(update, 0); });
            Assert.That(state.Condition, Is.EqualTo(1));
            Assert.Throws<ProtocolException>(() => ProtocolBuff.TryUpdate(new ProtocolFrame(ProtocolBuff.Update, new byte[45]), out _));
        }
        [Test]
        public void InfectionTickCarriesProtectedNegativeDamageAndAuthoritativeHpStaminaCondition()
        {
            var p = new byte[44]; I(p, 0, 1); I(p, 4, 9); S(p, 8, 17); I(p, 12, 1); I(p, 16, 9); I(p, 20, 1); I(p, 24, -2); I(p, 28, 123); S(p, 32, 0); I(p, 36, 1); I(p, 40, 1 << 16);
            Assert.That(ProtocolBuff.TryEffects(new ProtocolFrame(ProtocolBuff.Tick, p), out var effects), Is.True);
            Assert.That(effects[0].Hp, Is.EqualTo(123)); Assert.That(effects[0].NanoStamina, Is.Zero);
            Assert.That(effects[0].NanoDeactivated, Is.True); Assert.That(effects[0].Condition, Is.EqualTo(1 << 16));
            I(p, 16, 10); Assert.Throws<ProtocolException>(() => ProtocolBuff.TryEffects(new ProtocolFrame(ProtocolBuff.Tick, p), out _));
        }
        [Test]
        public void NpcConditionRoundTripsEntryAndAuthoritativeTimeout()
        {
            var session = new WorldEntitySession(1); var entry = new byte[36]; I(entry, 0, 77); I(entry, 4, 59); I(entry, 8, 100); I(entry, 12, 1 << 9);
            session.Apply(new ProtocolFrame(ProtocolWorld.NpcEnter, entry)); Assert.That(session.TryGet(WorldEntityKind.Npc, 77, out var npc), Is.True);
            Assert.That(npc.ConditionFlags, Is.EqualTo(1 << 9));
            var timeout = new byte[12]; I(timeout, 0, 4); I(timeout, 4, 77); session.Apply(new ProtocolFrame(ProtocolBuff.Timeout, timeout));
            session.TryGet(WorldEntityKind.Npc, 77, out npc); Assert.That(npc.ConditionFlags, Is.Zero);
        }
        [Test]
        public void LeechDecodesDistinctCasterHealAndTargetDamage()
        {
            var p = new byte[72]; I(p, 0, 1); S(p, 6, 24); S(p, 24, 8); S(p, 26, 150); I(p, 28, 30); I(p, 32, 1);
            I(p, 36, 1); I(p, 40, 1); I(p, 44, 4); I(p, 48, 200);
            I(p, 52, 4); I(p, 56, 77); I(p, 64, 8); I(p, 68, 100);
            ProtocolNanoSkill.TryDecode(new ProtocolFrame(ProtocolNanoSkill.Success, p), out var result);
            Assert.That(result.Effects.Count, Is.EqualTo(2)); Assert.That(result.Effects[0].Id, Is.EqualTo(1)); Assert.That(result.Effects[0].Hp, Is.EqualTo(200));
            Assert.That(result.Effects[1].Kind, Is.EqualTo(WorldEntityKind.Npc)); Assert.That(result.Effects[1].Id, Is.EqualTo(77)); Assert.That(result.Effects[1].Hp, Is.EqualTo(100));
        }
        [Test]
        public void TreasureDetectorReadsActualShinyInterestAndRejectsPartialOrRepeatedPages()
        {
            var p = new byte[28]; I(p, 0, 1); I(p, 4, 81); I(p, 8, 45); I(p, 12, 149); I(p, 16, 1000); I(p, 20, 2000); I(p, 24, 3000);
            Assert.That(ProtocolShiny.TryDecode(new ProtocolFrame(ProtocolShiny.Around, p), out var values), Is.True);
            Assert.That(values.Count, Is.EqualTo(1)); Assert.That(values[0].Id, Is.EqualTo(81)); Assert.That(values[0].Map, Is.EqualTo(149));
            Assert.That((values[0].X, values[0].Y, values[0].Z), Is.EqualTo((1000, 2000, 3000)));
            var leave = new byte[4]; I(leave, 0, 81); ProtocolShiny.TryDecode(new ProtocolFrame(ProtocolShiny.Leave, leave), out values);
            Assert.That(values[0].Removed, Is.True); Assert.That(values[0].Id, Is.EqualTo(81));
            I(p, 0, 2); Assert.Throws<ProtocolException>(() => ProtocolShiny.TryDecode(new ProtocolFrame(ProtocolShiny.Around, p), out _));
            var duplicate = new byte[52]; Buffer.BlockCopy(p, 0, duplicate, 0, 28); Buffer.BlockCopy(p, 4, duplicate, 28, 24);
            Assert.Throws<ProtocolException>(() => ProtocolShiny.TryDecode(new ProtocolFrame(ProtocolShiny.Around, duplicate), out _));
        }
        [Test]
        public void ItemSkillReceiptKeepsExactSlotRemainingStackAndCompleteEffectTrailer()
        {
            var p = new byte[52]; I(p, 0, 9); I(p, 4, 1); I(p, 8, 3); S(p, 12, 7); S(p, 14, 125); I(p, 16, 2);
            S(p, 24, 186); I(p, 28, 32); I(p, 32, 1); I(p, 36, 1); I(p, 40, 9); I(p, 48, 1 << 19);
            Assert.That(ProtocolItemUse.TryDecode(new ProtocolFrame(ProtocolItemUse.Success, p), out var value), Is.True);
            Assert.That((value.Slot, value.SkillId, value.Remaining.Id, value.Remaining.Option), Is.EqualTo((3, 186, 125, 2)));
            Assert.That(value.Effects[0].Condition, Is.EqualTo(1 << 19));
            Assert.Throws<ProtocolException>(() => ProtocolItemUse.TryDecode(new ProtocolFrame(ProtocolItemUse.Success, new byte[36]), out _));
            var broadcast = new byte[16]; I(broadcast, 0, 9); S(broadcast, 4, 144); I(broadcast, 8, 33); I(broadcast, 12, 1);
            Assert.Throws<ProtocolException>(() => ProtocolItemUse.TryDecode(new ProtocolFrame(ProtocolItemUse.Broadcast, broadcast), out _));
            Assert.That(ProtocolItemUse.TryDecode(new ProtocolFrame(0x310000d2, new byte[4]), out value), Is.True); Assert.That(value.Failed, Is.True);
            Assert.That(ProtocolItemUse.TryDecode(new ProtocolFrame(0x310000d5, new byte[4]), out _), Is.False);
            Assert.That(ProtocolItemUse.Payload(3, 2).Length, Is.EqualTo(12));
        }
        [TestCase(6, 200, 0, 0, 8)]
        [TestCase(6, 200, 300, 10, 7)]
        [TestCase(6, 0, 900, 0, 0)]
        public void NativeGroundSpeedAddsThenVehicleMaxThenSubtracts(float baseline, int positive, int negative, float vehicle, float expected)
            => Assert.That(NetworkWorldController.ResolveBuffSpeed(baseline, positive, negative, vehicle), Is.EqualTo(expected).Within(.00001));
        [Test]
        public void PhoenixPayloadUsesNativeThreeAndFourWithoutChangingOrdinaryRespawn()
        {
            Assert.That(BitConverter.ToInt32(ProtocolGameplay.PhoenixRegenPayload(false), 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(ProtocolGameplay.PhoenixRegenPayload(true), 0), Is.EqualTo(4));
            Assert.That(BitConverter.ToInt32(ProtocolGameplay.RegenPayload(false), 0), Is.EqualTo(1));
        }
    }
}
