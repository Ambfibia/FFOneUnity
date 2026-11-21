using FFOne.Content;
using FFOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        bool locationCaption;
        TutorialOverlayView tutorialOverlay;

        void AttachTutorialOverlay(VisualElement parent)
        {
            tutorialOverlay = new TutorialOverlayView(this);
            UiLayers.For(parent).Add(tutorialOverlay.Root, UiLayer.Cinematic);
            tutorialOverlay.Refresh();
        }

        void DetachTutorialOverlay()
        {
            tutorialOverlay?.Root.RemoveFromHierarchy();
            tutorialOverlay = null;
        }

        void LateUpdate()
        {
            try { tutorialOverlay?.Refresh(); }
            catch (System.Exception error) { LastError = error.Message; Debug.LogException(error, this); Cleanup(); }
        }

        sealed class TutorialOverlayView
        {
            const string TutorialArt = "ui/en/gameplay/tutorial/";
            const string ViewAddress = "ui/views/Retrobution/GameplayHud";
            readonly TutorialController owner;
            internal VisualElement Root { get; }
            readonly VisualElement scene, instructions, pause, topBar, bottomBar, blackout, jumpKey;
            readonly Image hint, arrow, scenePicture, auxiliaryPicture, auxiliaryPointer;
            readonly Label instruction, instructionShadow, secondary, secondaryShadow;
            readonly Label location, locationShadow, pauseTitle;
            readonly Dialogue scripted, voice;
            readonly Button skip, resume, exit;
            int hintChapter = -1, hintStep = -1;
            JToken displayedPicture;

            public TutorialOverlayView(TutorialController owner)
            {
                this.owner = owner;
                Root = FFView.InstantiateClass(ViewAddress, "tutorial-overlay");
                scene = Role<VisualElement>("tutorial-presentation");
                instructions = Role<VisualElement>("tutorial-instructions");
                hint = Role<Image>("tutorial-hint-image");
                arrow = Role<Image>("tutorial-hint-arrow");
                jumpKey = Role<VisualElement>("tutorial-localized-jump-key");
                var jumpArtwork = Texture("tut_jump");
                SetJumpStrip(jumpArtwork, 0, 0, 0, 298, 30);
                SetJumpStrip(jumpArtwork, 1, 0, 55, 298, 36);
                SetJumpStrip(jumpArtwork, 2, 0, 30, 30, 25);
                SetJumpStrip(jumpArtwork, 3, 265, 30, 33, 25);
                SetJumpStrip(jumpArtwork, 4, 140, 30, 1, 25);
                instructionShadow = Role<Label>("tutorial-instruction-shadow");
                instruction = Role<Label>("tutorial-instruction");
                topBar = Role<VisualElement>("tutorial-top-bar");
                bottomBar = Role<VisualElement>("tutorial-bottom-bar");
                if (owner.PanPlayer != null) Role<VisualElement>("tutorial-pan-host").Add(owner.PanPlayer.View);
                scenePicture = Role<Image>("tutorial-scene-picture");
                auxiliaryPicture = Role<Image>("tutorial-auxiliary-picture");
                auxiliaryPointer = Role<Image>("tutorial-auxiliary-pointer");
                secondaryShadow = Role<Label>("tutorial-secondary-instruction-shadow");
                secondary = Role<Label>("tutorial-secondary-instruction");
                locationShadow = Role<Label>("tutorial-location-shadow");
                location = Role<Label>("tutorial-location");
                scripted = new Dialogue(Role<VisualElement>("tutorial-scripted"));
                voice = new Dialogue(Role<VisualElement>("tutorial-voice"));
                blackout = Role<VisualElement>("tutorial-fade");
                skip = Role<Button>("tutorial-cinematic-skip");
                skip.clicked += () =>
                {
                    if (owner.IsCinematic && !owner.ExternalInputBlocked) owner.SkipScene();
                };
                pause = Role<VisualElement>("tutorial-pause");
                pauseTitle = Role<Label>("tutorial-pause-title");
                resume = BindPauseButton("tutorial-resume", () => owner.paused = false);
                exit = BindPauseButton("tutorial-exit", owner.Cancel);
            }

            T Role<T>(string name) where T : VisualElement => FFView.RequireClass<T>(Root, name);

            void SetJumpStrip(Texture2D artwork, int index, float x, float y, float width, float height)
            {
                // Sample the original strips around the baked English label.
                var strip = Role<Image>("tutorial-jump-strip-" + index);
                strip.image = artwork;
                strip.uv = new Rect(x / 298, 1 - (y + height) / 91, width / 298, height / 91);
            }

            Button BindPauseButton(string role, System.Action action)
            {
                var button = Role<Button>(role);
                button.clicked += () =>
                {
                    if (!owner.paused || owner.ExternalInputBlocked) return;
                    owner.hudInputFrame = Time.frameCount;
                    action();
                };
                return button;
            }

            internal void Refresh()
            {
                bool ready = owner.IsReady && !owner.ending && Root.parent != null;
                Show(Root, ready);
                if (!ready) return;
                float width = Root.parent.resolvedStyle.width;
                float height = Root.parent.resolvedStyle.height;
                if (!float.IsFinite(width) || width <= 0) width = Screen.width;
                if (!float.IsFinite(height) || height <= 0) height = Screen.height;
                Show(pause, owner.paused);
                Show(scene, !owner.paused);
                if (owner.paused)
                {
                    pauseTitle.text = owner.Russian ? "ПАУЗА" : "PAUSED";
                    resume.text = owner.Russian ? "Продолжить" : "Continue";
                    exit.text = owner.Russian ? "Выбор персонажа" : "Character selection";
                    return;
                }

                float scale = RetrobutionLayout.Scale(height);
                bool cinematic = owner.IsCinematic;
                bool showHints = !cinematic && owner.hiddenHud == 0 && owner.showComputressHints;
                Show(instructions, showHints);
                if (showHints) UpdateInstructions(width, height, scale);

                Show(topBar, owner.bars > 0);
                Show(bottomBar, owner.bars > 0);
                topBar.style.backgroundColor = bottomBar.style.backgroundColor = new Color(0, 0, 0, owner.bars);

                owner.PanPlayer?.Present(width, height);
                UpdatePicture(width, height);
                UpdateAuxiliary(width, height, scale);
                UpdateSubtitles(width, height, scale);
                Show(blackout, owner.fade > 0);
                blackout.style.opacity = owner.fade;
                Show(skip, cinematic);
                if (cinematic)
                {
                    float skipScale = FrontendLayout.CharacterScale(width, height);
                    skip.text = owner.Russian ? "Нажмите ПРОБЕЛ, чтобы пропустить." : "Press SPACE BAR to skip.";
                    skip.style.fontSize = Mathf.RoundToInt(12 * skipScale);
                    FFWidgets.Rect(skip, width / 2 - 250 * skipScale, 5 * skipScale, 500 * skipScale, 25 * skipScale);
                }
            }

            void UpdateInstructions(float width, float height, float scale)
            {
                var stage = owner.Stage;
                if (hintChapter != stage.Chapter || hintStep != stage.Step)
                {
                    hintChapter = stage.Chapter;
                    hintStep = stage.Step;
                    string name = stage.Chapter == 0 ? stage.Step switch
                    {
                        >= 3 and <= 6 => "tut_mouse",
                        7 or 9 or 10 => "tut_move_w",
                        8 => "tut_move_s",
                        11 => "tut_jump",
                        _ => null
                    } : stage.Chapter == 1 && stage.Step >= 2 && stage.Step <= 7 ? "tut_lmouse"
                        : stage.Chapter == 5 && stage.Step == 4 ? "tut_one"
                        : stage.Chapter == 5 && stage.Step == 6 ? "tut_rmouse" : null;
                    hint.image = Texture(name);
                    arrow.image = stage.Chapter == 0 ? Texture(stage.Step switch
                    {
                        3 => "tut_right", 4 => "tut_left", 5 => "tut_up", 6 => "tut_down", _ => null
                    }) : null;
                }
                bool showHint = hint.image && !owner.AuxiliaryOwnsHint;
                bool localizedJump = showHint && owner.Russian && hint.image.name == "tut_jump";
                Show(hint, showHint && !localizedJump);
                Show(jumpKey, localizedJump);
                Show(arrow, showHint && arrow.image);
                if (showHint)
                {
                    var rect = TutorialInstructionLayout.Illustration(hint.image.name, Size(hint.image), height, scale);
                    if (localizedJump) LayoutJumpKey(rect, scale);
                    else FFWidgets.Rect(hint, rect);
                    if (arrow.image) FFWidgets.Rect(arrow, TutorialInstructionLayout.Arrow(stage.Step, Size(arrow.image), height, Time.time, scale));
                }
                Caption(instruction, instructionShadow, owner.instruction?.ToUpperInvariant(), TutorialInstructionLayout.Text(width, height, false, scale), scale, 16);
            }

            void LayoutJumpKey(Rect rect, float scale)
            {
                jumpKey.style.left = rect.x;
                jumpKey.style.top = rect.y;
                jumpKey.style.scale = new Scale(new Vector3(scale, scale, 1));
            }

            void UpdatePicture(float width, float height)
            {
                var picture = owner.picture;
                if (!ReferenceEquals(displayedPicture, picture))
                {
                    displayedPicture = picture;
                    scenePicture.image = Texture((string)picture?["resource"]);
                }
                Show(scenePicture, scenePicture.image);
                if (!scenePicture.image) return;
                var position = picture["position"]["args"];
                float x = Coordinate(position[0], width, height);
                float y = Coordinate(position[1], width, height);
                var size = Size(scenePicture.image);
                if ((int?)picture["legacy_pivot"] == 2) x -= size.x / 2;
                FFWidgets.Rect(scenePicture, new Rect(new Vector2(x, y), size));
            }

            void UpdateAuxiliary(float width, float height, float scale)
            {
                bool visible = owner.AuxiliaryPresentationVisible;
                auxiliaryPicture.image = owner.auxiliaryPicture;
                auxiliaryPointer.image = owner.auxiliaryPointer;
                Show(auxiliaryPicture, visible && auxiliaryPicture.image);
                Show(auxiliaryPointer, visible && auxiliaryPointer.image);
                if (visible && auxiliaryPicture.image)
                    FFWidgets.Rect(auxiliaryPicture, TutorialAuxiliaryGeometry.Rect(owner.auxiliaryPictureAnchor, false,
                        PictureSize(auxiliaryPicture.image.name), (int)width, (int)height, Time.time, scale));
                if (visible && auxiliaryPointer.image)
                    FFWidgets.Rect(auxiliaryPointer, TutorialAuxiliaryGeometry.Rect(owner.auxiliaryPointerAnchor, true,
                        new Vector2(88, 69), (int)width, (int)height, Time.time, scale));
                Caption(secondary, secondaryShadow, visible ? owner.auxiliarySecondary?.ToUpperInvariant() : null,
                    TutorialInstructionLayout.Text(width, height, true, scale), scale, 16);
            }

            void UpdateSubtitles(float width, float height, float scale)
            {
                string text = null;
                if (!string.IsNullOrEmpty(owner.subtitle) && (owner.locationCaption || !owner.IsCinematic || owner.bars > .9f))
                {
                    text = DexterShipTimeline.NormalizeSubtitle(owner.subtitle);
                    if (owner.subtitleCharacters < text.Length) text = text.Substring(0, owner.subtitleCharacters);
                }
                float barTop = height * (1 - DexterShipTimeline.BlackBarRatio);
                var locationRect = new Rect(50 * scale, barTop - 100 * scale, 200 * scale, 100 * scale);
                Caption(location, locationShadow, owner.locationCaption ? text : null, locationRect, scale, 14, owner.subtitleOpacity > .9f);
                location.style.opacity = owner.subtitleOpacity;
                FFWidgets.Padding(location, 10 * scale, 6 * scale, 4 * scale, 6 * scale);
                FFWidgets.Padding(locationShadow, 10 * scale, 6 * scale, 4 * scale, 6 * scale);

                Rect bounds = TutorialInstructionLayout.Dialogue(width, height, scale);
                scripted.Present(owner.locationCaption ? null : text, bounds, scale, owner.subtitleOpacity);
                voice.Present(owner.VoiceSubtitleVisible ? DexterShipTimeline.NormalizeSubtitle(owner.VoiceSubtitleText) : null, bounds, scale, 1);
            }

            static float Coordinate(JToken value, float width, float height)
            {
                float amount = (float)value["args"][0];
                return (string)value["type"] switch { "WidthMinus" => width - amount, "HeightMinus" => height - amount, _ => amount };
            }

            static void Caption(Label label, Label shadow, string text, Rect rect, float scale, int size, bool drawShadow = true)
            {
                bool visible = !string.IsNullOrEmpty(text);
                Show(label, visible);
                Show(shadow, visible && drawShadow);
                if (!visible) return;
                label.text = shadow.text = text;
                label.style.fontSize = shadow.style.fontSize = Mathf.RoundToInt(size * scale);
                FFWidgets.Rect(label, rect);
                rect.position += Vector2.one * scale;
                FFWidgets.Rect(shadow, rect);
            }

            static Texture2D Texture(string name) => name == null ? null : ContentAssets.Load<Texture2D>(TutorialArt + name);
            static Vector2 Size(Texture texture) => new(texture.width, texture.height);
            static void Show(VisualElement element, bool show) => element.EnableInClassList("ui-suppressed", !show);

            sealed class Dialogue
            {
                readonly VisualElement area;
                readonly Label speaker, body;
                string previousText;
                bool hasSpeaker;

                internal Dialogue(VisualElement area)
                {
                    this.area = area;
                    speaker = FFView.RequireClass<Label>(area, "tutorial-dialogue-speaker");
                    body = FFView.RequireClass<Label>(area, "tutorial-dialogue-body");
                }

                internal void Present(string text, Rect bounds, float scale, float alpha)
                {
                    Show(area, !string.IsNullOrEmpty(text));
                    if (string.IsNullOrEmpty(text)) return;
                    if (text != previousText)
                    {
                        previousText = text;
                        int separator = text.IndexOf(':');
                        hasSpeaker = separator >= 0 && separator < 40;
                        speaker.text = hasSpeaker ? text.Substring(0, separator + 1) : "";
                        body.text = hasSpeaker ? text.Substring(separator + 1).TrimStart() : text;
                    }
                    float offset = hasSpeaker ? 22 * scale : 0;
                    Show(speaker, hasSpeaker);
                    speaker.style.fontSize = Mathf.RoundToInt(12 * scale);
                    body.style.fontSize = Mathf.RoundToInt(14 * scale);
                    area.style.opacity = alpha;
                    FFWidgets.Rect(area, bounds);
                    FFWidgets.Rect(speaker, 0, 0, bounds.width, offset);
                    FFWidgets.Rect(body, 0, offset, bounds.width, Mathf.Max(0, bounds.height - offset));
                }
            }
        }
    }

    public static class TutorialInstructionLayout
    {
        public static Rect Dialogue(float width, float height, float scale)
        {
            // Same accepted screen-space dialogue width as the ship scene; keep
            // the text scale independent so wide/high-resolution views still wrap.
            float margin = width * .1977848f;
            float barHeight = height * DexterShipTimeline.BlackBarRatio;
            return new Rect(margin, height - barHeight, width - margin * 2, barHeight);
        }
        public static Rect Text(float width, float height, bool secondary, float scale)
        {
            float y = secondary ? 0 : (int)height / 4;
            Vector2 pivot = secondary ? new Vector2(width / 2, 0) : new Vector2(width / 2, y + 30);
            var rect = Scaled(new Rect(0, y, width, 60), pivot, scale);
            // Screen width is already in pixels; scaling it again lets long
            // translated instructions wrap outside the visible viewport.
            rect.x = 0; rect.width = width;
            return rect;
        }

        public static Rect Illustration(string resource, Vector2 size, float height, float scale)
        {
            float x = resource == "tut_jump" ? 0 : 80;
            float bottom = resource == "tut_one" ? 250 : resource == "tut_jump" ? 290 : resource.StartsWith("tut_move") ? 330 : 350;
            return Scaled(new Rect(new Vector2(x, height - bottom), size), new Vector2(0, height), scale);
        }

        public static Rect Arrow(int step, Vector2 size, float height, float time, float scale)
        {
            float offset = -10 + Mathf.Sin(time * Mathf.PI * 4) * 10;
            Vector2 position = step switch {
                3 => new Vector2(186 + offset, height - 269),
                4 => new Vector2(-offset, height - 269),
                5 => new Vector2(107, height - 396 - offset),
                6 => new Vector2(107, height - 200 + offset),
                _ => Vector2.zero
            };
            return Scaled(new Rect(position, size), new Vector2(0, height), scale);
        }

        static Rect Scaled(Rect rect, Vector2 pivot, float scale) => new(pivot + (rect.position - pivot) * scale, rect.size * scale);
    }
}
