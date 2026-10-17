using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static FFOne.Tutorial.TutorialCinematicExpressions;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialCinematics
    {
        sealed class Move { public Transform Actor; public Vector3 Target; public float Speed; }
        sealed class Sequence
        {
            public readonly JToken Data;
            public readonly float Start;
            public int LastFrame = -1;
            public Vector3 Origin;
            public bool CapturedOrigin;
            public Sequence(JToken data, float start) { Data = data; Start = start; }
        }
        string cameraMode = "None";
        Vector3 cameraStart, cameraTarget, cameraCurrentTarget, cameraPosition, shakeStart, shakeTarget;
        Quaternion cameraRotation;
        float cameraDistance = 5;
        bool forward = true, hasStart, hasTarget;

        void ResetCamera()
        {
            cameraMode = "None"; hasStart = hasTarget = false; forward = true; cameraDistance = 5;
            var transform = owner.ViewCamera.transform;
            cameraPosition = cameraStart = transform.position; cameraRotation = transform.rotation;
            cameraTarget = cameraCurrentTarget = transform.position + transform.forward * 5;
            shakeStart = shakeTarget = Vector3.zero;
        }

        void Camera(JToken action)
        {
            var arg = Arg(action);
            switch (Kind(action))
            {
                case "Mode": cameraMode = Kind(arg); cameraPosition = owner.ViewCamera.transform.position; break;
                case "CurrentTarget": cameraCurrentTarget = expressions.Position(arg); break;
                case "Target": cameraTarget = expressions.Position(arg); hasTarget = true; shakeTarget = Vector3.zero; break;
                case "FreezeCurrentTarget": cameraTarget = cameraCurrentTarget; hasTarget = true; shakeTarget = Vector3.zero; break;
                case "Start": cameraStart = expressions.Position(arg); hasStart = true; shakeStart = Vector3.zero; break;
                case "StoredStart": cameraPosition = expressions.Position(arg); break;
                case "TargetRotation": cameraRotation = expressions.Rotation(arg, owner.ViewCamera.transform.position); break;
                case "Distance": cameraDistance = (float)arg; break;
                case "ForwardInterpolation": forward = (bool)arg; break;
                case "LookAt": owner.ViewCamera.transform.rotation = Look(owner.ViewCamera.transform.position, expressions.Position(arg)); break;
                case "CaptureTransform": expressions.Capture(Kind(arg)); break;
                default: throw new InvalidOperationException("Unknown cinematic camera action: " + action);
            }
        }

        void Advance(float delta)
        {
            foreach (var pair in moves.ToArray())
            {
                var motion = pair.Value;
                if (!motion.Actor) { moves.Remove(pair.Key); continue; }
                var direction = motion.Target - motion.Actor.position;
                if (direction.sqrMagnitude > .0001f)
                {
                    direction.y = 0;
                    if (direction.sqrMagnitude > .0001f) motion.Actor.rotation = Quaternion.LookRotation(direction);
                    motion.Actor.position = Vector3.MoveTowards(motion.Actor.position, motion.Target, motion.Speed * delta);
                }
                if ((motion.Target - motion.Actor.position).sqrMagnitude < .0001f) moves.Remove(pair.Key);
            }
            SampleSequences(elapsed + delta);
            float amount = Mathf.Clamp01(delta);
            if (cameraMode == "FromToCamera")
            {
                if (hasStart) cameraPosition = forward ? Vector3.Lerp(cameraPosition, cameraStart, amount) : cameraStart;
                if (hasTarget) cameraCurrentTarget = forward ? Vector3.Lerp(cameraCurrentTarget, cameraTarget, amount) : cameraTarget;
            }
            else if (cameraMode == "EventCameraControl" && hasTarget)
            {
                if (forward)
                {
                    cameraPosition = Vector3.Lerp(cameraPosition, cameraTarget - cameraRotation * Vector3.forward * cameraDistance, amount);
                    cameraCurrentTarget = cameraTarget;
                }
                else
                {
                    cameraCurrentTarget = Vector3.Lerp(cameraCurrentTarget, cameraTarget, amount);
                    cameraPosition = cameraCurrentTarget - cameraRotation * Vector3.forward * cameraDistance;
                }
            }
            if (cameraMode != "None")
            {
                var transform = owner.ViewCamera.transform;
                transform.position = cameraPosition + shakeStart;
                transform.rotation = Look(transform.position, cameraCurrentTarget + shakeTarget);
            }
        }

        void SampleSequences(float time)
        {
            foreach (var sequence in sequences.ToArray())
            {
                var data = sequence.Data;
                string kind = Kind(data);
                if (kind == "Fade") data = Arg(data);
                float step = (float?)data["seconds_per_frame"] ?? (float?)data["seconds_per_step"] ?? (float?)data["seconds_per_character"] ?? 1;
                int count = (int?)data["frames"] ?? (int?)data["steps"] ?? (int?)data["canonical_english_chars"] ?? 1;
                int frame = step <= 0 ? count - 1 : Mathf.Clamp(Mathf.FloorToInt((time - sequence.Start + .000001f) / step), 0, count - 1);
                if (frame != sequence.LastFrame)
                {
                    switch (kind)
                    {
                        case "Fade":
                            float value = Mathf.LerpUnclamped((float)data["from"], (float)data["to"], frame / (float)data["divisor"]);
                            switch (Kind(data["channel"]))
                            {
                                case "Overlay": owner.SetFade(Mathf.Clamp01(value)); break;
                                case "CinematicBars": owner.SetBars(Mathf.Clamp01(value)); break;
                                case "Subtitle": owner.SetSubtitleOpacity(Mathf.Clamp01(value)); break;
                            }
                            break;
                        case "SubtitleTypewriter":
                            owner.Subtitle((string)data["localization_key"]);
                            owner.SetSubtitleCharacters((int)data["first_character"] + frame, (int)data["canonical_english_chars"]);
                            break;
                        case "NpcDelta":
                            var actor = RequiredActor((int)data["id"]).transform;
                            if (!sequence.CapturedOrigin) { sequence.Origin = actor.position; sequence.CapturedOrigin = true; }
                            actor.position = sequence.Origin + Vector(data["translation_per_frame"]) * (frame + 1);
                            if (data["rotation_each_frame"].Type != JTokenType.Null) actor.rotation = expressions.Rotation(data["rotation_each_frame"], actor.position);
                            break;
                        case "NpcLerp":
                            RequiredActor((int)data["id"]).transform.position = Vector3.LerpUnclamped(Vector(data["from"]), Vector(data["to"]), (frame + (int)data["numerator_offset"]) / (float)data["denominator"]);
                            break;
                        case "CameraShake":
                            float sampleTime = sequence.Start + frame * step;
                            shakeStart = Shake(data["start_wave"], sampleTime, (float)data["amplitude"]);
                            shakeTarget = Shake(data["target_wave"], sampleTime, (float)data["amplitude"]);
                            break;
                        case "PlayerPathAndFade":
                            owner.WarpPlayer(Vector(data["start"]) + Vector(data["translation_per_frame"]) * frame);
                            owner.SetFade(Mathf.Clamp01((float)data["fade_from"] + frame * (float)data["fade_numerator_per_frame"] / (float)data["fade_divisor"]));
                            break;
                        default: throw new InvalidOperationException("Unknown cinematic frame sequence: " + sequence.Data);
                    }
                    sequence.LastFrame = frame;
                }
                if (time - sequence.Start >= count * step) sequences.Remove(sequence);
            }
        }
        static Vector3 Shake(JToken wave, float time, float amplitude) => amplitude * new Vector3(
            Mathf.Sin(time * (float)wave["x_sin_multiplier"]),
            Mathf.Cos(time * (float)wave["y_cos_multiplier"]),
            Mathf.Sin(time * (float)wave["z_sin_multiplier"]) + Mathf.Cos(time * (float)wave["z_cos_multiplier"]));
    }
}
