using FFOne.Content;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FFOne.Tutorial
{
    // Each graph instance owns the command/sequence state; the asset is shared.
    public sealed class TutorialActionClip : PlayableAsset, ITimelineClipAsset
    {
        public NativeDataDocument Action;
        [HideInInspector] public int Order;
        public ClipCaps clipCaps => ClipCaps.None;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<TutorialActionBehaviour>.Create(graph);
            if (Action) playable.GetBehaviour().Action = TutorialActionData.Read(Action);
            return playable;
        }
    }
}
