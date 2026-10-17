using System;
using UnityEngine;

namespace FFOne.World
{
    public sealed class WorldTileContent : ScriptableObject
    {
        public string Id;
        public GameObject Terrain;
        public TextAsset Behaviour, Environment;
        public WorldPlacement[] Visuals = Array.Empty<WorldPlacement>();
        public WorldCollider[] Colliders = Array.Empty<WorldCollider>();
    }
}
