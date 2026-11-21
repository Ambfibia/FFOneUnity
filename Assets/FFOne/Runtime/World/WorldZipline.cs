using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.World
{
    internal sealed class WorldZipline
    {
        internal static readonly List<WorldZipline> Loaded = new();
        internal readonly WorldBehaviour Owner;
        internal readonly Camera View;
        internal readonly Vector3 Start, End;
        internal readonly float Speed;
        readonly Matrix4x4 worldToTrigger;
        readonly Vector3 center, size, axis;
        readonly string shape;
        readonly float radius, halfSegment;

        internal WorldZipline(WorldBehaviour owner, Camera view, JToken route, JToken volume)
        {
            Owner = owner;
            View = view;
            var placement = owner.transform.localToWorldMatrix * WorldBehaviour.NativeMatrix(route["worldMatrix"]);
            worldToTrigger = placement.inverse;
            Start = placement.MultiplyPoint3x4(WorldBehaviour.NativeVector(route["from"]));
            End = placement.MultiplyPoint3x4(WorldBehaviour.NativeVector(route["to"]));
            Speed = Mathf.Abs((float?)route["velocity"] ?? 0);
            center = WorldBehaviour.NativeVector(volume["center"]);
            size = WorldBehaviour.Vector(volume["size"]);
            radius = Mathf.Max(0, (float?)volume["radius"] ?? 0);
            halfSegment = Mathf.Max(0, Mathf.Abs((float?)volume["height"] ?? 0) * .5f - radius);
            axis = ((int?)volume["direction"] ?? 1) switch { 0 => Vector3.right, 2 => Vector3.forward, _ => Vector3.up };
            shape = (string)volume["kind"];
        }

        internal bool Contains(Vector3 position)
        {
            var offset = worldToTrigger.MultiplyPoint3x4(position) - center;
            if (shape == "box")
                return Mathf.Abs(offset.x) <= Mathf.Abs(size.x) * .5f &&
                    Mathf.Abs(offset.y) <= Mathf.Abs(size.y) * .5f && Mathf.Abs(offset.z) <= Mathf.Abs(size.z) * .5f;
            if (shape == "capsule")
                offset -= axis * Mathf.Clamp(Vector3.Dot(offset, axis), -halfSegment, halfSegment);
            return offset.sqrMagnitude <= radius * radius;
        }

        internal static WorldZipline Find(Camera view, Vector3 position)
        {
            WorldZipline nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var route in Loaded)
            {
                if (route.View != view || !route.Owner || !route.Owner.isActiveAndEnabled ||
                    !route.Owner.Ready || !route.Contains(position)) continue;
                float distance = (route.Start - position).sqrMagnitude;
                if (distance < nearestDistance) { nearest = route; nearestDistance = distance; }
            }
            return nearest;
        }
    }
}
