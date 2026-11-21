using UnityEngine;

namespace FFOne.World
{
    internal sealed class MovingWorldPlatform : MonoBehaviour { }

    /// <summary>Forwards contact from the player's actual CharacterController to its world owner.</summary>
    public sealed class SliderGroundContact : MonoBehaviour
    {
        internal NetworkWorldController Owner;
        void OnControllerColliderHit(ControllerColliderHit hit) => Owner?.SliderContact(hit);
    }
}
