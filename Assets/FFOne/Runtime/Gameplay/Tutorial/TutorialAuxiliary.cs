using System;
using System.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        TutorialAuxiliarySequence auxiliarySequence;
        TutorialAuxiliaryTimeline auxiliaryTimeline;
        float auxiliaryElapsed, auxiliaryCaptionTime, auxiliaryMessageTime;
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
            if (subtitle == auxiliaryCaption) subtitle = "";
            auxiliaryCaption = ""; auxiliaryCaptionTime = 0;
        }

        void TickAuxiliary(float delta)
        {
            if (!float.IsFinite(delta) || delta < 0) return;
            if (!string.IsNullOrEmpty(auxiliaryCaption))
            {
                auxiliaryCaptionTime -= delta;
                if (auxiliaryCaptionTime <= 0 && (!voice || !voice.isPlaying))
                { if (subtitle == auxiliaryCaption) subtitle = ""; auxiliaryCaption = ""; }
            }
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
                        Subtitle("content.tutorial.scene.2." + action.Value); auxiliaryCaption = subtitle;
                        auxiliaryCaptionTime = voice && voice.clip ? Mathf.Max(1, voice.clip.length) : 5;
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

        void RenderAuxiliary(float width, float height)
        {
            if (IsCinematic) return;
            if (AuxiliaryPresentationVisible)
            {
                if (auxiliaryPicture)
                    GUI.DrawTexture(TutorialAuxiliaryGeometry.Rect(auxiliaryPictureAnchor, false, PictureSize(auxiliaryPicture.name), (int)width, (int)height, Time.time), auxiliaryPicture);
                if (auxiliaryPointer)
                    GUI.DrawTexture(TutorialAuxiliaryGeometry.Rect(auxiliaryPointerAnchor, true, new Vector2(88, 69), (int)width, (int)height, Time.time), auxiliaryPointer);
                if (!string.IsNullOrEmpty(auxiliarySecondary))
                    GUI.Label(new Rect(width * .1f, 0, width * .8f, 65), auxiliarySecondary, instructionStyle);
            }
            if (Stage.Chapter == 4 && Stage.Step <= 2)
            {
                var meter = new Rect(width - 182, 201, 160, 14);
                Box(meter, new Color(.08f, .02f, .15f, .9f));
                Box(new Rect(meter.x, meter.y, meter.width * auxiliaryFusionMatter / 220f, meter.height), new Color(.61f, .24f, .9f));
                GUI.Label(new Rect(meter.x, meter.y + 14, meter.width, 25), auxiliaryFusionMatter + " / 220", labelStyle);
            }
            if (auxiliaryMessageTime > 0 && !string.IsNullOrEmpty(auxiliaryMessage))
            {
                var rect = new Rect(24, 68, Mathf.Min(350, width * .34f), 115);
                Box(rect, new Color(.02f, .08f, .1f, .92f));
                GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 25), Localized("content.npc.2671.name"), instructionStyle);
                GUI.Label(new Rect(rect.x + 10, rect.y + 34, rect.width - 20, 75), auxiliaryMessage, labelStyle);
            }
            if (auxiliaryMyPoint || auxiliaryWaypointFlash)
            {
                Vector2 center = new(width - 102, 100);
                if (auxiliaryWaypointFlash && Actor(auxiliaryWaypoint))
                {
                    var difference = Actor(auxiliaryWaypoint).transform.position - Player.position;
                    center += Vector2.ClampMagnitude(new Vector2(difference.x, -difference.z) * .8f, 70);
                }
                float size = 10 + 8 * (.5f + .5f * Mathf.Sin(Time.time * Mathf.PI * 4));
                Box(new Rect(center.x - size / 2, center.y - size / 2, size, size), new Color(1, 1, 0, .45f));
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
                TutorialPointerAnchor.FusionMatter => new Vector2(width - 259, 200),
                _ => throw new ArgumentOutOfRangeException(nameof(anchor))
            };
            Vector2 pivot = anchor switch {
                TutorialPointerAnchor.MoveKeys or TutorialPointerAnchor.NanoMouse => new Vector2(0, height),
                TutorialPointerAnchor.Talk => new Vector2(width / 2, height / 4),
                TutorialPointerAnchor.Mission or TutorialPointerAnchor.FusionMatter => new Vector2(width * 3 / 4, height / 2),
                _ => new Vector2(width, 0)
            };
            if (pointer) position.x += -10 + Mathf.Sin(time * Mathf.PI * 4) * 10;
            return new Rect(pivot + (position - pivot) * scale, size * scale);
        }
    }
}
