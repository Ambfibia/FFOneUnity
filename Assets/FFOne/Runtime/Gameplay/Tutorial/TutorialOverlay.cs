using UnityEngine;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        GUIStyle subtitleStyle, instructionStyle, labelStyle, titleStyle;
        GUIStyle cinematicSpeakerStyle, cinematicLocationStyle, cinematicSkipStyle;
        bool locationCaption;
        Texture2D hint;
        int lastHintChapter = -1, lastHintStep = -1;
        void OnGUI()
        {
            if (!IsReady || ending) return;
            EnsureStyles();
            float width = Screen.width, height = Screen.height;
            if (paused)
            {
                Box(new Rect(width / 2 - 180, height / 2 - 100, 360, 200), new Color(.04f, .07f, .08f, .96f));
                GUI.Label(new Rect(width / 2 - 160, height / 2 - 85, 320, 40), Russian ? "ПАУЗА" : "PAUSED", titleStyle);
                if (GUI.Button(new Rect(width / 2 - 130, height / 2 - 30, 260, 38), Russian ? "Продолжить" : "Continue")) paused = false;
                if (GUI.Button(new Rect(width / 2 - 130, height / 2 + 20, 260, 38), Russian ? "Выбор персонажа" : "Character selection")) Cancel();
                return;
            }
            if (!IsCinematic && hiddenHud == 0)
            {
                DrawInstruction(width, height);
                DrawTarget(width);
                DrawMarker();
                DrawMinimap(width);
                if (interacting != 0) DrawNpcWindow(width, height);
                else if (journal) DrawJournal(width, height);
                else if (mainMenu) DrawMainMenu(width, height);
            }
            if (bars > 0)
            {
                Box(new Rect(0, 0, width, height * .13f * bars), Color.black);
                Box(new Rect(0, height * (1 - .13f * bars), width, height * .13f * bars), Color.black);
            }
            PanPlayer?.Render(new Rect(0, 0, width, height));
            DrawPicture(width, height);
            RenderAuxiliary(width, height);
            if (!string.IsNullOrEmpty(subtitle))
            {
                Color color = GUI.color; GUI.color = new Color(1, 1, 1, subtitleOpacity);
                string text = subtitle.Substring(0, Mathf.Min(subtitle.Length, subtitleCharacters));
                if (IsCinematic) DrawCinematicSubtitle(width, height, text);
                else GUI.Label(new Rect(width * .13f, height * .79f, width * .74f, height * .17f), text, subtitleStyle);
                GUI.color = color;
            }
            if (fade > 0) Box(new Rect(0, 0, width, height), new Color(0, 0, 0, fade));
            if (IsCinematic)
            {
                float scale = FFOne.UI.FrontendLayout.CharacterScale(width, height);
                cinematicSkipStyle.fontSize = Mathf.RoundToInt(12 * scale);
                if (GUI.Button(new Rect(width / 2 - 250 * scale, 5 * scale, 500 * scale, 25 * scale),
                    Russian ? "Нажмите ПРОБЕЛ, чтобы пропустить." : "Press SPACE BAR to skip.", cinematicSkipStyle)) SkipScene();
            }
        }

        void DrawCinematicSubtitle(float width, float height, string text)
        {
            float scale = FFOne.UI.FrontendLayout.CharacterScale(width, height);
            cinematicSpeakerStyle.fontSize = cinematicLocationStyle.fontSize = Mathf.RoundToInt(12 * scale);
            if (locationCaption)
            {
                GUI.Label(new Rect(width * .04f, height * .73f, width * .55f, height * .2f), text, cinematicLocationStyle);
                return;
            }
            int separator = text.IndexOf(':');
            if (separator >= 0 && separator < 40)
            {
                GUI.Label(new Rect(width * .13f, height * .845f, width * .74f, 22 * scale), text.Substring(0, separator + 1), cinematicSpeakerStyle);
                text = text.Substring(separator + 1).TrimStart();
            }
            int previousSize = subtitleStyle.fontSize;
            subtitleStyle.fontSize = Mathf.RoundToInt(14 * scale);
            GUI.Label(new Rect(width * .13f, height * .88f, width * .74f, height * .1f), text, subtitleStyle);
            subtitleStyle.fontSize = previousSize;
        }
        void EnsureStyles()
        {
            if (subtitleStyle != null) return;
            // Reuse accepted replacement fonts imported with the front end, including Cyrillic.
            var fonts = FFOne.Content.ContentAssets.LoadAll<Font>("fonts"); Font font = null;
            foreach (var candidate in fonts) if (candidate.HasCharacter('Ж')) { font = candidate; break; }
            subtitleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            subtitleStyle.normal.textColor = Color.white;
            instructionStyle = new GUIStyle(subtitleStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(subtitleStyle) { fontSize = 16, alignment = TextAnchor.UpperLeft };
            titleStyle = new GUIStyle(instructionStyle) { fontSize = 22 };
            var headingFont = FFOne.Content.ContentAssets.Load<Font>("fonts/jeffe");
            cinematicSpeakerStyle = new GUIStyle(subtitleStyle) { font = headingFont ? headingFont : font };
            cinematicLocationStyle = new GUIStyle(cinematicSpeakerStyle) { alignment = TextAnchor.UpperLeft };
            cinematicSkipStyle = new GUIStyle(subtitleStyle) { wordWrap = false };
        }
        static void Box(Rect rect, Color color)
        {
            Color saved = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = saved;
        }
        void DrawInstruction(float width, float height)
        {
            if (lastHintChapter != Stage.Chapter || lastHintStep != Stage.Step)
            {
                lastHintChapter = Stage.Chapter; lastHintStep = Stage.Step;
                string name = Stage.Chapter == 0 ? Stage.Step switch {
                    3 => "tut_right", 4 => "tut_left", 5 => "tut_up", 6 => "tut_down", 7 => "tut_move_w", 8 => "tut_move_s", 9 => "tut_move", 10 => "tut_move", 11 => "tut_jump", _ => null
                } : Stage.Chapter == 1 && Stage.Step >= 2 && Stage.Step <= 4 ? "tut_lmouse"
                    : Stage.Chapter == 5 && Stage.Step == 4 ? "tut_one" : Stage.Chapter == 5 && Stage.Step == 6 ? "tut_rmouse" : null;
                hint = name == null ? null : FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/tutorial/" + name);
            }
            if (hint && !AuxiliaryOwnsHint) GUI.DrawTexture(new Rect(48, height - 250, 182, 131), hint, ScaleMode.ScaleToFit);
            if (!string.IsNullOrEmpty(instruction))
            {
                Box(new Rect(width * .18f, height - 105, width * .64f, 76), new Color(0, .06f, .09f, .8f));
                GUI.Label(new Rect(width * .2f, height - 100, width * .6f, 65), instruction, instructionStyle);
            }
            if (nanoActive) GUI.Label(new Rect(25, height - 80, 170, 45), "1  BUTTERCUP", labelStyle);
            Box(new Rect(24, 24, 190, 16), new Color(.15f, .05f, .05f, .9f));
            Box(new Rect(24, 24, 190 * playerHp / 1000f, 16), new Color(.6f, .92f, .18f));
        }
        void DrawPicture(float width, float height)
        {
            if (picture == null) return;
            var texture = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/tutorial/" + (string)picture["resource"]);
            if (!texture) return;
            var position = picture["position"]!["args"]!;
            float x = ScreenCoordinate(position[0], width, height), y = ScreenCoordinate(position[1], width, height);
            int pivot = (int?)picture["legacy_pivot"] ?? 0;
            float w = texture.width, h = texture.height;
            if (pivot == 2) x -= w / 2;
            GUI.DrawTexture(new Rect(x, y, w, h), texture, ScaleMode.ScaleToFit);
        }
        static float ScreenCoordinate(Newtonsoft.Json.Linq.JToken value, float width, float height)
        {
            float amount = (float)value["args"]![0];
            return (string)value["type"] switch { "WidthMinus" => width - amount, "HeightMinus" => height - amount, _ => amount };
        }
        void DrawTarget(float width)
        {
            if (!target) return;
            Rect box = new(width / 2 - 140, 35, 280, 68);
            Box(box, new Color(.02f, .05f, .08f, .85f));
            GUI.Label(new Rect(box.x, box.y + 5, box.width, 28), target.DisplayName, instructionStyle);
            if (target.Hostile)
            {
                Box(new Rect(box.x + 14, box.y + 43, 252, 12), new Color(.2f, .02f, .02f));
                Box(new Rect(box.x + 14, box.y + 43, 252 * target.Hp / target.MaxHp, 12), new Color(.7f, .12f, .08f));
            }
        }
        void DrawMarker()
        {
            Vector3? destination = marker;
            if (destination == null && Actor(ObjectiveNpc)) destination = Actor(ObjectiveNpc).transform.position + Vector3.up * 3;
            if (destination == null) return;
            Vector3 point = ViewCamera.WorldToScreenPoint(destination.Value);
            if (point.z <= 0) return;
            Color color = GUI.color; GUI.color = Color.yellow;
            GUI.Label(new Rect(point.x - 20, Screen.height - point.y - 30, 40, 40), "▼", instructionStyle);
            GUI.color = color;
        }
        void DrawMinimap(float width)
        {
            if (Stage.Chapter < 2) return;
            Rect bounds = new(width - 182, 20, 160, 160); Box(bounds, new Color(.02f, .12f, .13f, .85f));
            Vector2 center = bounds.center;
            Box(new Rect(center.x - 3, center.y - 3, 6, 6), Color.cyan);
            var objective = Actor(ObjectiveNpc);
            if (objective)
            {
                Vector3 delta = objective.transform.position - Player.position;
                Vector2 d = Vector2.ClampMagnitude(new Vector2(delta.x, -delta.z) * .8f, 70);
                Box(new Rect(center.x + d.x - 4, center.y + d.y - 4, 8, 8), Color.yellow);
            }
            GUI.Label(new Rect(bounds.x, bounds.yMax + 4, bounds.width, 30), "TECH SQUARE", instructionStyle);
        }
        Rect Window(float width, float height, string title)
        {
            Rect rect = new(width / 2 - 230, height / 2 - 150, 460, 300);
            Box(rect, new Color(.015f, .055f, .075f, .97f));
            Box(new Rect(rect.x, rect.y, rect.width, 45), new Color(.05f, .28f, .3f));
            GUI.Label(new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 37), title, titleStyle); return rect;
        }
        void DrawNpcWindow(float width, float height)
        {
            var npc = Actor(interacting); if (!npc) { interacting = 0; return; }
            var rect = Window(width, height, npc.GetComponent<TutorialActor>().DisplayName);
            if (interacting == 1009 || interacting == 1012 || interacting == 4001)
            {
                GUI.Label(new Rect(rect.x + 28, rect.y + 70, rect.width - 56, 80), instruction, labelStyle);
                GUI.enabled = pendingWarp < 0;
                if (GUI.Button(new Rect(rect.x + 80, rect.y + 188, 300, 38), Russian ? "Переместиться" : "Warp")) RequestWarp();
                GUI.enabled = true; return;
            }
            string title = Localized("content.mission.task." + (Stage.Chapter == 3 ? "2248" : "2250") + ".title");
            bool select = Stage.Chapter == 3 && (Stage.Step == 3 || Stage.Step == 16);
            bool accept = Stage.Chapter == 3 && Stage.Step == 101 || Stage.Chapter == 4 && (Stage.Step == 2 || Stage.Step == 109);
            bool reward = Stage.Chapter == 3 && Stage.Step == 17;
            GUI.Label(new Rect(rect.x + 28, rect.y + 65, rect.width - 56, 55), title, instructionStyle);
            if (select && GUI.Button(new Rect(rect.x + 60, rect.y + 125, 340, 40), title)) missionSelected = true;
            if (accept && GUI.Button(new Rect(rect.x + 80, rect.y + 185, 300, 40), Russian ? "Принять задание" : "Accept mission")) accepted = true;
            if (reward && GUI.Button(new Rect(rect.x + 80, rect.y + 185, 300, 40), Russian ? "Получить награду" : "Claim reward")) rewardClaimed = true;
            bool close = Stage.Chapter == 3 && (Stage.Step == 102 || Stage.Step == 18) || Stage.Chapter == 4 && Stage.Step == 102;
            if (close && GUI.Button(new Rect(rect.x + 130, rect.y + 235, 200, 35), Russian ? "Закрыть" : "Close")) interacting = 0;
        }
        void DrawJournal(float width, float height)
        {
            var rect = Window(width, height, Russian ? "ЖУРНАЛ" : "JOURNAL");
            GUI.Label(new Rect(rect.x + 25, rect.y + 65, rect.width - 50, 40), Localized("content.mission.task.2248.title"), instructionStyle);
            GUI.Label(new Rect(rect.x + 30, rect.y + 120, rect.width - 60, 90), Localized("content.mission.task.2249.objective"), labelStyle);
            GUI.enabled = Stage.Chapter != 3 || Stage.Step >= 12;
            if (GUI.Button(new Rect(rect.x + 130, rect.y + 240, 200, 35), Russian ? "Закрыть" : "Close")) journal = false;
            GUI.enabled = true;
        }
        void DrawMainMenu(float width, float height)
        {
            var rect = Window(width, height, "NANOCOM");
            if (GUI.Button(new Rect(rect.x + 70, rect.y + 110, 320, 55), Russian ? "Журнал" : "Journal")) { journal = true; mainMenu = false; }
        }
    }
}
