using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class NpcMotionRepair
    {
        public static void PublishCheeseSkills()
        {
            const string directory = "Assets/FFOne/Content/Characters/Nanos/Cheese";
            const string source = "Assets/FFOne/Editor/ActorMotionSources/AcademyCheeseSkills.glb";
            const string prefabPath = directory + "/Prefabs/Cheese.prefab";
            var motions = AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().OrderBy(c => c.name).ToArray();
            if (!motions.Select(c => c.name).SequenceEqual(new[] { "skill2", "skill3" }))
                throw new InvalidDataException("Expected the two source Academy Cheese skill clips.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var actor = prefab.GetComponentInChildren<Animation>(true);
            var clips = AnimationUtility.GetAnimationClips(actor).ToList();
            int initialCount = clips.Count;
            foreach (var motion in motions)
            {
                var bindings = AnimationUtility.GetCurveBindings(motion);
                if (!motion.legacy || bindings.Length == 0 || bindings.Any(b => !actor.transform.Find(b.path)))
                    throw new InvalidDataException("Source curves do not bind to the published Cheese rig: " + motion.name);
                string path = directory + "/Animation/" + motion.name + ".anim";
                var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!saved)
                {
                    saved = new AnimationClip();
                    AssetDatabase.CreateAsset(saved, path);
                }
                if (clips.Any(c => c.name == motion.name && c != saved))
                    throw new InvalidDataException("Another Cheese clip already owns " + motion.name);
                EditorUtility.CopySerialized(motion, saved);
                saved.name = motion.name;
                // WorldNanoPresentation plays the source sound and end cues exactly once.
                AnimationUtility.SetAnimationEvents(saved, Array.Empty<AnimationEvent>());
                EditorUtility.SetDirty(saved);
                AssetDatabase.SaveAssetIfDirty(saved);
                if (!clips.Contains(saved)) clips.Add(saved);
            }
            if (clips.Count != initialCount)
            {
                // Unity renames the serialized root to the prefab filename during a
                // normal save. Retain this accepted prefab's original "model" root.
                var before = File.ReadAllText(prefabPath);
                var field = new System.Text.RegularExpressions.Regex(@"(?m)^  m_Animations:\r?\n(?:  - \{fileID: [^}\n]+\}\r?\n)+");
                if (field.Matches(before).Count != 1) throw new InvalidDataException("Expected one Cheese clip list.");
                AnimationUtility.SetAnimationClips(actor, clips.ToArray());
                EditorUtility.SetDirty(actor);
                PrefabUtility.SavePrefabAsset(prefab);
                var after = File.ReadAllText(prefabPath);
                if (field.Matches(after).Count != 1) throw new InvalidDataException("Missing saved Cheese clip list.");
                File.WriteAllText(prefabPath, field.Replace(before, _ => field.Match(after).Value, 1));
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);
            }
            VerifyCheeseSkills();
        }

        public static void VerifyCheeseSkills()
        {
            const string prefabPath = "Assets/FFOne/Content/Characters/Nanos/Cheese/Prefabs/Cheese.prefab";
            const string source = "Assets/FFOne/Editor/ActorMotionSources/AcademyCheeseSkills.glb";
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            var published = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            var mesh = new Mesh();
            try
            {
                var expectedSkin = reference.GetComponentInChildren<SkinnedMeshRenderer>();
                var actualSkin = published.GetComponentInChildren<SkinnedMeshRenderer>();
                var player = published.GetComponentInChildren<Animation>();
                foreach (var original in AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>())
                {
                    var clip = player.GetClip(original.name);
                    if (!clip || clip.length != original.length) throw new InvalidDataException("Missing Cheese ability duration.");
                    Vector3[] earlier = null;
                    float movement = 0, error = 0;
                    foreach (float phase in new[] { 0f, .13f, .39f, .61f, .88f, 1f })
                    {
                        original.SampleAnimation(reference, phase * original.length);
                        clip.SampleAnimation(published, phase * original.length);
                        expectedSkin.BakeMesh(mesh);
                        var expected = mesh.vertices;
                        actualSkin.BakeMesh(mesh);
                        var actual = mesh.vertices;
                        for (int i = 0; i < actual.Length; i++)
                        {
                            error = Mathf.Max(error, Vector3.Distance(expected[i], actual[i]));
                            if (earlier != null) movement = Mathf.Max(movement, Vector3.Distance(actual[i], earlier[i]));
                        }
                        earlier = actual;
                    }
                    if (error > .00001f || movement < .0001f) throw new InvalidDataException("Cheese ability skin differs from its source: " + original.name);
                    Debug.Log($"Cheese {clip.name}: {earlier.Length} vertices, {clip.length:R}s, six poses, largest source difference {error:R}, motion {movement:R}");
                }
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(reference); Object.DestroyImmediate(published); }
        }

        const string Actors = "Assets/FFOne/Content/Characters";
        static readonly HashSet<string> DeltaActors = new(StringComparer.OrdinalIgnoreCase)
            { "mob_cerberus", "mob_oilmonster", "mob_sneakyspawn" };
        // Source event tails and non-transform curves identified in the actor audit.
        static readonly HashSet<string> SourceTracks = new(StringComparer.OrdinalIgnoreCase)
        {
            "fusion_belladonnaphase1", "fusion_belladonna", "fusion_belladonnashield", "fusion_ben",
            "fusion_iceking", "fusion_gunter", "fusion_spidermonkey", "npc_iceking", "npc_penguin", "mob_drum",
            "Runty", "Belladonna", "Computress", "npc_bubbie2", "npc_portalbase", "npc_unstablenano"
        };

        [MenuItem("FFOne/Animation/Repair Actor Motion")]
        public static void Publish()
        {
            var rows = new List<string> { "prefab\tcorrectedTracks\tnewOverlayStates" };
            var failures = new List<Exception>();
            string[] groups = { "NPCs", "Mobs", "Fusions", "Nanos" };
            var folders = groups.Select(group => Actors + "/" + group).ToArray();
            var paths = AssetDatabase.FindAssets("t:Prefab", folders).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("/Prefabs/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                try
                {
                    string directory = path[..path.IndexOf("/Prefabs/", StringComparison.Ordinal)];
                    string family = Path.GetFileName(directory);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var owner = prefab.GetComponentInChildren<NativeNpcAnimator>(true);
                    int tracks = 0;
                    if (SourceTracks.Contains(family))
                    {
                        foreach (string source in Directory.GetFiles(directory + "/Models/Source", "*.glb"))
                            tracks += ApplySource(prefab, owner, source);
                    }
                    int actions = owner ? AddOverlays(owner, DeltaActors.Contains(family)) : 0;
                    rows.Add($"{path}\t{tracks}\t{actions}");
                }
                catch (Exception e)
                {
                    rows.Add($"{path}\tFAILED\t{e.Message}");
                    failures.Add(new InvalidDataException(path, e));
                    Debug.LogError($"{path}: {e}");
                }
                if ((i + 1) % 12 == 0)
                {
                    EditorUtility.UnloadUnusedAssetsImmediate(true);
                    if ((i + 1) % 60 == 0) Debug.Log($"Actor motion repair {i + 1}/{paths.Length}");
                }
            }
            PlayerAnimatorSetup.PublishHnpcIdleMotions();
            const string log = "Logs/Studio/ActorRepair-20261120/animation";
            Directory.CreateDirectory(log);
            File.WriteAllLines(log + "/publication.tsv", rows);
            if (failures.Count != 0) throw new AggregateException("Actor motion publication", failures);
            Debug.Log($"Published actor motions across {paths.Length} prefabs.");
        }

        [MenuItem("FFOne/Animation/Publish Actor Material Tracks")]
        public static void PublishMaterialTracks()
        {
            var rows = new List<string> { "family\tpublishedBindings" };
            foreach (string family in new[] { "npc_bubbie2", "npc_portalbase", "npc_unstablenano" })
            {
                string folder = Actors + "/NPCs/" + family;
                string guid = AssetDatabase.FindAssets("t:Prefab", new[] { folder + "/Prefabs" }).Single();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var owner = prefab.GetComponentInChildren<NativeNpcAnimator>(true);
                string source = Directory.GetFiles(folder + "/Models/Source", "*.glb").Single();
                rows.Add($"{family}\t{ApplySource(prefab, owner, source)}");
            }
            const string log = "Logs/Studio/ActorRepair-20261120/animation";
            Directory.CreateDirectory(log);
            File.WriteAllLines(log + "/material-publication.tsv", rows);
            Debug.Log("Published actor material tracks for Bubbie, Fusion Gate and Unstable Nano Capsule.");
        }

        static int ApplySource(GameObject prefab, NativeNpcAnimator owner, string source)
        {
            var legacy = prefab.GetComponentInChildren<Animation>(true);
            var root = owner ? owner.transform : legacy ? legacy.transform : null;
            if (!root) return 0;
            var clips = owner ? owner.Animator.runtimeAnimatorController.animationClips.Distinct().ToArray()
                : legacy.Cast<AnimationState>().Select(state => state.clip).Distinct().ToArray();
            var json = NativeGlbDocument.Read(source).Json;
            int modified = 0;
            foreach (var animation in json["animations"] ?? new JArray())
            {
                string name = (string)animation["name"];
                var extras = animation["extras"];
                foreach (var clip in clips.Where(c => c.name == name || c.name == name + NativeNpcAnimator.LoopSuffix))
                {
                    // Authored editable clips own the corrections, including legacy Nano clips.
                    if (!AssetDatabase.GetAssetPath(clip).EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
                    var bindings = new List<EditorCurveBinding>();
                    var curves = new List<AnimationCurve>();
                    var vectors = new Dictionary<(Renderer Renderer, string Name), int>();
                    foreach (var track in extras?["nonTrs"]?["floatCurves"] ?? new JArray())
                    {
                        if ((int?)track["classId"] != 21 || track["times"] is not JArray times || times.Count == 0) continue;
                        string property = Property((string)track["property"]);
                        string target = (string)track["targetPath"];
                        // A recovered GLB has its original model root beneath the Unity animation owner.
                        var nodes = root.GetComponentsInChildren<Transform>(true).Where(t =>
                        {
                            string relative = AnimationUtility.CalculateTransformPath(t, root);
                            return relative == target || relative.EndsWith("/" + target, StringComparison.Ordinal);
                        }).ToArray();
                        if (nodes.Length != 1) throw new InvalidDataException($"{source}: ambiguous material path '{target}'.");
                        var renderers = nodes[0].GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.transform == nodes[0] || r.transform.parent == nodes[0]).ToArray();
                        if (renderers.Length == 0) throw new InvalidDataException($"{source}: missing renderer for '{target}'.");
                        var keys = times.Select((time, key) => new Keyframe((float)time, (float)track["values"][key],
                            (float)track["inTangents"][key], (float)track["outTangents"][key])).ToArray();
                        foreach (var renderer in renderers)
                        {
                            bindings.Add(EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(renderer.transform, root),
                                renderer.GetType(), "material." + property));
                            curves.Add(new AnimationCurve(keys));
                            if (property.Length > 2 && property[^2] == '.')
                            {
                                var key = (renderer, property[..^2]);
                                vectors[key] = vectors.GetValueOrDefault(key) | (1 << "xyzw".IndexOf(property[^1]));
                            }
                        }
                    }
                    foreach (var entry in vectors)
                    {
                        var (renderer, uniform) = entry.Key;
                        var material = renderer.sharedMaterial;
                        if (!material) throw new InvalidDataException($"No material on {renderer.name} for {uniform}.");
                        Vector4 defaults;
                        if (uniform == "_MainTex_ST")
                        {
                            var scale = material.GetTextureScale("_MainTex");
                            var offset = material.GetTextureOffset("_MainTex");
                            defaults = new Vector4(scale.x, scale.y, offset.x, offset.y);
                        }
                        else defaults = material.GetVector(uniform);
                        // Mecanim writes the entire float4 into the renderer's property block.
                        // Components missing from the clip become zero, including the UV scale.
                        for (int axis = 0; axis < 4; axis++)
                        {
                            if ((entry.Value & (1 << axis)) != 0) continue;
                            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
                            bindings.Add(EditorCurveBinding.FloatCurve(path, renderer.GetType(), "material." + uniform + "." + "xyzw"[axis]));
                            curves.Add(new AnimationCurve(new Keyframe(0, defaults[axis])));
                        }
                    }
                    if (bindings.Count > 0) AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
                    float expected = (float?)extras?["duration"] ?? 0;
                    bool tail = HoldPoseThrough(clip, expected);
                    bool lateCues = owner && (float?)extras?["eventDuration"] is float cueEnd &&
                        (float?)extras?["keyedDuration"] is float keyedEnd && cueEnd > keyedEnd + .0005f;
                    if (lateCues)
                    {
                        var events = AnimationUtility.GetAnimationEvents(clip).ToList();
                        foreach (var cue in TutorialAnimationEventImport.AttachedEffects(animation))
                            if (!events.Any(e => e.functionName == cue.functionName && e.stringParameter == cue.stringParameter &&
                                Mathf.Abs(e.time - cue.time) < .00001f)) events.Add(cue);
                        AnimationUtility.SetAnimationEvents(clip, events.OrderBy(e => e.time).ToArray());
                    }
                    if (bindings.Count == 0 && !tail && !lateCues) continue;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    if (expected > settings.stopTime)
                    {
                        settings.stopTime = expected;
                        AnimationUtility.SetAnimationClipSettings(clip, settings);
                    }
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssetIfDirty(clip);
                    modified += bindings.Count + (tail ? 1 : 0);
                }
            }
            return modified;
        }

        internal static bool HoldPoseThrough(AnimationClip clip, float seconds)
        {
            if (seconds <= clip.length + .0005f) return false;
            // Extend a position or scale component. Extending only one quaternion component
            // would allow Unity to re-key the other three across a previously held pose.
            var binding = AnimationUtility.GetCurveBindings(clip).First(b => b.type == typeof(Transform)
                && (b.propertyName.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
                    || b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)));
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var last = curve.keys[^1];
            last.outTangent = 0;
            curve.MoveKey(curve.length - 1, last);
            curve.AddKey(new Keyframe(seconds, last.value, 0, 0));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.stopTime = seconds;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return true;
        }

        static string Property(string source) => source switch
        {
            "_TintColor.a" => "_AnimatedAlpha",
            "_MainTex.rotation" => "_MainTexRotation",
            "_MainTex.offset.x" => "_MainTex_ST.z",
            "_MainTex.offset.y" => "_MainTex_ST.w",
            _ when source.StartsWith("_Emission.", StringComparison.Ordinal) || source.StartsWith("_Color.", StringComparison.Ordinal)
                => source[..^1] + "xyzw"["rgba".IndexOf(source[^1])],
            _ => throw new InvalidDataException("Unknown source actor material curve: " + source)
        };

        internal static int AddOverlays(NativeNpcAnimator owner, bool delta)
        {
            var controller = (AnimatorController)owner.Animator.runtimeAnimatorController;
            var states = controller.layers[0].stateMachine.states.Select(child => child.state).ToArray();
            if (!states.Any(state => NativeNpcAnimator.ActionLayer(state.name) != null)) return 0;
            var reference = states.FirstOrDefault(s => s.name == "stand1" + NativeNpcAnimator.LoopSuffix)?.motion as AnimationClip;
            var mask = CombatMask(controller, owner.transform, states);
            GameObject rig = null;
            int count = 0;
            bool changed = false;
            try
            {
                string LayerOf(AnimatorState state)
                {
                    string name = state.name;
                    if (name.EndsWith(NativeNpcAnimator.LoopSuffix, StringComparison.Ordinal))
                    {
                        if (!delta) return null;
                        name = name[..^NativeNpcAnimator.LoopSuffix.Length];
                    }
                    return NativeNpcAnimator.ActionLayer(name);
                }
                if (delta && controller.parameters.All(p => p.name != NativeNpcAnimator.DeltaActionsParameter))
                    controller.AddParameter(new AnimatorControllerParameter { name = NativeNpcAnimator.DeltaActionsParameter,
                        type = AnimatorControllerParameterType.Bool, defaultBool = true });
                foreach (var group in states.Where(state => LayerOf(state) != null)
                    .GroupBy(LayerOf)
                    .OrderBy(group => group.Key == NativeNpcAnimator.AttackLayer ? 0 : 1))
                {
                    var layer = controller.layers.FirstOrDefault(l => l.name == group.Key);
                    if (layer == null)
                    {
                        controller.AddLayer(group.Key);
                        var layers = controller.layers;
                        layer = layers[^1];
                        layer.defaultWeight = 1;
                        layer.blendingMode = AnimatorLayerBlendingMode.Additive;
                        controller.layers = layers;
                        var empty = layer.stateMachine.AddState("Empty");
                        empty.writeDefaultValues = false;
                        layer.stateMachine.defaultState = empty;
                        Persist(empty, controller);
                    }
                    var machine = layer.stateMachine;
                    if (layer.avatarMask != mask)
                    {
                        var layers = controller.layers;
                        layers.Single(l => l.name == group.Key).avatarMask = mask;
                        controller.layers = layers;
                        changed = true;
                    }
                    foreach (var original in group)
                    {
                        var source = (AnimationClip)original.motion;
                        var basis = delta ? reference : source;
                        var previous = machine.states.FirstOrDefault(child => child.state.name == original.name).state;
                        if (previous)
                        {
                            changed |= SetReference((AnimationClip)previous.motion, basis);
                            continue;
                        }
                        var overlay = Object.Instantiate(source);
                        overlay.hideFlags = HideFlags.HideInHierarchy;
                        overlay.name = source.name;
                        if (delta)
                        {
                            if (!reference) throw new InvalidDataException(owner.name + " has no standing reference pose.");
                            if (!rig)
                            {
                                rig = Object.Instantiate(owner.gameObject);
                                rig.hideFlags = HideFlags.HideAndDontSave;
                                reference.SampleAnimation(rig, 0);
                            }
                            PlayerAnimatorSetup.BakeDeltaOntoBindPose(overlay, rig);
                        }
                        AssetDatabase.AddObjectToAsset(overlay, controller);
                        SetReference(overlay, basis);
                        var state = machine.AddState(original.name);
                        state.motion = overlay;
                        state.writeDefaultValues = false;
                        if (!source.isLooping)
                        {
                            var end = state.AddTransition(machine.defaultState);
                            end.hasExitTime = true;
                            end.exitTime = 1;
                            end.duration = 0;
                            Persist(end, controller);
                        }
                        Persist(state, controller);
                        EditorUtility.SetDirty(overlay);
                        count++;
                    }
                    EditorUtility.SetDirty(machine);
                }
                if (count > 0 || changed)
                {
                    EditorUtility.SetDirty(controller);
                    AssetDatabase.SaveAssetIfDirty(controller);
                }
            }
            finally { if (rig) Object.DestroyImmediate(rig); }
            return count;
        }

        static bool SetReference(AnimationClip clip, AnimationClip reference)
        {
            if (!reference) throw new InvalidDataException("No reference animation for " + clip.name);
            // SetAdditiveReferencePose acts upon an already-built Mecanim stream. These
            // editable subassets must persist the import settings before Unity builds it.
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose && settings.additiveReferencePoseClip == reference &&
                settings.additiveReferencePoseTime == 0) return false;
            settings.hasAdditiveReferencePose = true;
            settings.additiveReferencePoseClip = reference;
            settings.additiveReferencePoseTime = 0;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return true;
        }

        static AvatarMask CombatMask(AnimatorController controller, Transform root, AnimatorState[] states)
        {
            var existing = controller.layers.FirstOrDefault(l => l.avatarMask && l.avatarMask.name == "Combat coverage")?.avatarMask;
            if (existing) return existing;
            string Semantic(AnimatorState s) => s.name.EndsWith(NativeNpcAnimator.LoopSuffix, StringComparison.Ordinal)
                ? s.name[..^NativeNpcAnimator.LoopSuffix.Length] : s.name;
            var source = states.GroupBy(Semantic).ToDictionary(g => g.Key, g => (AnimationClip)g.First().motion);
            var common = new HashSet<(string path, string property)>();
            bool first = true;
            foreach (string name in new[] { "stand1", "stand2", "stand3", "stand4", "walk", "run", "ready" })
            {
                if (!source.TryGetValue(name, out var clip)) continue;
                var tracks = AnimationUtility.GetCurveBindings(clip).Where(b => b.type == typeof(Transform)).Select(b => (b.path, b.propertyName));
                if (first) common.UnionWith(tracks);
                else common.IntersectWith(tracks);
                first = false;
            }
            // The accepted graph excludes an entire target when ANY high-layer source
            // channel has no corresponding property in one of the playable base poses.
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in source)
            {
                if (entry.Key.Contains("upper") || entry.Key.Contains("event") ||
                    !entry.Key.Contains("melee") && !entry.Key.Contains("wound")) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(entry.Value))
                    if (b.type == typeof(Transform) && !common.Contains((b.path, b.propertyName))) excluded.Add(b.path);
            }
            if (excluded.Count == 0) return null;
            var paths = root.GetComponentsInChildren<Transform>(true).Select(t => AnimationUtility.CalculateTransformPath(t, root)).ToArray();
            if (excluded.Except(paths).Any()) throw new InvalidDataException("Combat mask has an absent bone on " + controller.name);
            var mask = new AvatarMask { name = "Combat coverage", transformCount = paths.Length };
            for (int i = 0; i < paths.Length; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, !excluded.Contains(paths[i]));
            }
            Persist(mask, controller);
            return mask;
        }

        static void Persist(Object asset, AnimatorController controller)
        {
            if (!AssetDatabase.Contains(asset)) AssetDatabase.AddObjectToAsset(asset, controller);
            EditorUtility.SetDirty(asset);
        }
    }
}
