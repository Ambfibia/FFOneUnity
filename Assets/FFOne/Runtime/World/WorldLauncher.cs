using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.World
{
    internal sealed class WorldLauncher
    {
        internal static readonly List<WorldLauncher> Loaded = new();
        internal readonly WorldBehaviour Owner;
        internal readonly Camera View;
        internal readonly Vector3 Center, InitialRotation, MaximumRotation;
        internal readonly float Radius, MinPower, MaxPower;
        internal readonly int ObjectId;
        internal WorldLauncher(WorldBehaviour owner, Camera view, JToken record)
        {
            Owner = owner;
            View = view;
            var placement = owner.transform.localToWorldMatrix * WorldBehaviour.NativeMatrix(record["worldMatrix"]);
            Center = placement.MultiplyPoint3x4(WorldBehaviour.NativeVector(record["startPosition"]));
            Radius = (float)record["radius"];
            InitialRotation = placement.rotation.eulerAngles + WorldBehaviour.Vector(record["initialRotation"]);
            MaximumRotation = WorldBehaviour.Vector(record["maxRotation"]);
            MinPower = (float)record["minPower"];
            MaxPower = (float)record["maxPower"];
            ObjectId = (int)record["objectId"];
        }

        internal WorldLauncher(WorldBehaviour owner, Camera view, WorldLauncherPlacement placement)
        {
            Owner = owner;
            View = view;
            Center = owner.transform.TransformPoint(placement.Center);
            InitialRotation = new Vector3(0, owner.transform.eulerAngles.y + placement.Heading, 0);
            MaximumRotation = new Vector3(placement.AimLimits.x, placement.AimLimits.y, 0);
            Radius = placement.Radius;
            MinPower = placement.MinimumPower;
            MaxPower = placement.MaximumPower;
            ObjectId = placement.ObjectId;
        }

        internal static WorldLauncher Find(Camera view, Vector3 position)
        {
            WorldLauncher closest = null;
            float best = float.PositiveInfinity;
            foreach (var launcher in Loaded)
            {
                if (launcher.View != view || !launcher.Owner || !launcher.Owner.Ready) continue;
                float distance = (launcher.Center - position).sqrMagnitude;
                if (distance > launcher.Radius * launcher.Radius || distance >= best) continue;
                closest = launcher;
                best = distance;
            }
            return closest;
        }
    }
}
