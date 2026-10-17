using System;
using UnityEngine;

namespace FFOne.World
{
    // Authoring wrapper: the world's streamed placements keep their own original collision and behaviour graph.
    public sealed class EnvironmentObject : MonoBehaviour
    {
        public string SourceIdentity;
        [TextArea] public string CollisionNotes;
        public Animation[] OriginalAnimations = Array.Empty<Animation>();
        public string[] OriginalStates = Array.Empty<string>();

        public bool PlayOriginalState(string state)
        {
            bool played = false;
            foreach (var animation in OriginalAnimations)
                if (animation && animation.GetClip(state)) played |= animation.Play(state);
            return played;
        }
    }
}
