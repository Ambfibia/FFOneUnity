using UnityEngine;

namespace FFOne.World
{
    [System.Serializable]
    public sealed class WorldMotionBinding
    {
        public string SourceId, SourceHash;
        public AnimationClip[] Channels = System.Array.Empty<AnimationClip>();
    }

    [System.Serializable]
    public sealed class WorldLauncherPlacement
    {
        public int ObjectId;
        public Vector3 Center;
        public float Heading, Radius, MinimumPower, MaximumPower;
        public Vector2 AimLimits;
    }

    [CreateAssetMenu(menuName="FFOne/World/Cell Definition")]
    public sealed class WorldCellDefinition : ScriptableObject
    {
        public string TileId, SceneAddress, ImportedContentGuid, ImportedContentSha256;
        public int SourceBindingsVersion;
        public string[] UnpublishedModelIds = System.Array.Empty<string>();
        public FFOne.Content.NativeWorldEffectLibrary Effects;
        public WorldLauncherPlacement[] AdditionalLaunchers = System.Array.Empty<WorldLauncherPlacement>();
        public int MotionVersion;
        public WorldMotionBinding[] Motion = System.Array.Empty<WorldMotionBinding>();
        public Vector2Int Grid;
        public Vector3 Minimum, Maximum;
        [TextArea] public string AuthoringNotes;
    }
}
