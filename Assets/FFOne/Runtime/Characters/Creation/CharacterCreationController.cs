using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FFOne.Integration;
using FFOne.Network;
using FFOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Creation
{
    [RequireComponent(typeof(FrontendController), typeof(PreviewStage))]
    public sealed class CharacterCreationController : MonoBehaviour
    {
        const string Art = "ui/en/character/creation/";
        FrontendController frontend;
        PreviewStage preview;
        CreationCatalog catalog;
        CharacterCreation draft;
        VisualElement overlay, body;
        VisualTreeAsset view;
        Label error;
        Image avatarImage, background;
        Button fullscreen;
        TextField customName;
        Label generatedName;
        readonly VisualElement[] nameWheels = new VisualElement[3];
        readonly Label[][] nameRows = new Label[3][];
        readonly int[] nameDirections = new int[3];
        readonly bool[] nameHeld = new bool[3];
        readonly float[] nameOffsets = new float[3], nameStarted = new float[3];
        readonly int[] nameIndices = { 1, 2, 2 };
        readonly Dictionary<string, int> choiceIndices = new Dictionary<string, int>();
        readonly Dictionary<string, int> colorPages = new Dictionary<string, int>();
        bool custom, busy, appearanceOpen;
        string customNameText = "";
        AudioSource creationMusic, sounds;
        CancellationTokenSource lifetime, creationLifetime;
        public bool IsOpen => overlay != null;

        void Start()
        {
            frontend = GetComponent<FrontendController>();
            preview = GetComponent<PreviewStage>();
            frontend.CreateCharacterRequested += Open;
            frontend.NetworkEventReceived += OnNetworkEvent;
            frontend.Root.RegisterCallback<GeometryChangedEvent>(ViewportChanged);
            lifetime = new CancellationTokenSource();
            creationMusic = gameObject.AddComponent<AudioSource>();
            creationMusic.playOnAwake = false; creationMusic.loop = true; creationMusic.volume = .7f;
            creationMusic.clip = FFOne.Content.ContentAssets.Load<AudioClip>("audio/music/charactercreation_loop");
            sounds = gameObject.AddComponent<AudioSource>();
            sounds.playOnAwake = false; sounds.volume = .7f;
        }
        string T(string key, string fallback) => frontend.Text(key, fallback);
        void OnNetworkEvent(NetworkEvent value)
        {
            if (value.Kind != NetworkEventKind.Disconnected || !IsOpen) return;
            busy = false;
            Close();
        }
        void Open(int slot)
        {
            if (IsOpen || busy) return;
            try
            {
                catalog ??= new CreationCatalog();
                custom = false; customNameText = ""; appearanceOpen = false;
                creationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                draft = new CharacterCreation { Slot = slot, Style = new CharacterStyle { Height = 2, Body = 1 } };
                foreach (var character in frontend.Session.Characters)
                    if (character.Slot == slot && !character.Style.AppearanceComplete)
                    {
                        draft.ExistingUid = character.Uid;
                        draft.FirstName = character.FirstName;
                        draft.LastName = character.LastName;
                    }
                choiceIndices.Clear(); colorPages.Clear();
                SetChoices();
                RandomName();
                view = FFOne.Content.ContentAssets.Load<VisualTreeAsset>("ui/views/CharacterCreation");
                if (!view) throw new InvalidOperationException("Missing CharacterCreation.uxml.");
                var shell = view.CloneTree();
                overlay = Required<VisualElement>(shell, "character-creation");
                overlay.RemoveFromHierarchy();
                overlay.styleSheets.Add(FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/FFTheme"));
                var styles = FFOne.Content.ContentAssets.Load<StyleSheet>("ui/styles/CharacterCreation");
                if (!styles) throw new InvalidOperationException("Missing CharacterCreation.uss.");
                overlay.styleSheets.Add(styles);
                overlay.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                frontend.Root.Add(overlay);
                frontend.SetCreationPresentationActive(true);
                if (PlayerPrefs.GetInt("ffone.music", 1) != 0 && creationMusic.clip) creationMusic.Play();
                background = Required<Image>(overlay, "creation-background");
                background.image = FFWidgets.Texture(Art + "background/CharCreationBG");
                background.scaleMode = ScaleMode.StretchToFill;
                fullscreen = Required<Button>(overlay, "creator-fullscreen");
                fullscreen.clicked += () => { frontend.ToggleFullscreen(); UpdateViewport(); };
                FFWidgets.Font(fullscreen, false, 12);
                FFStyle.Button(fullscreen, FFWidgets.Controls + "CSFullScreenButton", FFWidgets.Controls + "FullscreenModeButton_Over");
                if (draft.ExistingUid != 0) { RollAppearance(true); Appearance(); } else NameScreen();
            }
            catch (Exception ex) { Close(); frontend.ShowError(ex.Message); }
        }
        void ClearBody(string panel)
        {
            string focusedName = (frontend.Root.panel?.focusController.focusedElement as VisualElement)?.name;
            Array.Clear(nameDirections, 0, nameDirections.Length);
            Array.Clear(nameHeld, 0, nameHeld.Length);
            Array.Clear(nameOffsets, 0, nameOffsets.Length);
            body?.RemoveFromHierarchy();
            var tree = view.CloneTree();
            body = Required<VisualElement>(tree, panel);
            body.RemoveFromHierarchy();
            body.name = "creation-panel";
            overlay.Add(body);
            fullscreen.BringToFront();
            UpdateViewport();
            FFWidgets.FitTree(body);
            body.schedule.Execute(() => {
                var focused = frontend.Root.panel?.focusController.focusedElement as VisualElement;
                if (focused != null && overlay.Contains(focused)) return;
                var replacement = string.IsNullOrEmpty(focusedName) ? null : body.Q<VisualElement>(focusedName);
                if (replacement != null && replacement.focusable && replacement.enabledInHierarchy) replacement.Focus();
                else overlay.Focus();
            });
        }
        void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape) { if (!busy) Close(); evt.StopImmediatePropagation(); }
            else if (!appearanceOpen && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
            { if (!busy) ContinueName(); evt.StopImmediatePropagation(); }
        }
        void ViewportChanged(GeometryChangedEvent _) => UpdateViewport();
        void UpdateViewport()
        {
            if (overlay == null || body == null) return;
            float width = frontend.Root.resolvedStyle.width, height = frontend.Root.resolvedStyle.height;
            if (!(width > 0 && height > 0)) return;
            float scale = FrontendLayout.CharacterScale(width, height);
            body.style.scale = background.style.scale = new Scale(new Vector3(scale, scale, 1));
            // UXML inline dimensions are resolved after the first layout pass.
            // Keep its percentage anchor and negative half-size margins rather than
            // reading style.width here, which can still be Auto immediately after CloneTree.
            body.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            FFWidgets.ScaledRect(fullscreen, new Rect(width - 45, height - 35, 35, 31), scale, new Vector2(width, height));
            FFWidgets.Skin(fullscreen, FFWidgets.Controls + (Screen.fullScreen ? "CSFullScreenButton" : "CSFullToWindow"),
                FFWidgets.Controls + (Screen.fullScreen ? "FullscreenModeButton_Over" : "WindowModeButton_Over"));
        }
        static TElement Required<TElement>(VisualElement parent, string name) where TElement : VisualElement
        {
            return parent.Q<TElement>(name) ?? throw new InvalidOperationException("Character creation UXML is missing " + name + " (" + typeof(TElement).Name + ").");
        }
        VisualElement Picture(string name, string path)
        {
            var picture = Required<VisualElement>(body, name);
            FFStyle.Background(picture, Art + path);
            return picture;
        }
        Label Label(string name, string text, bool cyan = false)
        {
            var label = Required<Label>(body, name);
            label.text = FFStyle.Caption(text);
            FFWidgets.Font(label, false, cyan ? 12 : 14);
            FFWidgets.AutoFit(label);
            return label;
        }
        Button Action(string name, string label, System.Action action, bool red = false)
        {
            var button = Required<Button>(body, name);
            button.text = FFStyle.Caption(label); button.clicked += action;
            FFWidgets.Font(button, false, 14); FFWidgets.AutoFit(button);
            if (red) button.AddToClassList("creation-exit");
            FFStyle.Button(button, FFWidgets.Controls + (red ? "red_button_normal" : "blue_button_normal"),
                FFWidgets.Controls + (red ? "red_button_over" : "blue_button_over"),
                FFWidgets.Controls + (red ? "red_button_normal" : "blue_button_normal"));
            return button;
        }
        Button ArtButton(string name, string path, System.Action action)
        {
            string normal = path + "Normal", over = path + "Over";
            if (path == "camera/CCRotateRight") over = "camera/CCRoatateRightOver";
            if (path == "name/CCScrollDown") normal = "name/CCScrollDownUp";
            if (path.EndsWith("TopButton", StringComparison.Ordinal)) over = path.Replace("TopButton", "TopOver");
            if (path == "clothes/CCClothesRightBottomButton") over = "clothes/CCClothesRightBottomOver";
            var button = Required<Button>(body, name); button.clicked += action;
            FFWidgets.Font(button, false, 14);
            FFStyle.Button(button, Art + normal, Art + over, Art + normal);
            return button;
        }
        void NameTab(string name, string title, bool ownName)
        {
            var tab = Required<Button>(body, name); tab.text = FFStyle.Caption(title);
            FFWidgets.Font(tab, false, 12); FFWidgets.AutoFit(tab);
            tab.clicked += () => {
                if (custom == ownName) return;
                PlaySound("tab_click01"); custom = ownName; NameScreen();
            };
            FFStyle.Button(tab, Art + "name/CCNameTabButtonNormal", Art + "name/CCNameTabButtonOver", Art + "name/CCNameTabButtonNormal");
        }

        void NameScreen()
        {
            appearanceOpen = false;
            ClearBody(custom ? "custom-name-panel" : "generated-name-panel");
            Picture("name-background", "name/CCNameBG");
            Label("creation-title", T("ui.character_create.title", "CHARACTER CREATION"));
            NameTab("generated-name", T("ui.character_create.generate_name", "GENERATE A NAME"), false);
            NameTab("custom-name", T("ui.character_create.custom_name", "CREATE YOUR OWN"), true);
            var or = Label("name-or", T("ui.common.or", "-OR-"), true);
            or.style.paddingTop = 4; or.style.paddingBottom = 7;
            Picture("name-tab", custom ? "name/CCNameTab2" : "name/CCNameTab1");
            var selectedTab = Label("selected-name-tab", T(custom ? "ui.character_create.custom_name" : "ui.character_create.generate_name", custom ? "CREATE YOUR OWN" : "GENERATE A NAME"));
            selectedTab.style.fontSize = 12; selectedTab.style.paddingTop = 4; selectedTab.style.paddingBottom = 7;
            if (custom)
            {
                var question = Label("name-question", T("ui.character_create.custom_name_question", "WANT TO CREATE YOUR OWN NAME?"));
                question.style.fontSize = 12; question.style.color = Color.white;
                var help = Required<Label>(body, "name-help");
                help.text = T("ui.character_create.custom_name_help", "Type in the name you wish to use and\nclick 'CONTINUE'. Your custom name\nwill be submitted for review.");
                FFWidgets.Font(help, true, 14); FFWidgets.AutoFit(help);
                customName = Required<TextField>(body, "custom-character-name");
                FFStyle.Background(customName, "ui/en/launcher/login/ff-textfield-normal", new Vector4(4, 4, 4, 4));
                customName.maxLength = 25;
                FFWidgets.Font(customName, false, 14);
                customName.Q<VisualElement>("unity-text-input")?.AddToClassList("creation-name-input");
                customName.value = customNameText;
                customName.RegisterValueChangedCallback(e => { customNameText = e.newValue; SaveCustomName(e.newValue); });
                customName.Focus();
            }
            else
            {
                UpdateGeneratedName();
                Picture("name-display-background", "name/CCNameDisplay");
                generatedName = Label("name-display", draft.FirstName + " " + draft.LastName);
                generatedName.style.color = Color.yellow; FFWidgets.Font(generatedName, false, 12);
                FFWidgets.Padding(generatedName);
                Picture("name-scroll-background", "name/CCScrollBG");
                for (int part = 0; part < 3; part++)
                {
                    NameScrollButton(part, -1, "name/CCScrollUp");
                    var wheel = nameWheels[part] = Required<VisualElement>(body, "name-wheel-" + part);
                    nameRows[part] = new Label[7];
                    for (int row = 0; row < 7; row++)
                    {
                        var label = nameRows[part][row] = Required<Label>(wheel, "name-" + part + "-" + row);
                        FFWidgets.Font(label); FFWidgets.AutoFit(label);
                        label.style.color = new Color(.8f, 1, 1);
                        FFWidgets.Padding(label);
                    }
                    RefreshNameWheel(part);
                    NameScrollButton(part, 1, "name/CCScrollDown");
                }
                body.schedule.Execute(TickNameWheels).Every(16);
                Picture("name-wheel-shade", "name/CCNameShade");
                var random = Action("random-name", T("ui.character_create.random", "RANDOM"), () => {
                    PlaySound("randomize"); RandomName();
                    NameScreen();
                });
                random.style.fontSize = 12;
            }
            error = Label("creation-error", custom ? T("ui.character_create.custom_name_rules", "Names must have at least two parts.\nFirst name: up to 8 characters. Last name: up to 16.") :
                T("ui.character_create.generated_name_rules", "Names must have at least two parts.\nSingle-word names will not be accepted."));
            error.text = custom ? T("ui.character_create.custom_name_rules", "Names must have at least two parts.\nFirst name: up to 8 characters. Last name: up to 16.") :
                T("ui.character_create.generated_name_rules", "Names must have at least two parts.\nSingle-word names will not be accepted.");
            FFWidgets.Font(error, true, 12); error.style.whiteSpace = WhiteSpace.Normal;
            Action("creation-exit", T("ui.common.exit", "EXIT"), Close, true);
            Action("name-continue", T("ui.common.continue", "CONTINUE"), ContinueName);
            FFWidgets.FitTree(body);
        }
        void NameScrollButton(int part, int direction, string art)
        {
            var button = ArtButton("name-" + (direction < 0 ? "up-" : "down-") + part, art, () => {});
            button.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0 || !button.enabledInHierarchy) return;
                if (!StartNameScroll(part, direction)) return;
                nameHeld[part] = true; button.CapturePointer(evt.pointerId);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(evt => { nameHeld[part] = false; button.ReleasePointer(evt.pointerId); }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCaptureOutEvent>(_ => nameHeld[part] = false);
            button.RegisterCallback<NavigationSubmitEvent>(_ => StartNameScroll(part, direction), TrickleDown.TrickleDown);
        }
        bool StartNameScroll(int part, int direction)
        {
            if (busy) return false;
            if (nameDirections[part] != 0) return nameDirections[part] == direction;
            nameDirections[part] = direction; nameStarted[part] = Time.unscaledTime;
            return true;
        }
        void RefreshNameWheel(int part)
        {
            var names = catalog.Names(part);
            for (int row = 0; row < 7; row++)
            {
                string word = (string)names[Cycle(nameIndices[part], row - 3, names.Count)]["value"];
                nameRows[part][row].text = string.IsNullOrWhiteSpace(word) ? T("ui.character_create.name.blank_option", "<BLANK>") : FFStyle.Caption(word);
            }
            UpdateGeneratedName();
            generatedName.text = FFStyle.Caption(draft.FirstName + " " + draft.LastName);
            FFWidgets.FitTree(nameWheels[part]);
        }
        void TickNameWheels()
        {
            if (busy || appearanceOpen || custom || !IsOpen) return;
            for (int part = 0; part < 3; part++)
            {
                int direction = nameDirections[part];
                if (direction == 0) continue;
                nameOffsets[part] += Mathf.Min(Time.unscaledDeltaTime, .1f) * 150 * Mathf.Clamp(Time.unscaledTime - nameStarted[part], 1, 3);
                if (nameOffsets[part] >= 30)
                {
                    nameIndices[part] = Cycle(nameIndices[part], direction, catalog.Names(part).Count);
                    nameOffsets[part] -= 30; RefreshNameWheel(part);
                    if (!nameHeld[part]) { nameDirections[part] = 0; nameOffsets[part] = 0; }
                }
                for (int row = 0; row < 7; row++) nameRows[part][row].style.top = (row - 1) * 30 - direction * nameOffsets[part];
            }
        }
        static int Cycle(int index, int delta, int count) => CharacterCreationRules.CycleName(index, delta, count);
        void RandomName()
        {
            for (int part = 0; part < 3; part++)
                nameIndices[part] = UnityEngine.Random.Range(part == 0 ? 1 : 2, catalog.Names(part).Count - 1);
        }
        void SaveCustomName(string text)
        {
            CharacterCreationRules.SplitCustomName(text, out draft.FirstName, out draft.LastName);
            draft.FirstNameCode = draft.MiddleNameCode = draft.LastNameCode = 0;
        }
        void UpdateGeneratedName()
        {
            var first = catalog.Names(0)[nameIndices[0]];
            var middle = catalog.Names(1)[nameIndices[1]];
            var last = catalog.Names(2)[nameIndices[2]];
            draft.FirstName = (string)first["value"];
            draft.LastName = CharacterCreationRules.ComposeLastName((string)middle["value"], (string)last["value"]);
            draft.FirstNameCode = (int)first["code"];
            draft.MiddleNameCode = (int)middle["code"];
            draft.LastNameCode = (int)last["code"];
        }
        void SetChoices()
        {
            foreach (string category in new[] { "face", "hair", "shirt", "pants", "shoes" })
            {
                var choices = catalog.Choices(category, draft.Style.Gender);
                if (!choiceIndices.TryGetValue(category, out var index)) index = 0;
                index = (index % choices.Count + choices.Count) % choices.Count;
                choiceIndices[category] = index;
                var value = (int)choices[index]["value"];
                var creationIndex = (int)choices[index]["creationIndex"];
                switch (category)
                {
                    case "face": draft.Style.FaceStyle = value; draft.FaceStyleIndex = creationIndex; break;
                    case "hair": draft.Style.HairStyle = value; draft.HairStyleIndex = creationIndex; break;
                    case "shirt": draft.UpperBodyId = value; draft.UpperBodyIndex = creationIndex; break;
                    case "pants": draft.LowerBodyId = value; draft.LowerBodyIndex = creationIndex; break;
                    case "shoes": draft.FootId = value; draft.FootIndex = creationIndex; break;
                }
            }
        }
        void Step(string category, int delta)
        {
            if (delta == 0) return;
            PlayButtonSound();
            choiceIndices[category] += delta;
            SetChoices(); Appearance();
        }
        void Appearance()
        {
            appearanceOpen = true;
            ClearBody("appearance-panel");
            Picture("appearance-background", "layout/CCBG");
            Picture("character-display", "layout/CCCharacterDisplayArea");
            Picture("right-background", "layout/CCRightBG");
            Label("creation-title", T("ui.character_create.title", "CHARACTER CREATION"));
            Label("choose-body", T("ui.character_create.step.body", "1. CHOOSE BODY"), true);
            Picture("body-section", "layout/CCLeftIn12BG");
            GenderButton("gender-boy", T("ui.character_create.boy", "BOY"), 1);
            GenderButton("gender-girl", T("ui.character_create.girl", "GIRL"), 2);
            Stepper("height", SizeLabel(draft.Style.Height, true), delta => { PlaySound(delta < 0 ? "height_down" : "height_up"); draft.Style.Height = (draft.Style.Height + delta + 5) % 5; Appearance(); });
            Stepper("body", SizeLabel(draft.Style.Body, false), delta => { PlaySound(delta < 0 ? "girth_narrow" : "girth_wide"); draft.Style.Body = (draft.Style.Body + delta + 3) % 3; Appearance(); });
            Label("skin-label", T("ui.character_create.skin", "SKIN"), true);
            Picture("skin-background", "name/CCNameDisplay");
            Palette("skin", 12);
            Label("choose-features", T("ui.character_create.step.features", "2. CHOOSE FEATURES"), true);
            Picture("features-section", "layout/CCLeftIn12BG");
            Stepper("hair", ChoiceLabel("hair"), delta => Step("hair", delta));
            Label("face-label", T("ui.character_create.face_label", "FACE"), true);
            Stepper("face", ChoiceLabel("face"), delta => Step("face", delta));
            Picture("hair-colors-background", "name/CCNameDisplay");
            Palette("hair", 18);
            Label("eye-label", T("ui.character_create.eye_color", "EYE COLOR"), true);
            Picture("eye-colors-background", "name/CCNameDisplay");
            Palette("eye", 5);
            Label("choose-clothes", T("ui.character_create.step.clothes", "3. CHOOSE CLOTHES"), true);
            Picture("clothes-section", "layout/CCLeftIn3BG");
            Picture("clothes-background", "clothes/CCClothesBG");
            Clothes("shirt", 0); Clothes("pants", 1); Clothes("shoes", 2);
            avatarImage = Required<Image>(body, "creation-avatar");
            avatarImage.scaleMode = ScaleMode.ScaleToFit;
            FFWidgets.Hold(ArtButton("rotate-left", "camera/CCRotateLeft", () => {}), dt => preview.Rotate(-100 * dt));
            FFWidgets.Hold(ArtButton("rotate-right", "camera/CCRotateRight", () => {}), dt => preview.Rotate(100 * dt));
            FFWidgets.Hold(ArtButton("zoom-in", "camera/CCZoomIn", () => {}), dt => preview.Zoom(-.4f * dt));
            FFWidgets.Hold(ArtButton("zoom-out", "camera/CCZoomOut", () => {}), dt => preview.Zoom(.4f * dt));
            var random = Action("random-appearance", T("ui.character_create.random", "RANDOM"), Randomize);
            FFWidgets.Font(random, false, 12);
            Action("save-character", T("ui.common.continue", "CONTINUE"), Submit);
            Action("appearance-exit", T("ui.common.exit", "EXIT"), () => { PlayButtonSound(); Close(); }, true);
            error = Label("creation-error", "");
            error.style.fontSize = 11; error.style.whiteSpace = WhiteSpace.Normal;
            preview.ShowDraft(CreationCatalog.Preview(draft), avatarImage);
            FFWidgets.FitTree(body);
        }
        string ChoiceLabel(string category)
        {
            var choice = catalog.Choices(category, draft.Style.Gender)[choiceIndices[category]];
            string gender = draft.Style.Gender == 1 ? "male" : "female";
            string label = (string)choice["label"];
            string prefix = category.ToUpperInvariant() + " ";
            if (label != null && label.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(label.Substring(prefix.Length), out int number))
                return T("ui.character_create." + category + "_variant", prefix + "{index}").Replace("{index}", number.ToString());
            return T($"content.appearance.{gender}.{category}.{(int)choice["creationIndex"]}.name", label ?? prefix + (choiceIndices[category] + 1));
        }
        string SizeLabel(int value, bool height)
        {
            var word = height ? new[] { "shortest", "short", "medium", "tall", "tallest" }[Mathf.Clamp(value, 0, 4)]
                : new[] { "light", "medium", "heavy" }[Mathf.Clamp(value, 0, 2)];
            return T("ui.character_create." + (height ? "height." : "body.") + word, word.ToUpperInvariant());
        }
        void Gender(int gender)
        {
            if (draft.Style.Gender == gender) return;
            PlayButtonSound(); draft.Style.Gender = gender;
            foreach (string category in new[] { "face", "hair", "shirt", "pants", "shoes" })
                choiceIndices[category] = Mathf.Min(choiceIndices[category], catalog.Choices(category, gender).Count - 1);
            SetChoices(); Appearance();
        }
        void GenderButton(string name, string text, int gender)
        {
            bool selected = draft.Style.Gender == gender;
            string normal = selected ? "CCCheckboxChecked" : "CCCheckboxNormal";
            string over = selected ? "CCCheckboxCkeckedOver" : "CCCheckboxOver";
            var button = Required<Button>(body, name); button.clicked += () => Gender(gender);
            FFStyle.Button(button, Art + "body/checkbox/" + normal, Art + "body/checkbox/" + over);
            button.style.backgroundSize = new BackgroundSize(22, 22);
            button.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            button.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            button.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            var label = Required<Label>(button, name + "-label"); label.text = FFStyle.Caption(text);
            label.style.left = 30; label.style.width = 50;
            FFWidgets.Font(label); FFWidgets.AutoFit(label);
        }
        void Stepper(string name, string value, Action<int> action)
        {
            Picture(name + "-display", "body/CCBodyshapeDisplay");
            ArtButton(name + "-previous", "body/CCBodyshapeRight", () => action(-1));
            ArtButton(name + "-next", "body/CCBodyshapeLeft", () => action(1));
            var label = Label(name + "-value", value);
            FFWidgets.Font(label, false, 12);
            FFWidgets.Padding(label);
        }
        void Palette(string category, int count)
        {
            var colors = catalog.Palette(category);
            if (!colorPages.TryGetValue(category, out var page)) page = 0;
            for (int i = 0; i < count; i++)
            {
                int selected = page * count + i;
                var outline = Required<VisualElement>(body, category + "-outline-slot-" + i);
                var marker = Required<VisualElement>(body, category + "-selected-slot-" + i);
                var button = Required<Button>(body, category + "-color-slot-" + i);
                bool available = selected < colors.Length;
                outline.style.display = button.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                int current = category == "skin" ? draft.Style.SkinColor : category == "hair" ? draft.Style.HairColor : draft.Style.EyeColor;
                marker.style.display = available && current == selected + 1 ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) continue;
                outline.name = category + "-outline-" + selected;
                marker.name = category + "-selected-" + selected;
                button.name = category + "-color-" + selected;
                FFStyle.Background(outline, Art + "colors/CCColorOutline");
                FFStyle.Background(marker, Art + "colors/CCSelectedColor");
                FFStyle.Background(button, Art + "colors/CCColorInside");
                button.style.unityBackgroundImageTintColor = colors[selected];
                button.clicked += () => {
                    PlaySound("select_color");
                    if (category == "skin") draft.Style.SkinColor = selected + 1;
                    else if (category == "hair") draft.Style.HairColor = selected + 1;
                    else draft.Style.EyeColor = selected + 1;
                    Appearance();
                };
            }
            int pages = (colors.Length + count - 1) / count;
            Required<Button>(body, category + "-page-left").style.display =
                Required<Button>(body, category + "-page-right").style.display = pages > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (pages > 1)
            {
                ArtButton(category + "-page-left", category == "eye" ? "body/CCBodyshapeRight" : "clothes/CCClothesLeftButton",
                    () => { PlayButtonSound(); colorPages[category] = (page + pages - 1) % pages; Appearance(); });
                ArtButton(category + "-page-right", category == "eye" ? "body/CCBodyshapeLeft" : "clothes/CCClothesRightButton",
                    () => { PlayButtonSound(); colorPages[category] = (page + 1) % pages; Appearance(); });
            }
        }
        void Clothes(string category, int row)
        {
            var choices = catalog.Choices(category, draft.Style.Gender);
            var suffix = row == 0 ? "TopButton" : row == 1 ? "Button" : "BottomButton";
            ArtButton(category + "-previous", "clothes/CCClothesLeft" + suffix, () => Step(category, -1));
            ArtButton(category + "-next", "clothes/CCClothesRight" + suffix, () => Step(category, 1));
            for (int i = 0; i < 5; i++)
            {
                int delta = i - 2;
                int index = (choiceIndices[category] + delta + choices.Count) % choices.Count;
                var button = Required<VisualElement>(body, category + "-item-" + i);
                if (delta != 0) ((Button)button).clicked += () => Step(category, delta);
                var icon = CreationCatalog.Icon(choices[index]);
                button.style.backgroundImage = icon == null ? new StyleBackground(StyleKeyword.None) : new StyleBackground(FFWidgets.Texture(icon));
                // The original carousel fades the icons with GUI.color alpha, keeping their RGB intact.
                float alpha = i == 2 ? 1 : (i == 1 || i == 3 ? .75f : .5f);
                button.style.unityBackgroundImageTintColor = new Color(1, 1, 1, alpha);
                button.tooltip = (string)choices[index]["label"];
            }
        }
        void Randomize()
        {
            PlaySound("randomize"); RollAppearance(false); Appearance();
        }
        void RollAppearance(bool initial)
        {
            foreach (string category in new[] { "face", "hair", "shirt", "pants", "shoes" })
                choiceIndices[category] = UnityEngine.Random.Range(0, catalog.Choices(category, draft.Style.Gender).Count);
            draft.Style.SkinColor = UnityEngine.Random.Range(1, (initial ? Mathf.Min(12, catalog.Palette("skin").Length) : catalog.Palette("skin").Length) + 1);
            draft.Style.HairColor = UnityEngine.Random.Range(1, catalog.Palette("hair").Length + 1);
            draft.Style.EyeColor = UnityEngine.Random.Range(1, catalog.Palette("eye").Length + 1);
            draft.Style.Body = UnityEngine.Random.Range(0, 3);
            draft.Style.Height = UnityEngine.Random.Range(0, 5);
            colorPages["skin"] = (draft.Style.SkinColor - 1) / 12;
            colorPages["hair"] = (draft.Style.HairColor - 1) / 18;
            colorPages["eye"] = (draft.Style.EyeColor - 1) / 5;
            SetChoices();
        }
        void PlaySound(string name)
        {
            if (!IsOpen || busy || sounds == null) return;
            var clip = FFOne.Content.ContentAssets.Load<AudioClip>("audio/sfx/ui/" + name);
            if (clip) sounds.PlayOneShot(clip);
        }
        void PlayButtonSound() => PlaySound("mouse_click0" + UnityEngine.Random.Range(1, 6));
        async void ContinueName()
        {
            if (busy || !IsOpen) return;
            PlaySound("continue_01");
            if (custom) SaveCustomName(customNameText); else UpdateGeneratedName();
            if (!CharacterCreationRules.ValidName(draft.FirstName, draft.LastName))
            { error.text = T("ui.character_create.custom_name_rules", "Use a first name (up to 8 characters) and a last name (up to 16)."); return; }
            var operation = creationLifetime;
            var session = frontend.Session;
            busy = true; body.SetEnabled(false);
            error.text = T("ui.login.connecting", "CONNECTING...");
            try
            {
                var reserved = await session.ReserveCharacterNameAsync(draft, operation.Token);
                if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                draft.ExistingUid = reserved.Uid;
                draft.FirstName = reserved.FirstName; draft.LastName = reserved.LastName;
                RollAppearance(true); Appearance();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (this != null && operation == creationLifetime && error != null) error.text = ex.Message;
            }
            finally
            {
                if (this != null && operation == creationLifetime) { busy = false; body?.SetEnabled(true); }
            }
        }
        async void Submit()
        {
            if (busy) return;
            PlaySound("continue_01");
            var operation = creationLifetime;
            var session = frontend.Session;
            busy = true; body.SetEnabled(false);
            try
            {
                await session.CreateCharacterAsync(draft, operation.Token);
                if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                int createdSlot = draft.Slot;
                busy = false; Close(); frontend.RefreshRoster(); frontend.ShowSelection(); frontend.SelectRosterSlot(createdSlot);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (this == null || operation != creationLifetime) return;
                if (error != null) error.text = ex.Message;
                // SaveName may already have assigned this slot. Preserve it on a retry.
                foreach (var entry in session.Characters)
                    if (entry.Slot == draft.Slot && !entry.Style.AppearanceComplete) draft.ExistingUid = entry.Uid;
            }
            finally { if (this != null && operation == creationLifetime) { busy = false; body?.SetEnabled(true); } }
        }
        public void Close()
        {
            if (busy) return;
            creationLifetime?.Cancel(); creationLifetime?.Dispose(); creationLifetime = null;
            creationMusic?.Stop(); sounds?.Stop();
            overlay?.RemoveFromHierarchy(); overlay = null; body = null;
            if (frontend != null)
            {
                frontend.SetCreationPresentationActive(false);
                if (frontend.Session.IsLoggedIn) frontend.RefreshRoster();
            }
            if (preview != null) preview.EndDraft();
        }
        void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose();
            creationLifetime?.Cancel(); creationLifetime?.Dispose();
            if (creationMusic != null) Destroy(creationMusic);
            if (sounds != null) Destroy(sounds);
            if (frontend != null)
            {
                frontend.CreateCharacterRequested -= Open;
                frontend.NetworkEventReceived -= OnNetworkEvent;
                frontend.Root.UnregisterCallback<GeometryChangedEvent>(ViewportChanged);
            }
        }
    }
}
