using System;
using System.Collections.Generic;
using UnityEngine;

namespace FFOne.Content
{
    [RequireComponent(typeof(Animator))]
    public sealed class NativePlayerAnimator : MonoBehaviour
    {
        public const string BaseLayer = "Base Motion";
        public const string UpperLayer = "Upper Body";
        readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
        readonly Dictionary<string, AnimationClip> clips = new(StringComparer.Ordinal);
        Animator animator;
        string pending;
        int pendingLayer;
        int pendingFrame = -1;
        public Animator Animator => animator;
        public event Action<string> SourceSound;
        int soundState;
        float soundSeconds = -1;
        static readonly string[] VehicleSoundClips = { "board_jumpstart", "board_jumpend", "scooter_jumpstart", "scooter_jumpend" };

        public void Configure(string gender, int height, int body)
        {
            var profile = FFOne.Content.ContentAssets.Load<NativePlayerAnimationProfile>("characters/player/shared/animation/" + gender + "-profile");
            if (!profile || !profile.Controller || !profile.Avatar)
                throw new InvalidOperationException("Build the accepted player Animator Controllers with FFOne/Animation/Rebuild Player Controllers.");
            animator = GetComponent<Animator>();
            animator.runtimeAnimatorController = profile.Controller;
            animator.avatar = profile.Avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.keepAnimatorStateOnDisable = true;
            ids.Clear(); clips.Clear();
            for (int i = 0; i < profile.ClipNames.Length; i++) ids.Add(profile.ClipNames[i], i);
            foreach (var clip in profile.Controller.animationClips) clips[clip.name] = clip;
            animator.Rebind();
            SetBodyShape(height, body);
            animator.Play(BaseLayer + ".stand1", 0, 0);
            animator.Update(0);
            animator.SetLayerWeight(1, 0);
            soundState = 0; soundSeconds = -1;
        }

        public void SetBodyShape(int height, int body)
        {
            animator.SetFloat("Height", 1 - Mathf.Clamp(height, 0, 4) / 4f);
            animator.SetFloat("BodyShape", Mathf.Clamp(body, 0, 2) / 2f);
        }

        public bool Supports(string clip) => ids.ContainsKey(clip);
        public float Duration(string clip) => clips.TryGetValue(clip, out var value) ? value.length : 0;
        public bool Play(string clip, bool restart = false)
        {
            if (!ids.TryGetValue(clip, out int id) || clip is "height" or "height_Add" or "shape" or "shape_Add") return false;
            int layer = clip.EndsWith("upper", StringComparison.Ordinal) ? 1 : 0;
            string layerName = layer == 0 ? BaseLayer : UpperLayer;
            int hash = UnityEngine.Animator.StringToHash(layerName + "." + clip);
            if (restart)
            {
                if (layer == 1) animator.SetLayerWeight(1, 1);
                animator.ResetTrigger(layer == 0 ? "PlayMotion" : "PlayUpperMotion");
                animator.Play(hash, layer, 0);
                animator.Update(0);
                pending = null; pendingFrame = -1;
                return true;
            }
            if (!restart && (!animator.IsInTransition(layer) && animator.GetCurrentAnimatorStateInfo(layer).fullPathHash == hash
                || animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).fullPathHash == hash
                || pending == clip && pendingLayer == layer && pendingFrame == Time.frameCount)) return true;
            animator.SetInteger(layer == 0 ? "Motion" : "UpperMotion", id);
            animator.SetTrigger(layer == 0 ? "PlayMotion" : "PlayUpperMotion");
            pending = clip; pendingLayer = layer; pendingFrame = Time.frameCount;
            return true;
        }

        void LateUpdate()
        {
            if (!animator || animator.layerCount < 2) return;
            TickVehicleSound();
            if (pending != null)
            {
                string layer = pendingLayer == 0 ? BaseLayer : UpperLayer;
                int hash = UnityEngine.Animator.StringToHash(layer + "." + pending);
                if (animator.GetCurrentAnimatorStateInfo(pendingLayer).fullPathHash == hash
                    || animator.IsInTransition(pendingLayer) && animator.GetNextAnimatorStateInfo(pendingLayer).fullPathHash == hash)
                { pending = null; pendingFrame = -1; }
            }
            int emptyHash = UnityEngine.Animator.StringToHash(UpperLayer + ".Empty");
            bool empty = animator.GetCurrentAnimatorStateInfo(1).fullPathHash == emptyHash;
            if (animator.IsInTransition(1))
            {
                bool enteringEmpty = animator.GetNextAnimatorStateInfo(1).fullPathHash == emptyHash;
                float progress = Mathf.Clamp01(animator.GetAnimatorTransitionInfo(1).normalizedTime);
                animator.SetLayerWeight(1, enteringEmpty ? 1 - progress : empty ? progress : 1);
            }
            else if (!empty) animator.SetLayerWeight(1, 1);
            else if (pendingFrame != Time.frameCount) animator.SetLayerWeight(1, 0);
        }
        void TickVehicleSound()
        {
            if (SourceSound == null) return;
            var state = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            string name = null;
            foreach (string clip in VehicleSoundClips)
                if (state.IsName(BaseLayer + "." + clip)) { name = clip; break; }
            if (name == null) { soundState = 0; soundSeconds = -1; return; }
            float seconds = state.normalizedTime * Duration(name);
            if (soundState != state.fullPathHash || seconds < soundSeconds) soundSeconds = -1;
            if (soundSeconds < NativeVehicleAnimation.SoundSeconds && seconds >= NativeVehicleAnimation.SoundSeconds) SourceSound?.Invoke(NativeVehicleAnimation.Sound(name));
            soundState = state.fullPathHash; soundSeconds = seconds;
        }
    }
}
