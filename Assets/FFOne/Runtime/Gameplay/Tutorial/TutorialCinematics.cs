using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static FFOne.Tutorial.TutorialCinematicExpressions;

namespace FFOne.Tutorial
{
    /// <summary>Ordered playback of the ten finite cntutorialscript coroutines.</summary>
    public sealed partial class TutorialCinematics : IDisposable
    {
        readonly TutorialController owner;
        readonly TutorialCinematicExpressions expressions;
        readonly Dictionary<TutorialScene, JObject> scenes;
        readonly List<Sequence> sequences = new();
        readonly Dictionary<int, Move> moves = new();
        JObject definition;
        JArray actions;
        TutorialAudioCue[] audio;
        int cursor, audioCursor;
        float elapsed;
        bool skipped;
        public float Duration => (float?)definition?["deterministic_duration_seconds"] ?? 0;
        public bool Finished { get; private set; } = true;
        public float Elapsed => elapsed;
        public TutorialScene Scene { get; private set; }
        public Vector3 ResolvePosition(JToken expression) => expressions.Position(expression);

        public TutorialCinematics(TutorialController owner)
        {
            this.owner = owner;
            expressions = new TutorialCinematicExpressions(owner);
            var data = FFOne.Content.NativeData.Object("data/tutorial/cinematics");
            if ((string)data["schema"] != "ffone.unity-tutorial-cinematics.v1") throw new InvalidOperationException("Unknown cinematic schema.");
            scenes = data["scenes"].OfType<JObject>().ToDictionary(s => Enum.Parse<TutorialScene>(Kind(s["scene"])));
        }

        public void Begin(TutorialScene scene)
        {
            if (!scenes.TryGetValue(scene, out definition)) throw new InvalidOperationException("No finite tutorial scene: " + scene);
            Scene = scene; actions = (JArray)definition["actions"];
            TutorialAudioTimeline.Cues.TryGetValue(scene, out audio);
            cursor = audioCursor = 0; elapsed = 0; Finished = false; skipped = false;
            sequences.Clear(); moves.Clear(); expressions.Clear(); ResetCamera();
            Tick(0);
        }

        public void Tick(float seconds)
        {
            if (Finished) return;
            float target = Mathf.Min(Duration, elapsed + Mathf.Max(0, seconds));
            // Resolve each authored point in order even after a long frame. Live
            // captures and spawns must see the state at their own action time.
            while (true)
            {
                float actionTime = cursor < actions.Count ? (float)actions[cursor]["time"] : float.PositiveInfinity;
                float audioTime = audio != null && audioCursor < audio.Length ? audio[audioCursor].Seconds : float.PositiveInfinity;
                float at = Mathf.Min(actionTime, audioTime);
                if (at > target + .00001f) break;
                Advance(Mathf.Max(0, at - elapsed)); elapsed = at;
                if (actionTime <= audioTime) Dispatch(actions[cursor++]["action"]);
                else
                {
                    var cue = audio[audioCursor++];
                    if (cue.Kind == 0) owner.PlayVoice(cue.Cue);
                    else if (cue.Kind == 3) owner.StopLoops();
                    else owner.PlaySound(cue.Cue, cue.Kind == 2);
                }
            }
            Advance(Mathf.Max(0, target - elapsed)); elapsed = target;
            if (elapsed >= Duration) { Finished = true; sequences.Clear(); moves.Clear(); }
        }

        public void Skip()
        {
            if (Finished || skipped) return;
            skipped = true; sequences.Clear(); moves.Clear();
            foreach (var action in definition["skip"]["scene_specific"]) Dispatch(action);
            foreach (var action in definition["skip"]["common_cleanup"]) Dispatch(action);
            foreach (var loop in definition["loops"]) Dispatch(new JObject { ["type"] = "Loop", ["args"] = new JArray(loop["skip_action"].DeepClone()) });
            owner.SetFade(Kind(definition["skip"]["final_fade"]) == "OpaqueOverlay" ? 1 : 0);
            owner.SetBars(0); elapsed = Duration; Finished = true;
        }

        void Dispatch(JToken action)
        {
            var value = Arg(action);
            switch (Kind(action))
            {
                case "Npc": Npc(value); break;
                case "Player": Player(value); break;
                case "Camera": Camera(value); break;
                case "Sequence": sequences.Add(new Sequence(value, elapsed)); SampleSequences(elapsed); break;
                case "Loop":
                    // The native presenter owns natural scene audio; loop actions
                    // also carry the independent lifecycle contract for Skip.
                    if (!skipped) break;
                    if (Kind(value) == "StopIfPresent") owner.StopLoops();
                    else owner.PlaySound((string)Arg(value), true);
                    break;
                case "Sound":
                    // These one-shots are already on the source presenter audio
                    // timeline. Ambient changes retain their separate ownership.
                    if (Kind(value) == "Ambient") owner.ApplyCinematicAction("Ambient", value);
                    break;
                case "SubtitleClear": owner.Subtitle(""); break;
                case "GameFadeIn": owner.SetFade(0); break;
                case "Cinematic": if (!(bool)value) owner.SetBars(0); break;
                case "FadeEnabled": if (!(bool)value) owner.SetFade(0); break;
                case "Pan": owner.ApplyCinematicAction("Pan", value); break;
                // Gameplay presentation and exact effect prefabs share the
                // tutorial controller's ownership and cleanup boundaries.
                case "Effect": case "Projectile": case "Nano": case "Equipment": case "Progress":
                case "Picture": case "Hud": case "SpatialAudio": case "EventScene":
                case "VoiceOff": case "StopBgm": owner.ApplyCinematicAction(Kind(action), value); break;
                default: throw new InvalidOperationException("Unsupported cinematic action: " + action);
            }
        }

