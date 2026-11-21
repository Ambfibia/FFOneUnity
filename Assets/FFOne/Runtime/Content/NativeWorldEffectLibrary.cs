using System;
using UnityEngine;
namespace FFOne.Content
{
    public sealed class NativeWorldEffectLibrary : ScriptableObject
    {
        public string SourceHash;
        [TextArea] public string Behaviour;
        public NativeWorldParticleDefinition[] Particles=Array.Empty<NativeWorldParticleDefinition>();
        public NativeEffectDefinition[] Elements=Array.Empty<NativeEffectDefinition>();
    }
}
