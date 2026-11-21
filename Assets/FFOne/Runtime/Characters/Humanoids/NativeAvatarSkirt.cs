using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        // Emote clips hold the skirt helpers at their bind rotations while moving both thighs.
        // Tilt each outward-facing panel only as far as the nearer knee pushes toward it.
        sealed class SkirtPose
        {
            SkinnedMeshRenderer fabric, legs;
            Mesh fabricBake, legBake;
            readonly List<Vector3> fabricPoints = new(), legPoints = new();
            readonly Transform pelvis, leftThigh, rightThigh, leftCalf, rightCalf;
            readonly Vector3 down, leftRest, rightRest;
            readonly (Transform bone, Quaternion rotation, Vector3 position, Vector3 outward, int side)[] panels;

            SkirtPose(Transform pelvis, Transform leftThigh, Transform rightThigh, Transform leftCalf,
                Transform rightCalf, Transform[] helpers)
            {
                this.pelvis = pelvis;
                this.leftThigh = leftThigh;
                this.rightThigh = rightThigh;
                this.leftCalf = leftCalf;
                this.rightCalf = rightCalf;
                leftRest = Direction(leftThigh, leftCalf);
                rightRest = Direction(rightThigh, rightCalf);
                down = (leftRest + rightRest).normalized;
                panels = helpers.Select(bone => {
                    var radial = Vector3.ProjectOnPlane(bone.localPosition, down).normalized;
                    int side = bone.name.EndsWith("left", StringComparison.Ordinal) || bone.name.EndsWith("[L]", StringComparison.Ordinal) ? -1 :
                        bone.name.EndsWith("right", StringComparison.Ordinal) || bone.name.EndsWith("[R]", StringComparison.Ordinal) ? 1 : 0;
                    return (bone, bone.localRotation, bone.localPosition, radial, side);
                }).ToArray();
            }

            internal static SkirtPose FromRig(IReadOnlyDictionary<string, Transform> rig)
            {
                var pelvis = rig.Values.FirstOrDefault(b => b.name == "Bip01 Pelvis");
                if (!pelvis) return null;
                var helpers = pelvis.Cast<Transform>().Where(b => b.name.StartsWith("Bip01 Skirt ", StringComparison.Ordinal)).ToArray();
                if (helpers.Length == 0) return null;
                var leftThigh = pelvis.Find("Bip01 L Thigh");
                var rightThigh = pelvis.Find("Bip01 R Thigh");
                var leftCalf = leftThigh ? leftThigh.Find("Bip01 L Calf") : null;
                var rightCalf = rightThigh ? rightThigh.Find("Bip01 R Calf") : null;
                return leftCalf && rightCalf ? new SkirtPose(pelvis, leftThigh, rightThigh, leftCalf, rightCalf, helpers) : null;
            }

            internal void BindGarment(IEnumerable<GameObject> parts)
            {
                fabric = null;
                legs = null;
                foreach (var part in parts)
                {
                    var skins = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    fabric = skins.FirstOrDefault(r => r.bones.Any(b => b && b.name.StartsWith("Bip01 Skirt ", StringComparison.Ordinal)));
                    if (!fabric) continue;
                    legs = skins.FirstOrDefault(r => r != fabric && r.sharedMaterial &&
                        r.sharedMaterial.name.IndexOf("sub", StringComparison.OrdinalIgnoreCase) >= 0);
                    break;
                }
            }

            void Sample(SkinnedMeshRenderer source, Mesh bake, List<Vector3> points)
            {
                source.BakeMesh(bake);
                bake.GetVertices(points);
                var worldToPelvis = pelvis.worldToLocalMatrix * source.transform.localToWorldMatrix;
                for (int i = 0; i < points.Count; ++i) points[i] = worldToPelvis.MultiplyPoint3x4(points[i]);
            }

            internal void Dispose()
            {
                if (fabricBake) Release(fabricBake);
                if (legBake) Release(legBake);
            }

            Vector3 Direction(Transform hip, Transform knee) =>
                pelvis.InverseTransformDirection(knee.position - hip.position).normalized;

            internal void FollowThighs()
            {
                if (!fabric || !legs) return;
                var left = Direction(leftThigh, leftCalf);
                var right = Direction(rightThigh, rightCalf);
                foreach (var panel in panels)
                {
                    float Tilt(Vector3 leg, Vector3 rest) =>
                        Mathf.Atan2(Vector3.Dot(leg, panel.outward), Vector3.Dot(leg, down)) -
                        Mathf.Atan2(Vector3.Dot(rest, panel.outward), Vector3.Dot(rest, down));
                    float leftTilt = Tilt(left, leftRest), rightTilt = Tilt(right, rightRest);
                    bool followLeft = panel.side < 0 || panel.side == 0 && leftTilt >= rightTilt;
                    var thigh = followLeft ? leftThigh : rightThigh;
                    float tilt = followLeft ? leftTilt : rightTilt;
                    var rotation = panel.side != 0
                        ? Quaternion.FromToRotation(followLeft ? leftRest : rightRest, followLeft ? left : right)
                        : Quaternion.AngleAxis(Mathf.Max(0, tilt * Mathf.Rad2Deg), Vector3.Cross(down, panel.outward));
                    // The helpers start below the anatomical hip. Rotating around their own pivot
                    // leaves the fabric above that pivot behind the raised thigh.
                    panel.bone.localPosition = thigh.localPosition + rotation * (panel.position - thigh.localPosition);
                    panel.bone.localRotation = rotation * panel.rotation;
                }
                fabricBake ??= new Mesh();
                legBake ??= new Mesh();
                Sample(fabric, fabricBake, fabricPoints);
                Sample(legs, legBake, legPoints);
                float top = float.PositiveInfinity, bottom = float.NegativeInfinity;
                foreach (var point in fabricPoints)
                {
                    float depth = Vector3.Dot(point, down);
                    top = Mathf.Min(top, depth);
                    bottom = Mathf.Max(bottom, depth);
                }
                foreach (var panel in panels)
                {
                    float clothRadius = 0, legRadius = 0;
                    foreach (var p in fabricPoints) clothRadius = Mathf.Max(clothRadius, Vector3.Dot(p, panel.outward));
                    foreach (var p in legPoints)
                    {
                        float depth = Vector3.Dot(p, down);
                        if (depth >= top && depth <= bottom) legRadius = Mathf.Max(legRadius, Vector3.Dot(p, panel.outward));
                    }
                    if (legRadius > clothRadius)
                        panel.bone.localPosition += panel.outward * (legRadius - clothRadius + .008f);
                }
            }
        }
    }
}
