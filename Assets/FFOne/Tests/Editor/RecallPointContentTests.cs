using System.Collections;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FFOne.Tests
{
    public sealed class RecallPointContentTests
    {
        [Test] public void RecallRegistrationProtocolValidatesIdentityAndExactFrameSize()
        {
            Assert.AreEqual(4, ProtocolRecall.Registration(569).Length);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ProtocolRecall.Registration(0));
            Assert.IsTrue(ProtocolRecall.TryDecode(Registration(569), out var accepted));
            Assert.AreEqual(569, accepted.NpcId); Assert.AreEqual(-16066, accepted.Z);
            Assert.Throws<ProtocolException>(() => ProtocolRecall.TryDecode(Registration(0), out _));
            Assert.Throws<ProtocolException>(() => ProtocolRecall.TryDecode(new ProtocolFrame(ProtocolRecall.Success, ProtocolTravel.Words(569, 1, 2, 3, 4)), out _));
            Assert.IsTrue(ProtocolRecall.TryDecode(new ProtocolFrame(ProtocolRecall.Fail, ProtocolTravel.Words(1)), out var failure));
            Assert.IsTrue(failure.Failed);
        }
        [TestCase("RXcom", "accepted/canonical-rxcom.glb")]
        [TestCase("rxcom", WorldNpcCatalog.RecallPointModel)]
        public void RecallRecoveryFillsOnlyTheExactMissingModelRoute(string routedName, string expected)
        {
            var data = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                data.Import("{\"_ffone\":{\"tables\":[{\"name\":\"native_asset_routes\",\"value\":{\"m_pCharacterModelData\":[{\"logicalName\":\"" + routedName +
                    "\",\"glb\":\"accepted/canonical-rxcom.glb\"}]}}]},\"m_pNpcTable\":{\"m_pNpcMeshData\":[{\"m_pstrMMeshModelString\":\"RXcom\"},{\"m_pstrMMeshModelString\":\"RXCom\"}]," +
                    "\"m_pNpcStringData\":[{\"m_strName\":\"Recall Point\"}],\"m_pNpcData\":[{\"m_iNpcNumber\":1386,\"m_iMesh\":0,\"m_iNpcName\":0,\"m_iNpcType\":17}," +
                    "{\"m_iNpcNumber\":9999,\"m_iMesh\":1,\"m_iNpcName\":0,\"m_iNpcType\":17}]}}");
                var catalog = WorldNpcCatalog.Read(data);
                Assert.AreEqual(expected, catalog.Find(1386).Model, "An accepted exact route wins; the recovered route only fills an absent RXcom key.");
                Assert.IsNull(catalog.Find(9999).Model, "A similar name must not claim the recovered original model.");
            }
            finally { Object.DestroyImmediate(data); }
        }

        [Test] public void RecallPointLoadsOriginalRenderlessHierarchyAndMesh()
        {
            var definition = WorldNpcCatalog.Load().Find(1386);
            Assert.AreEqual("Recall Point", definition.Name);
            Assert.AreEqual(17, definition.Category);
            var prefab = NativeModelAssets.Load(definition.Model);
            Assert.IsNotNull(prefab, "The published Recall Point route must load through the actual model loader.");
            // Unity names the loaded main prefab object after model.prefab. Check
            // the exact published asset and its saved original root name instead.
            string path = AssetDatabase.GetAssetPath(prefab);
            Assert.AreEqual(WorldNpcCatalog.RecallPointModel.Substring("ffone/".Length), AssetDatabase.AssetPathToGUID(path));
            StringAssert.Contains("\n  m_Name: RXcom\n", File.ReadAllText(path).Replace("\r\n", "\n"));
            Assert.AreEqual(4, prefab.GetComponentsInChildren<Transform>(true).Length);
            Assert.IsEmpty(prefab.GetComponentsInChildren<Renderer>(true), "The original RXcom has no Renderer; keep its Box unrendered.");
            var box = prefab.transform.Find("Box01/Box01 NonAccum/Box");
            Assert.IsNotNull(box);
            var mesh = box.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.AreEqual("Box", mesh.name);
            Assert.AreEqual(24, mesh.vertexCount);
            Assert.AreEqual(36, mesh.triangles.Length);
            Assert.That(mesh.vertices[0].x, Is.EqualTo(.1231704685f).Within(.000001f));
            Assert.That(mesh.bounds.center.z, Is.EqualTo(.1004813015f).Within(.000001f));
        }

        [Test] public void RecallPointKeepsSourceStandCurvesAndEndEvent()
        {
            var prefab = NativeModelAssets.Load(WorldNpcCatalog.RecallPointModel);
            Assert.IsNotNull(prefab);
            var animation = prefab.GetComponent<Animation>();
            Assert.IsNotNull(animation);
            Assert.AreEqual("nif-default", animation.clip.name);
            var clip = animation.GetClip("stand1");
            Assert.IsNotNull(clip);
            Assert.IsTrue(clip.legacy);
            Assert.That(clip.length, Is.EqualTo(.9333336949f).Within(.0001f));
            var end = clip.events.Single();
            Assert.AreEqual("end", end.functionName);
            Assert.That(end.time, Is.EqualTo(.7166666985f).Within(.000001f));
            Assert.AreEqual(SendMessageOptions.DontRequireReceiver, end.messageOptions);
            var instance = Object.Instantiate(prefab);
            try
            {
                clip.SampleAnimation(instance, .5f);
                var animated = instance.transform.Find("Box01/Box01 NonAccum");
                Assert.That(animated.localPosition.x, Is.EqualTo(.0039727464f).Within(.000001f));
                Assert.That(animated.localPosition.y, Is.EqualTo(.002658695f).Within(.000001f));
                Assert.That(animated.localPosition.z, Is.EqualTo(.0005403195f).Within(.000001f));
                // Original packed words 0x7fdfed9f and 0x7fdff7ff. Both keys in
                // each track are identical. Compare complete orientations: q and
                // -q are equivalent, and the animation differs from the bind pose.
                var sourceBoxRotation = new Quaternion(.702544031311f, -.002932551320f, -.000977517107f, -.711633563481f);
                var sourceNonAccumRotation = new Quaternion(-.001956947162f, -.000977517107f, -.000977517107f, -.999997129635f);
                AssertSourceRotation(sourceBoxRotation, instance.transform.Find("Box01").localRotation);
                AssertSourceRotation(sourceNonAccumRotation, animated.localRotation);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        static void AssertSourceRotation(Quaternion source, Quaternion actual)
        {
            float sign = Quaternion.Dot(source, actual) < 0 ? -1 : 1;
            Assert.That(actual.x * sign, Is.EqualTo(source.x).Within(.000001f), "source quaternion x");
            Assert.That(actual.y * sign, Is.EqualTo(source.y).Within(.000001f), "source quaternion y");
            Assert.That(actual.z * sign, Is.EqualTo(source.z).Within(.000001f), "source quaternion z");
            Assert.That(actual.w * sign, Is.EqualTo(source.w).Within(.000001f), "source quaternion w");
            Assert.That(Quaternion.Angle(source, actual), Is.LessThan(.05f));
        }

        [Test] public void RecallPointUsesAuthoredIconEffectsAtOriginalRootFallback()
        {
            var actor = new GameObject("Recall Point test");
            try
            {
                var presentation = actor.AddComponent<RecallPointPresentation>();
                presentation.Initialize(WorldNpcCatalog.Load().Find(1386), null);
                Assert.AreEqual(446, presentation.EffectId);
                var unselected = presentation.EffectRoot;
                presentation.SetSelected(false);
                Assert.AreSame(unselected, presentation.EffectRoot, "Repeated server state must not restart the authored particles.");
                Assert.That(presentation.EffectRoot.localPosition.y, Is.EqualTo(1.08f).Within(.00001f));
                Assert.That(Quaternion.Angle(Quaternion.Euler(90, 0, 0), presentation.EffectRoot.localRotation), Is.LessThan(.001f));
                Assert.AreEqual(2, presentation.EffectRoot.GetComponentsInChildren<MeshRenderer>().Length);
                presentation.SetSelected(true);
                Assert.AreEqual(811, presentation.EffectId);
                Assert.IsFalse(unselected, "Replacing a marker must release the old particle owner.");
                Assert.AreEqual(1, actor.transform.childCount);
                Assert.AreEqual(2, presentation.EffectRoot.GetComponentsInChildren<MeshRenderer>().Length);
                presentation.SetSelected(false);
                Assert.AreEqual(446, presentation.EffectId);
            }
            finally { Object.DestroyImmediate(actor); }
        }

        [UnityTest]
        public IEnumerator RecallServerRegistrationSelectsInstanceIdentityAndRejectsMalformedStateAtomically()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            // EnterPlayMode reloads the domain and does not preserve the NUnit
            // fixture reference held by an iterator. Keep all post-reload state
            // in iterator locals rather than instance fields on this fixture.
            var networkOwner = new GameObject("Recall Point network acceptance");
            try
            {
                var replicas = networkOwner.AddComponent<NetworkWorldEntities>();
                // The first word is historically called iMapNum, but the primary
                // client compares it with Status.iID, not the NPC type or map ID.
                replicas.Begin(99, new[] { Registration(569) });
                replicas.Receive(NpcEnter(569)); replicas.Receive(NpcEnter(570));
                float deadline = Time.realtimeSinceStartup + 30;
                while (replicas.Actors.Count(a => a.GetComponent<RecallPointPresentation>()?.EffectRoot) != 2)
                {
                    Assert.That(replicas.ContentFailures, Is.Empty);
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The actual asynchronous RXcom route must load for both network actors.");
                    yield return null;
                }
                var first = replicas.Actors.Single(a => a.Entity.Id == 569).GetComponent<RecallPointPresentation>();
                var second = replicas.Actors.Single(a => a.Entity.Id == 570).GetComponent<RecallPointPresentation>();
                Assert.AreEqual(811, first.EffectId);
                Assert.AreEqual(446, second.EffectId);
                var firstEffect = first.EffectRoot;
                replicas.Receive(Registration(569));
                Assert.AreSame(firstEffect, first.EffectRoot, "An unchanged registration must preserve particle playback.");
                replicas.Receive(new ProtocolFrame(0x310000ce, ProtocolTravel.Words(1)));
                Assert.AreEqual(811, first.EffectId, "A rejected registration cannot clear the accepted point.");
                Assert.Throws<ProtocolException>(() => replicas.Receive(new ProtocolFrame(0x310000cd, ProtocolTravel.Words(570, 0, 0))));
                Assert.AreSame(firstEffect, first.EffectRoot, "A short registration cannot mutate selection.");
                Assert.Throws<ProtocolException>(() => replicas.Receive(Registration(0)));
                Assert.AreSame(firstEffect, first.EffectRoot, "An invalid NPC identity cannot mutate selection.");
                Assert.Throws<ProtocolException>(() => replicas.Receive(new ProtocolFrame(ProtocolTravel.WarpOk, new byte[4])));
                Assert.AreSame(firstEffect, first.EffectRoot, "A short warp cannot clear selection.");
                Assert.Throws<ProtocolException>(() => replicas.Receive(new ProtocolFrame(ProtocolTravel.WarpOk, ProtocolTravel.Words(0, 0, 0, 0, 0, 0, 0, 0, -1))));
                Assert.AreSame(firstEffect, first.EffectRoot, "A full-size invalid warp cannot clear selection before wallet validation.");
                replicas.Receive(Registration(570));
                Assert.AreEqual(446, first.EffectId);
                Assert.AreEqual(811, second.EffectId, "Selection follows a particular network NPC, even when both actors use type 1386.");
                replicas.Receive(new ProtocolFrame(ProtocolTravel.WarpOk, ProtocolTravel.Words(1, 2, 3, 0, 0, 0, 0, 0, 10)));
                Assert.AreEqual(446, first.EffectId);
                Assert.AreEqual(446, second.EffectId, "Only a validated successful NPC warp clears the accepted Recall point.");
            }
            finally { if (networkOwner) Object.Destroy(networkOwner); }
        }

        static ProtocolFrame Registration(int instanceId) => new ProtocolFrame(0x310000cd, ProtocolTravel.Words(instanceId, 130715, 223861, -16066));
        static ProtocolFrame NpcEnter(int instanceId) => new ProtocolFrame(ProtocolWorld.NpcEnter, ProtocolTravel.Words(instanceId, 1386, 100, 0, 130715, 223861, -16066, 0, 0));

        [UnityTearDown]
        public IEnumerator CleanupNetworkAcceptance()
        {
            var remainingOwner = GameObject.Find("Recall Point network acceptance");
            if (remainingOwner) Object.Destroy(remainingOwner);
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
