using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Playables;
using static FFOne.Tutorial.TutorialCinematicExpressions;

namespace FFOne.Tutorial
{
    /// <summary>Ordered playback of the ten finite cntutorialscript coroutines.</summary>
    public sealed partial class TutorialCinematics : IDisposable
    {
        readonly TutorialController owner;
        readonly TutorialCinematicExpressions expressions;
        readonly Dictionary<TutorialScene, TutorialTimelineScene> scenes;
        readonly Dictionary<int, Move> moves = new();
        readonly List<TutorialActionBehaviour> clips = new();
        readonly List<Playable> tracks = new();
        TutorialTimelineScene definition;
        JToken skipActions, loops;
        UnityEngine.Playables.PlayableDirector director;
        GameObject directorHost;
        double sampledTime;
        Exception timelineFailure;
        bool skipped, sortClips;
        double TimelineDuration => definition ? definition.Timeline.duration : 0;
        public float Duration => (float)TimelineDuration;
        public bool Finished { get; private set; } = true;
        internal double TimelineTime => director && director.playableGraph.IsValid()
            ? director.playableGraph.GetRootPlayable(0).GetTime() : Finished ? TimelineDuration : 0;
        public float Elapsed => (float)Math.Min(TimelineDuration, TimelineTime);
        internal UnityEngine.Playables.PlayableDirector Director => director;
        internal IReadOnlyList<TutorialActionBehaviour> TimelineClips => clips;
        public TutorialScene Scene { get; private set; }
        public Vector3 ResolvePosition(JToken expression) => expressions.Position(expression);

