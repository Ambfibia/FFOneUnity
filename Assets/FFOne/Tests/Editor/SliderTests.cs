using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFOne.Content;
using FFOne.Network;
using FFOne.World;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public class SliderTests
    {
        static ProtocolFrame Frame(uint type, params int[] words) => new ProtocolFrame(type, ProtocolTravel.Words(words));
        [Test] public void LifecyclePreservesSeparateEntityNamespaceAndHeading()
        {
            var session = new WorldEntitySession(99);
            Assert.IsTrue(session.Apply(Frame(ProtocolSlider.Enter, 3, 1, 1, 100, 200, 300)));
            session.Apply(Frame(ProtocolSlider.Move, 3, 1, 1, 2, 3, 1200, 0));
            Assert.IsTrue(session.TryGet(WorldEntityKind.Transportation, 1, out var bus));
            Assert.AreEqual(1, bus.TransportationType); Assert.AreEqual(1200, bus.Speed); Assert.AreEqual(0, bus.Angle);
            Assert.IsFalse(session.TryGet(WorldEntityKind.Npc, 1, out _));
            session.Apply(Frame(ProtocolSlider.Exit, 3, 1)); Assert.AreEqual(0, session.Count);
            session.Apply(Frame(ProtocolSlider.Move, 3, 1, 0, 0, 0, 1200, 0)); Assert.AreEqual(0, session.Count);
        }
        [Test] public void MalformedPageDoesNotApplyEarlierEntries()
        {
            var session = new WorldEntitySession(99);
            Assert.Throws<ProtocolException>(() => session.Apply(Frame(ProtocolSlider.Around, 2, 3, 1, 1, 0, 0, 0, 3, 0, 1, 0, 0, 0)));
            Assert.AreEqual(0, session.Count);
            Assert.Throws<ProtocolException>(() => session.Apply(Frame(ProtocolSlider.Around, int.MaxValue)));
        }
        [Test] public void PassengerUsesExactPackedLocalAndAbsoluteCoordinates()
        {
            var p = ProtocolSlider.PassengerPayload(7, 11, 22, 33, 100, 200, 300, 45, 1, 2, 3, 1200, 1);
            Assert.AreEqual(60, p.Length); Assert.AreEqual(11, Protocol0104.Read32(p, 8)); Assert.AreEqual(300, Protocol0104.Read32(p, 28));
            Assert.AreEqual(7, Protocol0104.Read32(p, 44)); Assert.AreEqual(45, Protocol0104.Read32(p, 48));
            var broadcast = new byte[72]; Buffer.BlockCopy(p, 0, broadcast, 0, p.Length);
            Protocol0104.Write32(broadcast, 60, 55); Protocol0104.Write64(broadcast, 64, 1234);
            Assert.IsTrue(ProtocolSlider.TryDecode(new ProtocolFrame(ProtocolSlider.PassengerMove, broadcast), out var updates));
            var rider = updates.Single().Entity; Assert.AreEqual(55, rider.Id); Assert.AreEqual(7, rider.TransportId);
            Assert.AreEqual(22, rider.LocalY); Assert.AreEqual(1234ul, rider.ServerTime);
        }
        [Test] public void SliderHasAcceptedVisualAndAuthoredCollision()
        {
            var prefab = NativeModelAssets.Load(ProtocolSlider.Model); Assert.IsNotNull(prefab);
            Assert.IsTrue(prefab.GetComponentsInChildren<Renderer>(true).Any());
            Assert.AreEqual(1, prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name == "collision"));
            var collision = NativeModelAssets.Load(ProtocolSlider.Model.Replace(".glb", ".collision.glb")); Assert.IsNotNull(collision);
            var mesh = collision.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
            Assert.AreEqual(134, mesh.vertexCount); Assert.AreEqual(531, mesh.triangles.Length);
            var native = prefab.GetComponentsInChildren<MeshCollider>(true).Single();
            Assert.IsTrue(native.enabled); Assert.IsFalse(native.convex); Assert.IsFalse(native.isTrigger);
            Assert.AreEqual(UnityEditor.AssetDatabase.GetAssetPath(prefab), UnityEditor.AssetDatabase.GetAssetPath(native.sharedMesh));
            CollectionAssert.AreEqual(mesh.triangles, native.sharedMesh.triangles);
            CollectionAssert.AreEqual(mesh.vertices, native.sharedMesh.vertices);
        }
        [TestCase(true)] [TestCase(false)]
        public void PassengerUsesBusPositionFromTheSameFrameRegardlessOfInterestArrivalOrder(bool passengerFirst)
        {
            var owner = new GameObject("Slider passenger update order");
            try
            {
                var replicas = owner.AddComponent<NetworkWorldEntities>();
                var views = (Dictionary<(WorldEntityKind, int), NetworkEntityView>)typeof(NetworkWorldEntities)
                    .GetField("views", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(replicas);
                ProtocolSlider.TryDecode(Frame(ProtocolSlider.Enter, 3, 7, 1, 0, 0, 0), out var buses);
                var payload = new byte[72];
                Protocol0104.Write32(payload, 8, 100); Protocol0104.Write32(payload, 12, 200); Protocol0104.Write32(payload, 16, 300);
                Protocol0104.Write32(payload, 44, 7); Protocol0104.Write32(payload, 60, 55);
                ProtocolSlider.TryDecode(new ProtocolFrame(ProtocolSlider.PassengerMove, payload), out var passengers);
                NetworkEntityView Add(WorldEntity entity)
                {
                    var actor = new GameObject(entity.Kind + " " + entity.Id); actor.transform.SetParent(owner.transform, false);
                    var view = actor.AddComponent<NetworkEntityView>();
                    typeof(NetworkEntityView).GetProperty(nameof(NetworkEntityView.Entity)).SetValue(view, entity);
                    views.Add((entity.Kind, entity.Id), view); return view;
                }
                NetworkEntityView bus, passenger;
                if (passengerFirst) { passenger = Add(passengers.Single().Entity); bus = Add(buses.Single().Entity); }
                else { bus = Add(buses.Single().Entity); passenger = Add(passengers.Single().Entity); }
                // A server correction above the snap threshold makes this independent of editor deltaTime.
                var destination = new Vector3(40, 2, 10);
                typeof(NetworkEntityView).GetField("Destination", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bus, destination);
                typeof(NetworkWorldEntities).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(replicas, null);
                Assert.That(bus.transform.position, Is.EqualTo(destination));
                Assert.That(passenger.transform.position, Is.EqualTo(bus.transform.TransformPoint(WorldCoordinates.FromServer(100, 200, 300))),
                    "A passenger must not trail its bus by one render frame when the player entered interest first.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
