using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialCinematics
    {
        PresentationGate presentation;

        // Creating models/materials is not enough: shader uploads and skinning
        // must have reached an actual frame before voice/timeline playback.
        sealed class PresentationGate : IDisposable
        {
            readonly Camera camera;
            readonly float deadline;
            readonly List<(AnimationState State, float Speed)> animations = new();
            readonly List<(Animator Animator, float Speed)> animators = new();
            readonly List<TutorialEffectPlayback> effects = new();
            readonly List<AudioSource> audio = new();
            bool rendered, disposed;

            public PresentationGate(TutorialController owner)
            {
                camera = owner.ViewCamera; deadline = Time.realtimeSinceStartup + 30;
                var root = owner.WorldStream.gameObject;
                foreach (var animation in root.GetComponentsInChildren<Animation>(true))
                    foreach (AnimationState state in animation)
                    { animations.Add((state, state.speed)); state.speed = 0; }
                FreezeAnimators(root.transform);
                if (owner.Player && !owner.Player.IsChildOf(root.transform)) FreezeAnimators(owner.Player);
                foreach (var effect in root.GetComponentsInChildren<TutorialEffectPlayback>(true))
                    if (effect.enabled) { effects.Add(effect); effect.enabled = false; }
                foreach (var source in camera.GetComponents<AudioSource>())
                    if (source.isPlaying) { audio.Add(source); source.Pause(); }
                RenderPipelineManager.endCameraRendering += Rendered;
            }

            void FreezeAnimators(Transform root)
            {
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                { animators.Add((animator, animator.speed)); animator.speed = 0; }
            }

            void Rendered(ScriptableRenderContext context, Camera view)
            { if (view == camera) rendered = true; }

            public bool Ready
            {
                get
                {
                    if (!rendered && Time.realtimeSinceStartup > deadline)
                        throw new TimeoutException("Tutorial cinematic did not render its prepared first frame.");
                    return rendered;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                RenderPipelineManager.endCameraRendering -= Rendered;
                foreach (var item in animations) if (item.State != null) item.State.speed = item.Speed;
                foreach (var item in animators) if (item.Animator) item.Animator.speed = item.Speed;
                foreach (var effect in effects) if (effect) effect.enabled = true;
                foreach (var source in audio) if (source) source.UnPause();
            }
        }
    }
}
