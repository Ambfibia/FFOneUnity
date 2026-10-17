using System;
using System.Linq;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Tests
{
    public sealed class LiveGameplayContractTests
    {
        static byte[] Bytes(int size, params (int offset, int value)[] fields)
        { var p = new byte[size]; foreach (var f in fields) Protocol0104.Write32(p, f.offset, f.value); return p; }
        static GameplayUiSession Session()
        {
            var p = Bytes(2700, (0, 51), (96, 100)); Protocol0104.Write64(p, 8, 123);
            Protocol0104.Write16(p, 4 + 1672, -1);
            var s = new GameplayUiSession(_ => new GameplayLevelLimits(200, 1000)); s.SetWorld(Protocol0104.DecodeWorldEntry(p, 123)); return s;
        }
        [Test] public void InitialNpcPageAndExitUseRuntimeIdAndNeverDuplicate()
        {
            var s = new WorldEntitySession(1); var p = Bytes(40, (0, 1), (4, 77), (8, 2669), (12, 100), (20, 125));
            Assert.True(s.Apply(new ProtocolFrame(ProtocolWorld.NpcsAround, p))); s.Apply(new ProtocolFrame(ProtocolWorld.NpcsAround, p));
            Assert.AreEqual(1, s.Count); Assert.True(s.TryGet(WorldEntityKind.Npc, 77, out var npc)); Assert.AreEqual(2669, npc.NpcType);
            s.Apply(new ProtocolFrame(ProtocolWorld.NpcLeave, Bytes(4, (0, 77)))); Assert.AreEqual(0, s.Count);
        }
        [Test] public void MalformedPageCannotPartiallyPopulateInterestSet()
        {
            var s = new WorldEntitySession(1); var p = Bytes(76, (0, 2), (4, 77), (8, 2669), (40, 0), (44, 2669));
            Assert.Throws<ProtocolException>(() => s.Apply(new ProtocolFrame(ProtocolWorld.NpcsAround, p))); Assert.AreEqual(0, s.Count);
        }
        [Test] public void UnknownMovementDoesNotInventActors()
        {
            var s = new WorldEntitySession(1); s.Apply(new ProtocolFrame(ProtocolWorld.NpcMove, Bytes(24, (0, 99), (4, 500)))); Assert.AreEqual(0, s.Count);
        }
        [Test] public void PlayerAndNpcNamespacesCanShareIds()
        {
            var s = new WorldEntitySession(1); s.Apply(new ProtocolFrame(ProtocolWorld.NpcEnter, Bytes(36, (0, 7), (4, 3))));
            s.Apply(new ProtocolFrame(ProtocolWorld.PlayerEnter, Bytes(232, (0, 7)))); Assert.AreEqual(2, s.Count);
        }
        [Test] public void AppearanceNamesRespectUtf16AlignmentAfterTheNameCheckByte()
        {
            var p = Bytes(232, (0, 7)); Protocol0104.Write64(p, 4, 9876543210);
            Protocol0104.WriteUtf16(p, 14, 9, "Бобби"); Protocol0104.WriteUtf16(p, 32, 17, "Юнити");
            var s = new WorldEntitySession(1); s.Apply(new ProtocolFrame(ProtocolWorld.PlayerEnter, p));
            Assert.True(s.TryGet(WorldEntityKind.Player, 7, out var player)); Assert.AreEqual("Бобби Юнити", player.Character.DisplayName);
            Assert.AreEqual(9876543210, player.Character.Uid);
        }
        [Test] public void AttackRequiresBoundedDistinctTargetsAndDoesNotGuessDamage()
        {
            Assert.Throws<ArgumentException>(() => ProtocolGameplay.AttackPayload(new[] { 7, 7 }));
            Assert.Throws<ArgumentException>(() => ProtocolGameplay.AttackPayload(new[] { 1, 2, 3, 4 }));
            Assert.AreEqual(12, ProtocolGameplay.AttackPayload(new[] { 7, 8 }).Length);
        }
        [TestCase(2)] [TestCase(4)] public void CombatAcceptsFriendlyNpcAndMobWireTypes(int type)
        {
            var p = Bytes(32, (0, 12), (4, 1), (8, type), (12, 7), (20, 40), (24, 60));
            Assert.True(ProtocolGameplay.TryCombat(new ProtocolFrame(ProtocolGameplay.AttackSuccess, p), out var result));
            Assert.AreEqual(60, result.Hits[0].Hp); Assert.AreEqual(WorldEntityKind.Npc, result.Hits[0].Kind);
        }
        [Test] public void NpcHitUpdatesLocalHudOnlyForMatchingPlayer()
        {
            var s = Session(); s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.NpcAttack, Bytes(32, (0, 88), (4, 1), (8, 1), (12, 51), (20, 20), (24, 80))));
            Assert.AreEqual(80, s.Snapshot.Hp); Assert.AreEqual(GameplayHealthOrigin.ServerUpdate, s.Snapshot.HealthOrigin);
        }
        [Test] public void MalformedRewardDoesNotPublishAnyInventoryOrCurrency()
        {
            var s = Session(); var old = s.Snapshot; var p = Bytes(56, (0, 1000), (48, 1), (52, 99)); p[16] = 1;
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Reward, p))); Assert.AreSame(old, s.Snapshot);
        }
        [Test] public void TickRefreshesHealthAndCarriedNanoWithoutDerivingLevelFromId()
        {
            var s = Session(); var p = Bytes(32, (0, 93), (24, 42)); Protocol0104.Write16(p, 4, 68); Protocol0104.Write16(p, 6, 13); Protocol0104.Write16(p, 8, 140);
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Tick, p));
            Assert.AreEqual(93, s.Snapshot.Hp); Assert.AreEqual(68, s.Snapshot.NanoSlots[0]); Assert.AreEqual(42, s.Snapshot.NanoBattery);
            Assert.AreEqual(140, s.Snapshot.Nanos.Single(n => n.Id == 68).Stamina); Assert.AreEqual(0, s.Snapshot.Level);
        }
        [Test] public void ActiveNanoCannotReferenceEmptySlot()
        {
            var s = Session(); Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.NanoActiveSuccess, new byte[8])));
        }
        [Test] public void MissionChangesOnlyAfterServerAcknowledgmentAndUsesTypeKillCounters()
        {
            var s = Session();
            s.MissionDefinition = id => new FFOne.World.WorldMissionDefinition { TaskId = id, MissionId = 7, NextTask = id == 10 ? 11 : 0,
                KillTypes = new[] { 88, 0, 0 }, KillCounts = new[] { 2, 0, 0 }, ItemIds = new int[3], ItemCounts = new int[3] };
            s.ApplyFrame(new ProtocolFrame(0x3100001c, Bytes(8, (0, 10))));
            Assert.AreEqual(2, s.Snapshot.RunningQuests.Single(q => q.TaskId == 10).RemainingKillCounts[0]);
            s.ApplyFrame(new ProtocolFrame(0x31000034, Bytes(4, (0, 88))));
            Assert.AreEqual(1, s.Snapshot.RunningQuests.Single(q => q.TaskId == 10).RemainingKillCounts[0]);
            var accepted = s.Snapshot;
            s.ApplyFrame(new ProtocolFrame(0x3100001f, Bytes(8, (0, 10), (4, 0)))); Assert.AreSame(accepted, s.Snapshot);
            int followup = 0; s.FollowUpTask += id => followup = id;
            s.ApplyFrame(new ProtocolFrame(0x3100001e, Bytes(4, (0, 10)))); Assert.AreEqual(11, followup);
            Assert.False(s.Snapshot.CompletedMissions.Contains(7));
        }
        [Test] public void NanoAcquisitionUsesExplicitServerLevelAndQuestItemPostState()
        {
            var s = Session(); var p = Bytes(28, (0, 500), (4, -1));
            Protocol0104.Write16(p, 20, 68); Protocol0104.Write16(p, 22, 13); Protocol0104.Write16(p, 24, 150); Protocol0104.Write16(p, 26, 2);
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.NanoCreate, p)); Assert.AreEqual(2, s.Snapshot.Level);
            Assert.AreEqual(500, s.Snapshot.FusionMatter); Assert.True(s.Snapshot.Nanos.Any(n => n.Id == 68));
        }
        [Test] public void NanoSkillRequiresItsExactTrailerBeforePublishingStamina()
        {
            var s = Session(); var p = Bytes(68, (0, 51), (28, 8), (32, 1), (36, 4), (40, 77), (52, 100));
            Protocol0104.Write16(p, 6, 1); Protocol0104.Write16(p, 24, 1); Protocol0104.Write16(p, 26, 124);
            s.ApplyFrame(new ProtocolFrame(ProtocolNanoSkill.Success, p)); Assert.AreEqual(124, s.Snapshot.Nanos.Single(n => n.Id == 1).Stamina);
            var accepted = s.Snapshot; Array.Resize(ref p, 67);
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolNanoSkill.Success, p))); Assert.AreSame(accepted, s.Snapshot);
        }
        [TestCase(47)] [TestCase(74)] [TestCase(97)] [TestCase(109)] [TestCase(111)] [TestCase(86)]
        [TestCase(174)] [TestCase(175)] [TestCase(176)] [TestCase(177)]
        [TestCase(153)]
        public void HnpcUsesAcceptedBodyPartsAndExactSharedSkeleton(int appearance)
        {
            var root = new GameObject("HNPC contract");
            try
            {
                var avatar = root.AddComponent<FFOne.Content.NativeAvatarPreview>(); avatar.ApplyHnpc(appearance);
                Assert.True(avatar.IsReady, avatar.LastError);
                Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.GreaterThan(4));
                Assert.NotNull(avatar.PlayerAnimation);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void PlumberHnpcBackpackRetainsItsWeightedIndependentSkeletonAndStartsItsSourceLoop()
        {
            Assert.AreEqual(153, FFOne.World.WorldNpcCatalog.Load().Find(646).HnpcAppearance);
            var root = new GameObject("Plumber HNPC independent skin");
            try
            {
                var avatar = root.AddComponent<FFOne.Content.NativeAvatarPreview>(); avatar.ApplyHnpc(153);
                Assert.True(avatar.IsReady, avatar.LastError); Assert.IsNull(avatar.LastError);
                var socket = avatar.PlayerAnimation.transform.Find("m/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 back01");
                Assert.IsNotNull(socket); Assert.AreEqual(1, socket.childCount);
                var backpack = socket.GetChild(0);
                var skin = backpack.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var source = FFOne.Content.NativeModelAssets.Load("characters/player/items/back/back_octibackpack/models/back_plumber/model.glb")
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                Assert.AreSame(source.sharedMesh, skin.sharedMesh, "The accepted mesh and its weights remain unchanged.");
                Assert.AreEqual(21, skin.bones.Length);
                Assert.That(skin.bones.Select(b => b.name), Is.EqualTo(source.bones.Select(b => b.name)));
                Assert.That(skin.bones.All(b => b.IsChildOf(backpack)), Is.True, "The backpack's own joints must not be remapped onto the player skeleton.");
                int box = Array.FindIndex(skin.bones, b => b.name == "Box08"); Assert.That(box, Is.GreaterThanOrEqualTo(0));
                int influenced = skin.sharedMesh.boneWeights.Count(w => w.boneIndex0 == box && w.weight0 > 0 || w.boneIndex1 == box && w.weight1 > 0 ||
                    w.boneIndex2 == box && w.weight2 > 0 || w.boneIndex3 == box && w.weight3 > 0);
                Assert.AreEqual(216, influenced, "Box08 is a real weighted joint, not an optional helper transform.");
                var animation = backpack.GetComponentInChildren<Animation>(true);
                Assert.IsNotNull(animation); Assert.IsNotNull(animation.clip); Assert.AreEqual("nif-default", animation.clip.name);
                Assert.AreEqual(WrapMode.Loop, animation.wrapMode); Assert.AreEqual(WrapMode.Loop, animation["nif-default"].wrapMode);
                Assert.True(animation.IsPlaying("nif-default"), "The source backpack loop must be started explicitly because the published prefab has no default clip.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void EveryRenderableAcceptedHnpcAppearanceAssemblesWithItsDeclaredSkinOwnership()
        {
            var appearances = FFOne.Content.NativeData.Object("data/hnpc/catalog")["appearances"].Where(a => a["parts"].Any()).ToArray();
            Assert.AreEqual(195, appearances.Length);
            foreach (var appearance in appearances)
            {
                int index = (int)appearance["index"];
                var root = new GameObject("HNPC declared assembly " + index);
                try
                {
                    var avatar = root.AddComponent<FFOne.Content.NativeAvatarPreview>(); avatar.ApplyHnpc(index);
                    Assert.True(avatar.IsReady, "HNPC appearance " + index + ": " + avatar.LastError);
                    Assert.IsNull(avatar.LastError, "HNPC appearance " + index);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        [Test] public void HnpcPaletteUsesZeroBasedExtendedSelectorsIndependentlyOfPlayerCreatorCodes()
        {
            Assert.AreEqual(new Color(.2265625f, .15234375f, .09765625f, 1), FFOne.Content.NativeAvatarPreview.HnpcColor("skin", 0));
            Assert.AreEqual(new Color(.39453125f, .30859375f, .23046875f, 1), FFOne.Content.NativeAvatarPreview.HnpcColor("skin", 15));
            Assert.AreEqual(new Color(.09765625f, .48828125f, .171875f, 1), FFOne.Content.NativeAvatarPreview.HnpcColor("hair", 19));
            Assert.AreEqual(Color.white, FFOne.Content.NativeAvatarPreview.HnpcColor("hair", -1));
            Assert.AreEqual(Color.white, FFOne.Content.NativeAvatarPreview.HnpcColor("skin", 20));
        }
        [Test] public void CompactPublishedSkinCannotChooseAnAmbiguousTerminalJoint()
        {
            var part = new GameObject("compact"); var actor = new GameObject("actor");
            try
            {
                var bip = new GameObject("Bip01"); bip.transform.SetParent(part.transform);
                var joint = new GameObject("Bip01 Spine"); joint.transform.SetParent(bip.transform);
                var skin = part.AddComponent<SkinnedMeshRenderer>(); skin.bones = new[] { joint.transform };
                var a = new GameObject("Bip01 Spine"); a.transform.SetParent(actor.transform);
                var b = new GameObject("Bip01 Spine"); b.transform.SetParent(actor.transform);
                var bones = new System.Collections.Generic.Dictionary<string, Transform> { ["m"] = actor.transform,
                    ["m/A/Bip01 Spine"] = a.transform, ["m/B/Bip01 Spine"] = b.transform };
                Assert.Throws<System.IO.InvalidDataException>(() => FFOne.Content.NativeAvatarPreview.BindSharedRig(part, bones, "m"));
            }
            finally { UnityEngine.Object.DestroyImmediate(part); UnityEngine.Object.DestroyImmediate(actor); }
        }
        [Test] public void GameplayEquipmentCanUseNonCreatorClothingAndExactAttachmentSockets()
        {
            var root = new GameObject("Equipped avatar contract");
            try
            {
                var character = new CharacterInfo();
                character.Equipment[1] = new EquipmentItem { ItemType = 1, ItemId = 193, Option = 1 };
                character.Equipment[2] = new EquipmentItem { ItemType = 2, ItemId = 47, Option = 1 };
                character.Equipment[3] = new EquipmentItem { ItemType = 3, ItemId = 34, Option = 1 };
                character.Equipment[4] = new EquipmentItem { ItemType = 4, ItemId = 29, Option = 1 };
                character.Equipment[0] = new EquipmentItem { ItemType = 0, ItemId = 328, Option = 1 };
                var avatar = root.AddComponent<FFOne.Content.NativeAvatarPreview>(); avatar.Apply(character);
                Assert.True(avatar.IsReady, avatar.LastError);
                Assert.IsNull(avatar.LastError);
                var skeleton = avatar.PlayerAnimation.transform;
                var weapon = skeleton.Find("m/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Neck/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand/Bip01 Rweapon01");
                Assert.IsNotNull(weapon); Assert.Greater(weapon.childCount, 0);
                avatar.PlayerAnimation.Animator.Update(.1f); avatar.FinishPose();
                Assert.AreEqual(Vector3.one, weapon.localScale);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test] public void NanoEquipAcknowledgmentDoesNotInventChangesToOtherSlots()
        {
            var s = Session(); var tick = Bytes(32, (0, 100)); Protocol0104.Write16(tick, 4, 1); Protocol0104.Write16(tick, 8, 150);
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Tick, tick)); var equip = new byte[8];
            Protocol0104.Write16(equip, 0, 1); Protocol0104.Write16(equip, 2, 1);
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.NanoEquipSuccess, equip));
            Assert.AreEqual(1, s.Snapshot.NanoSlots[0]); Assert.AreEqual(1, s.Snapshot.NanoSlots[1]);
        }
        [Test] public void NanoRecallUsesAuthoritativeMapAndCoordinatesInHud()
        {
            var s = Session(); var p = Bytes(60, (0, 51), (28, 27), (32, 1), (36, 1), (40, 51), (44, 0), (48, 125), (52, 375), (56, -250));
            Protocol0104.Write16(p, 6, 19); Protocol0104.Write16(p, 24, 1); Protocol0104.Write16(p, 26, 120);
            s.ApplyFrame(new ProtocolFrame(ProtocolNanoSkill.Success, p));
            Assert.AreEqual(new Vector3(1.25f, -2.5f, 3.75f), s.Snapshot.WorldPosition); Assert.AreEqual(0, s.Snapshot.MapNumber);
        }
        [TestCase(0)] [TestCase(-37)] public void CorruptionPublishesLethalHealthAndNanoPostStateAtomically(int hp)
        {
            var s = Session(); var tick = Bytes(32, (0, 100));
            Protocol0104.Write16(tick, 4, 1); Protocol0104.Write16(tick, 8, 150);
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Tick, tick));
            s.ApplyFrame(new ProtocolFrame(ProtocolGameplay.NanoActiveSuccess, new byte[8]));
            var p = Bytes(64, (0, 88), (20, 1), (24, 1), (28, 51), (40, hp), (48, 1));
            Protocol0104.Write16(p, 52, 1); Protocol0104.Write16(p, 54, 25);
            s.ApplyFrame(new ProtocolFrame(ProtocolNpcSkill.Corruption, p));
            Assert.AreEqual(0, s.Snapshot.Hp); Assert.AreEqual(GameplayHealthOrigin.ServerUpdate, s.Snapshot.HealthOrigin);
            Assert.AreEqual(-1, s.Snapshot.ActiveNanoSlot); Assert.AreEqual(25, s.Snapshot.Nanos.Single(n => n.Id == 1).Stamina);
            var accepted = s.Snapshot; Array.Resize(ref p, 63);
            Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolNpcSkill.Corruption, p)));
            Assert.AreSame(accepted, s.Snapshot);
        }
        [Test] public void NpcDamageSkillUsesItsOwnHeaderAndUpdatesMatchingPlayer()
        {
            var s = Session(); var p = Bytes(48, (0, 88), (20, 1), (24, 1), (28, 1), (32, 51), (44, 0));
            s.ApplyFrame(new ProtocolFrame(ProtocolNpcSkill.Hit, p)); Assert.AreEqual(0, s.Snapshot.Hp);
            Assert.AreEqual(GameplayHealthOrigin.ServerUpdate, s.Snapshot.HealthOrigin);
        }
        [Test] public void ServerBranchOverridesSerializedFinalRowWithoutCompletingMission()
        {
            var s = Session(); s.MissionDefinition = id => new FFOne.World.WorldMissionDefinition {
                TaskId = id, MissionId = 7, IsFinal = true, NextTask = 0, KillTypes = new int[3], KillCounts = new int[3],
                ItemIds = new int[3], ItemCounts = new int[3] };
            s.ApplyFrame(new ProtocolFrame(0x3100001c, Bytes(8, (0, 10))));
            s.ApplyFrame(new ProtocolFrame(0x3100013f, Bytes(16, (0, 10), (12, 11))));
            int followup = 0; s.FollowUpTask += id => followup = id;
            s.ApplyFrame(new ProtocolFrame(0x3100001e, Bytes(4, (0, 10))));
            Assert.AreEqual(11, followup); Assert.False(s.Snapshot.CompletedMissions.Contains(7));
        }
    }
}
