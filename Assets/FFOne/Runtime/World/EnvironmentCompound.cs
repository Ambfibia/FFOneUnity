using System;
using UnityEngine;

namespace FFOne.World
{
    // Stable slots belong to the reusable object. SourceRoot belongs to a scene instance.
    [SelectionBase]
    public sealed class EnvironmentCompound : MonoBehaviour
    {
        [Serializable] public sealed class Part
        {
            public string Role;
            public Transform Target;
        }
        public string Key;
        public string SourceRoot;
        public Part[] Parts = Array.Empty<Part>();
    }
}
