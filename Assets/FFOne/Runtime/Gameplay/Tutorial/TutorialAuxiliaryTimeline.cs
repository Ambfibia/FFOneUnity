using System;

namespace FFOne.Tutorial
{
    public enum TutorialAuxiliarySequence { None, BasicArrowKey, MinimapEvent, TalkNumTwo1, TalkNumTwo2, TalkButtercup1, TalkDexter1, TalkDexter2, TalkDexter3, NanoPowerTalk1 }
    public enum TutorialAuxiliaryCommand { Voice, Picture, Cursor, HidePicture, Primary, Secondary, ClearText, Preload, Message, SpawnNumbuhTwo, NumbuhTwoMarker, NumbuhTwoAngle, NumbuhTwoAnimation, MyPoint, WaypointFlash, Waypoint, FusionMatter, ClearEffects }
    public enum TutorialPointerAnchor { MoveKeys, NanoMouse, Minimap, MyPoint, Waypoint, Talk, Mission, FusionMatter }

    public readonly struct TutorialAuxiliaryAction
    {
        public readonly float Seconds;
        public readonly TutorialAuxiliaryCommand Command;
        public readonly string Text;
        public readonly int Value;
        public readonly TutorialPointerAnchor Anchor;
        public TutorialAuxiliaryAction(float seconds, TutorialAuxiliaryCommand command, string text = null, int value = 0, TutorialPointerAnchor anchor = default)
        { Seconds = seconds; Command = command; Text = text; Value = value; Anchor = anchor; }
    }

    // cntutorialscript source spans 2769..2778, 3541..3657, 4202..4225 and
    // 4894..4905; preserved by ffone-tutorial-core/tutorial_auxiliary_choreography.
    public sealed class TutorialAuxiliaryTimeline
    {
        public readonly TutorialAuxiliaryAction[] Actions, Repeating;
        public readonly float RepeatAt, RepeatEvery;
        public TutorialAuxiliaryTimeline(TutorialAuxiliaryAction[] actions, float repeatAt = float.PositiveInfinity, TutorialAuxiliaryAction[] repeating = null)
        { Actions = actions; RepeatAt = repeatAt; RepeatEvery = 25; Repeating = repeating ?? Array.Empty<TutorialAuxiliaryAction>(); }
        static TutorialAuxiliaryAction A(float t, TutorialAuxiliaryCommand kind, string text = null, int value = 0, TutorialPointerAnchor anchor = default) => new(t, kind, text, value, anchor);
        static TutorialAuxiliaryAction V(float t, string cue, int sceneText = 0) => A(t, TutorialAuxiliaryCommand.Voice, cue, sceneText);
        static TutorialAuxiliaryAction C(float t, TutorialPointerAnchor anchor) => A(t, TutorialAuxiliaryCommand.Cursor, "tut_right", anchor: anchor);
        static TutorialAuxiliaryAction P(float t, string texture, TutorialPointerAnchor anchor) => A(t, TutorialAuxiliaryCommand.Picture, texture, anchor: anchor);

        public static TutorialAuxiliarySequence ForStage(int chapter, int step) => (chapter, step) switch {
            (0, 7) => TutorialAuxiliarySequence.BasicArrowKey,
            (2, 5) => TutorialAuxiliarySequence.MinimapEvent,
            (3, 3) => TutorialAuxiliarySequence.TalkNumTwo1,
            (3, 16) => TutorialAuxiliarySequence.TalkNumTwo2,
            (4, 0) => TutorialAuxiliarySequence.TalkButtercup1,
            (4, 8) => TutorialAuxiliarySequence.TalkDexter1,
            (4, 9) => TutorialAuxiliarySequence.TalkDexter2,
            (4, 15) => TutorialAuxiliarySequence.TalkDexter3,
            (5, 6) => TutorialAuxiliarySequence.NanoPowerTalk1,
            _ => TutorialAuxiliarySequence.None
        };

