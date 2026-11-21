using UnityEngine;

namespace FFOne.Tutorial
{
    // Primary cnDexterShipEventScene NameEvent / TutorialEvent, with the accepted
    // native NPC and audio identities already used by FFOneClient's presenter.
    public static class DexterShipTimeline
    {
        public const float TutorialDuration = 36;
        public const int DefaultCaptionLength = 56;
        public const float BlackBarRatio = .15673982f;
        public const string Background = "ui/en/character/creation/background/CharCreationBG";
        public const string Scanline = "ui/en/character/creation/background/holo_line";
        public const string ComputerModel = "map/shared/effects/models/es740/m_inven";
        public const string DexterBodyTexture = "characters/npcs/npc_dexter/textures/npc_dexter";
        public const string DexterFaceTexture = "characters/npcs/npc_dexter2/textures/npc_dexter2_face_0";
        public static readonly string[] ActorModels = {
            "characters/npcs/npc_dexter2/npc_dexter2",
            "characters/npcs/npc_deedee/npc_deedee",
            "characters/npcs/npc_computress/npc_computress"
        };
        public readonly struct Cue
        {
            public readonly float Time;
            public readonly string Path;
            public readonly int Action; // 0 one-shot, 1 replace loop, 2 add loop, 3 stop loops
            public readonly bool Voice;
            public Cue(float time, string path, int action = 0, bool voice = false)
            { Time = time; Path = path; Action = action; Voice = voice; }
            public string Resource(string language) => Voice ? "audio/voice/" + language + "/" + Path : "audio/sfx/character_creation/" + Path;
        }
        // NameEvent types Substring(0,i), i=1..Length-1, waiting .05 each,
        // then .9 before observe and .1 before Hello. The translated caption
        // therefore participates in source timing, rather than a fixed 2s timer.
        public static float CreationReveal(int captionLength) => Mathf.Max(0, captionLength - 1) * .05f + 1;
        public static float CreationInput(int captionLength) => CreationReveal(captionLength) + 3.5f;
        public static float CreationVoiceClear(int captionLength) => CreationInput(captionLength) + 10;
        public static float CreationDuration(int captionLength) => CreationVoiceClear(captionLength) + 1;
        public static Cue[] CreationCues(int captionLength)
        {
            float reveal = CreationReveal(captionLength);
            return new[] {
                new Cue(0, "dexlabstext_typed"), new Cue(reveal, "hologramon"), new Cue(reveal, "machine_running", 1),
                new Cue(reveal, "dexter/cchello", voice:true), new Cue(CreationInput(captionLength), "dexter/ccinput", voice:true)
            };
        }
        public static readonly Cue[] Creation = CreationCues(DefaultCaptionLength);
        public static readonly Cue[] Tutorial = {
            new(0, "hologramon"), new(0, "machine_running", 1), new(0, "dexter/cc01", voice:true), new(0, "beeping"),
            new(5.5f, "computress/cc01", voice:true), new(6.5f, "beeping"), new(11.8f, "dexter/ccenginepwr", voice:true),
            new(11.8f, "beeping"), new(12.3f, "deedee/laugh_01", voice:true), new(15.8f, "engine"),
            new(15.8f, "vibration1", 1), new(15.8f, "deedee/laugh02", voice:true), new(18.8f, "dexter/ccreadings", voice:true),
            new(20.3f, "deedee/laugh_03", voice:true), new(23.3f, "deedee/laugh_01", voice:true), new(23.8f, "dexter/cclaunch", voice:true),
            new(24.9f, "deedee/ccbutton", voice:true), new(26.9f, "button"), new(27.4f, "vibration2", 1),
            new(27.4f, "warning_siren", 2), new(27.4f, "dexter/ccdeedeeno", voice:true), new(30.9f, "thunderous_crash"),
            new(32.5f, "", 3), new(33, "hologramoff"), new(33, "timejumptext_typed")
        };
        public static string DexterClip(bool creation, float t, int captionLength = DefaultCaptionLength) => creation
            ? t < CreationReveal(captionLength) - .1f ? "stand1" : t < CreationVoiceClear(captionLength) ? "observe" : "cutscene_stand_1"
            : t < 27.4f ? "excellent" : t < 28.9f ? "deedeeno" : "busyrun";
        public static string ComputressClip(bool creation, float t) => creation || t < 5.5f ? "stand1" : t < 10.5f ? "event1" : t < 27.4f ? "event2" : "stand2";
        public static int SubtitleLine(bool creation, float t, int captionLength = DefaultCaptionLength) => creation
            ? t < CreationReveal(captionLength) || t >= CreationVoiceClear(captionLength) ? 0 : t < CreationInput(captionLength) ? 2 : 3
            : t < 5.5f ? 4 : t < 11.8f ? 5 : t < 15.8f ? 6 : t < 18.8f ? 0 : t < 23.8f ? 7 : t < 24.9f ? 8 : t < 27.4f ? 9 : 10;
        public static float Fade(bool creation, float t, int captionLength = DefaultCaptionLength) => creation
            ? Mathf.Clamp01(1 - Mathf.Floor(Mathf.Max(0, t - CreationReveal(captionLength)) / .1f + .00001f) / 9)
            : Mathf.Clamp01((t - 31.9f) / .6f);
        public static string NormalizeSubtitle(string text) => (text ?? "").Replace("%s", "\n");
        public static void SplitSubtitle(string text, out string speaker, out string dialogue)
        {
            text = NormalizeSubtitle(text);
            int separator = text?.IndexOf(':') ?? -1;
            speaker = separator < 0 ? "" : text.Substring(0, separator + 1);
            dialogue = separator < 0 ? text ?? "" : text.Substring(separator + 1);
        }
        public static Vector3 DexterPosition(bool creation, float t) => creation ? Vector3.zero : new Vector3(-2 * Mathf.Clamp01(t - 28.9f), 0, 0);
        public static Vector3 DeeDeePosition(bool creation, float t, out Quaternion rotation, out string clip)
        {
            rotation = Quaternion.identity; clip = "stand1";
            if (creation || t < 12.3f) return new Vector3(5, -.8f, -2);
            float Bounce(float start) => Mathf.Min(1, Mathf.Sin(Mathf.Clamp01((t - start) / 2) * Mathf.PI) + .5f);
            if (t < 14.3f) { rotation = Quaternion.Euler(0, 0, -70); return new Vector3(-4.1f + 1.6f * Bounce(12.3f), .5f, -2); }
            if (t < 15.8f) return new Vector3(4, .5f, -2);
            if (t < 17.8f) { rotation = Quaternion.Euler(0, 0, 180); return new Vector3(0, 4 - .8f * Bounce(15.8f), -2); }
            if (t < 20.3f) return new Vector3(4, .5f, -2);
            if (t < 22.3f) { rotation = Quaternion.Euler(0, 0, 70); return new Vector3(4 - 1.4f * Bounce(20.3f), .5f, -2); }
            if (t < 24.3f) { rotation = Quaternion.Euler(0, -90, 0); clip = "walk"; return new Vector3(2 - 4 * Mathf.Clamp01((t - 22.3f) / 2), -.8f, -2); }
            return new Vector3(-2, -.8f, -2);
        }
    }
}
