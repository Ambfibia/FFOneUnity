using ContentPaths = FFOne.Content.ContentPaths;
using System.Linq;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TutorialEffectsTests
    {
        [Test]
        public void RequiredEffectAssetsHaveDistinctExactRoutes()
        {
            var paths = TutorialCinematicEffects.RequiredNativePaths().ToArray();
            // The shipped cutscenes include effects triggered by actor animation events.
            Assert.That(paths.Length, Is.EqualTo(45));
            Assert.That(paths, Does.Contain("map/shared/effects/es768.closure.json"));
            Assert.That(paths, Does.Contain("map/shared/effects/models/es751/npc_samuraijack_event_eff.glb"));
            Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Length));
            Assert.That(paths, Does.Contain("map/shared/effects/models/es742/T_electric gun.glb"));
            Assert.That(paths, Does.Contain("map/shared/projectiles/effects/models/es391/tail2.glb"));
        }

        [Test]
        public void ImportedTutorialEffectsDecodeTheirNativeTexturesAndControllers()
        {
            var owner = new GameObject("Tutorial effect decode test");
            try
            {
                var effects = owner.AddComponent<TutorialCinematicEffects>();
                foreach (int id in new[] { 10, 372, 668, 705, 739, 740, 741, 742, 750, 767, 771, 15, 391 })
                    Assert.DoesNotThrow(() => effects.Preload(id), "Native effect " + id);
                var particleShader = FFOne.Content.ContentAssets.Load<Shader>("Tutorial/Particle");
                Assert.That(particleShader, Is.Not.Null);
                Assert.That(ShaderUtil.ShaderHasError(particleShader), Is.False);
                var meshShader = Shader.Find("FFOne/NativeToon");
                Assert.That(ShaderUtil.ShaderHasError(meshShader), Is.False);
                var material = new Material(meshShader);
                try
                {
                    Assert.That(material.HasProperty("_MainTexRotation"), Is.True);
                    Assert.That(material.GetFloat("_MainTexRotation"), Is.Zero);
                }
                finally { Object.DestroyImmediate(material); }
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
