using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FFOne.Tutorial
{
    // One script output mixes all authored command tracks in source order.
    internal sealed class TutorialSceneBehaviour : PlayableBehaviour
    {
        internal TutorialCinematics Runtime;
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            // Unity callback exceptions must reach the controller's existing
            // error/cleanup boundary, rather than leave a half-running scene.
            try { Runtime.EvaluateTimeline(playable.GetTime()); }
            catch (System.Exception error) { Runtime.ReportTimelineFailure(error); }
        }
    }
    internal sealed class TutorialActionBehaviour : PlayableBehaviour
    {
        internal JToken Action;
        internal double Start, Duration;
        internal int Order;
        internal bool Fired;
        TutorialCinematics runtime;
        TutorialCinematics.Sequence sequence;
        double nativeSceneTime = double.NaN, nativeLocalTime;
        bool nativeActive;
        internal int EvaluatedFrames { get; private set; }
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (runtime == null) return;
            nativeSceneTime = runtime.TimelineTime;
            nativeLocalTime = playable.GetTime();
            nativeActive = info.effectiveWeight > 0;
            EvaluatedFrames++;
        }
        internal void Configure(TutorialCinematics owner, TimelineClip clip, int order)
        {
            if (Action == null) throw new System.InvalidOperationException("Assign a tutorial Timeline action: " + clip.displayName);
            runtime = owner; Start = clip.start; Duration = clip.duration; Order = order;
            runtime.Register(this);
        }
        internal void Execute()
        {
            Fired = true;
            if (TutorialCinematicExpressions.Kind(Action) == "Sequence")
            {
                double sourceDuration = TutorialTimelineScene.ActionDuration(Action);
                sequence = new TutorialCinematics.Sequence(TutorialCinematicExpressions.Arg(Action), (float)Start) {
                    TimeScale = sourceDuration > 0 ? (float)(Duration / sourceDuration) : 1
                };
                Sample(Start);
            }
            else runtime.ExecuteTimelineAction(Action);
        }
        internal void Sample(double time)
        {
            if (sequence == null) return;
            // Active clips use Timeline's local playable time. Retrospective
            // samples are needed only at crossed command boundaries/completions.
            double local = nativeActive && System.Math.Abs(time - nativeSceneTime) < .000001
                ? nativeLocalTime : System.Math.Min(time, Start + Duration) - Start;
            runtime.SampleTimelineSequence(sequence, (float)(local / sequence.TimeScale));
            if (time >= Start + Duration) sequence = null;
        }
    }
}