        public TutorialCinematics(TutorialController owner) : this(owner,
            FFOne.Content.ContentAssets.Load<TutorialContent>("Tutorial/Content").Cinematics) { }
        internal TutorialCinematics(TutorialController owner, IEnumerable<TutorialTimelineScene> authored)
        {
            this.owner = owner;
            expressions = new TutorialCinematicExpressions(owner);
            scenes = authored.ToDictionary(s => s.Scene);
            if (scenes.Count == 0) throw new InvalidOperationException("Publish native tutorial Timelines before playback.");
        }
        public void Begin(TutorialScene scene)
        {
            presentation?.Dispose(); presentation = null;
            Finished = true; DestroyDirector();
            if (!scenes.TryGetValue(scene, out definition)) throw new InvalidOperationException("No finite tutorial scene: " + scene);
            if (!definition.Timeline || !definition.Skip || !definition.Loops) throw new InvalidOperationException("Incomplete tutorial Timeline: " + scene);
            skipActions = TutorialActionData.Read(definition.Skip); loops = TutorialActionData.Read(definition.Loops);
            Scene = scene; sampledTime = 0; timelineFailure = null; Finished = false; skipped = false;
            clips.Clear(); tracks.Clear(); moves.Clear(); expressions.Clear(); ResetCamera();
            directorHost = new GameObject("Tutorial Timeline · " + scene);
            directorHost.transform.SetParent(owner.transform, false);
            var binding = directorHost.AddComponent<TutorialTimelineBinding>(); binding.Runtime = this;
            director = directorHost.AddComponent<UnityEngine.Playables.PlayableDirector>();
            director.playOnAwake = false;
            director.timeUpdateMode = UnityEngine.Playables.DirectorUpdateMode.Manual;
            director.extrapolationMode = UnityEngine.Playables.DirectorWrapMode.Hold;
            director.playableAsset = definition.Timeline;
            foreach (var track in definition.Timeline.GetOutputTracks())
                if (track is TutorialActionTrack) director.SetGenericBinding(track, binding);
            director.RebuildGraph();
            var graph = director.playableGraph;
            var timeline = graph.GetRootPlayable(0);
            // Default Timeline outputs split the tracks. A single script mixer
            // preserves the ordering of stateful commands across those tracks.
            for (int output = graph.GetOutputCount() - 1; output >= 0; output--)
                if (graph.GetOutput(output).GetReferenceObject() is TutorialActionTrack) graph.DestroyOutput(graph.GetOutput(output));
            var mixer = ScriptPlayable<TutorialSceneBehaviour>.Create(graph, tracks.Count + 1);
            mixer.GetBehaviour().Runtime = this;
            mixer.SetDuration(definition.Timeline.duration); mixer.SetPropagateSetTime(true);
            if (!graph.Connect(timeline, 0, mixer, 0)) throw new InvalidOperationException("Unable to connect the tutorial Timeline graph.");
            mixer.SetInputWeight(0, 1);
            for (int track = 0; track < tracks.Count; track++)
            {
                int port = tracks[track].GetOutputCount(); tracks[track].SetOutputCount(port + 1);
                if (!graph.Connect(tracks[track], port, mixer, track + 1)) throw new InvalidOperationException("Unable to connect a tutorial action track.");
                mixer.SetInputWeight(track + 1, 1);
            }
            var scriptOutput = ScriptPlayableOutput.Create(graph, "Tutorial choreography");
            scriptOutput.SetSourcePlayable(mixer); scriptOutput.SetUserData(binding);
            director.Play();
            director.playableGraph.Evaluate(0);
            ThrowTimelineFailure();
            if (Application.isPlaying && owner.WorldStream && owner.ViewCamera) presentation = new PresentationGate(owner);
        }
        public void Tick(float seconds)
        {
            ThrowTimelineFailure();
            if (Finished || skipped) return;
            if (director.state != PlayState.Playing) return;
            if (presentation != null)
            {
                if (!presentation.Ready) return;
                presentation.Dispose(); presentation = null;
                seconds = 0;
            }
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            double remaining = Math.Max(0, TimelineDuration - TimelineTime);
            director.playableGraph.Evaluate((float)Math.Min(Math.Max(0, seconds), remaining));
            ThrowTimelineFailure();
        }
        internal void ReportTimelineFailure(Exception error) => timelineFailure ??= error;
        void ThrowTimelineFailure()
        {
            if (timelineFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(timelineFailure).Throw();
        }
        internal void Register(TutorialActionBehaviour clip) { clips.Add(clip); sortClips = true; }
        internal void RegisterTrack(Playable track) => tracks.Add(track);
        internal void EvaluateTimeline(double graphTime)
        {
            if (Finished || skipped || timelineFailure != null) return;
            if (sortClips)
            {
                clips.Sort((a,b) => { int time = a.Start.CompareTo(b.Start); return time != 0 ? time : a.Order.CompareTo(b.Order); });
                sortClips = false;
            }
            double duration = TimelineDuration;
            double target = graphTime >= duration - .000001 ? duration : graphTime;
            if (target + .000001 < sampledTime) throw new InvalidOperationException("Restart the scene before seeking backwards through gameplay commands.");
            // Stateful spawn/capture commands must see each preceding clip at its
            // own timestamp, even if a rendered frame crosses several short clips.
            foreach (var clip in clips)
            {
                if (clip.Fired || clip.Start > target + .000001) continue;
                AdvanceTimeline(clip.Start); clip.Execute();
            }
            AdvanceTimeline(target);
            if (target >= duration) { Finished = true; moves.Clear(); }
        }
        void AdvanceTimeline(double time)
        {
            float delta = (float)System.Math.Max(0, time - sampledTime);
            // Sample sequences within Advance, before applying camera constraints.
            sequenceSampleTime = time;
            Advance(delta); sampledTime = time;
        }
        double sequenceSampleTime;
        internal void ExecuteTimelineAction(JToken action) => Dispatch(action);
        internal void SampleTimelineSequence(Sequence sequence, float localTime) => SampleSequence(sequence, localTime);
        public void Skip()
        {
            presentation?.Dispose(); presentation = null;
            if (Finished || skipped) return;
            skipped = true; timelineFailure = null;
            director.Stop(); moves.Clear();
            foreach (var action in skipActions["scene_specific"]) Dispatch(action);
            foreach (var action in skipActions["common_cleanup"]) Dispatch(action);
            foreach (var loop in loops) ApplySkipLoop(loop["skip_action"]);
            owner.SetFade(Kind(skipActions["final_fade"]) == "OpaqueOverlay" ? 1 : 0);
            owner.SetBars(0);
            Finished = true;
        }
        void DestroyDirector()
        {
            if (director) director.Stop();
            if (directorHost) { if (Application.isPlaying) UnityEngine.Object.Destroy(directorHost); else UnityEngine.Object.DestroyImmediate(directorHost); }
            director = null; directorHost = null;
        }
        void ApplySkipLoop(JToken value)
        {
            if (Kind(value) == "StopIfPresent") owner.StopLoops();
            else owner.PlaySound((string)Arg(value), true);
        }

        void Dispatch(JToken action)
        {
            var value = Arg(action);
            switch (Kind(action))
            {
                case "Npc": Npc(value); break;
                case "AudioCue":
                    string cue = (string)action["cue"];
                    int cueKind = (int)action["kind"];
                    if (cueKind == 0) owner.PlayVoice(cue);
                    else if (cueKind == 3) owner.StopLoops();
                    else owner.PlaySound(cue, cueKind == 2);
                    break;
                case "Player": Player(value); break;
                case "Camera": Camera(value); break;
                case "Sequence": SampleSequence(new Sequence(value, (float)sampledTime), 0); break;
                case "Loop":
                    // The native presenter owns natural scene audio; loop actions
                    // also carry the independent lifecycle contract for Skip.
                    if (!skipped) break;
                    ApplySkipLoop(value);
                    break;
                case "Sound":
                    // These one-shots are already on the source presenter audio
                    // timeline. Ambient changes retain their separate ownership.
                    if (Kind(value) == "Ambient") owner.ApplyCinematicAction("Ambient", value);
                    break;
                case "SubtitleClear": owner.Subtitle(""); break;
                case "GameFadeIn": owner.SetFade(0); break;
                case "Cinematic": owner.ApplyCinematicAction("Cinematic", value); break;
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
            actor.GetComponent<TutorialActor>().ForceUpdate = (bool?)spawn["force_update"] ?? false;
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
                    float speed = (float)action["speed"] / 100;
                    moves[id] = new Move { Actor = transform, Target = expressions.Position(action["target"]), Speed = speed };
                    if (!actor.TryGetComponent<TutorialActor>(out var npc) ||
                        npc.AnimationPose is TutorialNpcPose.Rest or TutorialNpcPose.Locomotion)
                        Animate(actor, speed > 4 ? "run" : "walk", false, TutorialNpcPose.Locomotion, .3f);
                    break;
                case "Command":
                    var command = action["command"];
                    switch (Kind(command))
                    {
                        case "ForceStop": moves.Remove(id); break;
                        case "Pause":
                            if (actor.TryGetComponent<TutorialActor>(out var pausedNpc)) pausedNpc.AnimationPose = TutorialNpcPose.Paused;
                            foreach (var receiver in actor.GetComponentsInChildren<TutorialAnimationEffectReceiver>(true))
                                receiver.PauseNpc();
                            break;
                        // DeadMotion owns the death effect timer. The pose was
                        // already requested by the script's Animation command.
                        case "Dead": break;
                        case "ForceUpdate": actor.GetComponent<TutorialActor>().ForceUpdate = true; moves.Remove(id); break;
                        case "SetAngleTo": transform.rotation = Heading(transform.position, expressions.Entity(Arg(command)).position, transform.rotation); break;
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
                    string clip = (string)Arg(action);
                    if (!Animate(player.gameObject, clip, true))
                        Debug.LogWarning($"Tutorial player animation '{clip}' is unavailable.");
                    break;
                case "StandForce":
                    player.GetComponent<TutorialPlayerEquipment>()?.SetVisible(true);
                    player.GetComponentInChildren<FFOne.Content.NativePlayerAnimator>(true)?.ResetLocomotion();
                    Animate(player.gameObject, "stand1", false); break;
                case "Face": owner.FacePlayer(Heading(player.position, expressions.EntityPosition(Arg(action)), player.rotation)); break;
                case "FacePosition": owner.FacePlayer(Heading(player.position, expressions.Position(Arg(action)), player.rotation)); break;
                case "FaceAngle": owner.FacePlayer(Quaternion.Euler(0, (float)Arg(action), 0)); break;
                case "Hide": case "Show":
                    foreach (var renderer in player.GetComponentsInChildren<Renderer>(true)) renderer.enabled = Kind(action) == "Show"; break;
                case "SetTemporaryNanoAbsent": owner.ApplyCinematicAction("Player", action); break;
                default: throw new InvalidOperationException("Unknown player action: " + action);
            }
        }

        internal static bool IsPlayingLocomotion(GameObject actor) =>
            actor.TryGetComponent<TutorialActor>(out var npc) && npc.AnimationPose == TutorialNpcPose.Locomotion;

        internal static void AnimateLocomotionEnd(GameObject actor, bool ran) =>
            Animate(actor, ran ? "ready" : "idle", false, TutorialNpcPose.Rest, ran ? .2f : .3f);

        public static bool Animate(GameObject actor, string clip, bool once) =>
            Animate(actor, clip, once, clip == "idle" ? TutorialNpcPose.Rest : TutorialNpcPose.Forced, .3f);

        static bool Animate(GameObject actor, string clip, bool once, TutorialNpcPose pose, float fade)
        {
            if (!actor) return false;
            var player = actor.GetComponentInChildren<FFOne.Content.NativePlayerAnimator>(true);
            if (player) return player.Play(clip, once);
            bool npc = actor.GetComponent<TutorialActor>();
            bool idle = npc && clip == "idle";
            if (npc)
            {
                foreach (var receiver in actor.GetComponentsInChildren<TutorialAnimationEffectReceiver>(true))
                {
                    string resolved = idle ? SelectIdleStand(UnityEngine.Random.Range(0, 100),
                        receiver.GetComponent<FFOne.Content.NativeNpcAnimator>().CurrentClip) : clip;
                    bool death = resolved.Contains("death");
                    var completion = death ? TutorialAnimationEffectReceiver.Completion.Hold
                        : once && !idle ? TutorialAnimationEffectReceiver.Completion.Replay
                        : TutorialAnimationEffectReceiver.Completion.Loop;
                    if (receiver.PlayNpc(resolved, completion, fade, pose) ||
                        idle && receiver.PlayNpc("stand1", completion, fade, pose)) return true;
                }
            }
            else
            {
                foreach (var animation in actor.GetComponentsInChildren<Animation>(true))
                {
                    var state = animation[clip];
                    if (state == null) continue;
                    state.wrapMode = once || clip.Contains("death") ? WrapMode.Once : WrapMode.Loop;
                    animation.CrossFade(clip, .1f);
                    return true;
                }
            }
            // Missing named clips must be visible during reconstruction; never
            // silently substitute a different animation with a similar name.
            Debug.LogWarning($"Tutorial animation '{clip}' missing on '{actor.name}'."); return false;
        }
        public static string SelectIdleStand(int roll, string previous) => FFOne.Content.NativeNpcAnimator.SelectStand(roll, previous);
        public void Dispose() { presentation?.Dispose(); presentation = null; Finished = true; timelineFailure = null; DestroyDirector(); clips.Clear(); tracks.Clear(); moves.Clear(); expressions.Clear(); }
    }
}
