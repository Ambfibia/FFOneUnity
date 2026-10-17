using System;
using System.Linq;
using FFOne.Content;
using FFOne.Creation;
using FFOne.Network;
using FFOne.UI;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.Animations;

namespace FFOne.Tests
{
    public sealed class GameplayVehicleTests
    {
        static GameplayUiSession Session()
        {
            var p = new byte[2700]; Protocol0104.Write32(p, 0, 51); Protocol0104.Write64(p, 8, 1234); Protocol0104.Write16(p, 1676, -1);
            ProtocolServices.WriteItem(p, 4 + 136 + 8 * 12, new EquipmentItem { ItemType = 10, ItemId = 1, Option = 1, TimeLimit = 1234567 });
            ProtocolServices.WriteItem(p, 4 + 244 + 47 * 12, new EquipmentItem { ItemType = 10, ItemId = 4, Option = 1 });
            var s = new GameplayUiSession(_ => null); s.SetWorld(Protocol0104.DecodeWorldEntry(p, 1234)); return s;
        }
        [Test] public void OneByteToggleReceiptsAndPaddedStateUseTheirExactAbi()
        {
            Assert.AreEqual(new byte[1], ProtocolVehicle.TogglePayload());
            foreach (uint type in new[] { ProtocolVehicle.OnSuccess, ProtocolVehicle.OffSuccess })
            {
                Assert.IsTrue(ProtocolVehicle.TryDecode(new ProtocolFrame(type, new byte[1]), out var v));
                Assert.AreEqual(type == ProtocolVehicle.OnSuccess, v.Mounted);
                Assert.Throws<ProtocolException>(() => ProtocolVehicle.TryDecode(new ProtocolFrame(type, new byte[4]), out _));
            }
            var p = ProtocolTravel.Words(51, unchecked((int)0x7fffff08));
            ProtocolVehicle.TryDecode(new ProtocolFrame(ProtocolVehicle.StateChanged, p), out var state);
            Assert.AreEqual(8, state.StateFlags, "Padding must never be read as state bits."); Assert.IsTrue(state.Mounted);
        }
        [Test] public void MountFailureForeignStateAndEquipmentAloneNeverInventAMount()
        {
            var s = Session(); var before = s.Snapshot;
            Assert.IsFalse(before.VehicleMounted);
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.OnFailure, ProtocolTravel.Words(7))); Assert.AreSame(before, s.Snapshot);
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.StateChanged, ProtocolTravel.Words(52, 8))); Assert.AreSame(before, s.Snapshot);
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.OnSuccess, new byte[1])); Assert.IsTrue(s.Snapshot.VehicleMounted);
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.OffFailure, ProtocolTravel.Words(3))); Assert.IsTrue(s.Snapshot.VehicleMounted);
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.StateChanged, ProtocolTravel.Words(51, 0))); Assert.IsFalse(s.Snapshot.VehicleMounted);
        }
        [Test] public void ExpiryRemovesOnlyAuthoritativeSlotsAndDismountsEquippedVehicle()
        {
            var s = Session(); s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.OnSuccess, new byte[1])); var previous = s.Snapshot;
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.ExpiredItems, ProtocolTravel.Words(2, 0, 8, 1, 47)));
            Assert.IsTrue(s.Snapshot.Equipment[8].IsEmpty); Assert.IsTrue(s.Snapshot.Inventory[47].IsEmpty); Assert.IsFalse(s.Snapshot.VehicleMounted);
            Assert.AreEqual(1, previous.Equipment[8].ItemId); Assert.AreEqual(4, previous.Inventory[47].ItemId); Assert.IsTrue(previous.VehicleMounted);
        }
        [Test] public void BadExpiryPageIsAtomicAndUnopenedBankExpiryDoesNotInventContents()
        {
            var s = Session(); var before = s.Snapshot;
            foreach (var p in new[] { ProtocolTravel.Words(2, 0, 8), ProtocolTravel.Words(2, 0, 8, 0, 9), ProtocolTravel.Words(2, 0, 8, 0, 8), ProtocolTravel.Words(-1) })
            { Assert.Throws<ProtocolException>(() => s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.ExpiredItems, p))); Assert.AreSame(before, s.Snapshot); }
            s.ApplyFrame(new ProtocolFrame(ProtocolVehicle.ExpiredItems, ProtocolTravel.Words(1, 3, 199)));
            Assert.AreEqual(-1, s.Snapshot.BankNumber); Assert.IsEmpty(s.Snapshot.Bank);
        }
        [Test] public void RemoteMountedStateSurvivesMovementAndLateAppearanceEntry()
        {
            var world = new WorldEntitySession(51); var p = new byte[232]; Protocol0104.Write32(p, 0, 52); p[84] = 8;
            ProtocolServices.WriteItem(p, 112 + 8 * 12, new EquipmentItem { ItemType = 10, ItemId = 4 });
            world.Apply(new ProtocolFrame(ProtocolWorld.PlayerEnter, p));
            Assert.IsTrue(world.TryGet(WorldEntityKind.Player, 52, out var actor)); Assert.AreEqual(8, actor.StateFlags);
            var move = new byte[56]; Protocol0104.Write32(move, 44, 52); Protocol0104.Write32(move, 40, 900);
            world.Apply(new ProtocolFrame(ProtocolWorld.PlayerMove, move)); world.TryGet(WorldEntityKind.Player, 52, out actor);
            Assert.AreEqual(8, actor.StateFlags); Assert.AreEqual(900, actor.Speed);
            world.Apply(new ProtocolFrame(ProtocolVehicle.StateChanged, ProtocolTravel.Words(52, 0))); world.TryGet(WorldEntityKind.Player, 52, out actor);
            Assert.AreEqual(0, actor.StateFlags);
        }
        [TestCase(1, 1, "board_")] [TestCase(2, 4, "scooter_")]
        public void RemoteEquipmentAndOtherStateBitsPreserveMountedJumpUntilGroundMovement(int gender, int item, string prefix)
        {
            var catalog = new CreationCatalog(); int Choice(string category) => (int)catalog.Choices(category, gender)[0]["value"];
            var character = CreationCatalog.Preview(new CharacterCreation { Style = new CharacterStyle { Gender = gender, FaceStyle = Choice("face"), HairStyle = Choice("hair"), EyeColor = 1 },
                UpperBodyId = Choice("shirt"), LowerBodyId = Choice("pants"), FootId = Choice("shoes") });
            var entry = new byte[232]; Protocol0104.Write32(entry, 0, 52); Protocol0104.Write64(entry, 4, character.Uid);
            var style = character.Style;
            int[] appearance = { style.Gender, style.FaceStyle, style.HairStyle, style.HairColor, style.SkinColor, style.EyeColor, style.Height, style.Body };
            for (int i = 0; i < appearance.Length; i++) entry[66 + i] = (byte)appearance[i];
            entry[84] = ProtocolVehicle.MountedFlag; Protocol0104.Write32(entry, 88, 100);
            for (int i = 0; i < character.Equipment.Length; i++) ProtocolServices.WriteItem(entry, 112 + i * 12, character.Equipment[i]);
            ProtocolServices.WriteItem(entry, 112 + 8 * 12, new EquipmentItem { ItemType = 10, ItemId = item });
            var owner = new GameObject("Remote mounted jump acceptance");
            try
            {
                var replicas = owner.AddComponent<NetworkWorldEntities>(); replicas.Begin(51, new[] { new ProtocolFrame(ProtocolWorld.PlayerEnter, entry) });
                Assert.That(replicas.ContentFailures, Is.Empty);
                var peer = replicas.Actors.Single(); var avatar = peer.GetComponent<NativeAvatarPreview>(); var model = avatar.PersonalVehicleObject;
                var animator = avatar.PlayerAnimation.Animator;
                var movement = new byte[56]; Protocol0104.Write32(movement, 40, 750); Protocol0104.Write32(movement, 44, 52);
                replicas.Receive(new ProtocolFrame(ProtocolWorld.PlayerJump, movement)); animator.Update(.01f); animator.Update(.2f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "jumpstart"), Is.True);
                var equipment = new byte[20]; Protocol0104.Write32(equipment, 0, 52); Protocol0104.Write32(equipment, 4, 1);
                ProtocolServices.WriteItem(equipment, 8, character.Equipment[1]);
                foreach (var update in new[] { new ProtocolFrame(ProtocolInventory.EquipmentChanged, equipment),
                    new ProtocolFrame(ProtocolVehicle.StateChanged, ProtocolTravel.Words(52, ProtocolVehicle.MountedFlag | 16)) })
                {
                    replicas.Receive(update); animator.Update(.01f);
                    Assert.That(peer.Entity.Jumping, Is.True);
                    Assert.AreSame(model, avatar.PersonalVehicleObject, "A peer's ordinary equipment/state update must retain its confirmed mount.");
                    Assert.That(animator.IsInTransition(0), Is.False, "Equipment and other state bits cannot invent a grounded landing.");
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "jumpstart"), Is.True);
                }
                replicas.Receive(new ProtocolFrame(ProtocolWorld.PlayerMove, movement)); animator.Update(.01f);
                Assert.That(peer.Entity.Jumping, Is.False);
                Assert.That(animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "jumplandrun"), Is.True,
                    "The actual ground movement packet must still enter the mounted landing animation.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
        [TestCase(1, 1, 750)] [TestCase(4, 2, 750)]
        public void VehicleCatalogRetainsSourceFamilySpeedAndSound(int id, int family, int speed)
        {
            var row = GameplayUiCatalog.Load().Item(10, id);
            Assert.AreEqual(family, row.VehicleEquipType); Assert.AreEqual(speed, row.VehicleSpeed); Assert.IsNotEmpty(row.VehicleEngineSound);
        }
        [TestCase("board_jumpstart", .683333337f, "Vehicle_JumpStart.wav")]
        [TestCase("scooter_jumpend", .350000083f, "Vehicle_JumpLnd.wav")]
        [TestCase("board_jumplandrun", .350000083f, null)]
        [TestCase("scooter_run", .816666722f, null)]
        public void VehicleAnimationRetainsOriginalEndAndQuarterSecondSoundEvents(string name, float end, string sound)
        {
            Assert.AreEqual(end, NativeVehicleAnimation.EndSeconds(name)); Assert.AreEqual(sound, NativeVehicleAnimation.Sound(name));
            Assert.AreEqual(.25f, NativeVehicleAnimation.SoundSeconds);
            foreach (string gender in new[] { "male", "female" })
            {
                var profile = ContentAssets.Load<NativePlayerAnimationProfile>("characters/player/shared/animation/" + gender + "-profile");
                var clip = profile.Controller.animationClips.Single(c => c.name == name);
                var controller = (AnimatorController)profile.Controller;
                var state = controller.layers.Single(l => l.name == NativePlayerAnimator.BaseLayer).stateMachine.states.Single(s => s.state.name == name).state;
                string next = NativeVehicleAnimation.NextClip(name);
                if (next == null) Assert.That(state.transitions, Is.Empty, gender + "/" + name + " loops across its end event.");
                else
                {
                    var transition = state.transitions.Single();
                    Assert.That(transition.destinationState.name, Is.EqualTo(next));
                    Assert.That(transition.hasExitTime, Is.True);
                    Assert.That(transition.exitTime * clip.length, Is.EqualTo(end).Within(.00001f), gender + "/" + name + " original event boundary");
                    Assert.That(transition.duration, Is.EqualTo(.3f));
                }
            }
        }
        [TestCase(0, 1, 1.35f)] [TestCase(0, -1, .54f)] [TestCase(1, 1, 1.215f)] [TestCase(1, 0, .9f)]
        public void NativeVehicleAccelerationReachesDirectionalCapsAndCoastsPerFrame(float side, float forward, float multiplier)
        {
            var motion = new PersonalVehicleMotion();
            var first = motion.Step(side, forward, 0, 7.5f, true, 1f / 60);
            Assert.That(first.magnitude, Is.GreaterThan(0).And.LessThan(7.5f * multiplier));
            Vector3 last = first;
            for (int i = 0; i < 600; i++) last = motion.Step(side, forward, 0, 7.5f, true, 1f / 60);
            Assert.That(last.magnitude, Is.EqualTo(7.5f * multiplier).Within(.001));
            var coast = motion.Step(0, 0, 0, 7.5f, true, 1f / 60);
            Assert.That(coast.magnitude, Is.EqualTo(last.magnitude * .98f).Within(.00001));
            var air = motion.Step(0, 0, 0, 7.5f, false, 1f / 60);
            Assert.That(air.magnitude, Is.EqualTo(coast.magnitude * .997f).Within(.00001));
            motion.Reset(); Assert.AreEqual(Vector3.zero, motion.Momentum);
        }
        [TestCase(0, 1, 1.35f)] [TestCase(0, -1, .54f)] [TestCase(1, 1, 1.215f)] [TestCase(1, 0, .9f)]
        public void ObstacleResponseRetainsOriginalDirectionalCapAndCoastsFromTheCappedBounce(float side, float forward, float multiplier)
        {
            var motion = new PersonalVehicleMotion();
            var approaching = motion.Step(side, forward, 35, 7.5f, true, 1f / 60);
            var bounced = motion.Bounce();
            Assert.That(Vector3.Dot(approaching.normalized, bounced.normalized), Is.EqualTo(-1).Within(.00001f));
            Assert.That(bounced.magnitude, Is.EqualTo(Mathf.Min(7.5f, 7.5f * multiplier)).Within(.00001f),
                "The source clamps momentum after its obstacle response, including reverse and strafe limits.");
            Assert.That(motion.Momentum, Is.EqualTo(bounced));
            var coast = motion.Step(0, 0, 35, 7.5f, true, 1f / 60);
            Assert.That(Vector3.Distance(coast, bounced * .98f), Is.LessThan(.00001f));
            motion.Reset(); Assert.That(motion.Bounce(), Is.EqualTo(Vector3.zero));
        }
        [TestCase(1, 1, "board_")] [TestCase(2, 4, "scooter_")]
        public void VehicleUsesExactFootSocketAndOriginalWorldScaleWhileEquipmentRefreshRetainsIt(int gender, int item, string prefix)
        {
            var catalog = new CreationCatalog(); int Choice(string category) => (int)catalog.Choices(category, gender)[0]["value"];
            var character = CreationCatalog.Preview(new CharacterCreation { Style = new CharacterStyle { Gender = gender, FaceStyle = Choice("face"), HairStyle = Choice("hair"), EyeColor = 1 },
                UpperBodyId = Choice("shirt"), LowerBodyId = Choice("pants"), FootId = Choice("shoes") });
            var owner = new GameObject("Vehicle acceptance");
            try
            {
                var avatar = owner.AddComponent<NativeAvatarPreview>(); avatar.Apply(character); Assert.IsTrue(avatar.IsReady, avatar.LastError);
                Assert.IsTrue(avatar.SetPersonalVehicle(item), avatar.LastError);
                var model = avatar.PersonalVehicleObject;
                var exhaust = model.GetComponent<NativeVehicleExhaust>();
                Assert.That(exhaust.TrailCount, Is.GreaterThan(0)); Assert.AreEqual(item == 1 ? "board" : "standard", exhaust.Style);
                Assert.AreEqual("vehicle", model.transform.parent.name); Assert.That(model.transform.localPosition.magnitude, Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(model.transform.lossyScale, Vector3.one), Is.LessThan(.001));
                Assert.AreEqual(prefix + "run", avatar.LocomotionClip("run")); Assert.IsTrue(avatar.PlayerAnimation.Supports(prefix + "run"));
                Assert.IsTrue(avatar.PlayLocomotion("jumpstart", true));
                var animator = avatar.PlayerAnimation.Animator; animator.Update(.01f);
                Assert.That(animator.IsInTransition(0), Is.True, "Mounted jump entry must blend even when the movement caller requests a restart.");
                Assert.That(animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "jumpstart"), Is.True);
                Assert.That(animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.15f).Within(.00001f));
                animator.Update(.2f);
                Assert.IsTrue(avatar.PlayLocomotion("stand1")); animator.Update(.01f);
                Assert.That(animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "jumpend"), Is.True);
                Assert.That(animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.15f).Within(.00001f));
                animator.Update(.17f); animator.Update(.21f);
                Assert.That(animator.IsInTransition(0), Is.True);
                Assert.That(animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "stand1"), Is.True);
                Assert.That(animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.3f).Within(.00001f));
                Assert.IsTrue(avatar.PlayLocomotion("run")); animator.Update(.01f);
                Assert.That(animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + prefix + "stand1"), Is.True,
                    "Ground movement must not interrupt the confirmed landing return blend.");
                Assert.That(animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.3f).Within(.00001f));
                var motion = avatar.PlayerAnimation;
                var landingState = animator.GetCurrentAnimatorStateInfo(0);
                var returnState = animator.GetNextAnimatorStateInfo(0);
                var returnBlend = animator.GetAnimatorTransitionInfo(0);
                avatar.Apply(character);
                Assert.AreSame(motion, avatar.PlayerAnimation); Assert.AreEqual(item, avatar.PersonalVehicleId); Assert.IsNotNull(avatar.PersonalVehicleObject);
                Assert.AreSame(model, avatar.PersonalVehicleObject, "An equipment refresh on the same rig must retain its mounted attachment and exhaust.");
                Assert.That(animator.IsInTransition(0), Is.True, "Equipment refresh must preserve the landing return blend.");
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.EqualTo(landingState.fullPathHash));
                Assert.That(animator.GetNextAnimatorStateInfo(0).fullPathHash, Is.EqualTo(returnState.fullPathHash));
                Assert.That(animator.GetAnimatorTransitionInfo(0).normalizedTime, Is.EqualTo(returnBlend.normalizedTime).Within(.00001f));
                Assert.IsTrue(avatar.SetPersonalVehicle(0)); Assert.IsNull(avatar.PersonalVehicleObject); Assert.AreEqual("run", avatar.LocomotionClip("run"));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
