using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using FFOne.Editor;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialTests
    {
        [Test]
        public void OriginalNpcGravitySettlesAtFloorAndCapsFallSpeed()
        {
            Assert.That(TutorialController.AdvanceGrounding(11, 12, -3, .1f), Is.EqualTo((12f, 0f)));
            Assert.That(TutorialController.AdvanceGrounding(17, 11, 0, .1f), Is.EqualTo((16.9f, -1f)));
            Assert.That(TutorialController.AdvanceGrounding(12, 11, -9, .2f), Is.EqualTo((11f, 0f)));
            Assert.That(TutorialController.AdvanceGrounding(100, null, -9, 1), Is.EqualTo((90f, -10f)));
        }
        [Test]
        public void TutorialDamagePreservesStyleAdvantageAndInvulnerability()
        {
            Assert.That(TutorialController.VirtualDamage(-1, 1, false), Is.EqualTo(100), "No active Nano weak damage");
            Assert.That(TutorialController.VirtualDamage(1, 0, false), Is.EqualTo(300), "Buttercup style advantage");
            Assert.That(TutorialController.VirtualDamage(1, 1, false), Is.EqualTo(200));
            Assert.That(TutorialController.VirtualDamage(1, 2, false), Is.EqualTo(100));
            Assert.That(TutorialController.VirtualDamage(1, 0, true), Is.Zero, "The stun lesson's original-English target cannot die");
            Assert.That(TutorialController.VirtualDamage(4, 0, false), Is.Zero);
        }
        [Test]
        public void StaticTutorialMaterialBindsOriginalTextureAndEncodedPalette()
        {
            const string asset = "Assets/Resources/objects/nature/dt_tree_05/models/dt_tree_09_01_default/visual.glb";
            var material = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(asset)).OfType<Material>().Single();
            Assert.That(material.shader.name, Is.EqualTo("FFOne/NativeWorld"));
            Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, "Static extras have a different schema from animated extras.ffone.");
            Assert.That(material.GetVector("_Color").x, Is.EqualTo(.23529413f).Within(.00001));
            Assert.That(material.GetVector("_Emission").x, Is.EqualTo(.29411766f).Within(.00001));
            Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.SrcAlpha));
        }
        [Test]
        public void LegacyStagesRetainSparseStepsAndInputLocks()
        {
            Assert.That(TutorialStages.All.Length, Is.EqualTo(74));
            Assert.That(TutorialStages.All.Select(s => (s.Chapter, s.Step)).Distinct().Count(), Is.EqualTo(74));
            Assert.That(TutorialStages.Get(3, 100).Name, Is.EqualTo("NumbuhTwoDelay"));
            Assert.That(TutorialStages.Get(4, 210).Scene, Is.EqualTo(TutorialScene.InfectionB));
            Assert.That(TutorialStages.Get(0, 3).Allows(17), Is.True, "Look stage permits mouse");
            Assert.That(TutorialStages.Get(0, 3).Allows(0), Is.False, "Look stage locks forward movement");
            Assert.That(TutorialStages.Get(0, 7).Allows(0), Is.True);
            Assert.That(TutorialStages.Get(0, 7).Allows(1), Is.False, "Forward stage locks backward movement");
            Assert.That(TutorialStages.Get(5, 4).AllowedInputs, Is.EqualTo(1 << 18), "Summon stage permits only Nano activation");
            foreach (var stage in TutorialStages.All.Where(s => s.Scene != TutorialScene.None))
                Assert.That(stage.AllowedInputs, Is.Zero, stage.Name);
            Assert.Throws<InvalidOperationException>(() => TutorialStages.Get(4, 7));
        }
        [Test]
        public void FiniteScenesHaveSourceDurationsAndOrderedAudio()
        {
            Assert.That(TutorialAudioTimeline.Durations.Count, Is.EqualTo(10));
            Assert.That(TutorialAudioTimeline.Durations.ContainsKey(TutorialScene.InfectionD), Is.False);
            Assert.That(TutorialAudioTimeline.Durations[TutorialScene.BasicMove], Is.EqualTo(31.15f));
            Assert.That(TutorialAudioTimeline.Durations[TutorialScene.NanoPowerB], Is.EqualTo(47.15f));
            foreach (var scene in TutorialAudioTimeline.Cues)
            {
                Assert.That(scene.Value.Select(c => c.Seconds), Is.Ordered, scene.Key.ToString());
                Assert.That(scene.Value.All(c => c.Seconds <= TutorialAudioTimeline.Durations[scene.Key]), Is.True);
            }
        }
        [Test]
        public void NativeTutorialHeightmapPreservesRawRangeAndRowOrientation()
        {
            string root = NativeContentImport.DefaultSource;
            var definition = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(Path.Combine(root, "map/tiles/map_01_01/terrain/terrain.json"))));
            int resolution = (int)definition["dimensions"]!["width"]!;
            byte[] png = File.ReadAllBytes(ContentPaths.FilePath(Path.Combine(root, "map/tiles/map_01_01/terrain/heightmap.png")));
            var heights = TutorialHeightmap.Read(png, resolution);
            Assert.That(heights.Cast<float>().Min(), Is.EqualTo((int)definition["heightmap"]!["rawMin"]! / 32767f).Within(1e-7));
            Assert.That(heights.Cast<float>().Max(), Is.EqualTo((int)definition["heightmap"]!["rawMax"]! / 32767f).Within(1e-7));
            // Source starts at Unity +X,+Z tile corner (512,-300,512); no RGBA8 reduction.
            Assert.That(heights.GetLength(0), Is.EqualTo(129));
            Assert.Throws<InvalidDataException>(() => TutorialHeightmap.Read(png, 257));
            Assert.Throws<InvalidDataException>(() => TutorialHeightmap.Read(new byte[3], 129));
        }
        [Test]
        public void NativeInstanceBasisRoundTripsToUnityWithoutChangingScale()
        {
            var go = new GameObject("coordinate test");
            try
            {
                TutorialContentImport.ApplyNativeTransform(go.transform, JObject.Parse("{\"translation\":[-547,-105.4,655],\"rotation\":[0,0.70710677,0,0.70710677],\"scale\":[2,3,4]}"));
                Assert.That(Vector3.Distance(go.transform.position, TutorialController.StartPosition), Is.LessThan(.0001));
                Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(Vector3.Distance(go.transform.forward, Vector3.left), Is.LessThan(.0001));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test]
        public void ImportedTutorialHasExactTerrainAndCollisionWorld()
        {
            var content = FFOne.Content.ContentAssets.Load<TutorialContent>("Tutorial/Content");
            Assert.That(content, Is.Not.Null, "Run FFOne/Import Tutorial Content.");
            Assert.That(content.World, Is.Not.Null);
            Assert.That(content.ButtercupNano, Is.Not.Null);
            Assert.That(content.Actors.Select(a => a.NpcType), Does.Contain(2678));
            Assert.That(content.Actors.All(a => a.Model != null), Is.True);
            Assert.That(content.World.GetComponentsInChildren<MeshCollider>(true).Length, Is.EqualTo(441));
            var terrain = content.World.GetComponentInChildren<Terrain>();
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.terrainData.heightmapResolution, Is.EqualTo(129));
            Assert.That(terrain.transform.localPosition, Is.EqualTo(new Vector3(512, -300, 512)));
        }
    }
}
