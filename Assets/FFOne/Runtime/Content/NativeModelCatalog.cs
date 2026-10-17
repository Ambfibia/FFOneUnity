using System;
using UnityEngine;

namespace FFOne.Content
{
    public sealed class NativeModelCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Binding
        {
            public string OriginalResource, EditableResource;
            // Read only by the one-time editor migration, then cleared. Never populated
            // by new publication; runtime catalog entries contain no asset references.
            [HideInInspector] public GameObject Original;
            [HideInInspector] public GameObject Editable;
        }
        public Binding[] Bindings = Array.Empty<Binding>();
    }
}
