using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FFOne.Editor
{
    public static class PlayerAnimatorSetup
    {
        public const string Directory = "Assets/Resources/characters/player/shared/animation";
        // Retrobution CharacterSelection.resourceFile: male 34404 and female
        // 34533 end events, dispatched through cnAvatarAnimation.EndAnimation.
        const float StandupEndSeconds = 2.0833335f;
        // male_skeleton.glb, angry, emote_face_off. The final bone key precedes this cue.
        const float MaleAngryFaceOff = 1.254f;
        const float TutorialReturnBlend = .3f;

        [MenuItem("FFOne/Animation/Rebuild Player Controllers")]
        public static void Rebuild()
        {
            var catalog = JObject.Parse(FFOne.Content.ContentAssets.Load<TextAsset>("characters/player/shared/player_rig_contract").text);
            foreach (var rig in catalog["genders"]) Build(rig);
            AssetDatabase.SaveAssets();
            Debug.Log("FFOne player Animator Controllers rebuilt from every accepted male/female clip.");
        }

        static IEnumerable<string> HnpcClips(Animation source) => FFOne.World.WorldNpcCatalog.Load().Definitions
            .Where(npc => npc.IdleClips != null).SelectMany(npc => npc.IdleClips)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)
            .Where(name => name.Length > 0 && source.GetClip(name));

        static readonly string[] DodgeClips = { "stickdash", "rifledash", "rifletumbling", "rocketsomersault", "rifledodgeupper", "stickdodgeupper" };

        static IEnumerable<(string name, bool loop)> SupplementalMotions(Animation source)
        {
            if (source.GetClip("stand4")) yield return ("stand4", true);
            foreach (string name in HnpcClips(source).Where(name => name != "stand4")) yield return (name, true);
            foreach (string name in DodgeClips)
            {
                if (!source.GetClip(name)) throw new InvalidDataException("Missing accepted dodge clip: " + name);
                yield return (name, false);
            }
        }

        [MenuItem("FFOne/Animation/Publish HNPC Idle and Dodge Motions")]
        public static void PublishHnpcIdleMotions()
        {
            var rigs = JObject.Parse(ContentAssets.Load<TextAsset>("characters/player/shared/player_rig_contract").text);
            foreach (var rig in rigs["genders"])
            {
                string gender = (string)rig["gender"];
                string controllerPath = Directory + "/" + gender + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ContentPaths.Resolve(controllerPath));
                var profile = AssetDatabase.LoadAssetAtPath<NativePlayerAnimationProfile>(ContentPaths.Resolve(Directory + "/" + gender + "-profile.asset"));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve("Assets/Resources/" + (string)rig["skeletonGlb"]));
                if (!prefab || !profile || !controller || profile.Controller != controller)
                    throw new InvalidDataException("Missing or unbound player rig/profile for " + gender);
                var source = prefab.GetComponent<Animation>();
                var names = profile.ClipNames.ToList();
                var bindPose = AssetDatabase.LoadAssetAtPath<AnimationClip>(ContentPaths.Resolve(Directory + "/" + gender + "/bind-pose.anim"));
                if (!bindPose || !source) throw new InvalidDataException("Incomplete accepted player rig: " + gender);
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(bindPose)).Replace('\\', '/');
                if (gender == "male")
                {
                    var angry = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/angry.anim");
                    if (angry && NpcMotionRepair.HoldPoseThrough(angry, MaleAngryFaceOff))
                    {
                        EditorUtility.SetDirty(angry);
                        AssetDatabase.SaveAssetIfDirty(angry);
                    }
                }
                foreach (var (name, loop) in SupplementalMotions(source))
                {
                    if (names.Contains(name)) continue;
                    bool upper = name.EndsWith("upper", StringComparison.Ordinal);
                    string layerName = upper ? NativePlayerAnimator.UpperLayer : NativePlayerAnimator.BaseLayer;
                    var machine = controller.layers.Single(layer => layer.name == layerName).stateMachine;
                    if (machine.states.Any(row => row.state.name == name))
                        throw new InvalidDataException("Existing unindexed HNPC state: " + gender + "/" + name);
                    string path = folder + "/" + name + ".anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (!clip)
                    {
                        clip = new AnimationClip();
                        EditorUtility.CopySerialized(source.GetClip(name), clip);
                        clip.name = name;
                        clip.legacy = false;
                        clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                        var settings = AnimationUtility.GetAnimationClipSettings(clip);
                        settings.loopTime = settings.loopBlend = loop;
                        settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
                        AnimationUtility.SetAnimationClipSettings(clip, settings);
                        if (!upper) CompleteBasePose(clip, prefab);
                        clip.EnsureQuaternionContinuity();
                        AssetDatabase.CreateAsset(clip, path);
                    }
                    var state = State(machine, name, clip, names.Count, controllerPath);
                    var request = machine.AddAnyStateTransition(state);
                    request.hasExitTime = false;
                    request.hasFixedDuration = true;
                    request.duration = .15f;
                    request.canTransitionToSelf = true;
                    request.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
                    request.orderedInterruption = false;
                    request.AddCondition(AnimatorConditionMode.Equals, names.Count, upper ? "UpperMotion" : "Motion");
                    request.AddCondition(AnimatorConditionMode.If, 0, upper ? "PlayUpperMotion" : "PlayMotion");
                    Persist(request, controllerPath);
                    if (!loop)
                    {
                        var destination = machine.states.Single(row => row.state.name == (upper ? "Empty" : Next(name))).state;
                        Return(state, destination, controllerPath);
                    }
                    EditorUtility.SetDirty(machine);
                    names.Add(name);
                }
                if (names.Count == profile.ClipNames.Length) continue;
                profile.ClipNames = names.ToArray();
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
        }

        [MenuItem("FFOne/Animation/Publish Tutorial Motion Transitions")]
        public static void PublishTutorialMotionTransitions()
        {
            var transitions = new List<(AnimatorController Controller, AnimatorStateTransition Transition, float ExitTime)>();
            foreach (string gender in new[] { "male", "female" })
            {
                string path = ContentPaths.Resolve(Directory + "/" + gender + ".controller");
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (!controller) throw new FileNotFoundException("Accepted player controller is missing: " + path);
                var states = controller.layers.Single(l => l.name == NativePlayerAnimator.BaseLayer).stateMachine.states;
                foreach (string name in new[] { "staying", "standup" })
                {
                    var state = states.Single(s => s.state.name == name).state;
                    var transition = state.transitions.Single(t => t.destinationState && t.destinationState.name == "stand1");
                    transitions.Add((controller, transition, TutorialExitTime(name, state.motion as AnimationClip)));
                }
            }
            foreach (var (controller, transition, exitTime) in transitions)
            {
                transition.hasExitTime = true;
                transition.exitTime = exitTime;
                transition.hasFixedDuration = true;
                transition.duration = TutorialReturnBlend;
                EditorUtility.SetDirty(transition);
                EditorUtility.SetDirty(controller);
            }
            foreach (var controller in transitions.Select(row => row.Controller).Distinct()) AssetDatabase.SaveAssetIfDirty(controller);
            Debug.Log("FFOne tutorial staying/standup transitions published to male and female Animator Controllers.");
        }

        static bool IsTutorialRest(string name) => name is "staying" or "standup";
        static float TutorialExitTime(string name, AnimationClip clip)
        {
            if (!clip || clip.length < (name == "standup" ? StandupEndSeconds : Mathf.Epsilon))
                throw new InvalidDataException("Accepted tutorial motion has no valid end sample: " + name);
            // The accepted FFOneClient rig holds staying to clip completion when
            // the script has not yet issued standup.
            return name == "standup" ? StandupEndSeconds / clip.length : 1;
        }

        static void Build(JToken rig)
        {
            string gender = (string)rig["gender"];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve("Assets/Resources/" + (string)rig["skeletonGlb"]));
            if (!prefab) throw new FileNotFoundException("Missing accepted player skeleton: " + gender);
            var source = prefab.GetComponent<Animation>();
            if (!source) throw new InvalidDataException("Accepted rig has no imported clips: " + gender);
            System.IO.Directory.CreateDirectory(ContentPaths.Resolve(Directory + "/" + gender));
            AssetDatabase.Refresh();
            var reference = BindPose(prefab, Directory + "/" + gender + "/bind-pose.anim");
            var vehicle = NativeData.Object("characters/player/shared/vehicle_animations")["genders"].Single(g => (string)g["gender"] == gender);
            var locomotion = NativeData.Object("characters/player/shared/locomotion_animations")["genders"].Single(g => (string)g["gender"] == gender);
            var damage = NativeData.Object("characters/player/shared/damage_animations")["genders"].Single(g => (string)g["gender"] == gender);
            var rows = rig["clips"].Concat(vehicle["clips"]).Concat(locomotion["clips"]).Concat(damage["clips"])
                .OrderBy(c => (int)c["gltfAnimationIndex"]).ToArray();
            // The XDT stores each human NPC's three ambient choices in its mesh row.
            // Keep the existing motion indices and append only clips present on this rig.
            var currentNames = new HashSet<string>(rows.Select(row => (string)row["name"]), StringComparer.Ordinal);
            var ambientClips = SupplementalMotions(source).Where(clip => !currentNames.Contains(clip.name))
                .Select(clip => (JToken)new JObject { ["name"] = clip.name, ["playback"] = clip.loop ? "loop" : "once" });
            rows = rows.Concat(ambientClips).ToArray();
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string name = (string)row["name"];
                var imported = source.GetClip(name);
                if (!imported) throw new InvalidDataException("Missing accepted animation: " + gender + "/" + name);
                // New clips belong beside the accepted bind pose. A new historical
                // identity has no migration record and Resolve alone would send it
                // to Incoming rather than the established animation owner.
                string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(reference)).Replace('\\', '/') + "/" + name + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                EditorUtility.CopySerialized(imported, clip);
                clip.name = name; clip.legacy = false;
                bool loop = (string)row["playback"] == "loop";
                clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = loop; settings.loopBlend = loop;
                settings.keepOriginalOrientation = true;
                settings.keepOriginalPositionY = true; settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                if (name is not ("height" or "height_Add" or "shape" or "shape_Add") && !NativePlayerAnimator.IsDirectionalPose(name) && !name.EndsWith("upper", StringComparison.Ordinal))
                    CompleteBasePose(clip, prefab);
                if (name.EndsWith("_Add", StringComparison.Ordinal) || NativePlayerAnimator.IsDirectionalPose(name)) BakeDeltaOntoBindPose(clip, prefab);
                if (name is "height" or "height_Add" or "shape" or "shape_Add" || NativePlayerAnimator.IsDirectionalPose(name))
                    AnimationUtility.SetAdditiveReferencePose(clip, reference, 0);
                clip.EnsureQuaternionContinuity();
                if (gender == "male" && name == "angry") NpcMotionRepair.HoldPoseThrough(clip, MaleAngryFaceOff);
                EditorUtility.SetDirty(clip); clips.Add(name, clip);
            }
            string controllerPath = Directory + "/" + gender + ".controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ContentPaths.Resolve(controllerPath));
            if (!controller) { controller = new AnimatorController(); AssetDatabase.CreateAsset(controller, ContentPaths.Resolve(controllerPath)); }
            controller.name = gender + " Player"; controller.layers = Array.Empty<AnimatorControllerLayer>();
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            foreach (var old in AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(controllerPath)).Where(o => o != controller)) UnityEngine.Object.DestroyImmediate(old, true);
            controller.AddParameter("Motion", AnimatorControllerParameterType.Int);
            controller.AddParameter("PlayMotion", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("UpperMotion", AnimatorControllerParameterType.Int);
            controller.AddParameter("PlayUpperMotion", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Height", AnimatorControllerParameterType.Float);
            controller.AddParameter("BodyShape", AnimatorControllerParameterType.Float);
            controller.AddParameter("LeftTurn", AnimatorControllerParameterType.Float);
            controller.AddParameter("RightTurn", AnimatorControllerParameterType.Float);
            controller.AddParameter("RifleTurn", AnimatorControllerParameterType.Bool);
            var layers = new List<AnimatorControllerLayer>();
            var baseMachine = Machine(NativePlayerAnimator.BaseLayer, controllerPath);
            var upperMachine = Machine(NativePlayerAnimator.UpperLayer, controllerPath);
            var baseStates = new Dictionary<string, AnimatorState>();
            var upperStates = new Dictionary<string, AnimatorState>();
            var empty = State(upperMachine, "Empty", null, 0, controllerPath);
            upperMachine.defaultState = empty;
            for (int i = 0; i < rows.Length; i++)
            {
                string name = (string)rows[i]["name"];
                if (name is "height" or "height_Add" or "shape" or "shape_Add" || NativePlayerAnimator.IsDirectionalPose(name)) continue;
                bool upper = name.EndsWith("upper", StringComparison.Ordinal);
                var machine = upper ? upperMachine : baseMachine;
                var state = State(machine, name, clips[name], i, controllerPath);
                (upper ? upperStates : baseStates).Add(name, state);
                var request = machine.AddAnyStateTransition(state);
                request.hasExitTime = false;
                request.duration = NativeVehicleAnimation.EndSeconds(name) > 0 ? NativeVehicleAnimation.EntryBlendSeconds : .15f;
                request.hasFixedDuration = true;
                request.canTransitionToSelf = true;
                request.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
                request.orderedInterruption = false;
                request.AddCondition(AnimatorConditionMode.Equals, i, upper ? "UpperMotion" : "Motion");
                request.AddCondition(AnimatorConditionMode.If, 0, upper ? "PlayUpperMotion" : "PlayMotion");
                Persist(request, controllerPath);
            }
            baseMachine.defaultState = baseStates["stand1"];
            foreach (var row in rows)
            {
                string name = (string)row["name"];
                if (name is "height" or "height_Add" or "shape" or "shape_Add" || NativePlayerAnimator.IsDirectionalPose(name)) continue;
                float vehicleEnd = NativeVehicleAnimation.EndSeconds(name);
                // MakeUperLayer assigns Loop to vehicle run/back/jump/idle. Their
                // end callbacks do not authorize a transition that resets playback.
                if ((string)row["playback"] == "loop") continue;
                if (upperStates.TryGetValue(name, out var upper)) Return(upper, empty, controllerPath);
                else if (IsTutorialRest(name)) Return(baseStates[name], baseStates[Next(name)], controllerPath,
                    TutorialExitTime(name, clips[name]), TutorialReturnBlend);
                else Return(baseStates[name], baseStates[Next(name)], controllerPath, vehicleEnd > 0 ? vehicleEnd / clips[name].length : 1,
                    NativeVehicleAnimation.NextClip(name) != null ? NativeVehicleAnimation.ReturnBlendSeconds : .15f);
            }
            layers.Add(new AnimatorControllerLayer { name = NativePlayerAnimator.BaseLayer, stateMachine = baseMachine, defaultWeight = 1 });
            var mask = new AvatarMask { name = "Upper Body" };
            var paths = prefab.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, prefab.transform)).ToArray();
            mask.transformCount = paths.Length;
            for (int i = 0; i < paths.Length; i++) { mask.SetTransformPath(i, paths[i]); mask.SetTransformActive(i, paths[i].Contains("/Bip01 Spine", StringComparison.Ordinal)); }
            Persist(mask, controllerPath);
            layers.Add(new AnimatorControllerLayer { name = NativePlayerAnimator.UpperLayer, stateMachine = upperMachine, defaultWeight = 1, avatarMask = mask });
            foreach (var turn in new[] { (NativePlayerAnimator.LeftTurnLayer, "turnleft", "LeftTurn"), (NativePlayerAnimator.RightTurnLayer, "turnright", "RightTurn") })
            {
                var machine = Machine(turn.Item1, controllerPath);
                foreach (string prefix in new[] { "", "rifle" })
                {
                    var state = State(machine, prefix + turn.Item2, clips[prefix + turn.Item2], 0, controllerPath);
                    state.timeParameter = turn.Item3; state.timeParameterActive = true; state.speed = 0;
                    if (prefix.Length == 0) machine.defaultState = state;
                    var transition = machine.AddAnyStateTransition(state);
                    transition.hasExitTime = false; transition.duration = 0; transition.canTransitionToSelf = false;
                    transition.AddCondition(prefix.Length == 0 ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, "RifleTurn");
                    Persist(transition, controllerPath);
                }
                layers.Add(new AnimatorControllerLayer { name = turn.Item1, stateMachine = machine, defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Additive });
            }
            foreach (var pose in new[] { "height_Add", "height", "shape_Add", "shape" })
            {
                var machine = Machine(pose, controllerPath);
                var state = State(machine, pose, clips[pose], 0, controllerPath);
                state.timeParameter = pose.StartsWith("height", StringComparison.Ordinal) ? "Height" : "BodyShape";
                state.timeParameterActive = true; state.speed = 0; machine.defaultState = state;
                layers.Add(new AnimatorControllerLayer { name = pose, stateMachine = machine, defaultWeight = 1, blendingMode = AnimatorLayerBlendingMode.Additive });
            }
            controller.layers = layers.ToArray(); EditorUtility.SetDirty(controller);
            string avatarPath = Directory + "/" + gender + "-avatar.asset";
            var builtAvatar = AvatarBuilder.BuildGenericAvatar(prefab, "");
            builtAvatar.name = gender + " Accepted Rig";
            if (!builtAvatar.isValid) throw new InvalidDataException("Invalid generic avatar: " + gender);
            var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ContentPaths.Resolve(avatarPath));
            if (!avatar) { avatar = builtAvatar; AssetDatabase.CreateAsset(avatar, ContentPaths.Resolve(avatarPath)); }
            else { EditorUtility.CopySerialized(builtAvatar, avatar); UnityEngine.Object.DestroyImmediate(builtAvatar); EditorUtility.SetDirty(avatar); }
            string profilePath = Directory + "/" + gender + "-profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<NativePlayerAnimationProfile>(ContentPaths.Resolve(profilePath));
            if (!profile) { profile = ScriptableObject.CreateInstance<NativePlayerAnimationProfile>(); AssetDatabase.CreateAsset(profile, ContentPaths.Resolve(profilePath)); }
            profile.Controller = controller; profile.Avatar = avatar;
            profile.ClipNames = rows.Select(r => (string)r["name"]).ToArray(); EditorUtility.SetDirty(profile);
        }

        static string Next(string name)
        {
            foreach (string weapon in new[] { "board_", "scooter_", "rifle", "stick", "pistol", "bomb", "rocket", "" })
            {
                if (name == weapon + "jumpstart") return weapon + "jump";
                if (name == weapon + "jumplandrun") return weapon + "run";
                if (weapon.Length > 0 && name.StartsWith(weapon, StringComparison.Ordinal)) return weapon + "stand1";
            }
            return name == "die" ? "death" : "stand1";
        }
        static AnimatorStateMachine Machine(string name, string path)
        {
            var machine = new AnimatorStateMachine { name = name }; Persist(machine, path); return machine;
        }
        static AnimatorState State(AnimatorStateMachine machine, string name, Motion clip, int index, string path)
        {
            var state = machine.AddState(name, new Vector3(250 + index % 8 * 230, 80 + index / 8 * 80));
            state.motion = clip; state.writeDefaultValues = false; Persist(state, path); return state;
        }
        static void Return(AnimatorState source, AnimatorState destination, string path, float exitTime = 1, float duration = .15f)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = true; transition.exitTime = exitTime;
            transition.hasFixedDuration = true; transition.duration = duration;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
            transition.orderedInterruption = false; Persist(transition, path);
        }
        static void Persist(UnityEngine.Object value, string path)
        {
            if (!AssetDatabase.Contains(value)) AssetDatabase.AddObjectToAsset(value, ContentPaths.Resolve(path));
        }
        static AnimationClip BindPose(GameObject root, string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ContentPaths.Resolve(path));
            if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, ContentPaths.Resolve(path)); }
            clip.ClearCurves(); clip.name = "bind-pose"; clip.legacy = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string target = AnimationUtility.CalculateTransformPath(t, root.transform);
                for (int i = 0; i < 3; i++)
                {
                    clip.SetCurve(target, typeof(Transform), "localPosition." + "xyz"[i], AnimationCurve.Constant(0, 1, t.localPosition[i]));
                    clip.SetCurve(target, typeof(Transform), "localScale." + "xyz"[i], AnimationCurve.Constant(0, 1, t.localScale[i]));
                }
                for (int i = 0; i < 4; i++) clip.SetCurve(target, typeof(Transform), "localRotation." + "xyzw"[i], AnimationCurve.Constant(0, 1, t.localRotation[i]));
            }
            EditorUtility.SetDirty(clip); return clip;
        }
        static void CompleteBasePose(AnimationClip clip, GameObject root)
        {
            // Additive layers need a fresh base value on every frame. Source motions
            // often omit static scale/helper tracks; leaving them unwritten would
            // repeatedly add body deformation to the previous frame's result.
            var bindings = new HashSet<string>(AnimationUtility.GetCurveBindings(clip).Select(b => b.path + "/" + b.propertyName));
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(t, root.transform);
                for (int axis = 0; axis < 3; axis++)
                {
                    Add("m_LocalPosition.", "localPosition.", "xyz"[axis], t.localPosition[axis]);
                    Add("m_LocalScale.", "localScale.", "xyz"[axis], t.localScale[axis]);
                }
                for (int axis = 0; axis < 4; axis++) Add("m_LocalRotation.", "localRotation.", "xyzw"[axis], t.localRotation[axis]);
                void Add(string serialized, string property, char component, float value)
                {
                    if (!bindings.Contains(path + "/" + serialized + component))
                        clip.SetCurve(path, typeof(Transform), property + component, AnimationCurve.Constant(0, clip.length, value));
                }
            }
        }
        internal static void BakeDeltaOntoBindPose(AnimationClip clip, GameObject root)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var group in bindings.GroupBy(b => b.path))
            {
                var target = group.Key.Length == 0 ? root.transform : root.transform.Find(group.Key);
                if (!target) throw new InvalidDataException("Additive pose target missing: " + group.Key);
                foreach (var binding in group.Where(b => b.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                    || b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)))
                {
                    int axis = "xyz".IndexOf(binding.propertyName[^1]);
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    var keys = curve.keys;
                    // Native additive translation and scale tracks both encode zero deltas.
                    // Mecanim needs their absolute reference value before subtracting the bind pose.
                    float reference = binding.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal) ? target.localScale[axis] : target.localPosition[axis];
                    for (int i = 0; i < keys.Length; i++) keys[i].value += reference;
                    curve.keys = keys; AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                var rotations = group.Where(b => b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)).OrderBy(b => "xyzw".IndexOf(b.propertyName[^1])).ToArray();
                if (rotations.Length == 0) continue;
                if (rotations.Length != 4) throw new InvalidDataException("Incomplete additive quaternion: " + group.Key);
                var curves = rotations.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
                var output = curves.Select(c => c.keys).ToArray();
                for (int k = 0; k < output[0].Length; k++)
                {
                    var value = target.localRotation * new Quaternion(output[0][k].value, output[1][k].value, output[2][k].value, output[3][k].value);
                    var incoming = target.localRotation * new Quaternion(output[0][k].inTangent, output[1][k].inTangent, output[2][k].inTangent, output[3][k].inTangent);
                    var outgoing = target.localRotation * new Quaternion(output[0][k].outTangent, output[1][k].outTangent, output[2][k].outTangent, output[3][k].outTangent);
                    for (int axis = 0; axis < 4; axis++) { output[axis][k].value = value[axis]; output[axis][k].inTangent = incoming[axis]; output[axis][k].outTangent = outgoing[axis]; }
                }
                for (int axis = 0; axis < 4; axis++) { curves[axis].keys = output[axis]; AnimationUtility.SetEditorCurve(clip, rotations[axis], curves[axis]); }
            }
        }
    }
}
