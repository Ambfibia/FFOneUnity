using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Creation;
using FFOne.Network;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class PlayerAnimatorTests
    {
        static readonly string[] ShapeClips = { "height_Add", "height", "shape_Add", "shape" };
        static readonly MethodInfo AnimatorLateUpdate = typeof(NativePlayerAnimator).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        const float Frame = 1f / 120;

        [TestCase("male")]
        [TestCase("female")]
        public void EveryAcceptedClipHasItsOriginalPlaybackAndAnEditableReachableState(string gender)
        {
            var rig = Rig(gender);
            var vehicle = NativeData.Object("characters/player/shared/vehicle_animations")["genders"].Single(g => (string)g["gender"] == gender);
            var accepted = rig["clips"].Concat(vehicle["clips"]).OrderBy(c => (int)c["gltfAnimationIndex"]).ToArray();
            var profile = Profile(gender);
            var controller = profile.Controller as AnimatorController;
            Assert.That(controller, Is.Not.Null);
            Assert.That(profile.Avatar.isValid, Is.True);
            Assert.That(accepted.Length, Is.EqualTo(141));
            Assert.That(accepted.Count(c => (string)c["playback"] == "loop"), Is.EqualTo(57));
            Assert.That(accepted.Count(c => (string)c["playback"] == "clamp"), Is.EqualTo(84));
            Assert.That(profile.ClipNames, Is.EqualTo(accepted.Select(c => (string)c["name"]).ToArray()));
            var clips = controller.animationClips.GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            var legacy = Prefab(gender).GetComponent<Animation>();
            foreach (var row in accepted)
            {
                string name = (string)row["name"];
                Assert.That(clips.ContainsKey(name), Is.True, gender + "/" + name);
                var clip = clips[name];
                Assert.That(clip.legacy, Is.False, name);
                Assert.That(ContentPaths.Legacy(AssetDatabase.GetAssetPath(clip)), Does.EndWith(".anim"), name);
                Assert.That(clip.length, Is.EqualTo(legacy.GetClip(name).length).Within(1e-5), name);
                Assert.That(AnimationUtility.GetCurveBindings(clip), Is.Not.Empty, name);
                bool loop = (string)row["playback"] == "loop";
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.EqualTo(loop), name);
                Assert.That(clip.wrapMode, Is.EqualTo(loop ? WrapMode.Loop : WrapMode.ClampForever), name);
                var layer = controller.layers.Single(l => l.name == (ShapeClips.Contains(name) ? name
                    : name.EndsWith("upper", StringComparison.Ordinal) ? NativePlayerAnimator.UpperLayer : NativePlayerAnimator.BaseLayer));
                var state = layer.stateMachine.states.Single(s => s.state.name == name).state;
                Assert.That(state.motion, Is.SameAs(clip), name);
                float vehicleEnd = NativeVehicleAnimation.EndSeconds(name);
                if (vehicleEnd > 0)
                {
                    string next = NativeVehicleAnimation.NextClip(name);
                    if (next == null) Assert.That(state.transitions, Is.Empty, name + " is a continuous source loop.");
                    else
                    {
                        var transition = state.transitions.Single();
                        Assert.That(transition.destinationState.name, Is.EqualTo(next), name);
                        Assert.That(transition.hasExitTime, Is.True, name);
                        Assert.That(transition.exitTime * clip.length, Is.EqualTo(vehicleEnd).Within(.00001f), name + " original end event");
                        Assert.That(transition.hasFixedDuration, Is.True, name);
                        Assert.That(transition.duration, Is.EqualTo(.3f), name + " EndAnimation return blend");
                    }
                }
                if (ShapeClips.Contains(name)) continue;
                Assert.That(layer.stateMachine.anyStateTransitions.Any(t => t.destinationState == state), Is.True, name);
                if (loop) continue;
                Assert.That(state.transitions.Any(t => t.hasExitTime && t.destinationState != null), Is.True,
                    name + " must leave its one-shot state without exposing the bind pose.");
            }
            var baseLayer = controller.layers.Single(l => l.name == NativePlayerAnimator.BaseLayer);
            Assert.That(baseLayer.stateMachine.defaultState.name, Is.EqualTo("stand1"));
            Assert.That(baseLayer.stateMachine.states.All(s => s.state.motion != null), Is.True);
            foreach (var transition in baseLayer.stateMachine.anyStateTransitions.Concat(baseLayer.stateMachine.states.SelectMany(s => s.state.transitions)))
            {
                Assert.That(transition.destinationState, Is.Not.Null);
                Assert.That(transition.destinationState.motion, Is.Not.Null);
                Assert.That(transition.duration, Is.GreaterThan(0).And.LessThanOrEqualTo(.3f));
            }
        }

        [TestCase("male")]
        [TestCase("female")]
        public void AcceptedMotionStatesEvaluateFiniteBonePoses(string gender)
        {
            using var rig = new AnimatedRig(gender, false);
            foreach (string name in Profile(gender).ClipNames)
            {
                Assert.That(rig.Motion.Supports(name), Is.True, name);
                if (ShapeClips.Contains(name))
                {
                    Assert.That(rig.Motion.Play(name), Is.False, name + " is a static body customization pose.");
                    continue;
                }
                int layer = name.EndsWith("upper", StringComparison.Ordinal) ? 1 : 0;
                string layerName = layer == 0 ? NativePlayerAnimator.BaseLayer : NativePlayerAnimator.UpperLayer;
                rig.Animator.SetLayerWeight(1, layer == 1 ? 1 : 0);
                rig.Animator.Play(layerName + "." + name, layer, .2f);
                rig.Animator.Update(0);
                Assert.That(rig.Animator.GetCurrentAnimatorClipInfo(layer).Any(c => c.clip.name == name), Is.True, name);
                AssertFinite(rig.Pose(), name);
            }
        }

        [TestCase("male", "stand1")]
        [TestCase("male", "run")]
        [TestCase("female", "stand1")]
        [TestCase("female", "run")]
        [TestCase("male", "board_stand1")]
        [TestCase("male", "board_run")]
        [TestCase("male", "board_runback")]
        [TestCase("male", "board_jump")]
        [TestCase("male", "scooter_stand1")]
        [TestCase("male", "scooter_run")]
        [TestCase("male", "scooter_runback")]
        [TestCase("male", "scooter_jump")]
        [TestCase("female", "board_stand1")]
        [TestCase("female", "board_run")]
        [TestCase("female", "board_runback")]
        [TestCase("female", "board_jump")]
        [TestCase("female", "scooter_stand1")]
        [TestCase("female", "scooter_run")]
        [TestCase("female", "scooter_runback")]
        [TestCase("female", "scooter_jump")]
        public void LocomotionRepeatsTheSamePoseAfterACompleteLoop(string gender, string clip)
        {
            using var rig = new AnimatedRig(gender, false);
            rig.Animator.Play(NativePlayerAnimator.BaseLayer + "." + clip, 0, .23f);
            rig.Animator.Update(0);
            var before = rig.Pose();
            float normalized = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            rig.Advance(rig.Motion.Duration(clip));
            var state = rig.Animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(rig.Animator.IsInTransition(0), Is.False, "Loop end events must not introduce an automatic reset blend.");
            Assert.That(state.IsName(NativePlayerAnimator.BaseLayer + "." + clip), Is.True);
            Assert.That(state.loop, Is.True);
            Assert.That(state.normalizedTime, Is.EqualTo(normalized + 1).Within(.001));
            AssertPoseEqual(before, rig.Pose(), .05f, 1e-4f, "loop boundary");
            Assert.That(MaxAngle(rig.BindPose, before), Is.GreaterThan(5), "The loop must evaluate an authored pose.");
        }

        [TestCase("male", "board_stand1")]
        [TestCase("female", "board_stand1")]
        [TestCase("male", "scooter_stand1")]
        [TestCase("female", "scooter_stand1")]
        public void VehicleIdleEndCallbackDoesNotRewindItsActiveLoop(string gender, string clip)
        {
            using var rig = new AnimatedRig(gender, false);
            float duration = rig.Motion.Duration(clip), end = NativeVehicleAnimation.EndSeconds(clip);
            rig.Animator.Play(NativePlayerAnimator.BaseLayer + "." + clip, 0, (end - Frame) / duration);
            rig.Animator.Update(0);
            rig.Advance(Frame * 3);
            float before = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            // The original stand1 callback CrossFades to the same mounted idle.
            // Its wrapper does not rewind the AnimationState; repeat requests stay idempotent.
            Assert.That(rig.Motion.Play(clip), Is.True);
            rig.Step(Frame);
            Assert.That(rig.Animator.IsInTransition(0), Is.False);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(before + Frame / duration).Within(.001f));
        }

        [TestCase("male", "board_jumpstart", "board_jump")]
        [TestCase("male", "board_jumpend", "board_stand1")]
        [TestCase("male", "board_jumplandrun", "board_run")]
        [TestCase("male", "scooter_jumpstart", "scooter_jump")]
        [TestCase("male", "scooter_jumpend", "scooter_stand1")]
        [TestCase("male", "scooter_jumplandrun", "scooter_run")]
        [TestCase("female", "board_jumpstart", "board_jump")]
        [TestCase("female", "board_jumpend", "board_stand1")]
        [TestCase("female", "board_jumplandrun", "board_run")]
        [TestCase("female", "scooter_jumpstart", "scooter_jump")]
        [TestCase("female", "scooter_jumpend", "scooter_stand1")]
        [TestCase("female", "scooter_jumplandrun", "scooter_run")]
        public void VehicleClampEntersAtPoint15AndReturnsAtItsSourceEventWithPoint3Blend(string gender, string clip, string successor)
        {
            using var rig = new AnimatedRig(gender, false);
            Assert.That(rig.Motion.Play(clip), Is.True);
            rig.Step(Frame);
            Assert.That(rig.Animator.IsInTransition(0), Is.True);
            Assert.That(rig.Animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + clip), Is.True);
            Assert.That(rig.Animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.15f).Within(.00001f));
            rig.Advance(.2f);
            float end = NativeVehicleAnimation.EndSeconds(clip);
            rig.Animator.Play(NativePlayerAnimator.BaseLayer + "." + clip, 0, (end - Frame) / rig.Motion.Duration(clip));
            rig.Animator.Update(0);
            Assert.That(rig.Animator.IsInTransition(0), Is.False);
            rig.Advance(Frame * 3);
            Assert.That(rig.Animator.IsInTransition(0), Is.True);
            Assert.That(rig.Animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + successor), Is.True);
            Assert.That(rig.Animator.GetAnimatorTransitionInfo(0).duration, Is.EqualTo(.3f).Within(.00001f));
            rig.Advance(.15f);
            Assert.That(rig.Animator.IsInTransition(0), Is.True, "The original return must retain its full .3 second blend.");
            rig.Advance(.2f);
            Assert.That(rig.Animator.IsInTransition(0), Is.False);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + successor), Is.True);
        }

        [TestCase("male")]
        [TestCase("female")]
        public void IdleToRunBlendsThroughRealPosesAndRepeatedRequestsDoNotRestart(string gender)
        {
            using var rig = new AnimatedRig(gender, false);
            rig.Animator.Play(NativePlayerAnimator.BaseLayer + ".stand1", 0, .2f);
            rig.Animator.Update(0);
            var previous = rig.Pose();
            Assert.That(rig.Motion.Play("run"), Is.True);
            rig.Step(Frame);
            Assert.That(rig.Animator.IsInTransition(0), Is.True);
            for (int i = 0; i < 35; i++)
            {
                var pose = rig.Pose();
                AssertFinite(pose, "idle/run blend");
                Assert.That(MaxAngle(previous, pose), Is.LessThan(25), "A blend must not snap through the bind pose.");
                Assert.That(MaxAngle(rig.BindPose, pose), Is.GreaterThan(5), "The avatar entered the bind pose.");
                previous = pose;
                Assert.That(rig.Motion.Play("run"), Is.True);
                rig.Step(Frame);
            }
            Assert.That(rig.Animator.IsInTransition(0), Is.False);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + ".run"), Is.True);
            float time = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            rig.Motion.Play("run");
            rig.Step(Frame);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(time));
            rig.Motion.Play("run", true);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.LessThan(.01f), "Explicit restart must restart an already active state.");
        }

        [TestCase("male")]
        [TestCase("female")]
        public void ReturningToIdleCancelsAnActiveRunTransitionWithoutRepeatedIdleResets(string gender)
        {
            using var rig = new AnimatedRig(gender, false);
            string idle = NativePlayerAnimator.BaseLayer + ".stand1";
            rig.Animator.Play(idle, 0, .2f);
            rig.Animator.Update(0);
            Assert.That(rig.Motion.Play("run"), Is.True);
            rig.Advance(Frame * 4);
            Assert.That(rig.Animator.IsInTransition(0), Is.True);
            Assert.That(rig.Animator.GetNextAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + ".run"), Is.True);
            var previous = rig.Pose();
            Assert.That(rig.Motion.Play("stand1"), Is.True);
            rig.Step(Frame);
            var target = rig.Animator.IsInTransition(0) ? rig.Animator.GetNextAnimatorStateInfo(0) : rig.Animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(target.IsName(idle), Is.True, "Returning to the current idle state must cancel the pending run state.");
            for (int i = 0; i < 36; i++)
            {
                var pose = rig.Pose();
                Assert.That(MaxAngle(previous, pose), Is.LessThan(25), "Cancelling locomotion must preserve pose continuity.");
                Assert.That(MaxAngle(rig.BindPose, pose), Is.GreaterThan(5), "Cancelling locomotion entered the bind pose.");
                previous = pose;
                Assert.That(rig.Motion.Play("stand1"), Is.True);
                rig.Step(Frame);
            }
            Assert.That(rig.Animator.IsInTransition(0), Is.False);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).IsName(idle), Is.True);
            float normalized = rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            for (int i = 0; i < 12; i++) { rig.Motion.Play("stand1"); rig.Step(Frame); }
            Assert.That(rig.Animator.IsInTransition(0), Is.False, "Repeated idle requests must not start self-transitions.");
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime,
                Is.EqualTo(normalized + Frame * 12 / rig.Motion.Duration("stand1")).Within(.001f));
        }

        [TestCase("male", "jumpstart", "jump")]
        [TestCase("female", "jumpstart", "jump")]
        [TestCase("male", "jumplandrun", "run")]
        [TestCase("female", "jumplandrun", "run")]
        [TestCase("male", "hello", "stand1")]
        [TestCase("female", "hello", "stand1")]
        [TestCase("male", "die", "death")]
        [TestCase("female", "die", "death")]
        public void OneShotReturnsToAnAnimatedSuccessor(string gender, string clip, string successor)
        {
            using var rig = new AnimatedRig(gender, false);
            Assert.That(rig.Motion.Play(clip), Is.True);
            float elapsed = 0, duration = rig.Motion.Duration(clip) + .5f;
            var previous = rig.Pose();
            while (elapsed < duration)
            {
                rig.Step(Frame);
                Assert.That(rig.Animator.GetCurrentAnimatorClipInfo(0), Is.Not.Empty, clip);
                var pose = rig.Pose();
                AssertFinite(pose, clip);
                Assert.That(MaxAngle(previous, pose), Is.LessThan(45), clip + " must blend through its entry and exit.");
                Assert.That(MaxAngle(rig.BindPose, pose), Is.GreaterThan(5), clip + " entered the bind pose.");
                previous = pose;
                elapsed += Frame;
            }
            Assert.That(rig.Animator.IsInTransition(0), Is.False, clip);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).IsName(NativePlayerAnimator.BaseLayer + "." + successor), Is.True, clip);
            Assert.That(rig.Animator.GetCurrentAnimatorStateInfo(0).loop, Is.True, successor);
        }

        [TestCase("male")]
        [TestCase("female")]
        public void UpperAttackPreservesRunningLegsAndReleasesItsPoseOnExit(string gender)
        {
            using var attacking = new AnimatedRig(gender, false);
            using var running = new AnimatedRig(gender, false);
            foreach (var rig in new[] { attacking, running })
            {
                rig.Animator.Play(NativePlayerAnimator.BaseLayer + ".run", 0, .2f);
                rig.Animator.Update(0);
            }
            Assert.That(attacking.Motion.Play("rifleattack1upper"), Is.True);
            var legs = attacking.Paths.Select((path, i) => (path, i)).Where(p => p.path.Contains(" Thigh", StringComparison.Ordinal)
                || p.path.Contains(" Calf", StringComparison.Ordinal) || p.path.Contains(" Foot", StringComparison.Ordinal)).Select(p => p.i).ToArray();
            var arms = attacking.Paths.Select((path, i) => (path, i)).Where(p => p.path.Contains(" UpperArm", StringComparison.Ordinal)
                || p.path.Contains(" Forearm", StringComparison.Ordinal)).Select(p => p.i).ToArray();
            Assert.That(legs, Is.Not.Empty); Assert.That(arms, Is.Not.Empty);
            float peakUpperDifference = 0;
            float duration = attacking.Motion.Duration("rifleattack1upper") + .6f;
            var previous = attacking.Pose();
            for (float elapsed = 0; elapsed < duration; elapsed += Frame)
            {
                attacking.Step(Frame); running.Step(Frame);
                var attackPose = attacking.Pose(); var runPose = running.Pose();
                AssertPoseEqual(runPose, attackPose, .05f, 1e-4f, "Upper attack must preserve locomotion", legs);
                Assert.That(MaxAngle(previous, attackPose, arms), Is.LessThan(25), "Upper attack must fade out without snapping to locomotion.");
                peakUpperDifference = Mathf.Max(peakUpperDifference, MaxAngle(runPose, attackPose, arms));
                previous = attackPose;
            }
            Assert.That(peakUpperDifference, Is.GreaterThan(5), "Upper attack never animated the arms.");
            Assert.That(attacking.Animator.GetCurrentAnimatorStateInfo(1).IsName(NativePlayerAnimator.UpperLayer + ".Empty"), Is.True);
            AssertPoseEqual(running.Pose(), attacking.Pose(), .1f, 1e-4f, "The completed upper attack must release its previous pose");
            var layer = ((AnimatorController)Profile(gender).Controller).layers.Single(l => l.name == NativePlayerAnimator.UpperLayer);
            Assert.That(layer.avatarMask, Is.Not.Null);
            for (int i = 0; i < layer.avatarMask.transformCount; i++)
            {
                string path = layer.avatarMask.GetTransformPath(i);
                if (path.EndsWith("Bip01 Pelvis", StringComparison.Ordinal) || path.Contains(" Thigh", StringComparison.Ordinal))
                    Assert.That(layer.avatarMask.GetTransformActive(i), Is.False, path);
                if (path.EndsWith("Bip01 Spine", StringComparison.Ordinal) || path.EndsWith("Bip01 L UpperArm", StringComparison.Ordinal))
                    Assert.That(layer.avatarMask.GetTransformActive(i), Is.True, path);
            }
        }

        [TestCase("male", 0, 0)]
        [TestCase("male", 4, 2)]
        [TestCase("female", 0, 0)]
        [TestCase("female", 4, 2)]
        public void StaticHeightAndShapeModifyProportionsWithoutFreezingIdle(string gender, int height, int body)
        {
            using var shaped = new AnimatedRig(gender, true);
            using var unshaped = new AnimatedRig(gender, false);
            shaped.Motion.SetBodyShape(height, body);
            foreach (var rig in new[] { shaped, unshaped })
            {
                rig.Animator.Play(NativePlayerAnimator.BaseLayer + ".stand1", 0, .13f);
                rig.Animator.Update(0);
            }
            var firstShaped = shaped.Pose(); var firstUnshaped = unshaped.Pose();
            Assert.That(firstShaped.Scales.Zip(firstUnshaped.Scales, Vector3.Distance).Max()
                + firstShaped.Positions.Zip(firstUnshaped.Positions, Vector3.Distance).Max(), Is.GreaterThan(.001f), "Body customization was not applied.");
            foreach (var rig in new[] { shaped, unshaped })
            {
                rig.Animator.Play(NativePlayerAnimator.BaseLayer + ".stand1", 0, .57f);
                rig.Animator.Update(0);
            }
            var secondShaped = shaped.Pose(); var secondUnshaped = unshaped.Pose();
            var legacy = Prefab(gender).GetComponent<Animation>();
            var affected = new HashSet<string>(new[] { "height_Add", "shape_Add" }.SelectMany(n => AnimationUtility.GetCurveBindings(legacy.GetClip(n))).Select(b => b.path));
            int movingCustomizedBones = 0;
            for (int i = 0; i < shaped.Paths.Length; i++)
            {
                float expected = Quaternion.Angle(firstUnshaped.Rotations[i], secondUnshaped.Rotations[i]);
                if (!affected.Contains(shaped.Paths[i]) || expected < .05f) continue;
                movingCustomizedBones++;
                Assert.That(Quaternion.Angle(firstShaped.Rotations[i], secondShaped.Rotations[i]),
                    Is.EqualTo(expected).Within(.1f), shaped.Paths[i] + " must retain its idle motion under the static poses.");
            }
            Assert.That(movingCustomizedBones, Is.GreaterThan(0));
            AssertFinite(firstShaped, "body customization"); AssertFinite(secondShaped, "animated body customization");
        }

        [TestCase(1, "male")]
        [TestCase(2, "female")]
        public void SameGenderAppearanceChangesPreserveSkeletonAnimatorAndPlaybackTime(int genderCode, string gender)
        {
            var catalog = new CreationCatalog();
            var draft = Draft(genderCode);
            var root = new GameObject("Same gender avatar regression");
            try
            {
                var preview = root.AddComponent<NativeAvatarPreview>();
                preview.Apply(CreationCatalog.Preview(draft));
                Assert.That(preview.IsReady, Is.True, preview.LastError);
                var driver = preview.PlayerAnimation;
                Assert.That(driver, Is.Not.Null);
                driver.Animator.Update(.37f);
                var animator = driver.Animator;
                var skeleton = animator.gameObject;
                var before = animator.GetCurrentAnimatorStateInfo(0);
                var bones = Paths(gender).Select(path => skeleton.transform.Find(path)).ToArray();
                var pose = Snapshot(bones);
                draft.Style.HairColor = 2; draft.Style.EyeColor = 2;
                draft.Style.FaceStyle = (int)catalog.Choices("face", genderCode)[1]["value"];
                preview.Apply(CreationCatalog.Preview(draft));
                Assert.That(preview.IsReady, Is.True, preview.LastError);
                Assert.That(preview.PlayerAnimation, Is.SameAs(driver));
                Assert.That(preview.PlayerAnimation.Animator, Is.SameAs(animator));
                Assert.That(preview.PlayerAnimation.Animator.gameObject, Is.SameAs(skeleton));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, Is.EqualTo(before.fullPathHash));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(before.normalizedTime).Within(1e-5));
                Assert.That(Paths(gender).Select(path => skeleton.transform.Find(path)).ToArray(), Is.EqualTo(bones));
                AssertPoseEqual(pose, Snapshot(bones), .05f, 1e-4f, "Appearance replacement must retain the animated skeleton pose.");
                animator.Update(.1f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(before.normalizedTime));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void GameplayClothingVariantOutsideCreatorChoicesUsesAcceptedRigWithoutRestartingAnimation()
        {
            Assert.That(Rig("male")["creatorChoices"].Any(c => (string)c["appearanceCategory"] == "shirt" && (int)c["itemNumber"] == 19), Is.False);
            var root = new GameObject("Gameplay clothing regression");
            try
            {
                var preview = root.AddComponent<NativeAvatarPreview>();
                var character = CreationCatalog.Preview(Draft(1));
                preview.Apply(character); Assert.That(preview.IsReady, Is.True, preview.LastError);
                var animator = preview.PlayerAnimation.Animator;
                var controller = animator.runtimeAnimatorController;
                animator.Update(.37f);
                var state = animator.GetCurrentAnimatorStateInfo(0);
                character.Equipment[1] = new EquipmentItem { ItemType = 1, ItemId = 19, Option = 1 };
                preview.Apply(character);
                Assert.That(preview.LastError, Is.Null);
                Assert.That(preview.PlayerAnimation.Animator, Is.SameAs(animator));
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(state.normalizedTime).Within(1e-5));
                Assert.That(root.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Any(m => m.GetTexture("_MainTex")?.name == "m_shirt_baseballset"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(1, "male")]
        [TestCase(2, "female")]
        public void CustomizedAvatarDoesNotAccumulateBodyPoseAcrossTwentyIdleLoops(int genderCode, string gender)
        {
            var root = new GameObject("Long running customized avatar regression");
            try
            {
                var preview = root.AddComponent<NativeAvatarPreview>();
                preview.Apply(CreationCatalog.Preview(Draft(genderCode)));
                Assert.That(preview.IsReady, Is.True, preview.LastError);
                var motion = preview.PlayerAnimation;
                var animator = motion.Animator;
                animator.Play(NativePlayerAnimator.BaseLayer + ".stand1", 0, .23f);
                animator.Update(0);
                var paths = Paths(gender);
                var bones = paths.Select(path => animator.transform.Find(path)).ToArray();
                var first = Snapshot(bones);
                var firstBounds = BakedAvatarBounds(root);
                Assert.That(firstBounds.size.x, Is.GreaterThan(0));
                Assert.That(firstBounds.size.y, Is.GreaterThan(0));
                Assert.That(firstBounds.size.z, Is.GreaterThan(0));
                float phase = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                for (int loop = 1; loop <= 20; loop++)
                {
                    float remaining = motion.Duration("stand1");
                    while (remaining > Frame) { animator.Update(Frame); remaining -= Frame; }
                    if (remaining > 0) animator.Update(remaining);
                    AnimatorLateUpdate.Invoke(motion, null);
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(phase + loop).Within(.002f));
                    var pose = Snapshot(bones);
                    AssertFinite(pose, "idle loop " + loop);
                    AssertPoseEqual(first, pose, .1f, .001f, "Static body poses accumulated during idle loop " + loop);
                    for (int i = 0; i < pose.Scales.Length; i++)
                        for (int axis = 0; axis < 3; axis++)
                            Assert.That(pose.Scales[i][axis], Is.GreaterThan(0).And.LessThanOrEqualTo(first.Scales[i][axis] * 1.01f + .001f),
                                paths[i] + " has invalid accumulated scale during idle loop " + loop);
                    var bounds = BakedAvatarBounds(root);
                    Assert.That(Vector3.Distance(bounds.size, firstBounds.size), Is.LessThan(firstBounds.size.magnitude * .001f),
                        "The skinned avatar's world size drifted during idle loop " + loop);
                    Assert.That(Vector3.Distance(bounds.center, firstBounds.center), Is.LessThan(firstBounds.size.magnitude * .001f),
                        "The skinned avatar moved away from its original framing during idle loop " + loop);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static CharacterCreation Draft(int genderCode)
        {
            var catalog = new CreationCatalog();
            int First(string category) => (int)catalog.Choices(category, genderCode)[0]["value"];
            return new CharacterCreation {
                Style = new CharacterStyle { Gender = genderCode, FaceStyle = First("face"), HairStyle = First("hair"),
                    EyeColor = 1, SkinColor = 1, HairColor = 1, Height = 2, Body = 1 },
                UpperBodyId = First("shirt"), LowerBodyId = First("pants"), FootId = First("shoes")
            };
        }
        static Bounds BakedAvatarBounds(GameObject root)
        {
            var mesh = new Mesh();
            var bounds = new Bounds();
            bool found = false;
            try
            {
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    // BakeMesh evaluates current skinning in EditMode, where Renderer.bounds can
                    // still describe the previous scene repaint rather than Animator.Update's pose.
                    renderer.BakeMesh(mesh);
                    mesh.RecalculateBounds();
                    var local = mesh.bounds;
                    for (int x = -1; x <= 1; x += 2)
                        for (int y = -1; y <= 1; y += 2)
                            for (int z = -1; z <= 1; z += 2)
                            {
                                var point = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                                if (found) bounds.Encapsulate(point);
                                else { bounds = new Bounds(point, Vector3.zero); found = true; }
                            }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            Assert.That(found, Is.True, "The avatar must contain skinned renderers.");
            return bounds;
        }

        static JToken Rig(string gender) => JObject.Parse(FFOne.Content.ContentAssets.Load<TextAsset>("characters/player/shared/player_rig_contract").text)["genders"]
            .Single(r => (string)r["gender"] == gender);
        static string[] Paths(string gender) => Rig(gender)["nodes"].Select(n => (string)n["fullPath"]).ToArray();
        static GameObject Prefab(string gender)
        {
            string path = (string)Rig(gender)["skeletonGlb"];
            return FFOne.Content.ContentAssets.Load<GameObject>(path.Substring(0, path.LastIndexOf('.')));
        }
        static NativePlayerAnimationProfile Profile(string gender)
        {
            var profile = FFOne.Content.ContentAssets.Load<NativePlayerAnimationProfile>("characters/player/shared/animation/" + gender + "-profile");
            Assert.That(profile, Is.Not.Null, "Rebuild the accepted Player Controllers before animation verification.");
            Assert.That(profile.Controller, Is.Not.Null); Assert.That(profile.Avatar, Is.Not.Null);
            return profile;
        }
        sealed class AnimatedRig : IDisposable
        {
            public readonly GameObject Root;
            public readonly NativePlayerAnimator Motion;
            public readonly Animator Animator;
            public readonly string[] Paths;
            public readonly BonePose BindPose;
            readonly Transform[] bones;
            public AnimatedRig(string gender, bool bodyPoses)
            {
                Profile(gender);
                Root = UnityEngine.Object.Instantiate(Prefab(gender));
                foreach (var legacy in Root.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(legacy);
                Paths = PlayerAnimatorTests.Paths(gender);
                bones = Paths.Select(path => Root.transform.Find(path)).ToArray();
                Assert.That(bones.All(b => b != null), Is.True);
                BindPose = Snapshot(bones);
                Motion = Root.AddComponent<NativePlayerAnimator>();
                Motion.Configure(gender, 2, 1);
                Animator = Motion.Animator;
                if (!bodyPoses) for (int i = 2; i < Animator.layerCount; i++) Animator.SetLayerWeight(i, 0);
                Animator.Update(0);
            }
            public BonePose Pose() => Snapshot(bones);
            public void Step(float delta)
            {
                Animator.Update(delta);
                // EditMode's manual Animator evaluation omits the component callback Unity normally
                // executes afterward to release a completed upper-body overlay.
                AnimatorLateUpdate.Invoke(Motion, null);
            }
            public void Advance(float seconds)
            {
                while (seconds > Frame) { Step(Frame); seconds -= Frame; }
                if (seconds > 0) Step(seconds);
            }
            public void Dispose() { if (Root) UnityEngine.Object.DestroyImmediate(Root); }
        }
        sealed class BonePose
        {
            public Vector3[] Positions, Scales;
            public Quaternion[] Rotations;
        }
        static BonePose Snapshot(Transform[] bones) => new() {
            Positions = bones.Select(b => b.localPosition).ToArray(), Scales = bones.Select(b => b.localScale).ToArray(),
            Rotations = bones.Select(b => b.localRotation).ToArray()
        };
        static float MaxAngle(BonePose a, BonePose b, int[] indices = null) => ((IEnumerable<int>)indices ?? Enumerable.Range(0, a.Rotations.Length))
            .Select(i => Quaternion.Angle(a.Rotations[i], b.Rotations[i])).Max();
        static void AssertPoseEqual(BonePose expected, BonePose actual, float angle, float distance, string message, int[] indices = null)
        {
            foreach (int i in (IEnumerable<int>)indices ?? Enumerable.Range(0, expected.Rotations.Length))
            {
                Assert.That(Quaternion.Angle(expected.Rotations[i], actual.Rotations[i]), Is.LessThanOrEqualTo(angle), message + " rotation " + i);
                Assert.That(Vector3.Distance(expected.Positions[i], actual.Positions[i]), Is.LessThanOrEqualTo(distance), message + " position " + i);
                Assert.That(Vector3.Distance(expected.Scales[i], actual.Scales[i]), Is.LessThanOrEqualTo(distance), message + " scale " + i);
            }
        }
        static void AssertFinite(BonePose pose, string context)
        {
            for (int i = 0; i < pose.Rotations.Length; i++)
            {
                bool finite = true;
                for (int axis = 0; axis < 3; axis++)
                {
                    finite &= float.IsFinite(pose.Positions[i][axis]) && float.IsFinite(pose.Scales[i][axis]);
                }
                for (int axis = 0; axis < 4; axis++) finite &= float.IsFinite(pose.Rotations[i][axis]);
                if (!finite) Assert.Fail(context + " has a non-finite transform at accepted bone " + i);
            }
        }
    }
}
