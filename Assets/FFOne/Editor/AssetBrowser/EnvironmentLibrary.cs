using ContentPaths = FFOne.Content.ContentPaths;
using System;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class EnvironmentLibrary : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string Identity, Name, Category, PrefabGuid, Family;
            public string CardKey, RenderKey, AssemblyGuid, AssemblyStatus;
            public bool ManuallyClassified;
            public int SourcePlacements;
            public string[] PartIdentities = Array.Empty<string>();
        }
        public Entry[] Entries = Array.Empty<Entry>();
        public Entry[] CompoundEntries = Array.Empty<Entry>();
    }
}
