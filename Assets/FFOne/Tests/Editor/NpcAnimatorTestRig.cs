using System;
using FFOne.Content;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FFOne.Tests
{
    sealed class NpcAnimatorTestRig : IDisposable
    {
        readonly string path;
        public NativeNpcAnimator Animation { get; }
        public Animator Animator => Animation.Animator;

        public NpcAnimatorTestRig(GameObject root, params (string Name, bool Loop, float End)[] motions)
        {
            path = AssetDatabase.GenerateUniqueAssetPath("Assets/FFOne/Tests/Editor/NpcAnimatorFixture.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var layer = controller.layers[0];
            layer.name = NativeNpcAnimator.Layer;
            var machine = layer.stateMachine;
            machine.name = NativeNpcAnimator.Layer;
            controller.layers = new[] { layer };
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = NativeNpcAnimator.SpeedParameter,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1
            });
            foreach (var motion in motions)
            {
                string name = motion.Name + (motion.Loop ? NativeNpcAnimator.LoopSuffix : "");
                var clip = new AnimationClip { name = name };
                clip.SetCurve("", typeof(Transform), "localScale.x", AnimationCurve.Constant(0, 1, 1));
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = motion.Loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                if (motion.End > 0) AnimationUtility.SetAnimationEvents(clip,
                    new[] { new AnimationEvent { time = motion.End, functionName = "FFOneAnimationEnd" } });
                AssetDatabase.AddObjectToAsset(clip, controller);
                var state = machine.AddState(name);
                state.motion = clip;
                state.speedParameter = NativeNpcAnimator.SpeedParameter;
                state.speedParameterActive = true;
                if (!machine.defaultState) machine.defaultState = state;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            Animation = root.AddComponent<NativeNpcAnimator>();
            Animator.runtimeAnimatorController = controller;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.Rebind();
            Animator.Update(0);
        }

        public void Advance(float duration)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(duration * 60));
            for (int i = 0; i < steps; i++) Animator.Update(duration / steps);
        }

        public void Dispose()
        {
            if (Animation) Animator.runtimeAnimatorController = null;
            AssetDatabase.DeleteAsset(path);
        }
    }
}