        void Spawn(JToken spawn)
        {
            int id = (int)spawn["id"];
            float yaw = spawn["helper_angle_degrees"].Type == JTokenType.Null ? 0 : (float)spawn["helper_angle_degrees"] + 180;
            var actor = owner.SpawnActor(id, (int)spawn["npc_type"], Vector(spawn["position"]), yaw);
            if (spawn["initial_animation"].Type != JTokenType.Null) Animate(actor, (string)spawn["initial_animation"], false);
        }
        GameObject RequiredActor(int id) => owner.Actor(id) ?? throw new InvalidOperationException("Cinematic actor is absent: " + id);
        void Npc(JToken action)
        {
            int id = (int?)action["id"] ?? 0;
            switch (Kind(action))
            {
                case "Spawn": Spawn(Arg(action)); return;
                case "SpawnBatch": foreach (var spawn in Arg(action)) Spawn(spawn); return;
                case "Delete": owner.RemoveActor((int)Arg(action)); moves.Remove((int)Arg(action)); return;
                case "DeleteInclusive":
                    for (int n = (int)action["first"]; n <= (int)action["last"]; n++) { owner.RemoveActor(n); moves.Remove(n); } return;
                case "AnimationInclusive":
                    for (int n = (int)action["first"]; n <= (int)action["last"]; n++) if (owner.Actor(n)) Animate(owner.Actor(n), (string)action["clip"], (bool)action["once"]); return;
            }
            var actor = RequiredActor(id); var transform = actor.transform;
            switch (Kind(action))
            {
                case "Angle": transform.rotation = Quaternion.Euler(0, (float)action["helper_degrees"] + 180, 0); break;
                case "WarpServer": transform.position = Vector(action["target"]); moves.Remove(id); break;
                case "WarpClient": case "SetPosition": transform.position = expressions.Position(action["target"]); moves.Remove(id); break;
                case "SetRotation": transform.rotation = expressions.Rotation(action["rotation"], transform.position); break;
                case "Animation": Animate(actor, (string)action["clip"], (bool)action["once"]); break;
                case "Move":
                    moves[id] = new Move { Actor = transform, Target = expressions.Position(action["target"]), Speed = (float)action["speed"] / 100 };
                    Animate(actor, (float)action["speed"] > 400 ? "run" : "walk", false); break;
                case "Command":
                    var command = action["command"];
                    switch (Kind(command))
                    {
                        case "ForceStop": case "Pause": moves.Remove(id); break;
                        case "Dead": moves.Remove(id); Animate(actor, "death", true); break;
                        case "ForceUpdate": break;
                        case "SetAngleTo": transform.rotation = Look(transform.position, expressions.Entity(Arg(command)).position); break;
                        case "ForceAnimation": Animate(actor, (string)Arg(command), false); break;
                        default: throw new InvalidOperationException("Unknown NPC command: " + command);
                    }
                    break;
                default: throw new InvalidOperationException("Unknown NPC action: " + action);
            }
        }

        void Player(JToken action)
        {
            var player = owner.Player;
            switch (Kind(action))
            {
                case "Warp": owner.WarpPlayer(expressions.Position(Arg(action))); break;
                case "Animation":
                    player.GetComponent<TutorialPlayerEquipment>()?.SetVisible(false);
                    Animate(player.gameObject, (string)Arg(action), true);
                    break;
                case "StandForce":
                    player.GetComponent<TutorialPlayerEquipment>()?.SetVisible(true);
                    Animate(player.gameObject, "stand1", false); break;
                case "Face": owner.FacePlayer(Look(player.position, expressions.EntityPosition(Arg(action)))); break;
                case "FacePosition": owner.FacePlayer(Look(player.position, expressions.Position(Arg(action)))); break;
                case "FaceAngle": owner.FacePlayer(Quaternion.Euler(0, (float)Arg(action), 0)); break;
                case "Hide": case "Show":
                    foreach (var renderer in player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = Kind(action) == "Show"; break;
                case "SetTemporaryNanoAbsent": owner.ApplyCinematicAction("Player", action); break;
                default: throw new InvalidOperationException("Unknown player action: " + action);
            }
        }

        public static bool Animate(GameObject actor, string clip, bool once)
        {
            if (!actor) return false;
            var player = actor.GetComponentInChildren<FFOne.Content.NativePlayerAnimator>(true);
            if (player) return player.Play(clip, once);
            foreach (var animation in actor.GetComponentsInChildren<Animation>(true))
            {
                var state = animation[clip];
                if (state == null) continue;
                state.wrapMode = once ? WrapMode.Once : WrapMode.Loop; animation.CrossFade(clip, .1f); return true;
            }
            // Missing named clips must be visible during reconstruction; never
            // silently substitute a different animation with a similar name.
            Debug.LogWarning($"Tutorial animation '{clip}' missing on '{actor.name}'."); return false;
        }
        public void Dispose() { Finished = true; sequences.Clear(); moves.Clear(); expressions.Clear(); }
    }
}
