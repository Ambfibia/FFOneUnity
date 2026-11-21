using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class PlayerGroundingRegressionTests
    {
        [Test]
        public void ReleasedJumpIsLowerAndHeldJumpStillLandsAtDifferentFrameRates()
        {
            foreach (float delta in new[] { 1f / 120, 1f / 60, 1f / 30, .1f })
            {
                float shortJump = JumpHeight(.05f, delta);
                float longJump = JumpHeight(1, delta);
                Assert.That(longJump, Is.GreaterThan(shortJump + 1), "Held versus tap at " + delta);
                Assert.That(longJump, Is.InRange(2.4f, 3.3f));
            }
        }

        static float JumpHeight(float hold, float delta)
        {
            var root = new GameObject("jump regression");
            root.transform.position = Vector3.one * 10000;
            try
            {
                var floor = new GameObject("floor", typeof(BoxCollider)); floor.transform.SetParent(root.transform);
                floor.transform.localPosition = new Vector3(0, -.5f, 0); floor.GetComponent<BoxCollider>().size = new Vector3(20, 1, 20);
                var player = new GameObject("player", typeof(CharacterController)); player.transform.SetParent(root.transform);
                player.transform.localPosition = Vector3.zero;
                var motor = player.GetComponent<CharacterController>(); NativePlayerMotor.Configure(motor);
                Physics.SyncTransforms();
                float velocity = 5.68f * NativePlayerMotor.JumpMultiplier, peak = 0, age = 0;
                for (int i = 0; i < Mathf.CeilToInt(3 / delta); i++)
                {
                    NativePlayerMotor.Step(motor, Vector3.zero, ref velocity, ref age, delta, i * delta < hold, i == 0);
                    peak = Mathf.Max(peak, player.transform.localPosition.y);
                }
                Assert.That(NativePlayerMotor.IsGrounded(motor, velocity), Is.True);
                Assert.That(player.transform.localPosition.y, Is.InRange(-.04f, .08f));
                return peak;
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void DescendingSlopeRetainsSupportAndWallIsNotGround()
        {
            var root = new GameObject("slope regression");
            root.transform.position = Vector3.one * 10000;
            try
            {
                var ramp = new GameObject("ramp", typeof(BoxCollider)); ramp.transform.SetParent(root.transform);
                ramp.transform.localPosition = Vector3.zero;
                ramp.GetComponent<BoxCollider>().size = new Vector3(8, .5f, 30); ramp.transform.rotation = Quaternion.Euler(15, 0, 0);
                var player = new GameObject("player", typeof(CharacterController)); player.transform.SetParent(root.transform);
                player.transform.localPosition = new Vector3(0, 2, -4);
                var motor = player.GetComponent<CharacterController>(); NativePlayerMotor.Configure(motor); Physics.SyncTransforms();
                float velocity = 0;
                for (int i = 0; i < 240; i++)
                {
                    bool grounded = NativePlayerMotor.IsGrounded(motor, velocity);
                    velocity = grounded ? 0 : Mathf.Max(-10, velocity - 10 / 60f);
                    NativePlayerMotor.Move(motor, Vector3.forward * 3, ref velocity, 1f / 60, grounded);
                    if (i > 90) Assert.That(NativePlayerMotor.IsGrounded(motor, velocity), Is.True, "Slope contact frame " + i);
                }
                NativePlayerMotor.Teleport(motor, root.transform.TransformPoint(new Vector3(20, 2, 0)));
                var wall = new GameObject("wall", typeof(BoxCollider)); wall.transform.SetParent(root.transform);
                wall.transform.localPosition = new Vector3(20.7f, 3, 0); wall.GetComponent<BoxCollider>().size = new Vector3(1, 10, 10);
                Physics.SyncTransforms();
                Assert.That(NativePlayerMotor.IsGrounded(motor, 0), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EveryTutorialInstructionUsesItsRussianTranslation()
        {
            var root = new GameObject("localization regression");
            try
            {
                var tutorial = root.AddComponent<TutorialController>();
                tutorial.ApplyPreferences(FFOne.UI.SettingsPreferences.Defaults());
                typeof(TutorialController).GetProperty("Russian").SetValue(tutorial, true);
                var localize = typeof(TutorialController).GetMethod("LocalizeLiteral", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var text in TutorialStages.All.Select(s => s.Instruction).Where(s => !string.IsNullOrEmpty(s)).Distinct())
                    Assert.That((string)localize.Invoke(tutorial, new object[] { text }), Is.Not.EqualTo(text), text);
                Assert.That(LocalizationCatalog.Lookup("tutorial.instruction.look_right", "ru"), Does.Contain("вправо"));
                Assert.That(LocalizationCatalog.Lookup("tutorial.instruction.look_up", "ru"), Does.Contain("вверх"));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
