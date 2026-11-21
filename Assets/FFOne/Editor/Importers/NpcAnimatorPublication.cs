using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFOne.Content;
using FFOne.Tutorial;
using FFOne.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class NpcAnimatorPublication
    {
        static readonly string[] Owners =
        {
            "Assets/FFOne/Content/Characters/NPCs",
            "Assets/FFOne/Content/Characters/Mobs",
            "Assets/FFOne/Content/Characters/Fusions"
        };
        static readonly HashSet<string> LoopMotions = new(StringComparer.Ordinal)
        {
            "nif-default", "idle", "stand", "stand1", "stand2", "stand3", "stand4",
            "walk", "run", "ready", "stun", "sleep"
        };
        static readonly HashSet<string> FiniteMotions = new(StringComparer.Ordinal)
        {
            "death", "die", "melee1", "melee2", "attack1", "hit", "wound"
        };
        enum Operation { Inspect, Publish, RepairNames, Verify }

        sealed class Model
        {
            internal bool NeedsScriptVariants;
            internal readonly List<TutorialActorDefinition> Definitions = new();
        }

        readonly struct SourceClip
        {
            internal readonly string Name;
            internal readonly AnimationClip Clip;
            internal SourceClip(string name, AnimationClip clip) { Name = name; Clip = clip; }
        }

        [MenuItem("FFOne/Animation/Publish NPC Controllers")]
        public static void PublishAll() => Run(false, Operation.Publish);

        public static void PublishTutorial() => Run(true, Operation.Publish);

        public static void Inspect() => Run(false, Operation.Inspect);

        public static void RepairNames()
        {
            Run(false, Operation.RepairNames);
            var controller = AssetDatabase.FindAssets("t:AnimatorController", Owners).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("/ActorMotion/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<AnimatorController>).FirstOrDefault();
            if (!controller) return;
            var state = controller.layers[0].stateMachine.defaultState;
            string expected = state.name;
            string path = AssetDatabase.GetAssetPath(state.motion);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip || clip.name != expected)
                throw new InvalidDataException("The repaired motion name did not survive reimport: " + path);
        }

        public static void Verify() => Run(false, Operation.Verify);

        static void Run(bool tutorialOnly, Operation operation)
        {
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            var models = Discover(tutorialOnly, missing);
            var rows = new List<string> { "status\tpath\tdetail" };
            var failure = new List<Exception>();
            foreach (string route in missing)
                rows.Add("missing route\t" + route);
            try
            {
                EditorApplication.LockReloadAssemblies();
                int completed = 0;
                foreach (var model in models.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    try
                    {
                        string status = Process(model.Key, model.Value, operation);
                        rows.Add(status + "\t" + model.Key);
                    }
                    catch (Exception error)
                    {
                        rows.Add("failed\t" + model.Key + "\t" + error.Message.Replace('\n', ' '));
                        failure.Add(new InvalidOperationException(model.Key, error));
                    }
                    completed++;
                    if (completed % 8 == 0)
                    {
                        GC.Collect();
                        EditorUtility.UnloadUnusedAssetsImmediate(true);
                    }
                    if (completed % 25 == 0)
                        Debug.Log($"NPC animation publication processed {completed}/{models.Count} prefab routes.");
                }
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                Directory.CreateDirectory("Logs/Studio");
                string mode = (tutorialOnly ? "tutorial" : "all") + "-" + operation.ToString().ToLowerInvariant();
                string report = $"Logs/Studio/npc-animator-{mode}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.tsv";
                File.WriteAllLines(report, rows);
                Debug.Log($"NPC animation publication checked {models.Count} prefab routes, " +
                    $"{failure.Count} failed and {missing.Count} bindings are unavailable. {report}");
            }
            if (failure.Count > 0) throw new AggregateException("NPC animation publication failed.", failure);
        }

        static Dictionary<string, Model> Discover(bool tutorialOnly, ISet<string> missing)
        {
            var models = new Dictionary<string, Model>(StringComparer.Ordinal);
            var index = ContentAssets.Load<NativeModelCatalog>(NativeModelAssets.EditableRoot + "/Index");
            if (!index) throw new InvalidDataException("Missing editable model index.");
            var routes = index.Bindings.GroupBy(binding => binding.OriginalResource, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().EditableResource, StringComparer.Ordinal);
            if (!tutorialOnly)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", Owners))
                    Add(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var definition in WorldNpcCatalog.Load().Definitions)
                {
                    if (definition.HiddenMarker || definition.HnpcAppearance >= 0) continue;
                    if (string.IsNullOrEmpty(definition.Model)) missing.Add($"World NPC type {definition.Type}");
                    else AddRoute(definition.Model);
                }
            }
            var tutorial = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            if (!tutorial) throw new InvalidDataException("Missing tutorial content.");
            foreach (var definition in tutorial.Actors)
            {
                var prefab = NativeModelAssets.Resolve(definition.Model);
                if (!prefab) { missing.Add($"Tutorial NPC type {definition.NpcType}"); continue; }
                string path = AssetDatabase.GetAssetPath(prefab);
                if (Add(path) is not { } model) continue;
                model.NeedsScriptVariants = true;
                model.Definitions.Add(definition);
            }
            foreach (string route in DexterShipTimeline.ActorModels)
                if (AddRoute(route) is { } model) model.NeedsScriptVariants = true;
            return models;

            Model AddRoute(string route)
            {
                if (route.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) route = route[..^4];
                string editable = routes.GetValueOrDefault(route) ?? NativeModelAssets.EditableResourcePath(route);
                string path = ContentPaths.ResourcePath(editable) ?? ContentPaths.ResourcePath(route);
                return Add(path, route);
            }

            Model Add(string path, string route = null)
            {
                if (string.IsNullOrEmpty(path)) { missing.Add(route ?? "unnamed prefab"); return null; }
                if (!models.TryGetValue(path, out var model)) models.Add(path, model = new Model());
                return model;
            }
        }

        static string Process(string path, Model model, Operation operation)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new InvalidDataException("Model could not be loaded.");
            var legacy = asset.GetComponentsInChildren<Animation>(true)
                .Where(animation => animation.GetClipCount() > 0).ToArray();
            if (legacy.Length == 0)
            {
                var native = asset.GetComponentsInChildren<NativeNpcAnimator>(true);
                bool repaired = false;
                foreach (var animator in native)
                {
                    if (operation == Operation.RepairNames) repaired |= RepairClipNames(animator);
                    Validate(animator);
                }
                if (repaired) return "repaired clip names";
                return native.Length > 0 ? "wired" : "static model";
            }
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Animated route has no editable prefab.");
            if (legacy.Any(animation => animation.GetComponent<Animator>()))
                throw new InvalidDataException("Legacy and authored Animator are on the same model node.");
            if (operation == Operation.Verify)
                throw new InvalidDataException("Animated model still contains its legacy Animation.");
            if (operation != Operation.Publish) return "ready to publish";

            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var targets = prefab.GetComponentsInChildren<Animation>(true)
                    .Where(animation => animation.GetClipCount() > 0).ToArray();
                if (targets.Length != legacy.Length)
                    throw new InvalidDataException("Prefab instance animation roots differ from the asset.");
                for (int i = 0; i < targets.Length; i++)
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(legacy[i], out string _, out long localId))
                        throw new InvalidDataException("Missing Animation component identity.");
                    var target = targets[i];
                    var controller = Controller(path, target, localId, model.NeedsScriptVariants);
                    var body = target.gameObject;
                    Object.DestroyImmediate(target);
                    var animator = body.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.keepAnimatorStateOnDisable = true;
                    var owner = body.AddComponent<NativeNpcAnimator>();
                    using var serialized = new SerializedObject(owner);
                    serialized.FindProperty("animator").objectReferenceValue = animator;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    owner.AnimationSounds = model.Definitions.SelectMany(definition => definition.AnimationSounds)
                        .GroupBy(sound => sound.Cue, StringComparer.Ordinal).Select(group => group.First()).ToArray();
                    owner.Faces = model.Definitions.SelectMany(definition => definition.Faces)
                        .GroupBy(face => face.Name, StringComparer.Ordinal).Select(group => group.First()).ToArray();
                    Validate(owner);
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, path, out bool saved);
                if (!saved) throw new IOException("Could not save the animator on the existing prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            var reopened = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var animator in reopened.GetComponentsInChildren<NativeNpcAnimator>(true)) Validate(animator);
            return "published";
        }

        static AnimatorController Controller(string prefabPath, Animation animation, long localId, bool scripted)
        {
            string directory = Destination(prefabPath);
            string controllerPath = $"{directory}/motion-{localId}.controller";
            var originals = animation.Cast<AnimationState>().Select(state => new SourceClip(state.name, state.clip))
                .Where(state => state.Clip).OrderBy(state => state.Name, StringComparer.Ordinal).ToArray();
            var variants = originals.SelectMany(source => Variants(source, scripted)).ToArray();
            var stateNames = variants.Select(variant => StateName(variant.Source.Name, variant.Loop)).ToArray();
            ValidateStateNames(stateNames);
            var previous = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (previous)
            {
                var names = previous.layers[0].stateMachine.states.Select(child => child.state.name).ToHashSet(StringComparer.Ordinal);
                if (!variants.All(variant => names.Contains(StateName(variant.Source.Name, variant.Loop))))
                    throw new InvalidDataException("Existing controller has a different source motion set: " + controllerPath);
                RepairClipNames(previous);
                return previous;
            }
            RequireFreePath(controllerPath);
            string clipsDirectory = ClipDirectory(controllerPath);
            EnsureFolder(clipsDirectory);
            AnimationClip[] motions;
            AssetDatabase.StartAssetEditing();
            try { motions = variants.Select(variant => Copy(clipsDirectory, variant.Source, variant.Loop)).ToArray(); }
            finally { AssetDatabase.StopAssetEditing(); }
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = NativeNpcAnimator.SpeedParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1
            });
            var layer = controller.layers.Single();
            layer.name = NativeNpcAnimator.Layer;
            var machine = layer.stateMachine;
            machine.name = NativeNpcAnimator.Layer;
            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            for (int i = 0; i < motions.Length; i++)
            {
                var state = machine.AddState(motions[i].name, new Vector3(250 + i % 6 * 240, 90 + i / 6 * 65));
                state.motion = motions[i];
                state.speedParameterActive = true;
                state.speedParameter = NativeNpcAnimator.SpeedParameter;
                if (!AssetDatabase.Contains(state)) AssetDatabase.AddObjectToAsset(state, controller);
                EditorUtility.SetDirty(state);
                states.Add(state.name, state);
            }
            string initial = originals.FirstOrDefault(source => source.Clip == animation.clip).Name ?? "stand1";
            machine.defaultState = states.GetValueOrDefault(StateName(initial, true))
                ?? states.GetValueOrDefault(initial) ?? machine.states[0].state;
            controller.layers = new[] { layer };
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        static IEnumerable<(SourceClip Source, bool Loop)> Variants(SourceClip source, bool scripted)
        {
            bool sourceLoops = source.Clip.wrapMode == WrapMode.Loop || AnimationUtility.GetAnimationClipSettings(source.Clip).loopTime;
            yield return (source, sourceLoops);
            if (scripted || (sourceLoops ? FiniteMotions.Contains(source.Name) : LoopMotions.Contains(source.Name)))
                yield return (source, !sourceLoops);
        }

        static string StateName(string clip, bool loop) => loop ? clip + NativeNpcAnimator.LoopSuffix : clip;
        static string ClipDirectory(string controllerPath) => controllerPath[..^".controller".Length];

        static AnimationClip Copy(string directory, SourceClip source, bool loop)
        {
            string name = StateName(source.Name, loop);
            string path = $"{directory}/{name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                if (existing.legacy || existing.name != name || existing.isLooping != loop)
                    throw new InvalidDataException("Existing editable motion has a different playback identity: " + path);
                return existing;
            }
            RequireFreePath(path);
            var copy = Object.Instantiate(source.Clip);
            copy.name = name;
            copy.hideFlags = HideFlags.None;
            copy.legacy = false;
            copy.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            if (!loop) settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AnimationUtility.SetAnimationEvents(copy, AnimationUtility.GetAnimationEvents(source.Clip));
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        static void ValidateStateNames(string[] names)
        {
            if (names.Length == 0 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
                throw new InvalidDataException("Actor has an empty or ambiguous motion inventory.");
            foreach (string name in names)
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    name.TrimEnd(' ', '.') != name || Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Actor motion name cannot be used as a Unity asset filename: " + name);
        }

        static bool RepairClipNames(NativeNpcAnimator owner) =>
            owner.Animator.runtimeAnimatorController is AnimatorController controller && RepairClipNames(controller);

        static bool RepairClipNames(AnimatorController controller)
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (!controllerPath.Contains("/ActorMotion/", StringComparison.Ordinal)) return false;
            string sourceDirectory = Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            string clipDirectory = ClipDirectory(controllerPath);
            var states = controller.layers[0].stateMachine.states.Select(child => child.state).ToArray();
            ValidateStateNames(states.Select(state => state.name).ToArray());
            var moves = states.Select(state =>
                (Source: AssetDatabase.GetAssetPath(state.motion), Destination: $"{clipDirectory}/{state.name}.anim", Name: state.name))
                .Where(move => move.Source != move.Destination).ToArray();
            if (moves.Length == 0) return false;
            foreach (var move in moves)
            {
                if (Path.GetDirectoryName(move.Source).Replace('\\', '/') != sourceDirectory ||
                    !Regex.IsMatch(Path.GetFileName(move.Source), "^[0-9a-f]{32}--?[0-9]+-[0-9a-f]{8}-(loop|once)\\.anim$"))
                    throw new InvalidDataException("Controller references a motion outside the generated repair set: " + move.Source);
                RequireFreePath(move.Destination);
            }
            EnsureFolder(clipDirectory);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var move in moves)
                {
                    string error = AssetDatabase.MoveAsset(move.Source, move.Destination);
                    if (!string.IsNullOrEmpty(error)) throw new IOException(error);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var move in moves)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(move.Destination);
                if (!clip) throw new InvalidDataException("Could not reload moved motion: " + move.Destination);
                if (clip.name == move.Name) continue;
                clip.name = move.Name;
                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
            return true;
        }

        static void RequireFreePath(string path)
        {
            if (File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path))
                throw new IOException("An unrelated asset occupies " + path);
        }

        static string Destination(string prefabPath)
        {
            string directory = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            if (directory.Contains("/Prefabs/", StringComparison.Ordinal))
                directory = directory.Replace("/Prefabs/", "/Animation/", StringComparison.Ordinal);
            return directory + "/ActorMotion";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException("Could not create " + path);
        }

        static void Validate(NativeNpcAnimator owner)
        {
            var animator = owner.Animator;
            if (!animator || animator.applyRootMotion || animator.runtimeAnimatorController is not AnimatorController controller ||
                controller.layers.Length == 0 || controller.layers[0].name != NativeNpcAnimator.Layer)
                throw new InvalidDataException("Prefab Animator is missing its editable motion layer.");
            if (controller.parameters.All(parameter => parameter.name != NativeNpcAnimator.SpeedParameter ||
                parameter.type != AnimatorControllerParameterType.Float))
                throw new InvalidDataException("Actor motion controller has no speed parameter.");
            var states = controller.layers[0].stateMachine.states;
            if (states.Length == 0 || states.Any(child => child.state.motion is not AnimationClip clip || clip.legacy ||
                child.state.name != clip.name || clip.isLooping != clip.name.EndsWith(NativeNpcAnimator.LoopSuffix, StringComparison.Ordinal)))
                throw new InvalidDataException("Actor controller has an invalid motion binding.");
        }
    }
}
