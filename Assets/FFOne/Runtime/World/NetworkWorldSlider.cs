using System;
using System.Collections;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;

namespace FFOne.World
{
    public sealed partial class NetworkWorldEntities
    {
        IEnumerator LoadSlider((WorldEntityKind, int) key, NetworkEntityView view)
        {
            var request = NativeModelAssets.LoadAsync(ProtocolSlider.Model); requests[key] = request;
            yield return request;
            if (!views.TryGetValue(key, out var current) || current != view) yield break;
            if (!(request.asset is GameObject prefab)) { ContentFailure("Slider model cannot load."); yield break; }
            var model = Instantiate(prefab, view.transform, false);
            view.LegacyAnimation = model.GetComponentInChildren<Animation>(); view.Animate("stand1");
            var node = model.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "collision");
            if (!node) { ContentFailure("Slider source collision node is missing."); yield break; }
            var collision = NativeModelAssets.Load(ProtocolSlider.Model.Replace(".glb", ".collision.glb"));
            if (!collision) { ContentFailure("Slider authored collision cannot load."); yield break; }
            var shape = Instantiate(collision, node, false);
            foreach (var renderer in shape.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var filter in shape.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (!mesh || mesh.vertexCount != 134 || mesh.triangles.Length != 531)
                { ContentFailure("Slider collision differs from the accepted source."); yield break; }
                var collider = filter.GetComponent<MeshCollider>() ?? filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh; collider.convex = false; collider.isTrigger = false;
            }
        }
    }

    public sealed partial class NetworkWorldController
    {
        NetworkEntityView slider;
        Vector3 sliderPosition;
        float sliderContact;
        public int SliderId => slider && slider.gameObject.activeInHierarchy ? slider.Entity.Id : 0;
        public Vector3 SliderLocalPosition => SliderId != 0 ? slider.transform.InverseTransformPoint(Player.position) : Vector3.zero;

        void CarrySlider()
        {
            if (!slider) return;
            if (!slider.gameObject.activeInHierarchy || Riding || MapNumber != 0 || Time.unscaledTime - sliderContact > .2f)
            { LeaveSlider(); return; }
            var delta = slider.transform.position - sliderPosition; sliderPosition = slider.transform.position;
            if (delta.sqrMagnitude > 0)
            {
                motor.Move(delta); Stream.Anchor = Player.position;
                Moved?.Invoke(Player.position, yaw);
            }
        }
        internal void SliderContact(ControllerColliderHit hit)
        {
            var platform = hit.collider.GetComponentInParent<NetworkEntityView>();
            if (platform?.Entity?.Kind != WorldEntityKind.Transportation || hit.normal.y < .5f) return;
            if (slider != platform) { slider = platform; sliderPosition = platform.transform.position; }
            sliderContact = Time.unscaledTime;
        }
        void LeaveSlider() { slider = null; sliderContact = 0; }
    }
}
