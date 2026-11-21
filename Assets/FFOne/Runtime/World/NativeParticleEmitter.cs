using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.World
{
    // Source controllers retain admission and seeded births. Unity owns particle
    // storage, integration, lifetime, size curves, bounds and mesh rendering.
    internal sealed class NativeParticleEmitter : IDisposable
    {
        static Mesh quad;
        static int references;
        readonly ParticleSystem system;
        readonly AnimationCurve rotation;
        readonly int mode;
        readonly bool reflected;
        ParticleSystem.Particle[] particles = Array.Empty<ParticleSystem.Particle>();
        bool disposed;
        internal ParticleSystem System => system;
        internal int Count => system.particleCount;

        internal NativeParticleEmitter(GameObject child, Material material, float lifetime, float initialSize,
            AnimationCurve width, AnimationCurve height, AnimationCurve roll, Vector3 force, float scale,
            int renderMode, int xTiles, int yTiles, float cycles, bool world, bool animateColor, Color[] colors, int capacity)
        {
            mode = renderMode; reflected = world; rotation = roll;
            system = child.AddComponent<ParticleSystem>();
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false; main.loop = false; main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startLifetime = lifetime; main.startSpeed = 0; main.startSize3D = true;
            main.startSizeX = main.startSizeY = main.startSizeZ = 1;
            main.startRotation3D = true; main.gravityModifier = 0; main.maxParticles = Math.Max(1, capacity);
            system.useAutoRandomSeed = false; system.randomSeed = 1;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            var acceleration = system.forceOverLifetime;
            acceleration.enabled = force != Vector3.zero; acceleration.space = ParticleSystemSimulationSpace.World;
            acceleration.x = force.x; acceleration.y = force.y; acceleration.z = force.z;
            var size = system.sizeOverLifetime;
            size.enabled = true; size.separateAxes = true;
            AnimationCurve x = width.length > 0 ? width : height.length > 0 ? height : AnimationCurve.Constant(0, 1, initialSize);
            AnimationCurve y = height.length > 0 ? height : x;
            size.x = new ParticleSystem.MinMaxCurve(scale, x);
            size.y = new ParticleSystem.MinMaxCurve(scale, y);
            size.z = 1;
            if (!quad)
            {
                quad = new Mesh { name = "FFOne particle quad" };
                quad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                quad.triangles = new[] { 0,1,2,0,2,3 };
                quad.RecalculateBounds();
            }
            references++;
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = quad;
            renderer.alignment = ParticleSystemRenderSpace.World; renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.OldestInFront;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
                ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent
            });
            // Age travels in TEXCOORD0.z. The source atlas order and 16-sample world
            // palette are evaluated in the shader without 8-bit particle colour loss.
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_FFNativeParticles", 1);
            properties.SetVector("_FFParticleAtlas", new Vector4(xTiles, yTiles, cycles, world ? 1 : 0));
            properties.SetFloat("_FFParticleColorMode", animateColor ? world ? 16 : 1 : 0);
            if (animateColor)
                for (int i = 0; i < 5; i++) properties.SetVector("_FFParticleColor" + i, colors[i]);
            renderer.SetPropertyBlock(properties);
            system.Simulate(0, false, true, false);
        }

        internal void Emit(Vector3 position, Vector3 velocity)
        {
            system.Emit(new ParticleSystem.EmitParams {
                position = position, velocity = velocity, startColor = Color.white,
                startSize3D = Vector3.one, applyShapeToPosition = false, randomSeed = 1
            }, 1);
        }

        internal int Step(float delta)
        {
            int before = Count;
            if (delta > 0)
            {
                // The accepted controller integrates force once per rendered frame.
                // Unity otherwise subdivides long manual steps and changes that path.
                // Simulate is synchronous, so no other system sees this scoped limit.
                float previous = Time.maximumParticleDeltaTime;
                try
                {
                    if (delta > previous) Time.maximumParticleDeltaTime = delta;
                    system.Simulate(delta, false, false, false);
                }
                finally { if (delta > previous) Time.maximumParticleDeltaTime = previous; }
            }
            return before - Count;
        }

        internal void Face(Camera view)
        {
            int count = Count;
            if (count == 0) return;
            if (particles.Length < count) particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            count = system.GetParticles(particles);
            for (int i = 0; i < count; i++)
            {
                var particle = particles[i];
                float fraction = Mathf.Clamp01(1 - particle.remainingLifetime / particle.startLifetime);
                Quaternion basis = view ? view.transform.rotation : Quaternion.identity;
                if (mode == 4) basis = reflected ? Quaternion.LookRotation(Vector3.down, Vector3.back) : Quaternion.Euler(-90, 0, 0);
                else if (mode == 5 && view)
                {
                    var direction = view.transform.position - particle.position; direction.y = 0;
                    if (reflected) direction = -direction;
                    basis = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) : reflected ? Quaternion.Euler(0,180,0) : Quaternion.identity;
                }
                float roll = rotation.length > 0 ? rotation.Evaluate(fraction) : 0;
                particle.rotation3D = (basis * Quaternion.Euler(0, 0, roll)).eulerAngles;
                particles[i] = particle;
            }
            system.SetParticles(particles, count);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (system) { system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); WorldBehaviour.Release(system.gameObject); }
            if (--references == 0) { WorldBehaviour.Release(quad); quad = null; }
        }
    }
}
