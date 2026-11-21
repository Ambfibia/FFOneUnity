#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using FFOne.World;
using FFOne.Content;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class FrontendRecoverySmoke
    {
        [Serializable] sealed class NativeEnvironmentEvidence
        {
            public int cells, particleSystems, liveParticles, expiredBudget, transformChannels, platforms, clips;
            public string[] emitterNames;
            public int publishedEffects, legacyEffectDocuments;
        }
        void VerifyNativeEnvironment(EnvironmentCellBehaviour[] cells)
        {
            Require(cells.All(c=>c.Definition.MotionVersion==1),"World motion was not published as native clips.");
            Require(cells.All(c=>c.Runtime.UnsupportedBehaviours==0),"Native environment has unsupported behavior bindings.");
            var systems=cells.SelectMany(c=>c.GetComponentsInChildren<ParticleSystem>(true)).ToArray();
            int bindings=cells.Sum(c=>c.Runtime.ParticleEmitterBindings);
            Require(bindings>0 && systems.Length>=bindings,"World emitters have no native ParticleSystems.");
            foreach(var system in systems)
            {
                Require(!system.GetComponent<MeshFilter>(),"An ordinary emitter still owns a procedural mesh.");
                var renderer=system.GetComponent<ParticleSystemRenderer>();
                Require(renderer && renderer.sharedMaterial && renderer.sharedMaterial.shader.isSupported,"A native particle renderer has no supported shader.");
            }
            var legacy=Resources.FindObjectsOfTypeAll<NativeDataDocument>().Where(d=>d.Identity.EndsWith(".closure.json",StringComparison.Ordinal)).ToArray();
            Require(legacy.Length==0,"Runtime loaded a legacy VFX closure payload: "+string.Join(",",legacy.Select(d=>d.Identity)));
            Require(cells.All(c=>c.Definition.Effects),"A world cell has no published VFX library.");
            var evidence=new NativeEnvironmentEvidence {
                publishedEffects=Resources.FindObjectsOfTypeAll<NativeEffectDefinition>().Length, legacyEffectDocuments=legacy.Length,
                cells=cells.Length, particleSystems=systems.Length, liveParticles=systems.Sum(s=>s.particleCount),
                expiredBudget=cells.Sum(c=>c.Runtime.RetainedExpiredParticles), transformChannels=cells.Sum(c=>c.Runtime.TransformChannels),
                platforms=cells.Sum(c=>c.Runtime.PlatformBindings),
                clips=cells.SelectMany(c=>c.Definition.Motion).SelectMany(m=>m.Channels).Distinct().Count(),
                emitterNames=systems.Where(s=>s.particleCount>0).Select(s=>s.name).Distinct().OrderBy(n=>n).ToArray()
            };
            Require(evidence.clips>0 && evidence.transformChannels>0,"Native environment animation was not bound.");
            Require(evidence.liveParticles>0,"Native world emitters produced no live particles.");
            File.WriteAllText(Path.Combine(output,"native-environment.json"),JsonUtility.ToJson(evidence,true));
            Debug.Log($"NATIVE ENVIRONMENT PASSED: {evidence.particleSystems} ParticleSystems; {evidence.liveParticles} live particles; {evidence.clips} AnimationClips; {evidence.transformChannels} TRS channels.");
        }
    }
}
#endif
