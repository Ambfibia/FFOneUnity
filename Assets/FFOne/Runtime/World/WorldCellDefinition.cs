using UnityEngine;

namespace FFOne.World
{
    [CreateAssetMenu(menuName="FFOne/World/Cell Definition")]
    public sealed class WorldCellDefinition : ScriptableObject
    {
        public string TileId, SceneAddress, ImportedContentGuid, ImportedContentSha256;
        public Vector2Int Grid;
        public Vector3 Minimum, Maximum;
        [TextArea] public string AuthoringNotes;
    }
}
