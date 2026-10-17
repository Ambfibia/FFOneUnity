using System;
using UnityEngine;

namespace FFOne.World
{
    public sealed class WorldCatalog : ScriptableObject
    {
        public WorldTileReference[] Tiles = Array.Empty<WorldTileReference>();
        public string SourceCatalogSha256;
        public long SourceBytes;
        public int SourceFiles;
        public WorldAtmosphere[] Atmospheres = Array.Empty<WorldAtmosphere>();
        public WorldWaterTile[] NeighborWater = Array.Empty<WorldWaterTile>();
    }
    [Serializable] public sealed class WorldAtmosphere
    {
        public Vector2Int Grid;
        public float FogDepth;
        public Color Fog, Sky, Light;
    }
    [Serializable] public sealed class WorldTileReference
    {
        public string Id, Resource;
        public string SceneAddress;
        public int X, Z;
        public Vector3 Minimum, Maximum;
        public int Visuals, Colliders, Animations, Effects;
        public int SourceVisuals;
        public Vector2[] OverflowCenters = Array.Empty<Vector2>();
        public float DistanceSquared(Vector3 point)
        {
            float x = Mathf.Max(Minimum.x - point.x, 0, point.x - Maximum.x);
            float z = Mathf.Max(Minimum.z - point.z, 0, point.z - Maximum.z);
            float distance = x * x + z * z;
            foreach (var center in OverflowCenters)
                distance = Mathf.Min(distance, (new Vector2(point.x, point.z) - center).sqrMagnitude);
            return distance;
        }
    }
    [Serializable] public sealed class WorldPlacement
    {
        public string Name, LegacyId, SourceNode;
        public GameObject Model;
        public string ModelResource;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public float CpuPresentationEnd;
    }
    [Serializable] public sealed class WorldWaterTile
    {
        public string Id;
        public int X, Z;
        public WorldPlacement[] Visuals = Array.Empty<WorldPlacement>();
    }
    [Serializable] public sealed class WorldCollider
    {
        public string Name, LegacyId, SourceNode;
        public Mesh Mesh;
        public bool Trigger;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
    }
}
