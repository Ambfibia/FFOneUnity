using System;
using UnityEngine;

namespace FFOne.Tutorial
{
    [CreateAssetMenu(menuName = "FFOne/Tutorial content")]
    public sealed class TutorialContent : ScriptableObject
    {
        // Retained for serialized compatibility. Runtime streams the authored map_01_01 scene.
        public GameObject World;
        public Material Skybox;
        public GameObject ButtercupNano;
        public TutorialTimelineScene[] Cinematics = Array.Empty<TutorialTimelineScene>();
        public TutorialActorDefinition[] Actors = Array.Empty<TutorialActorDefinition>();
        public TutorialAudioDefinition[] Audio = Array.Empty<TutorialAudioDefinition>();
    }

    [Serializable]
    public sealed class TutorialActorDefinition
    {
        public int NpcType, MaxHp, Style, Class;
        public string Name;
        public float Scale = 1, AttackRange, Height, Radius;
        public float WalkAnimationSpeed = 1, RunAnimationSpeed = 1;
        public GameObject Model;
        public TutorialTextureBinding[] Textures = Array.Empty<TutorialTextureBinding>();
        public TutorialAnimationSound[] AnimationSounds = Array.Empty<TutorialAnimationSound>();
        public TutorialFaceTexture[] Faces = Array.Empty<TutorialFaceTexture>();
    }

    [Serializable] public sealed class TutorialTextureBinding { public string Material; public Texture2D Texture; }
    [Serializable] public sealed class TutorialAnimationSound { public string Cue; public AudioClip[] Variants, RussianVariants; }
    [Serializable] public sealed class TutorialFaceTexture { public string Name; public Texture2D Texture; }

    [Serializable]
    public sealed class TutorialAudioDefinition
    {
        public string Cue, Category;
        public AudioClip English, Russian;
    }

    internal enum TutorialNpcPose { Rest, Locomotion, Forced, Paused }

    public sealed class TutorialActor : MonoBehaviour
    {
        public int RuntimeId, NpcType, Hp, MaxHp, Style, Class;
        public bool IsScenery => NpcType == 2968;
        public bool Hostile, Damaged, Invulnerable, ForceUpdate;
        public string DisplayName;
        public float StunUntil;
        public float AttackRange, Height, VerticalVelocity;
        internal TutorialNpcPose AnimationPose;
    }
}
