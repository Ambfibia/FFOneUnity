using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FFOne.Tutorial
{
    [TrackClipType(typeof(TutorialActionClip))]
    [TrackBindingType(typeof(TutorialTimelineBinding))]
    [TrackColor(.23f, .65f, .82f)]
    public sealed class TutorialActionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var mixer = Playable.Create(graph, inputCount);
            if (owner.TryGetComponent<TutorialTimelineBinding>(out var binding)) binding.Runtime.RegisterTrack(mixer);
            return mixer;
        }
        protected override Playable CreatePlayable(PlayableGraph graph, GameObject owner, TimelineClip clip)
        {
            var playable = base.CreatePlayable(graph, owner, clip);
            if (playable.IsValid() && clip.asset is TutorialActionClip action && owner.TryGetComponent<TutorialTimelineBinding>(out var binding))
            {
                var behaviour = ((ScriptPlayable<TutorialActionBehaviour>)playable).GetBehaviour();
                behaviour.Configure(binding.Runtime, clip, action.Order);
            }
            return playable;
        }
    }
}
