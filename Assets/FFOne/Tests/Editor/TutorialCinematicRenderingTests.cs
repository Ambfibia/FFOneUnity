using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Editor;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialCinematicRenderingTests
    {
        [TestCase(2668)]
        [TestCase(2673)]
        [TestCase(2902)]
        public void DexterBodyTextureIsBoundBeforeTheFirstFrameWithoutChangingAccessories(int type)
        {
            var definition = ContentAssets.Load<TutorialContent>("Tutorial/Content").Actors.Single(a => a.NpcType == type);
            var instance = Object.Instantiate(definition.Model);
            try
            {
                var receiver = instance.GetComponentInChildren<NativeNpcAnimator>(true).gameObject.AddComponent<TutorialAnimationEffectReceiver>();
                receiver.Initialize(null, definition, null);
                var block = new MaterialPropertyBlock();
                int bound = 0;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        renderer.GetPropertyBlock(block, i);
                        if (renderer.sharedMaterials[i].name.Contains("main-link_a"))
                        {
                            Assert.That(block.GetTexture("_MainTex"), Is.Not.Null);
                            Assert.That(block.GetTexture("_MainTex").name, Is.EqualTo("npc_dexter")); bound++;
                        }
                        else Assert.That(block.GetTexture("_MainTex"), Is.Null, "Authored face, glass and weapon slots must be retained.");
                        block.Clear();
                    }
                Assert.That(bound, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void CinematicWarpsSettleOnTheAuthoredTerrainAndBridge()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/FFOne/Worlds/Overworld/Cells/map_01_01/map_01_01.unity",
                OpenSceneMode.Additive);
            var player = new GameObject("cinematic terrain probe");
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var terrain in root.GetComponentsInChildren<TerrainCollider>(true)) terrain.enabled = false;
                var owner = player.AddComponent<TutorialController>();
                var motor = player.AddComponent<CharacterController>();
                NativePlayerMotor.Configure(motor);
                typeof(TutorialController).GetProperty(nameof(TutorialController.Player)).SetValue(owner, player.transform);
                typeof(TutorialController).GetField("motor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, motor);
                Physics.SyncTransforms();
                foreach (var (position, floor) in new[] {
                    (new Vector3(566, -100, 665), -101.508f),
                    (new Vector3(588.4f, -133.5f, 983.8f), -133.243f),
                    (new Vector3(907, 11, 713), 11.236f),
                    (new Vector3(907, 11.119f, 687), 11.089f),
                    (new Vector3(987.9f, 111.2f, 547.3f), 111.252f)
                })
                {
                    owner.WarpPlayer(position);
                    for (int frame = 0; frame < 90; frame++) owner.StepCinematicPlayer(1f / 60);
                    Assert.That(player.transform.position.y, Is.EqualTo(floor).Within(.12f), position.ToString());
                    Assert.That(NativePlayerMotor.IsGrounded(motor, 0), Is.True, position.ToString());
                }
            }
            finally { Object.DestroyImmediate(player); EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void InclusiveRandomSoundVariantsPreservePaddingOutsideTheRandomToken()
        {
            Assert.That(TutorialActorPresentationImport.SoundVariants("Ben_AttackGrunt_0(RAND:1-3).wav"),
                Is.EqualTo(new[] { "Ben_AttackGrunt_01.wav", "Ben_AttackGrunt_02.wav", "Ben_AttackGrunt_03.wav" }));
        }

        [Test]
        public void RecoveredSpawnVariantsAndAnimatedFacesAreAvailable()
        {
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            var spawn = content.Actors.Single(a => a.NpcType == 2674);
            foreach (string cue in new[] { "Spawn_CS_Melee2_(RAND:1-3).wav", "Spawn_CS_Run1_(RAND:1-2).wav", "Spawn_CS_Skill0.wav" })
                Assert.That(spawn.AnimationSounds.Single(s => s.Cue == cue).Variants.All(clip => clip), Is.True, cue);
            var numberTwo = content.Actors.Single(a => a.NpcType == 2671);
            Assert.That(numberTwo.Faces.Length, Is.EqualTo(3));
            Assert.That(numberTwo.Faces.All(face => face.Texture), Is.True);
        }

        [Test]
        public void RootParticleEventsKeepTheSourceBasisAndWorldScale()
        {
            var owner = new GameObject("effect owner");
            var actor = new GameObject("scaled animation actor");
            try
            {
                actor.transform.localScale = Vector3.one * 2;
                var effects = owner.AddComponent<TutorialCinematicEffects>();
                effects.AttachRootAnimationEffect(actor.transform, 751);
                var effect = actor.transform.GetChild(0);
                Assert.That(effect.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(Quaternion.Angle(effect.localRotation, Quaternion.identity), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(effect.lossyScale, Vector3.one), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(actor); }
        }

        [TestCase(2664, "melee1", "FFOneAnimationSound")]
        [TestCase(2666, "melee1event", "FFOneParticleEffect")]
        [TestCase(2800, "fly", "FFOneParticleEffect")]
        public void PublishedClipsRetainSoundAndRootEffectCallbacks(int type, string clipName, string callback)
        {
            var prefab = ContentAssets.Load<TutorialContent>("Tutorial/Content").Actors.Single(a => a.NpcType == type).Model;
            var clip = prefab.GetComponentInChildren<NativeNpcAnimator>(true).GetClip(clipName, false);
            Assert.That(AnimationUtility.GetAnimationEvents(clip).Any(e => e.functionName == callback), Is.True);
        }

        [Test]
        public void PublishedFusionSpawnUsesPackedMasksRatherThanRedRgbArt()
        {
            var prefab = ContentAssets.Load<TutorialContent>("Tutorial/Content").Actors.Single(a => a.NpcType == 2674).Model;
            var material = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Distinct().Single(m => m.name == "mob_sneakyspawn-sub-link_a.dds");
            Assert.That(material.GetFloat("_FusionMatter"), Is.EqualTo(1));
            Assert.That(material.GetColor("_FusionTint"), Is.EqualTo(Color.green));
            Assert.That(material.mainTexture.name, Is.EqualTo("back_fusionmatter"));
        }

        [TestCase(2664, "melee1", .9f, "736:tag01")]
        [TestCase(2667, "melee1", .18f, "734:tag01")]
        [TestCase(2968, "fall", .25f, "769:tag06")]
        public void PublishedClipsRetainTheirTimedBoneEffects(int type, string clipName, float time, string parameter)
        {
            var prefab = ContentAssets.Load<TutorialContent>("Tutorial/Content").Actors.Single(a => a.NpcType == type).Model;
            var clip = prefab.GetComponentInChildren<NativeNpcAnimator>(true).GetClip(clipName, false);
            Assert.That(AnimationUtility.GetAnimationEvents(clip).Any(e => e.functionName == TutorialAnimationEventImport.EffectCallback
                && Mathf.Abs(e.time - time) < .0001f && e.stringParameter == parameter), Is.True);
        }

        [Test]
        public void SkylineCannotFallWhenItIsRecreatedForTheBridgeScene()
        {
            var ownerRoot = new GameObject("skyline grounding regression");
            var skylineRoot = new GameObject("skyline");
            try
            {
                var owner = ownerRoot.AddComponent<TutorialController>();
                var actor = skylineRoot.AddComponent<TutorialActor>();
                actor.NpcType = 2968; actor.RuntimeId = 2; actor.Class = 25; actor.Hp = 1;
                skylineRoot.transform.position = new Vector3(973, -76, 702);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                ((Dictionary<int, TutorialActor>)typeof(TutorialController).GetField("actors", flags).GetValue(owner)).Add(2, actor);
                typeof(TutorialController).GetMethod("GroundActors", flags).Invoke(owner, new object[] { 1f });
                Assert.That(skylineRoot.transform.position, Is.EqualTo(new Vector3(973, -76, 702)));
            }
            finally { Object.DestroyImmediate(ownerRoot); Object.DestroyImmediate(skylineRoot); }
        }

        [Test]
        public void CaveExitUpdatesTheCameraAndStaysOpaqueUntilTheBridgeSceneFadesIn()
        {
            var root = new GameObject("warp frame regression");
            var cameraRoot = new GameObject("old cave camera");
            try
            {
                var owner = root.AddComponent<TutorialController>();
                var view = cameraRoot.AddComponent<Camera>();
                typeof(TutorialController).GetProperty("Player").SetValue(owner, root.transform);
                typeof(TutorialController).GetProperty("ViewCamera").SetValue(owner, view);
                typeof(TutorialController).GetProperty("Stage").SetValue(owner, TutorialStages.Get(4, 16));
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(TutorialController).GetField("warpTarget", flags).SetValue(owner, new Vector3(907.65f, 10.43f, 715.58f));
                typeof(TutorialController).GetField("pendingWarp", flags).SetValue(owner, 1.5f);
                typeof(TutorialController).GetMethod("Evaluate", flags).Invoke(owner, null);
                Assert.That((float)typeof(TutorialController).GetField("fade", flags).GetValue(owner), Is.EqualTo(1));
                Assert.That(Vector3.Distance(view.transform.position, root.transform.position), Is.LessThan(12));
                Assert.That(owner.Stage.Chapter, Is.EqualTo(5));
                Assert.That(owner.Stage.Step, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraRoot); }
        }
    }
}
