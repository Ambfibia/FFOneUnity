using System;
using System.Collections.Generic;

namespace FFOne.Tutorial
{
    // Finite WaitForSeconds timing from cntutorialscript, preserved by the native presenter.
    public readonly struct TutorialAudioCue
    {
        public readonly float Seconds;
        public readonly string Cue;
        public readonly int Kind; // voice, one-shot, loop, stop loops
        public TutorialAudioCue(float seconds, string cue, int kind) { Seconds = seconds; Cue = cue; Kind = kind; }
    }
    public static class TutorialAudioTimeline
    {
        public static readonly Dictionary<TutorialScene, float> Durations = new() {
            [TutorialScene.BasicMove] = 31.15f,
            [TutorialScene.BasicCombatA] = 12.0f,
            [TutorialScene.BasicCombatB] = 39.6f,
            [TutorialScene.BasicCombatC] = 45.0f,
            [TutorialScene.InfectionA] = 15.0f,
            [TutorialScene.InfectionB] = 33.1f,
            [TutorialScene.InfectionC] = 47.9f,
            [TutorialScene.NanoPowerA] = 14.5f,
            [TutorialScene.NanoPowerA2] = 5.0f,
            [TutorialScene.NanoPowerB] = 47.15f,
        };
        public static readonly Dictionary<TutorialScene, TutorialAudioCue[]> Cues = new() {
            [TutorialScene.BasicMove] = new TutorialAudioCue[] {
                new(1.0f, "Computress_Tut01", 0),
                new(2.0f, "TechSquareText_Typed", 1),
                new(5.15f, "Flythru_Sting", 1),
                new(6.15f, "Explosion_01", 1),
                new(6.25f, "Explosion_01", 1),
                new(6.55f, "Explosion_02", 1),
                new(6.85f, "Explosion_03", 1),
                new(7.15f, "Explosion_04", 1),
                new(8.15f, "Buttercup_Flyby_Whoosh", 1),
                new(8.15f, "Btrcup_Tut01", 0),
                new(9.65f, "Buttercup_Flyby_Whoosh", 1),
                new(23.15f, "NumFive_TutFollowMe_REV", 0),
                new(25.15f, "Ben_Tut01", 0),
            },
            [TutorialScene.BasicCombatA] = new TutorialAudioCue[] {
                new(0.0f, "FusionSpawns_Sting", 1),
                new(1.0f, "Ben_Tut02", 0),
                new(6.0f, "NumFive_TutHoldThem_REV", 0),
            },
            [TutorialScene.BasicCombatB] = new TutorialAudioCue[] {
                new(3.3f, "Ben_Tut03", 0),
                new(8.8f, "Ben_Tut04", 0),
                new(15.9f, "Computress_Tut16", 0),
                new(30.1f, "Computress_Tut17", 0),
                new(32.6f, "Cyberus_Sting", 1),
                new(32.6f, "Ben_Tut05", 0),
                new(34.1f, "Cyberus_Landing", 1),
                new(35.1f, "Ben_Tut06", 0),
            },
            [TutorialScene.BasicCombatC] = new TutorialAudioCue[] {
                new(1.0f, "Ben_Tut08", 0),
                new(5.0f, "NumFive_TutIKnowYou", 0),
                new(14.5f, "NumFive_TutFuseExpl", 0),
                new(15.5f, "PlanetFusion_Sting", 1),
                new(25.5f, "Ben_Tut09", 0),
                new(39.0f, "Ben_Tut10", 0),
            },
            [TutorialScene.InfectionA] = new TutorialAudioCue[] {
                new(1.0f, "InfectedZone_Sting", 1),
                new(1.0f, "BtrCup_Tut08", 0),
            },
            [TutorialScene.InfectionB] = new TutorialAudioCue[] {
                new(1.0f, "Dexter_Tut03", 0),
                new(12.0f, "Dexter_Tut04", 0),
                new(17.1f, "Dexter_Tut08", 0),
                new(21.1f, "FusionButtercup_Stand1", 1),
                new(24.6f, "Dexter_Tut09", 0),
            },
            [TutorialScene.InfectionC] = new TutorialAudioCue[] {
                new(1.5f, "Nano_Creation", 1),
                new(3.75f, "Btrcup_NanSummon01_01", 0),
                new(8.9f, "Dexter_Tut10", 0),
                new(26.4f, "Computress_Tut55", 0),
                new(33.4f, "LairCollapse_Quake_LOOP", 2),
                new(34.4f, "Dexter_Tut11", 0),
            },
            [TutorialScene.NanoPowerA] = new TutorialAudioCue[] {
                new(0.0f, "SCAMPER_Sting", 1),
                new(0.0f, "RumbleQuake_LOOP", 2),
                new(4.0f, "Explosion_01", 1),
                new(4.3f, "Explosion_02", 1),
                new(4.6f, "Explosion_03", 1),
                new(4.9f, "Explosion_04", 1),
                new(5.2f, "Explosion_01", 1),
                new(5.5f, "Explosion_04", 1),
                new(5.5f, "Explosion_03", 1),
                new(8.5f, "NumTwo_Tut05", 0),
                new(13.5f, "", 3),
            },
            [TutorialScene.NanoPowerA2] = new TutorialAudioCue[] {
                new(0.0f, "TechWing_Sting", 1),
            },
            [TutorialScene.NanoPowerB] = new TutorialAudioCue[] {
                new(0.0f, "DexCarrier_Sting", 1),
                new(2.0f, "NumTwo_Tut06", 0),
                new(13.25f, "Explosion_01", 1),
                new(13.45f, "Explosion_02", 1),
                new(13.45f, "SCAMPER_Jet", 1),
                new(13.75f, "Explosion_03", 1),
                new(13.95f, "Explosion_04", 1),
                new(14.15f, "Explosion_01", 1),
                new(14.35f, "Explosion_02", 1),
                new(14.45f, "Explosion_03", 1),
                new(16.65f, "DexterHologram_LOOP", 2),
                new(16.65f, "Dexter_Tut12", 0),
                new(40.65f, "", 3),
                new(40.65f, "HologramOff", 1),
                new(41.15f, "NumTwo_Tut07", 0),
            },
        };
    }
}
