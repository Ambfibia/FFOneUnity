using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using FFOne.Editor;
using FFOne.Tutorial;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialTests
    {
        [Test]
        public void TutorialActorDefinitionsMatchAcceptedNpcAttributes()
        {
            var content = FFOne.Content.ContentAssets.Load<TutorialContent>("Tutorial/Content");
            Assert.That(content, Is.Not.Null);
            var xdt = FFOne.Content.NativeData.Object("data/tables/xdt");
            var rows = xdt["m_pNpcTable"]["m_pNpcData"].ToDictionary(row => (int)row["m_iNpcNumber"]);
            Assert.That(content.Actors, Is.Not.Empty);
            foreach (var actor in content.Actors)
            {
                Assert.That(rows.ContainsKey(actor.NpcType), Is.True, "NPC " + actor.NpcType);
                var row = rows[actor.NpcType];
                Assert.That(actor.MaxHp, Is.EqualTo((int)row["m_iHP"]), "HP " + actor.NpcType);
                Assert.That(actor.Style, Is.EqualTo((int)row["m_iNpcStyle"]), "Style " + actor.NpcType);
                Assert.That(actor.Class, Is.EqualTo((int)row["m_iNpcType"]), "Class " + actor.NpcType);
                Assert.That(actor.Scale, Is.EqualTo((float)row["m_fScale"]), "Scale " + actor.NpcType);
                Assert.That(actor.AttackRange, Is.EqualTo((int)row["m_iAtkRange"] / 100f), "Attack range " + actor.NpcType);
                Assert.That(actor.Height, Is.EqualTo((int)row["m_iHeight"] / 100f), "Height " + actor.NpcType);
                Assert.That(actor.Radius, Is.EqualTo((int)row["m_iRadius"] / 100f), "Radius " + actor.NpcType);
            }
        }

        [Test]
        public void ActorAttributeRefreshPreservesAuthoredIdentityAndRejectsIncompleteTablesBeforeMutation()
        {
            var content = ScriptableObject.CreateInstance<TutorialContent>();
            var model = new GameObject("Accepted model");
            try
            {
                var actor = new TutorialActorDefinition { NpcType = 2674, MaxHp = 361, Name = "Authored name", Model = model };
                var actors = new[] { actor }; content.Actors = actors; content.ButtercupNano = model;
                var cinematics = content.Cinematics; var audio = content.Audio;
                var xdt = JObject.Parse("{\"m_pNpcTable\":{\"m_pNpcData\":[{\"m_iNpcNumber\":2674,\"m_iHP\":400,\"m_iNpcStyle\":2,\"m_iNpcType\":0,\"m_fScale\":1.5,\"m_iAtkRange\":1740,\"m_iHeight\":180,\"m_iRadius\":60}]}}");
                Assert.That(TutorialContentImport.RefreshActorDefinitions(content, xdt), Is.EqualTo(1));
                Assert.That(actor.MaxHp, Is.EqualTo(400)); Assert.That(actor.Height, Is.EqualTo(1.8f));
                Assert.That(actor.Name, Is.EqualTo("Authored name")); Assert.That(actor.Model, Is.SameAs(model));
                Assert.That(content.Actors, Is.SameAs(actors)); Assert.That(content.ButtercupNano, Is.SameAs(model));
                Assert.That(content.Cinematics, Is.SameAs(cinematics)); Assert.That(content.Audio, Is.SameAs(audio));
                Assert.That(TutorialContentImport.RefreshActorDefinitions(content, xdt), Is.Zero, "Repair must be idempotent.");
                actor.MaxHp = 123;
                content.Actors = new[] { actor, new TutorialActorDefinition { NpcType = 999999 } };
                Assert.Throws<InvalidOperationException>(() => TutorialContentImport.RefreshActorDefinitions(content, xdt));
                Assert.That(actor.MaxHp, Is.EqualTo(123), "A missing later NPC row must not partially update earlier definitions.");
            }
            finally { UnityEngine.Object.DestroyImmediate(content); UnityEngine.Object.DestroyImmediate(model); }
        }

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
            Assert.That(content.World, Is.Null, "The obsolete monolithic prefab must not become a tutorial loading dependency.");
            Assert.That(content.ButtercupNano, Is.Not.Null);
            Assert.That(content.Actors.Select(a => a.NpcType), Does.Contain(2678));
            Assert.That(content.Actors.All(a => a.Model != null), Is.True);
            var world = FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog");
            var selected = TutorialController.CreateWorldCatalog(world);
            try
            {
                Assert.That(selected.Tiles.Select(t => t.Id), Is.EqualTo(new[] { "map_01_01" }));
                Assert.That(selected.Tiles[0].SceneAddress, Is.Not.Empty, "Tutorial must load the editable authored scene.");
                Assert.That(selected.Atmospheres, Is.SameAs(world.Atmospheres), "Keep all registered neighbor ambience colors.");
                Assert.That(selected.NeighborWater, Is.EquivalentTo(world.NeighborWater.Where(t => t.Id != TutorialController.WorldTileId)),
                    "Keep the surrounding tutorial water surfaces while the authored scene owns its own water.");
                Assert.That(selected.NeighborWater.Any(t => Mathf.Abs(t.X - 1) <= 1 && Mathf.Abs(t.Z - 1) <= 1), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(selected); }
            var cell = FFOne.Content.ContentAssets.Load<WorldTileContent>("World/Tiles/map_01_01/Content");
            Assert.That(cell, Is.Not.Null);
            Assert.That(cell.Colliders.Length, Is.EqualTo(441));
            Assert.That(cell.Terrain.GetComponent<MeshCollider>().sharedMesh, Is.Not.Null);
            var terrain = cell.Terrain.GetComponent<MeshRenderer>();
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.sharedMaterial.shader.name, Is.EqualTo("FFOne/WorldTerrain"));
            Assert.That(cell.Terrain.transform.localPosition, Is.EqualTo(new Vector3(512, -300, 512)));
            Assert.That(content.Skybox, Is.Not.Null, "The Retrobution six-sided tutorial sky must be published.");
            Assert.That(content.Skybox.shader.name, Is.EqualTo("Skybox/6 Sided"));
            foreach (string face in new[] { "Front", "Back", "Left", "Right", "Up", "Down" })
                Assert.That(content.Skybox.GetTexture("_" + face + "Tex"), Is.Not.Null, face);
        }
    }
}
