using UnityEngine;

namespace FFOne.Runtime.World
{
    // Terrain painting needs a TerrainCollider in the editor. Gameplay keeps the original mesh collision.
    [ExecuteAlways, RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
    public sealed class TerrainVegetationSurface : MonoBehaviour
    {
        void OnEnable() => Synchronize();
        void OnValidate() => Synchronize();
        public void Synchronize()
        {
            var terrain = GetComponent<Terrain>(); var collider = GetComponent<TerrainCollider>();
            if (!terrain || !collider) return;
            collider.terrainData = terrain.terrainData;
            collider.enabled = !Application.isPlaying;
        }
    }
}
