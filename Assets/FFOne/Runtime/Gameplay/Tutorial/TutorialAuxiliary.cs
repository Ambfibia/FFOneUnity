using System;
using System.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        TutorialAuxiliarySequence auxiliarySequence;
        TutorialAuxiliaryTimeline auxiliaryTimeline;
        float auxiliaryElapsed, auxiliaryMessageTime;
        int auxiliaryCursor, auxiliaryRepeatCycle, auxiliaryFusionMatter, auxiliaryWaypoint;
        bool auxiliaryMyPoint, auxiliaryWaypointFlash;
        string auxiliarySecondary, auxiliaryCaption, auxiliaryMessage;
        Texture2D auxiliaryPicture, auxiliaryPointer;
        TutorialPointerAnchor auxiliaryPictureAnchor, auxiliaryPointerAnchor;
        public bool AuxiliaryOwnsHint => AuxiliaryPresentationVisible && auxiliaryPicture;
        bool AuxiliaryPresentationVisible => auxiliarySequence != TutorialAuxiliarySequence.None && !IsCinematic &&
            !(auxiliarySequence == TutorialAuxiliarySequence.TalkButtercup1 && Stage.Chapter == 4 && Stage.Step != 0 && Stage.Step != 1);

        void SyncAuxiliary()
        {
            var wanted = TutorialAuxiliaryTimeline.ForStage(Stage.Chapter, Stage.Step);
            // TalkBCup_1 retains its full-FM timer through the mission menu; the
            // menu owns the cursor while that timer is running in the background.
            if (wanted == TutorialAuxiliarySequence.None && auxiliarySequence == TutorialAuxiliarySequence.TalkButtercup1 &&
                Stage.Chapter == 4 && (Stage.Step == 1 || Stage.Step == 2 || Stage.Step == 101 || Stage.Step == 102)) return;
            if (wanted == auxiliarySequence) return;
            bool retainWaypoint = auxiliarySequence == TutorialAuxiliarySequence.MinimapEvent && Stage.Chapter == 2 && Stage.Step == 6;
            StopAuxiliary();
            if (retainWaypoint && Actor(1005)) auxiliaryWaypoint = 1005;
            if (wanted == TutorialAuxiliarySequence.None) return;
            auxiliarySequence = wanted; auxiliaryTimeline = TutorialAuxiliaryTimeline.Get(wanted);
            instruction = "";
            if (wanted == TutorialAuxiliarySequence.TalkButtercup1) auxiliaryFusionMatter = 220;
            TickAuxiliary(0);
        }

        void StopAuxiliary()
        {
            auxiliarySequence = TutorialAuxiliarySequence.None; auxiliaryTimeline = null;
            auxiliaryElapsed = 0; auxiliaryCursor = auxiliaryRepeatCycle = 0;
            auxiliaryPicture = auxiliaryPointer = null; auxiliarySecondary = "";
            auxiliaryMyPoint = auxiliaryWaypointFlash = false;
            auxiliaryMessage = ""; auxiliaryMessageTime = 0;
            if (VoiceSubtitleText == auxiliaryCaption) voiceSubtitles.Clear();
            auxiliaryCaption = "";
        }

        void TickAuxiliary(float delta)
        {
            if (!float.IsFinite(delta) || delta < 0) return;
            auxiliaryMessageTime = Mathf.Max(0, auxiliaryMessageTime - delta);
            if (auxiliaryTimeline == null) return;
            float end = auxiliaryElapsed + delta;
            float nextVoice = NextAuxiliaryVoiceTime();
            // EN follows exact WaitForSeconds. Accepted localized playback waits
            // at the next voice boundary instead of cutting off a longer RU line.
            if (VoiceRussian && nextVoice <= end && nextVoice > auxiliaryElapsed)
                end = voice && voice.isPlaying ? auxiliaryElapsed : nextVoice;
            while (auxiliaryCursor < auxiliaryTimeline.Actions.Length && auxiliaryTimeline.Actions[auxiliaryCursor].Seconds <= end)
                ApplyAuxiliary(auxiliaryTimeline.Actions[auxiliaryCursor++]);
            while (auxiliaryTimeline.Repeating.Length > 0 && auxiliaryTimeline.RepeatAt + auxiliaryTimeline.RepeatEvery * auxiliaryRepeatCycle <= end)
            {
                foreach (var action in auxiliaryTimeline.Repeating) ApplyAuxiliary(action);
                auxiliaryRepeatCycle++;
            }
            auxiliaryElapsed = end;
        }

        float NextAuxiliaryVoiceTime()
        {
            float next = float.PositiveInfinity;
            for (int i = auxiliaryCursor; i < auxiliaryTimeline.Actions.Length; i++)
                if (auxiliaryTimeline.Actions[i].Command == TutorialAuxiliaryCommand.Voice) { next = auxiliaryTimeline.Actions[i].Seconds; break; }
            if (auxiliaryTimeline.Repeating.Any(a => a.Command == TutorialAuxiliaryCommand.Voice))
                next = Mathf.Min(next, auxiliaryTimeline.RepeatAt + auxiliaryTimeline.RepeatEvery * auxiliaryRepeatCycle);
            return next;
        }

        void ApplyAuxiliary(TutorialAuxiliaryAction action)
        {
            switch (action.Command)
            {
                case TutorialAuxiliaryCommand.Voice:
                    PlayVoice(action.Text);
                    if (action.Value != 0)
                    {
                        // Preserve explicit VOSubtitleOut lines, including audio-only cue identities.
                        auxiliaryCaption = Localized("content.tutorial.scene.2." + action.Value);
                        float duration = Mathf.Max(voiceSubtitles.Remaining, voice && voice.clip ? voice.clip.length : 5);
                        voiceSubtitles.Set(auxiliaryCaption, duration);
                    }
                    break;
                case TutorialAuxiliaryCommand.Picture:
                    auxiliaryPicture = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/tutorial/" + action.Text); auxiliaryPictureAnchor = action.Anchor; break;
                case TutorialAuxiliaryCommand.Cursor:
                    auxiliaryPointer = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/tutorial/" + action.Text); auxiliaryPointerAnchor = action.Anchor; break;
                case TutorialAuxiliaryCommand.HidePicture: auxiliaryPicture = auxiliaryPointer = null; break;
                case TutorialAuxiliaryCommand.Primary:
                    if (AuxiliaryPresentationVisible) instruction = action.Value == 0 ? LocalizeLiteral(action.Text) : Localized("content.tutorial.scene.2." + action.Value); break;
                case TutorialAuxiliaryCommand.Secondary: auxiliarySecondary = LocalizeLiteral(action.Text); break;
                case TutorialAuxiliaryCommand.ClearText: instruction = auxiliarySecondary = ""; break;
                case TutorialAuxiliaryCommand.Preload: effects.Preload(action.Value); break;
                case TutorialAuxiliaryCommand.Message:
                    auxiliaryMessage = LocalizeLiteral(action.Text); auxiliaryMessageTime = 10; break;
                case TutorialAuxiliaryCommand.SpawnNumbuhTwo:
                    SpawnActor(1005, 2671, new Vector3(651, -82, 739)); break;
                case TutorialAuxiliaryCommand.NumbuhTwoMarker:
                    var actor = Actor(1005) ?? throw new InvalidOperationException("MinimapEvent NPC 1005 is absent.");
                    effects.Spawn(668, actor.transform.position + Vector3.down, 1, true); break;
                case TutorialAuxiliaryCommand.NumbuhTwoAngle: Actor(1005).transform.rotation = Quaternion.Euler(0, action.Value + 180, 0); break;
                case TutorialAuxiliaryCommand.NumbuhTwoAnimation: Animate(Actor(1005), action.Text); break;
                case TutorialAuxiliaryCommand.MyPoint: auxiliaryMyPoint = action.Value != 0; break;
                case TutorialAuxiliaryCommand.WaypointFlash: auxiliaryWaypointFlash = action.Value != 0; break;
                case TutorialAuxiliaryCommand.Waypoint: auxiliaryWaypoint = action.Value; break;
                case TutorialAuxiliaryCommand.FusionMatter: auxiliaryFusionMatter = action.Value; break;
                case TutorialAuxiliaryCommand.ClearEffects: effects.ClearTracked(); break;
                default: throw new ArgumentOutOfRangeException();
            }
        }

        static Vector2 PictureSize(string resource) => resource.StartsWith("tut_move", StringComparison.Ordinal) ? new Vector2(182, 131) : new Vector2(116, 154);
    }

    public static class TutorialAuxiliaryGeometry
    {
        // Original FFGUIUtility.ScaleAroundPivot screen anchors, plus the native
        // tutorial cursor's -10 + sin(Time.time * PI * 4) * 10 horizontal offset.
        public static Rect Rect(TutorialPointerAnchor anchor, bool pointer, Vector2 size, int width, int height, float time, float scale = 1)
        {
            Vector2 position = anchor switch {
                TutorialPointerAnchor.MoveKeys => new Vector2(80, height - 330),
                TutorialPointerAnchor.NanoMouse => new Vector2(80, height - 350),
                TutorialPointerAnchor.Minimap => new Vector2(width - 280, 50),
                TutorialPointerAnchor.MyPoint => new Vector2(width - 200, 50),
                TutorialPointerAnchor.Waypoint => new Vector2(width - 140, 0),
                TutorialPointerAnchor.Talk => new Vector2(width / 2 - 100, height / 4),
                TutorialPointerAnchor.Mission => new Vector2(width * 3 / 4 - 246, height / 2 - 41),
                // The restored HUD displays Fusion Matter around the NanoCom
                // disk. Point at its left edge, not the objective text below it.
                TutorialPointerAnchor.FusionMatter => new Vector2(width - 259, 50),
                _ => throw new ArgumentOutOfRangeException(nameof(anchor))
            };
            Vector2 pivot = anchor switch {
                TutorialPointerAnchor.MoveKeys or TutorialPointerAnchor.NanoMouse => new Vector2(0, height),
                TutorialPointerAnchor.Talk => new Vector2(width / 2, height / 4),
                TutorialPointerAnchor.Mission => new Vector2(width * 3 / 4, height / 2),
                _ => new Vector2(width, 0)
            };
            if (pointer) position.x += -10 + Mathf.Sin(time * Mathf.PI * 4) * 10;
            return new Rect(pivot + (position - pivot) * scale, size * scale);
        }
    }
}
