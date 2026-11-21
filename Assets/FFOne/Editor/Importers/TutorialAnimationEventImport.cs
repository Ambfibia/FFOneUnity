using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class TutorialAnimationEventImport
    {
        public const string EffectCallback = "FFOneAttachedEffect";
        public static AnimationEvent[] AttachedEffects(JToken animation) =>
            (animation["extras"]?["nonTrs"]?["events"] ?? new JArray())
                .Where(e => Callback((string)e["functionName"]) != null)
                .Select(e => new AnimationEvent { time = (float)e["time"], functionName = Callback((string)e["functionName"]),
                    stringParameter = (string)e["stringParameter"], messageOptions = SendMessageOptions.DontRequireReceiver }).ToArray();
        static string Callback(string function) => function switch {
            "tag_p" => EffectCallback, "particle" => "FFOneParticleEffect", "sound" => "FFOneAnimationSound",
            "emote_face_on" => "FFOneFaceOn", "emote_face_off" => "FFOneFaceOff",
            "end" => "FFOneAnimationEnd", _ => null };
        static bool Owned(AnimationEvent e) => new[] { EffectCallback, "FFOneParticleEffect", "FFOneAnimationSound", "FFOneFaceOn", "FFOneFaceOff", "FFOneAnimationEnd" }.Contains(e.functionName);

        [MenuItem("FFOne/Tutorial/Restore Animation Effect Events")]
        public static void RepairMenu() => Debug.Log($"Tutorial animation effects: {Repair()} clips repaired.");
        public static int Repair()
        {
            var tutorial = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            var catalog = ContentAssets.Load<NativeModelCatalog>(NativeModelAssets.EditableRoot + "/Index");
            var changes = new Dictionary<AnimationClip, AnimationEvent[]>();
            foreach (var prefab in tutorial.Actors.Select(a => a.Model).Distinct())
            {
                var binding = catalog.Bindings.Single(b => b.EditableResource == ContentPaths.Resource(AssetDatabase.GetAssetPath(prefab)));
                var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(
                    ContentPaths.Resolve(NativeAssetPublisher.ManifestRoot + "/" + binding.OriginalResource + "/publication.json")));
                var document = NativeGlbDocument.Read(AssetDatabase.GUIDToAssetPath(manifest.SourceGuid));
                foreach (var animation in document.Json["animations"] ?? new JArray())
                {
                    var cues = AttachedEffects(animation);
                    if (cues.Length == 0) continue;
                    var entry = manifest.Assets.Single(e => e.Type == typeof(AnimationClip).FullName && e.Name == (string)animation["name"]);
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(entry.Guid));
                    if (!clip) throw new InvalidDataException("Missing published animation: " + entry.Path);
                    var previous = AnimationUtility.GetAnimationEvents(clip);
                    var owned = previous.Where(Owned).ToArray();
                    if (owned.Length == cues.Length && owned.Zip(cues, (a,b) => a.time == b.time && a.functionName == b.functionName && a.stringParameter == b.stringParameter).All(same => same)) continue;
                    changes[clip] = previous.Where(e => !Owned(e)).Concat(cues).OrderBy(e => e.time).ToArray();
                }
            }
            foreach (var change in changes)
            {
                AnimationUtility.SetAnimationEvents(change.Key, change.Value);
                EditorUtility.SetDirty(change.Key); AssetDatabase.SaveAssetIfDirty(change.Key);
            }
            return changes.Count;
        }
    }
}
