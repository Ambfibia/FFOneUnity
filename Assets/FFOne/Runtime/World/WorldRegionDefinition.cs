using UnityEngine;

namespace FFOne.World
{
    [CreateAssetMenu(menuName="FFOne/World/Region Definition")]
    public sealed class WorldRegionDefinition : ScriptableObject
    {
        public string RegionId;
        public Vector3 EntryPoint;
        public WorldCellDefinition[] Cells;
    }
}
