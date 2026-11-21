using System;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Timeline;

namespace FFOne.Tutorial
{
    [CreateAssetMenu(menuName = "FFOne/Tutorial/Cinematic scene")]
    public sealed class TutorialTimelineScene : ScriptableObject
    {
        public TutorialScene Scene;
        public TimelineAsset Timeline;
        [Tooltip("Actions used when the player skips this scene, including cast and effect cleanup.")]
        public NativeDataDocument Skip;
        public NativeDataDocument Loops;
        [Min(0), Tooltip("Keep this shot outside world geometry. Zero preserves the unadjusted authored camera path.")]
        public float CameraCollisionRadius;

#if UNITY_EDITOR
        // Source/export inspection only. Runtime plays the Timeline graph directly.
        public JObject Definition()
        {
            if (!Timeline || !Skip || !Loops) throw new InvalidOperationException("Incomplete tutorial cinematic: " + name);
            var clips = Timeline.GetOutputTracks().Where(t => !t.muted).SelectMany(t => t.GetClips())
                .Where(c => c.asset is TutorialActionClip).OrderBy(c => c.start)
                .ThenBy(c => ((TutorialActionClip)c.asset).Order);
            return new JObject {
                ["scene"] = new JObject { ["type"] = Scene.ToString() },
                ["deterministic_duration_seconds"] = Timeline.duration,
                ["camera_collision_radius"] = CameraCollisionRadius,
                ["actions"] = new JArray(clips.Select(c => new JObject {
                    ["time"] = c.start, ["action"] = Action(c)
                })),
                ["skip"] = Skip.ToToken(), ["loops"] = Loops.ToToken()
            };
        }

        static JToken Action(TimelineClip clip)
        {
            var action = ((TutorialActionClip)clip.asset).Action.ToToken();
            double original = ActionDuration(action);
            if (original > 0)
            {
                var sequence = action["args"][0];
                if ((string)sequence["type"] == "Fade") sequence = sequence["args"][0];
                foreach (string key in new[] { "seconds_per_frame", "seconds_per_step", "seconds_per_character" })
                    if (sequence[key] != null) sequence[key] = (double)sequence[key] * clip.duration / original;
            }
            return action;
        }

#endif
        public static double ActionDuration(JToken action)
        {
            if ((string)action["type"] != "Sequence") return 0;
            var sequence = action["args"][0];
            if ((string)sequence["type"] == "Fade") sequence = sequence["args"][0];
            double step = (double?)sequence["seconds_per_frame"] ?? (double?)sequence["seconds_per_step"] ?? (double?)sequence["seconds_per_character"] ?? 1;
            int count = (int?)sequence["frames"] ?? (int?)sequence["steps"] ?? (int?)sequence["canonical_english_chars"] ?? 1;
            return step * count;
        }
    }
}
