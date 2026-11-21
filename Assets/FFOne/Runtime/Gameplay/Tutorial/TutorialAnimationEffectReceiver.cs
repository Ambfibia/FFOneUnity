using System;
using System.Linq;
using FFOne.Content;
using UnityEngine;

namespace FFOne.Tutorial
{
    // Animation lives on the model child; the controller/effect owner lives
    // outside it. Preserve the original tag_p time and bone name in the clip.
    public sealed class TutorialAnimationEffectReceiver : MonoBehaviour
    {
        public TutorialCinematicEffects Effects;
        public TutorialController Owner;
        public TutorialActorDefinition Definition;
        internal enum Completion { Loop, Replay, Hold, Ready }
        NativeNpcAnimator animation;
        bool paused;
        Completion completion;

        internal bool PlayNpc(string clip, Completion after, float fade = .3f,
            TutorialNpcPose pose = TutorialNpcPose.Forced)
        {
            float speed = clip == "walk" ? Definition?.WalkAnimationSpeed ?? 1
                : clip == "run" ? Definition?.RunAnimationSpeed ?? 1 : 1;
            if (!animation.Play(clip, after == Completion.Loop, fade, after != Completion.Loop, speed)) return false;
            if (paused) animation.Animator.speed = 1;
            paused = false;
            completion = after;
            var actor = GetComponentInParent<TutorialActor>();
            if (actor) actor.AnimationPose = pose;
            return true;
        }

        internal void PauseNpc()
        {
            paused = true;
            animation.Animator.speed = 0;
        }

        internal void LateUpdate()
        {
            if (completion != Completion.Replay && completion != Completion.Ready) return;
            if (paused || animation.Animator.speed == 0 || !animation.ReachedEnd) return;
            if (completion == Completion.Replay)
            {
                PlayNpc(animation.CurrentClip, Completion.Replay, 0);
                return;
            }
            if (!PlayNpc("ready", Completion.Loop, .2f, TutorialNpcPose.Rest) &&
                !PlayNpc("stand1", Completion.Loop, .2f, TutorialNpcPose.Rest)) completion = Completion.Hold;
        }

        public void Initialize(TutorialController owner, TutorialActorDefinition definition, TutorialCinematicEffects effects)
        {
            Owner = owner; Definition = definition; Effects = effects;
            animation = GetComponent<NativeNpcAnimator>();
            animation.TutorialEvents = this;
            foreach (var binding in definition.Textures) SetTexture(binding.Material, binding.Texture);
        }
        void SetTexture(string materialName, Texture texture)
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    if (renderer.sharedMaterials[i] && renderer.sharedMaterials[i].name == materialName)
                    {
                        renderer.GetPropertyBlock(block, i); block.SetTexture("_MainTex", texture);
                        renderer.SetPropertyBlock(block, i); block.Clear();
                    }
        }
        internal void ApplyAttachedEffect(string parameter)
        {
            if (Effects) Effects.AttachAnimationEffect(transform, parameter);
        }
        internal void ApplyParticleEffect(string parameter)
        {
            if (Effects) Effects.AttachRootAnimationEffect(transform, int.Parse(parameter));
        }
        internal void ApplyAnimationSound(string parameter)
        {
            var sound = Definition?.AnimationSounds.FirstOrDefault(s => s.Cue == parameter);
            if (sound == null || sound.Variants.Length == 0) throw new InvalidOperationException("Unprepared animation sound: " + parameter);
            int variant = UnityEngine.Random.Range(0, sound.Variants.Length);
            var clip = Owner && Owner.VoiceRussian && sound.RussianVariants != null && sound.RussianVariants.Length > variant && sound.RussianVariants[variant]
                ? sound.RussianVariants[variant] : sound.Variants[variant];
            if (clip && Owner) Owner.PlayAnimationSound(clip, transform.position);
        }
    }
}
