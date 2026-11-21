using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace FFOne.Editor
{
    public static class TutorialTimelineImport
    {
        public const string Root = "Assets/FFOne/Worlds/Tutorial/Cinematics";

        [MenuItem("FFOne/Tutorial/Create Missing Editable Timelines")]
        public static void CreateMissing()
        {
            var content = ContentAssets.Load<TutorialContent>("Tutorial/Content");
            if (!content) throw new InvalidOperationException("Tutorial content is missing.");
            var source = NativeData.Object("data/tutorial/cinematics");
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            var scenes = new List<TutorialTimelineScene>();
            foreach (var definition in source["scenes"].OfType<JObject>())
            {
                string name = (string)definition["scene"]["type"];
                string path = Root + "/" + name + ".asset";
                var scene = AssetDatabase.LoadAssetAtPath<TutorialTimelineScene>(path);
                if (!scene)
                {
                    // Never republish over an authored scene or its montage.
                    string timelinePath = Root + "/" + name + ".playable";
                    if (File.Exists(path) || File.Exists(timelinePath)) throw new IOException("Cinematic publication conflict: " + path);
                    scene = ScriptableObject.CreateInstance<TutorialTimelineScene>();
                    scene.Scene = Enum.Parse<TutorialScene>(name);
                    AssetDatabase.CreateAsset(scene, path);
                    scene.Skip = Document(definition["skip"], path, name + " skip cleanup");
                    scene.Loops = Document(definition["loops"], path, name + " persistent audio");
                    var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                    AssetDatabase.CreateAsset(timeline, timelinePath);
                    timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                    timeline.fixedDuration = (double)definition["deterministic_duration_seconds"];
                    scene.Timeline = timeline;
                    var tracks = new Dictionary<string, TutorialActionTrack>();
                    int order = 0;
                    void Add(float time, JToken action)
                    {
                        string kind = (string)action["type"];
                        string lane = kind == "Sequence" ? "Sequence / " + (string)action["args"]?[0]?["type"] : kind;
                        if (!tracks.TryGetValue(lane, out var track))
                            tracks.Add(lane, track = timeline.CreateTrack<TutorialActionTrack>(null, lane));
                        var clip = track.CreateClip<TutorialActionClip>();
                        clip.start = time;
                        clip.duration = Math.Max(1d / 60, TutorialTimelineScene.ActionDuration(action));
                        var argument = action["args"]?.First as JObject;
                        clip.displayName = kind + " · " + ((string)argument?["type"] ?? (string)action["cue"] ?? "");
                        var playable = (TutorialActionClip)clip.asset;
                        playable.Order = order++;
                        playable.Action = Document(action, timelinePath, clip.displayName);
                        EditorUtility.SetDirty(playable);
                    }
                    foreach (var item in definition["actions"]) Add((float)item["time"], item["action"]);
                    if (TutorialAudioTimeline.Cues.TryGetValue(scene.Scene, out var cues))
                        foreach (var cue in cues)
                            Add(cue.Seconds, new JObject { ["type"] = "AudioCue", ["cue"] = cue.Cue, ["kind"] = cue.Kind });
                    EditorUtility.SetDirty(timeline); EditorUtility.SetDirty(scene);
                }
                scenes.Add(scene);
            }
            Undo.RecordObject(content, "Assign editable tutorial cinematics");
            content.Cinematics = scenes.ToArray(); EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();
            Selection.activeObject = scenes[0].Timeline;
            Debug.Log("Tutorial: ten editable Timeline montages are assigned to runtime content.");
        }

        static NativeDataDocument Document(JToken token, string owner, string name)
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            document.name = name; document.Identity = name; document.Import(token.ToString(Newtonsoft.Json.Formatting.None));
            document.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(document, owner);
            return document;
        }
    }
}
