using System;
using System.Collections.Generic;
using UnityEngine;

namespace FFOne.Tutorial
{
    /// <summary>Accepted FFOneClient VoiceOut identities, TextManager scene 2 and original maximum durations.</summary>
    public sealed class TutorialVoiceSubtitles
    {
        // Source: tutorial_runtime/voice_subtitles/audio_tutorial_voice_subtitle_state.rs.
        // Audio-only VoiceOut calls intentionally have no mapping. Lines 31/42/74/80 are absent.
        static readonly Dictionary<string, (int line, float seconds, string english)> specs = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Computress_tut01", (1, 5.0f, null) },
            { "Btrcup_Tut01", (2, 2.0f, null) },
            { "NumFive_TutFollowme_Rev", (3, 3.0f, null) },
            { "Ben_Tut01", (4, 3.0f, null) },
            { "Computress_Tut07", (5, 7.0f, null) },
            { "Computress_Tut08", (6, 5.0f, null) },
            { "Computress_Tut02", (7, 2.0f, null) },
            { "Computress_Tut03", (8, 3.0f, null) },
            { "Computress_Tut04", (9, 3.0f, null) },
            { "Computress_Tut05", (10, 3.0f, null) },
            { "Computress_Tut06", (11, 3.0f, null) },
            { "Computress_Tut09", (12, 3.0f, null) },
            { "Computress_Tut10", (13, 5.0f, null) },
            { "Computress_Tut11", (14, 5.0f, null) },
            { "Computress_Tut12", (15, 10.0f, null) },
            { "Ben_Tut02", (16, 5.0f, null) },
            { "NumFive_TutHoldthem_Rev", (17, 2.0f, null) },
            { "Ben_Tut03", (18, 3.0f, null) },
            { "Ben_Tut04", (19, 7.0f, null) },
            { "Computress_Tut16", (20, 14.0f, null) },
            { "Computress_Tut17", (21, 3.0f, null) },
            { "Ben_Tut05", (22, 2.0f, null) },
            { "Ben_Tut06", (23, 3.0f, null) },
            { "Ben_Tut08", (24, 2.0f, null) },
            { "NumFive_TutIKnowYou", (25, 10.0f, null) },
            { "NumFive_TutFuseExpl", (26, 12.0f, null) },
            { "Ben_Tut09", (27, 13.0f, null) },
            { "Ben_Tut10", (28, 4.0f, null) },
            { "Computress_Tut13", (29, 10.0f, null) },
            { "Computress_Tut14", (30, 10.0f, null) },
            { "Computress_Tut15", (32, 10.0f, null) },
            { "Computress_Tut18", (33, 10.0f, null) },
            { "Computress_Tut19", (34, 5.0f, null) },
            { "Computress_Tut20", (35, 3.0f, null) },
            { "Computress_Tut21", (36, 3.0f, null) },
            { "Computress_Tut22", (37, 10.0f, null) },
            { "Computress_Tut23", (38, 5.0f, null) },
            { "Computress_Tut25", (39, 10.0f, null) },
            { "Computress_Tut26", (40, 10.0f, null) },
            { "Computress_Tut38", (41, 10.0f, null) },
            { "Btrcup_Tut03", (43, 3.0f, null) },
            { "Computress_Tut41", (44, 10.0f, null) },
            { "Computress_Tut24", (45, 10.0f, null) },
            { "NumTwo_Tut02", (46, 3.0f, null) },
            { "Computress_Tut27", (47, 10.0f, null) },
            { "Computress_Tut28", (48, 5.0f, null) },
            { "Computress_Tut29", (49, 10.0f, null) },
            { "NumTwo_Tut03", (50, 3.0f, null) },
            { "Computress_Tut30", (51, 10.0f, null) },
            { "Computress_Tut31", (52, 5.0f, null) },
            { "Computress_Tut32", (53, 5.0f, null) },
            { "Computress_Tut33", (54, 5.0f, null) },
            { "Computress_Tut34", (55, 5.0f, null) },
            { "Computress_Tut35", (56, 10.0f, null) },
            { "Computress_Tut36", (57, 10.0f, null) },
            { "Computress_Tut37", (58, 5.0f, null) },
            { "Computress_Tut39", (59, 10.0f, null) },
            { "Computress_Tut40", (60, 5.0f, null) },
            { "Btrcup_Tut08", (61, 12.0f, null) },
            { "Dexter_Tut03", (62, 11.0f, null) },
            { "Dexter_Tut04", (63, 5.0f, null) },
            { "Dexter_Tut08", (64, 5.0f, null) },
            { "Dexter_Tut09", (65, 6.0f, null) },
            { "Dexter_Tut10", (66, 17.0f, "DEXTER: Congratulations, you have made your first Nano. Like Fusions, Nanos are made of Fusion Matter. You have used the Fusion Matter you collected in your Fusion Matter Meter to create Nano Buttercup. Don't be discouraged by her size. She may be small, but she is powerful!") },
            { "Computress_Tut55", (67, 6.0f, null) },
            { "Dexter_Tut11", (68, 10.0f, null) },
            { "Dexter_Tut01", (69, 5.0f, null) },
            { "Computress_Tut49", (70, 10.0f, null) },
            { "Dexter_Tut02", (71, 5.0f, null) },
            { "Computress_Tut56", (72, 3.0f, null) },
            { "Computress_Tut57", (73, 5.0f, null) },
            { "Computress_Tut44", (75, 5.0f, null) },
            { "Btrcup_Tut07", (76, 10.0f, null) },
            { "Computress_Tut45", (77, 5.0f, null) },
            { "Computress_Tut46", (78, 10.0f, null) },
            { "Computress_Tut47", (79, 5.0f, null) },
            { "Computress_Tut50", (81, 10.0f, null) },
            { "Computress_Tut51", (82, 5.0f, null) },
            { "Computress_Tut53", (83, 3.0f, null) },
            { "NumTwo_tut05", (84, 4.0f, null) },
            { "NumTwo_Tut06", (85, 11.0f, null) },
            { "Dexter_Tut12", (86, 24.0f, "DEXTER: Despite our best efforts, Tech Square has been lost. But this war is not over yet. Not by a long shot. Our time-traveling friend here is just the hero we need. Between Mandark and Numbuh Two, I am sure you will be able to rebuild my time capsule. Don't worry about me. I will be fine. Get back to Sector V and to safety. Dexter, out.") },
            { "NumTwo_Tut07", (87, 4.0f, null) },
            { "Computress_Tut60", (88, 8.0f, null) },
            { "Computress_Tut61", (89, 10.0f, null) },
            { "Computress_Tut59", (90, 10.0f, null) },
            { "Computress_Tut62", (91, 3.0f, null) },
        };
        public static IEnumerable<string> Cues => specs.Keys;
        public string Text { get; private set; } = "";
        public float Remaining { get; private set; }

        public void Play(string cue, bool russian, Func<string, string> localize, float localizedRecordingSeconds = 0)
        {
            Clear();
            if (string.IsNullOrEmpty(cue) || !specs.TryGetValue(cue, out var spec)) return;
            Set(!russian && spec.english != null ? spec.english : localize("content.tutorial.scene.2." + spec.line),
                Mathf.Max(spec.seconds, localizedRecordingSeconds));
        }
        public void Set(string text, float seconds)
        {
            Text = text ?? "";
            Remaining = Mathf.Max(0, seconds);
        }
        public void Tick(float delta)
        {
            if (!float.IsFinite(delta) || delta <= 0) return;
            Remaining = Mathf.Max(0, Remaining - delta);
            if (Remaining <= .00001f) Clear();
        }
        public void Clear() { Text = ""; Remaining = 0; }
    }
}
