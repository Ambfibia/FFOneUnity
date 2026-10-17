using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    // The published choreography is in original Unity metres. Reflecting native
    // GLB geometry is the importer's job; do not reflect these expressions twice.
    public sealed class TutorialCinematicExpressions
    {
        readonly TutorialController owner;
        readonly Dictionary<string, Pose> captures = new();
        public TutorialCinematicExpressions(TutorialController owner) { this.owner = owner; }
        public static string Kind(JToken t) => (string)t?["type"] ?? "";
        public static JToken Arg(JToken t, int index = 0) => t?["args"]?[index];
        public static Vector3 Vector(JToken t) => new((float)t[0], (float)t[1], (float)t[2]);
        public void Clear() => captures.Clear();
        public void Capture(string slot) => captures[slot] = new Pose(owner.ViewCamera.transform.position, owner.ViewCamera.transform.rotation);
        public Transform Entity(JToken reference)
        {
            Transform found = Kind(reference) switch {
                "Player" => owner.Player,
                "Camera" => owner.ViewCamera.transform,
                "Npc" => owner.Actor((int)Arg(reference))?.transform,
                "NewNano" => owner.NanoTransform,
                _ => null
            };
            if (!found) throw new InvalidOperationException("Missing cinematic entity: " + reference);
            return found;
        }
        public Vector3 EntityPosition(JToken reference) => Kind(reference) == "StartPosition"
            ? TutorialController.StartPosition : Entity(reference).position;
        public Vector3 Position(JToken expression)
        {
            if (expression is JArray) return Vector(expression);
            return Kind(expression) switch {
                "Client" => Vector(Arg(expression)),
                "Entity" => EntityPosition(Arg(expression)),
                "EntityOffset" => EntityPosition(expression["entity"]) + Vector(expression["offset"]),
                "Midpoint" => (EntityPosition(expression["left"]) + EntityPosition(expression["right"])) / 2 + (expression["offset"] == null ? Vector3.zero : Vector(expression["offset"])),
                "CapturedCamera" => CapturePose(Kind(Arg(expression))).position,
                "OrientedOffset" => Position(expression["origin"]) + Vector(expression["world_offset"]) + Basis(expression["orientation"]) * Vector(expression["local_offset"]),
                _ => throw new InvalidOperationException("Unsupported cinematic position: " + expression)
            };
        }
        Pose CapturePose(string slot) => captures.TryGetValue(slot, out var pose) ? pose : throw new InvalidOperationException("Camera capture absent: " + slot);
        Quaternion Basis(JToken expression) => Kind(expression) switch {
            "Euler" => Quaternion.Euler(Vector(Arg(expression))),
            "Entity" => Entity(Arg(expression)).rotation,
            "CapturedCameraYaw" => Quaternion.Euler(0, CapturePose(Kind(Arg(expression))).rotation.eulerAngles.y, 0),
            _ => throw new InvalidOperationException("Unsupported cinematic basis: " + expression)
        };
        public Quaternion Rotation(JToken expression, Vector3 origin)
        {
            switch (Kind(expression))
            {
                case "Euler": return Quaternion.Euler(Vector(Arg(expression)));
                case "FaceEntity": return Look(origin, EntityPosition(Arg(expression)));
                case "FacePosition": return Look(origin, Position(Arg(expression)));
                case "EntityYaw": return Quaternion.Euler((float)expression["pitch"], Entity(expression["entity"]).eulerAngles.y + (float)expression["yaw_offset"], (float)expression["roll"]);
                case "LookAtYaw": return Quaternion.Euler((float)expression["pitch"], Look(origin, Vector(expression["target"])).eulerAngles.y + (float)expression["yaw_offset"], (float)expression["roll"]);
                default: throw new InvalidOperationException("Unsupported cinematic rotation: " + expression);
            }
        }
        public static Quaternion Look(Vector3 origin, Vector3 target)
        {
            var direction = target - origin;
            return direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        }
    }
}
