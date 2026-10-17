using System;
using UnityEngine;

namespace FFOne.Tutorial
{
    [CreateAssetMenu(menuName = "FFOne/Tutorial content")]
    public sealed class TutorialContent : ScriptableObject
    {
        public GameObject World;
        public GameObject ButtercupNano;
        public TutorialActorDefinition[] Actors = Array.Empty<TutorialActorDefinition>();
        public TutorialAudioDefinition[] Audio = Array.Empty<TutorialAudioDefinition>();
    }

    [Serializable]
    public sealed class TutorialActorDefinition
    {
        public int NpcType, MaxHp, Style, Class;
        public string Name;
        public float Scale = 1, AttackRange, Height, Radius;
        public GameObject Model;
    }

    [Serializable]
    public sealed class TutorialAudioDefinition
    {
        public string Cue, Category;
        public AudioClip English, Russian;
    }

    public sealed class TutorialActor : MonoBehaviour
    {
        public int RuntimeId, NpcType, Hp, MaxHp, Style;
        public bool Hostile, Damaged, Invulnerable, ForceUpdate;
        public string DisplayName;
        public float StunUntil;
        public float AttackRange, Height, VerticalVelocity;
    }
}
