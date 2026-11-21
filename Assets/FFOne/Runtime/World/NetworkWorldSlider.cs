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
            // The editable vehicle prefab owns its standard Unity colliders,
            // including their embedded mesh. No separate collision GLB is loaded.
            var colliders = node.GetComponentsInChildren<MeshCollider>(true);
            if (colliders.Length == 0 || colliders.Any(c => !c.sharedMesh || !c.enabled || c.isTrigger || c.convex))
            { ContentFailure("Slider native Unity collision is missing or invalid."); yield break; }
        }
    }

    public sealed partial class NetworkWorldController
    {
        NetworkEntityView slider;
        Transform support;
        Collider supportSurface;
        Matrix4x4 supportMatrix;
        float sliderContact;
        Vector3 passengerLocal;
        public int SliderId => slider && slider.gameObject.activeInHierarchy ? slider.Entity.Id : 0;
        public Vector3 SliderLocalPosition => SliderId != 0 ? slider.transform.InverseTransformPoint(Player.position) : Vector3.zero;

        void CarrySlider()
        {
            if (!support) return;
            if (!support.gameObject.activeInHierarchy || Riding || Jumping || Ziplining)
            { LeaveSlider(); return; }
            var currentMatrix = support.localToWorldMatrix;
            var destination = currentMatrix.MultiplyPoint3x4(slider ? passengerLocal : supportMatrix.inverse.MultiplyPoint3x4(Player.position));
            // A stationary controller or streaming pause can omit contact callbacks.
            // Test the next support pose so walking off still releases the character.
            if (!IsReady || slider && MoveDirection == 0) sliderContact = Time.unscaledTime;
            if (Time.unscaledTime - sliderContact > .2f)
            {
                if (!SupportedAt(destination))
                { LeaveSlider(); return; }
                sliderContact = Time.unscaledTime;
            }
            if (currentMatrix == supportMatrix) return;
            var delta = destination - Player.position;
            supportMatrix = currentMatrix;
            if (delta.sqrMagnitude > 0)
            {
                // The server advances the vehicle independently of PhysX. Sweeping
                // through its new mesh pose pushes the passenger off on fast segments.
                if (slider) NativePlayerMotor.Teleport(motor, destination);
                else motor.Move(delta);
                Stream.Anchor = Player.position;
                Moved?.Invoke(Player.position, yaw);
            }
        }
        bool SupportedAt(Vector3 feet)
        {
            var ray = new Ray(feet + Vector3.up * .4f, Vector3.down);
            return supportSurface && supportSurface.Raycast(ray, out var hit, .9f) && hit.normal.y >= .5f;
        }

        void UpdatePassengerFooting()
        {
            if (SupportedAt(Player.position)) passengerLocal = slider.transform.InverseTransformPoint(Player.position);
            else LeaveSlider();
        }

        internal void SliderContact(ControllerColliderHit hit)
        {
            if (hit.normal.y < .5f) return;
            var platform = hit.collider.GetComponentInParent<NetworkEntityView>();
            if (platform?.Entity?.Kind != WorldEntityKind.Transportation) platform = null;
            var next = platform ? platform.transform : hit.collider.GetComponentInParent<MovingWorldPlatform>()?.transform;
            if (!next) return;
            supportSurface = hit.collider;
            if (next != support)
            {
                slider = platform;
                support = next;
                if (slider) passengerLocal = next.InverseTransformPoint(Player.position);
                supportMatrix = next.localToWorldMatrix;
            }
            sliderContact = Time.unscaledTime;
        }
        void LeaveSlider() { slider = null; support = null; supportSurface = null; sliderContact = 0; }
    }
}
