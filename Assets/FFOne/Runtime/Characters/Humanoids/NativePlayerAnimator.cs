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
        public const string LeftTurnLayer = "Left Turn";
        public const string RightTurnLayer = "Right Turn";
        readonly Dictionary<string, int> ids = new(StringComparer.Ordinal);
        readonly Dictionary<string, AnimationClip> clips = new(StringComparer.Ordinal);
        Animator animator;
        readonly int[] pendingStates = new int[2];
        readonly int[] pendingFrames = { -1, -1 };
        public Animator Animator => animator;
        public event Action<string> SourceSound;
        int soundState;
        float soundSeconds = -1;
        float leftTurn, rightTurn;
        bool airborneMotion;
        static readonly string[] VehicleSoundClips = { "board_jumpstart", "board_jumpend", "scooter_jumpstart", "scooter_jumpend" };

        public void Configure(string gender, int height, int body)
            => Configure(FFOne.Content.ContentAssets.Load<NativePlayerAnimationProfile>(ProfileAddress(gender)), height, body);

        internal static string ProfileAddress(string gender) => "characters/player/shared/animation/" + gender + "-profile";

        internal void Configure(NativePlayerAnimationProfile profile, int height, int body)
        {
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
            Array.Clear(pendingStates, 0, pendingStates.Length);
            Array.Fill(pendingFrames, -1);
            soundState = 0; soundSeconds = -1;
            leftTurn = rightTurn = 0; airborneMotion = false;
        }

        public void SetBodyShape(int height, int body)
        {
            animator.SetFloat("Height", 1 - Mathf.Clamp(height, 0, 4) / 4f);
            animator.SetFloat("BodyShape", Mathf.Clamp(body, 0, 2) / 2f);
        }

        public bool Supports(string clip) => ids.ContainsKey(clip);
        public float Duration(string clip) => clips.TryGetValue(clip, out var value) ? value.length : 0;
        public static bool IsDirectionalPose(string clip) => clip.EndsWith("turnleft", StringComparison.Ordinal)
            || clip.EndsWith("turnright", StringComparison.Ordinal);

        public void SetMovementDirection(float side, float forward, float delta)
        {
            // cnAvatarAnimation.UpdateTurn samples additive poses at 0, .5 or 1.
            // Backward diagonals reverse the turn while retaining runback.
            float amount = side == 0 ? 0 : forward == 0 ? 1 : .5f;
            bool left = forward < 0 ? side > 0 : side < 0;
            leftTurn = Mathf.MoveTowards(leftTurn, left ? amount : 0, Mathf.Max(0, delta) * 2);
            rightTurn = Mathf.MoveTowards(rightTurn, left ? 0 : amount, Mathf.Max(0, delta) * 2);
            animator.SetFloat("LeftTurn", leftTurn);
            animator.SetFloat("RightTurn", rightTurn);
        }

        public void UpdateLocomotion(float side, float forward, bool grounded, bool launched, bool armed, float delta)
            => UpdateLocomotion(side, forward, grounded, launched, armed ? "rifle" : "", false, delta);

        public void UpdateLocomotion(float side, float forward, bool grounded, bool launched, string prefix, bool ready,
            float delta, float vertical = float.NegativeInfinity)
        {
            bool moving = side != 0 || forward != 0;
            // The legacy motor treats an unsupported edge as a fall only after
            // descending past half gravity, then dispatches the usual jump entry.
            bool airborne = launched || !grounded && (airborneMotion || vertical < -NativePlayerMotor.Gravity * .5f);
            if (launched || airborne && !airborneMotion) Play(prefix + "jumpstart", launched);
            else if (airborne)
            {
                if (!BaseIs(prefix + "jumpstart") && !BaseIs(prefix + "jump")) Play(prefix + "jump");
            }
            else if (airborneMotion) Play(prefix + (moving ? "jumplandrun" : "jumpend"));
            else if (BaseIs(prefix + "jumpend") && moving) Play(prefix + "jumplandrun");
            else if (!BaseIs(prefix + "jumpend") && !BaseIs(prefix + "jumplandrun"))
            {
                string idle = prefix + (ready ? "ready" : "stand1");
                Play(moving ? prefix + (forward < 0 ? "runback" : "run") : Supports(idle) ? idle : prefix + "stand1");
            }
            SetMovementDirection(airborne ? 0 : side, airborne ? 0 : forward, delta);
            airborneMotion = airborne;
        }

        internal void ResetLocomotion()
        {
            airborneMotion = false;
            SetMovementDirection(0, 0, 1);
        }

        internal void CancelUpperMotion()
        {
            if (!animator || animator.layerCount < 2) return;
            pendingFrames[1] = -1;
            animator.ResetTrigger("PlayUpperMotion");
            animator.Play(UpperLayer + ".Empty", 1, 0);
            animator.SetLayerWeight(1, 0);
            animator.Update(0);
        }

        bool BaseIs(string clip)
        {
            int hash = UnityEngine.Animator.StringToHash(BaseLayer + "." + clip);
            return pendingFrames[0] == Time.frameCount ? pendingStates[0] == hash : StateIs(0, hash);
        }

        bool StateIs(int layer, int hash) => hash == (animator.IsInTransition(layer)
            ? animator.GetNextAnimatorStateInfo(layer).fullPathHash : animator.GetCurrentAnimatorStateInfo(layer).fullPathHash);

        public bool Play(string clip, bool restart = false)
        {
            if (!ids.TryGetValue(clip, out int id) || clip is "height" or "height_Add" or "shape" or "shape_Add" || IsDirectionalPose(clip)) return false;
            int layer = clip.EndsWith("upper", StringComparison.Ordinal) ? 1 : 0;
            if (layer == 0)
            {
                animator.SetBool("RifleTurn", clip.StartsWith("rifle", StringComparison.Ordinal));
                if (!clip.EndsWith("run", StringComparison.Ordinal) && !clip.EndsWith("runback", StringComparison.Ordinal)
                    && !clip.EndsWith("stand1", StringComparison.Ordinal) && !clip.EndsWith("ready", StringComparison.Ordinal))
                    SetMovementDirection(0, 0, 1);
            }
            string layerName = layer == 0 ? BaseLayer : UpperLayer;
            string trigger = layer == 0 ? "PlayMotion" : "PlayUpperMotion";
            int hash = UnityEngine.Animator.StringToHash(layerName + "." + clip);
            if (restart)
            {
                if (layer == 1) animator.SetLayerWeight(1, 1);
                animator.ResetTrigger(trigger);
                animator.Play(hash, layer, 0);
                animator.Update(0);
                pendingFrames[layer] = -1;
                return true;
            }
            if (pendingFrames[layer] == Time.frameCount && pendingStates[layer] == hash) return true;
            if (StateIs(layer, hash))
            {
                // Reaffirming the active pose cancels a different request that
                // shared this layer but has not been evaluated yet.
                animator.ResetTrigger(trigger);
                pendingFrames[layer] = -1;
                return true;
            }
            animator.SetInteger(layer == 0 ? "Motion" : "UpperMotion", id);
            animator.SetTrigger(trigger);
            pendingStates[layer] = hash;
            pendingFrames[layer] = Time.frameCount;
            return true;
        }

        void LateUpdate()
        {
            if (!animator || animator.layerCount < 2) return;
            TickVehicleSound();
            for (int layer = 0; layer < pendingFrames.Length; layer++)
                if (pendingFrames[layer] >= 0 && StateIs(layer, pendingStates[layer])) pendingFrames[layer] = -1;
            int emptyHash = UnityEngine.Animator.StringToHash(UpperLayer + ".Empty");
            bool empty = animator.GetCurrentAnimatorStateInfo(1).fullPathHash == emptyHash;
            if (animator.IsInTransition(1))
            {
                bool enteringEmpty = animator.GetNextAnimatorStateInfo(1).fullPathHash == emptyHash;
                float progress = Mathf.Clamp01(animator.GetAnimatorTransitionInfo(1).normalizedTime);
                animator.SetLayerWeight(1, enteringEmpty ? 1 - progress : empty ? progress : 1);
            }
            else if (!empty) animator.SetLayerWeight(1, 1);
            else if (pendingFrames[1] != Time.frameCount) animator.SetLayerWeight(1, 0);
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
