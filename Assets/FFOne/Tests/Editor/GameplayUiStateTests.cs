using System;
using System.Collections.Generic;
using FFOne.Content;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class GameplayUiStateTests
    {
        static WorldEntry Entry(params ProtocolFrame[] bootstrap)
        {
            var bytes = new byte[2700];
            Protocol0104.Write32(bytes, 0, 51);
            Protocol0104.Write64(bytes, 8, 1234);
            Protocol0104.WriteUtf16(bytes, 18, 9, "Eddy");
            Protocol0104.WriteUtf16(bytes, 36, 17, "Tester");
            bytes[85] = 1; // Completed tutorial; regular level-dependent FM threshold.
            Protocol0104.Write16(bytes, 88, 7);
            Protocol0104.Write32(bytes, 96, 420);
            Protocol0104.Write32(bytes, 100, 11);
            Protocol0104.Write32(bytes, 104, 22);
            Protocol0104.Write32(bytes, 108, 12345);
            Protocol0104.Write32(bytes, 112, 777);
            Protocol0104.Write32(bytes, 120, 2);
            Protocol0104.Write32(bytes, 124, 100);
            Protocol0104.Write32(bytes, 128, 200);
            Protocol0104.Write32(bytes, 132, 300);
            Item(bytes, 4 + 136 + 8 * 12, 8, 82, 19, 2000000);
            Item(bytes, 4 + 244 + 49 * 12, 7, 513, 3, 0);
            Item(bytes, 4 + 844, 8, 42, 4, 0);
            Protocol0104.Write16(bytes, 4 + 1444 + 6 * 6, 6);
            Protocol0104.Write16(bytes, 4 + 1444 + 6 * 6 + 2, 113);
            Protocol0104.Write16(bytes, 4 + 1444 + 6 * 6 + 4, 150);
            Protocol0104.Write16(bytes, 4 + 1666, 6);
            Protocol0104.Write16(bytes, 4 + 1672, -1);
            Protocol0104.Write32(bytes, 4 + 2032 + 8 * 52, 991);
            Protocol0104.Write32(bytes, 4 + 2032 + 8 * 52 + 4, 615);
            Protocol0104.Write32(bytes, 4 + 2032 + 8 * 52 + 16, 3);
            Protocol0104.Write32(bytes, 4 + 2032 + 8 * 52 + 28, 89);
            Protocol0104.Write32(bytes, 4 + 2032 + 8 * 52 + 40, 5);
            Protocol0104.Write32(bytes, 4 + 2500, 64);
            var entry = Protocol0104.DecodeWorldEntry(bytes, 1234);
            typeof(WorldEntry).GetProperty(nameof(WorldEntry.BootstrapPackets)).SetValue(entry, bootstrap);
            return entry;
        }

        static void Item(byte[] bytes, int offset, int type, int id, int option, int expiry)
        {
            Protocol0104.Write16(bytes, offset, type); Protocol0104.Write16(bytes, offset + 2, id);
            Protocol0104.Write32(bytes, offset + 4, option); Protocol0104.Write32(bytes, offset + 8, expiry);
        }

        static ProtocolFrame NanoPage(int size, int offset, int id = 0, long owner = 51)
        {
            var bytes = new byte[76];
            Protocol0104.Write64(bytes, 0, owner); Protocol0104.Write32(bytes, 8, size); Protocol0104.Write32(bytes, 12, offset);
            Protocol0104.Write16(bytes, 16, id); Protocol0104.Write16(bytes, 18, id == 0 ? 0 : 131);
            return new ProtocolFrame(0x31000134, bytes);
        }

        [Test]
        public void ServerEntryPreservesSlotIdentityUnknownMaximumHpAndRemainingQuestCounts()
        {
            var session = new GameplayUiSession(_ => null); session.SetWorld(Entry());
            var state = session.Snapshot;
            Assert.That(state.DisplayName, Is.EqualTo("Eddy Tester"));
            Assert.That(state.Level, Is.EqualTo(7)); Assert.That(state.Hp, Is.EqualTo(420));
            Assert.That(state.MaxHp, Is.Null); Assert.That(state.HealthFraction, Is.Null);
            Assert.That(state.HealthOrigin, Is.EqualTo(GameplayHealthOrigin.ServerEntry));
            Assert.That(state.WorldPosition, Is.EqualTo(new Vector3(1, 3, 2)));
            Assert.That(state.Taros, Is.EqualTo(12345)); Assert.That(state.FusionMatter, Is.EqualTo(777));
            Assert.That(state.WeaponBattery, Is.EqualTo(11)); Assert.That(state.NanoBattery, Is.EqualTo(22));
            Assert.That(state.Equipment.Count, Is.EqualTo(9)); Assert.That(state.Equipment[8].ItemId, Is.EqualTo(82));
            Assert.That(state.Equipment[8].TimeLimit, Is.EqualTo(2000000));
            Assert.That(state.Inventory.Count, Is.EqualTo(50)); Assert.That(state.Inventory[49].Slot, Is.EqualTo(49));
            Assert.That(state.Inventory[49].ItemId, Is.EqualTo(513)); Assert.That(state.Inventory[49].Option, Is.EqualTo(3));
            Assert.That(state.Inventory[0].IsEmpty, Is.True); Assert.That(state.QuestInventory[0].ItemId, Is.EqualTo(42));
            Assert.That(state.Nanos[6].SkillId, Is.EqualTo(113)); Assert.That(state.Nanos[6].Stamina, Is.EqualTo(150));
            Assert.That(state.NanoSlots[0], Is.EqualTo(6)); Assert.That(state.ActiveNanoSlot, Is.EqualTo(-1));
            Assert.That(state.RunningQuests[8].TaskId, Is.EqualTo(991));
            Assert.That(state.RunningQuests[8].KillNpcIds[0], Is.EqualTo(615));
            Assert.That(state.RunningQuests[8].RemainingKillCounts[0], Is.EqualTo(3));
            Assert.That(state.RunningQuests[8].NeededItemCounts[0], Is.EqualTo(5));
            Assert.That(state.CurrentMissionId, Is.EqualTo(64));
        }

        [Test]
        public void EntryAndCollectionsCannotMutatePublishedSnapshot()
        {
            var entry = Entry(); var session = new GameplayUiSession(_ => null); session.SetWorld(entry);
            var original = session.Snapshot;
            Array.Clear(entry.RawLoadData, 0, entry.RawLoadData.Length);
            Assert.That(original.Inventory[49].ItemId, Is.EqualTo(513));
            Assert.Throws<NotSupportedException>(() => ((IList<GameplayItemState>)original.Inventory)[49] = default);
            Assert.Throws<NotSupportedException>(() => ((IList<int>)original.RunningQuests[8].RemainingKillCounts)[0] = 999);
            session.SetTutorialHealth(800, 1000);
            Assert.That(original.Hp, Is.EqualTo(420)); Assert.That(original.MaxHp, Is.Null);
            Assert.That(session.Snapshot.HealthOrigin, Is.EqualTo(GameplayHealthOrigin.LocalTutorial));
            Assert.That(session.Snapshot.HealthFraction, Is.EqualTo(.8f));
        }

        [Test]
        public void NanoResizePreludeDoesNotEraseBaseBookAndUnknownTailStaysIncomplete()
        {
            var session = new GameplayUiSession(_ => null); session.SetWorld(Entry(NanoPage(70, 0)));
            Assert.That(session.Snapshot.Nanos.Count, Is.EqualTo(70));
            Assert.That(session.Snapshot.Nanos[6].Id, Is.EqualTo(6));
            Assert.That(session.Snapshot.NanoBankComplete, Is.False);
            session.SetWorld(Entry(NanoPage(40, 0), NanoPage(40, 30, 30)));
            Assert.That(session.Snapshot.Nanos[30].Id, Is.EqualTo(30));
            Assert.That(session.Snapshot.NanoBankComplete, Is.True);
        }

        [Test]
        public void MalformedOrWrongOwnerNanoPagesDoNotReplaceActiveState()
        {
            var session = new GameplayUiSession(_ => null); session.SetWorld(Entry()); var previous = session.Snapshot;
            // The packet field named PCUID uses the live player ID, not the persistent UID1234.
            Assert.Throws<ProtocolException>(() => session.SetWorld(Entry(NanoPage(70, 0, owner: 1234))));
            Assert.Throws<ProtocolException>(() => session.SetWorld(Entry(NanoPage(int.MaxValue, 0))));
            Assert.Throws<ProtocolException>(() => session.SetWorld(Entry(NanoPage(70, -1))));
            Assert.Throws<ProtocolException>(() => session.SetWorld(Entry(NanoPage(70, 70))));
            Assert.Throws<ProtocolException>(() => session.SetWorld(Entry(NanoPage(70, 0), NanoPage(80, 10))));
            Assert.That(session.Snapshot, Is.SameAs(previous));
        }

        [Test]
        public void LocalChangesAreExplicitAndClearingPreventsCrossCharacterStateLeaks()
        {
            var session = new GameplayUiSession(_ => null); int changes = 0; session.Changed += _ => changes++;
            Assert.Throws<InvalidOperationException>(() => session.SetTutorialHealth(100, 100));
            session.SetWorld(Entry());
            session.SetLocalPosition(new Vector3(1, 3, 2)); Assert.That(changes, Is.EqualTo(1));
            session.SetLocalPosition(Vector3.zero); Assert.That(changes, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => session.SetLocalPosition(new Vector3(float.NaN, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.SetTutorialHealth(101, 100));
            session.Clear(); session.Clear();
            Assert.That(changes, Is.EqualTo(3)); Assert.That(session.Snapshot.IsActive, Is.False);
            Assert.That(session.Snapshot.Inventory, Is.Empty); Assert.That(session.Snapshot.Capabilities, Is.EqualTo(GameplayUiCapabilities.None));
        }

        [Test]
        public void GrowthLookupUsesDeclaredLevelRatherThanRowIndexAndRejectsAmbiguity()
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                document.Import("{\"m_pAvatarTable\":{\"m_pAvatarGrowData\":[{\"m_iLevel\":7,\"m_iMaxHP\":1450,\"m_iReqBlob_NanoCreate\":8190,\"m_iFMLimit\":323320},{\"m_iLevel\":1,\"m_iMaxHP\":1000,\"m_iReqBlob_NanoCreate\":220,\"m_iFMLimit\":5940}]}}");
                var limits = GameplayUiSession.ReadLevelLimits(document);
                var session = new GameplayUiSession(level => limits.TryGetValue(level, out var value) ? value : (GameplayLevelLimits?)null);
                session.SetWorld(Entry());
                Assert.That(session.Snapshot.IsTutorial, Is.False);
                Assert.That(session.Snapshot.MaxHp, Is.EqualTo(1450));
                Assert.That(session.Snapshot.MaxFusionMatter, Is.EqualTo(8190));
                Assert.That(session.Snapshot.HealthFraction, Is.EqualTo(420f / 1450f));
                Assert.That(session.Snapshot.FusionMatterFraction, Is.EqualTo(777f / 8190f));
                var tutorialEntry = Entry(); tutorialEntry.RawLoadData[81] = 0; session.SetWorld(tutorialEntry);
                Assert.That(session.Snapshot.IsTutorial, Is.True);
                Assert.That(session.Snapshot.MaxFusionMatter, Is.EqualTo(220));
                document.Import("{\"m_pAvatarTable\":{\"m_pAvatarGrowData\":[{\"m_iLevel\":7,\"m_iMaxHP\":1450,\"m_iReqBlob_NanoCreate\":8190},{\"m_iLevel\":7,\"m_iMaxHP\":1450,\"m_iReqBlob_NanoCreate\":8190}]}}");
                Assert.Throws<System.IO.InvalidDataException>(() => GameplayUiSession.ReadLevelLimits(document));
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public void DefaultAdapterResolvesAuthoredGrowthTable()
        {
            var session = new GameplayUiSession(); session.SetWorld(Entry());
            Assert.That(session.Snapshot.MaxHp, Is.EqualTo(1450));
            Assert.That(session.Snapshot.MaxFusionMatter, Is.EqualTo(8190));
        }
    }
}