        public static TutorialAuxiliaryTimeline Get(TutorialAuxiliarySequence sequence) => sequence switch {
            TutorialAuxiliarySequence.BasicArrowKey => new(new[] {
                V(0, "Computress_Tut07", 5), P(0, "tut_move", TutorialPointerAnchor.MoveKeys),
                A(0, TutorialAuxiliaryCommand.Primary, "Press and hold the \"W\" key to move forward."),
                V(6, "Computress_Tut08", 6), P(6, "tut_move_w", TutorialPointerAnchor.MoveKeys)
            }),
            TutorialAuxiliarySequence.MinimapEvent => new(new[] {
                A(0, TutorialAuxiliaryCommand.Preload, value:668),
                A(0, TutorialAuxiliaryCommand.Message, "I need some help with a sooper important mission! Report to me right away!", 2671),
                V(0, "NumTwo_Tut01"), A(0, TutorialAuxiliaryCommand.SpawnNumbuhTwo),
                A(1, TutorialAuxiliaryCommand.NumbuhTwoMarker), A(1, TutorialAuxiliaryCommand.NumbuhTwoAngle, value:68), A(1, TutorialAuxiliaryCommand.NumbuhTwoAnimation, "think"),
                C(4, TutorialPointerAnchor.Minimap), V(4, "Computress_Tut19", 34),
                A(10, TutorialAuxiliaryCommand.MyPoint, value:1), A(10, TutorialAuxiliaryCommand.HidePicture), C(10, TutorialPointerAnchor.MyPoint), V(10, "Computress_Tut20", 35),
                A(13, TutorialAuxiliaryCommand.MyPoint), A(13, TutorialAuxiliaryCommand.WaypointFlash, value:1), A(13, TutorialAuxiliaryCommand.HidePicture),
                C(13, TutorialPointerAnchor.Waypoint), V(13, "Computress_Tut21", 36), A(13, TutorialAuxiliaryCommand.Waypoint, value:1005),
                A(16, TutorialAuxiliaryCommand.WaypointFlash)
            }, 16, new[] { V(0, "Computress_Tut22", 37), A(0, TutorialAuxiliaryCommand.HidePicture), A(0, TutorialAuxiliaryCommand.Primary, "Go to Numbuh Two.") }),
            TutorialAuxiliarySequence.TalkNumTwo1 => new(new[] {
                A(0, TutorialAuxiliaryCommand.ClearEffects), V(0, "Computress_Tut25", 39), C(0, TutorialPointerAnchor.Talk),
                V(4.5f, "Computress_Tut26", 40), A(4.5f, TutorialAuxiliaryCommand.Waypoint),
                A(4.5f, TutorialAuxiliaryCommand.Secondary, "Select \"Transmitter Critters\" from mission menu."), A(4.5f, TutorialAuxiliaryCommand.HidePicture), C(4.5f, TutorialPointerAnchor.Mission)
            }),
            TutorialAuxiliarySequence.TalkNumTwo2 => new(new[] {
                V(0, "Computress_Tut38", 41), C(0, TutorialPointerAnchor.Talk), A(4.5f, TutorialAuxiliaryCommand.HidePicture),
                A(4.5f, TutorialAuxiliaryCommand.Secondary, "Select \"Transmitter Critters\" from mission menu."), C(4.5f, TutorialPointerAnchor.Mission)
            }),
            TutorialAuxiliarySequence.TalkButtercup1 => new(new[] {
                V(0, "Btrcup_Tut03", 43), V(5, "Computress_Tut41", 44), C(5, TutorialPointerAnchor.Minimap),
                A(27, TutorialAuxiliaryCommand.FusionMatter), A(27, TutorialAuxiliaryCommand.HidePicture), C(27, TutorialPointerAnchor.FusionMatter), A(27, TutorialAuxiliaryCommand.Primary, "Talk to Buttercup.")
            }),
            TutorialAuxiliarySequence.TalkDexter1 => new(new[] { V(0, "Dexter_Tut01", 69), V(5, "Computress_Tut49", 70), A(5, TutorialAuxiliaryCommand.Primary, "Go to the Fusion Portal.") }),
            TutorialAuxiliarySequence.TalkDexter2 => new(new[] { V(0, "Dexter_Tut02", 71), A(5, TutorialAuxiliaryCommand.Primary, "Talk to Dexter.") }),
            TutorialAuxiliarySequence.TalkDexter3 => new(new[] {
                V(0, "Computress_Tut56", 72), A(0, TutorialAuxiliaryCommand.Primary, "Use the warp gate to exit."), A(3, TutorialAuxiliaryCommand.ClearText),
                V(3, "Computress_Tut57", 73), A(3, TutorialAuxiliaryCommand.Primary, value:74)
            }),
            TutorialAuxiliarySequence.NanoPowerTalk1 => new(new[] { V(0, "Computress_Tut60", 88), P(0, "tut_rmouse", TutorialPointerAnchor.NanoMouse) },
                8, new[] { V(0, "Computress_Tut61", 89), A(0, TutorialAuxiliaryCommand.Primary, "Stun the monster.") }),
            _ => throw new ArgumentOutOfRangeException(nameof(sequence))
        };
    }
}
