using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Integration;
using FFOne.Network;
using FFOne.UI;
using FFOne.Tutorial;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Creation
{
    [RequireComponent(typeof(FrontendController), typeof(PreviewStage))]
    public sealed class CharacterCreationController : MonoBehaviour
    {
        const string BindingPrefix = "creator-bind-";
        [SerializeField] FrontendController frontend;
        [SerializeField] PreviewStage preview;
        DexterShipCutscene cutscene;
        CreationCatalog catalog;
        CharacterCreation draft;
        VisualElement overlay, panelHost, body, background;
        VisualTreeAsset view;
        Label error;
        Button fullscreen;
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
        CancellationTokenSource lifetime, creationLifetime;
        public bool IsOpen => overlay != null;
        internal VisualElement GamepadView => overlay;
        internal bool LookWithGamepad(Vector2 stick, float elapsed, bool barberOpen = false)
        {
            if ((!appearanceOpen || !IsOpen) && !barberOpen || !preview) return false;
            if (stick.x != 0) preview.Rotate(stick.x * 100 * elapsed);
            if (stick.y != 0) preview.Zoom(-stick.y * .4f * elapsed);
            return true;
        }


        void Start()
        {
            if (!frontend) TryGetComponent(out frontend);
            if (!preview) TryGetComponent(out preview);
            TryGetComponent(out cutscene);
            frontend.CreateCharacterRequested += Open;
            frontend.NetworkEventReceived += OnNetworkEvent;
            frontend.Root.RegisterCallback<GeometryChangedEvent>(ViewportChanged);
            lifetime = new CancellationTokenSource();
        }
        string T(string key, string fallback) => frontend.Text(key, fallback);
        void OnNetworkEvent(NetworkEvent value)
        {
            if (value.Kind != NetworkEventKind.Disconnected || creationLifetime == null) return;
            busy = false;
            Close();
        }
        async void Open(int slot)
        {
            if (IsOpen || busy) return;
            busy = true;
            creationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var operation = creationLifetime;
            try
            {
                await frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterCreation);
                custom = false; customNameText = ""; appearanceOpen = false;
                draft = new CharacterCreation { Slot = slot, Style = new CharacterStyle { Height = 2, Body = 1 } };
                foreach (var character in frontend.Session.Characters)
                    if (character.Slot == slot && !character.Style.AppearanceComplete)
                    {
                        draft.ExistingUid = character.Uid;
                        draft.FirstName = character.FirstName;
                        draft.LastName = character.LastName;
                    }
                if (draft.ExistingUid == 0)
                {
                    if (!cutscene) cutscene = gameObject.AddComponent<DexterShipCutscene>();
                    cutscene.VoiceLanguage = frontend.VoiceLanguage;
                    await cutscene.PreloadBeforeCreationAsync(frontend.Language, operation.Token);
                    if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                    frontend.SetCreationPresentationActive(true);
                    frontend.HideLoading();
                    var performance = cutscene.PlayBeforeCreationAsync(frontend.Root, frontend.Language, operation.Token);
                    var creatorResources = PreloadCreatorAsync(operation.Token);
                    await Task.WhenAll(creatorResources, CompleteIntroAsync());
                    async Task CompleteIntroAsync()
                    {
                        await performance;
                        if (!creatorResources.IsCompleted && this != null && operation == creationLifetime && !operation.IsCancellationRequested)
                            await frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterCreation);
                    }
                }
                else await PreloadCreatorAsync(operation.Token);
                if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                choiceIndices.Clear(); colorPages.Clear();
                SetChoices();
                RandomName();
                view = FFOne.Content.ContentAssets.Load<VisualTreeAsset>("ui/views/CharacterCreation");
                if (!view) throw new InvalidOperationException("Missing CharacterCreation.uxml.");
                var shell = view.CloneTree();
                overlay = Required<VisualElement>(shell, "character-creation");
                for (int i = 0; i < shell.styleSheets.count; i++)
                    if (!overlay.styleSheets.Contains(shell.styleSheets[i])) overlay.styleSheets.Add(shell.styleSheets[i]);
                overlay.RemoveFromHierarchy();
                overlay.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                frontend.Root.Insert(0, overlay);
                frontend.SetCreationPresentationActive(true);
                frontend.PlayCreationMusic();
                background = Required<VisualElement>(overlay, "creation-background");
                panelHost = Required<VisualElement>(overlay, "creation-panel-host");
                fullscreen = Required<Button>(overlay, "creator-fullscreen");
                fullscreen.clicked += () => { if (appearanceOpen) PlayButtonSound(); frontend.ToggleFullscreen(); UpdateViewport(); };
                if (draft.ExistingUid != 0)
                {
                    RollAppearance(true);
                    await NativeAvatarPreview.PreloadAsync(NativeAvatarPreview.Snapshot(CreationCatalog.Preview(draft)), operation.Token);
                    if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                    Appearance();
                    body.SetEnabled(false);
                    frontend.SetLoadingResourceProgressVisible(false);
                    await WaitForDraftAsync(operation.Token);
                }
                else NameScreen();
                _ = WarmTutorialIntroAsync(operation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (this && operation == creationLifetime) { busy = false; Close(); frontend.ShowError(ex.Message); } }
            finally
            {
                if (this && operation == creationLifetime) { busy = false; body?.SetEnabled(true); frontend.HideLoading(); }
            }
        }
        async Task PreloadCreatorAsync(CancellationToken cancellation)
        {
            var requests = new List<Task> {
                LoadCatalog(), ContentAssets.GetAsync<VisualTreeAsset>("ui/views/CharacterCreation", cancellation)
            };
            foreach (string gender in new[] { "male", "female" })
                requests.Add(ContentAssets.GetAsync<NativePlayerAnimationProfile>("characters/player/shared/animation/" + gender + "-profile", cancellation));
            foreach (string sound in new[] { "randomize", "continue_01", "select_color", "height_down", "height_up", "girth_narrow", "girth_wide", "mouse_click01", "mouse_click02", "mouse_click03", "mouse_click04", "mouse_click05" })
                requests.Add(ContentAssets.GetAsync<AudioClip>("audio/sfx/ui/" + sound, cancellation));
            int completed = 0, total = requests.Count;
            await Task.WhenAll(requests.Select(async request => {
                await request; frontend.SetLoadingProgress(.65f * ++completed / total, 1);
            }));
            var icons = new[] { 1, 2 }.SelectMany(gender => new[] { "shirt", "pants", "shoes" }
                .SelectMany(category => catalog.Choices(category, gender))).Select(CreationCatalog.Icon)
                .Where(path => path != null).Distinct().ToArray();
            for (int offset = 0; offset < icons.Length; offset += 16)
            {
                await Task.WhenAll(icons.Skip(offset).Take(16).Select(icon => ContentAssets.GetAsync<Texture2D>(icon, cancellation)));
                frontend.SetLoadingProgress(.65f + .35f * Mathf.Min(offset + 16, icons.Length) / Mathf.Max(1, icons.Length), 1);
            }
            async Task LoadCatalog() { if (catalog == null) catalog = await CreationCatalog.LoadAsync(cancellation); }
        }
        async Task WarmTutorialIntroAsync(CancellationToken cancellation)
        {
            try
            {
                // The next scene's effect must not compete with the first ship
                // frame. Prepare it while the player edits their character.
                await Task.Yield();
                cancellation.ThrowIfCancellationRequested();
                if (!cutscene) cutscene = gameObject.AddComponent<DexterShipCutscene>();
                cutscene.VoiceLanguage = frontend.VoiceLanguage;
                await cutscene.PreloadBeforeTutorialAsync(frontend.Language, cancellation);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.LogWarning("Dexter ship background preload: " + ex.Message, this); }
        }
        void ClearBody(string panel)
        {
            var focusedControl = frontend.Root.panel?.focusController.focusedElement as VisualElement;
            string focusedBinding = focusedControl?.GetClasses().FirstOrDefault(role => role.StartsWith(BindingPrefix, StringComparison.Ordinal));
            Array.Clear(nameDirections, 0, nameDirections.Length);
            Array.Clear(nameHeld, 0, nameHeld.Length);
            Array.Clear(nameOffsets, 0, nameOffsets.Length);
            body?.RemoveFromHierarchy();
            var tree = view.CloneTree();
            body = Required<VisualElement>(tree, panel);
            body.RemoveFromHierarchy();
            panelHost.Add(body);
            UpdateViewport();
            FFWidgets.FitTree(body);
            body.schedule.Execute(() => {
                var focused = frontend.Root.panel?.focusController.focusedElement as VisualElement;
                if (focused != null && overlay.Contains(focused)) return;
                var replacement = focusedBinding == null ? null : body.Q(className: focusedBinding);
                if (replacement != null && replacement.focusable && replacement.enabledInHierarchy) replacement.Focus();
                else overlay.Focus();
            });
        }
        void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape) { if (!busy) Close(); evt.StopImmediatePropagation(); }
            else if (!appearanceOpen && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) &&
                (evt.target is not VisualElement target || target.GetFirstAncestorOfType<Button>() == null && target is not Button))
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
            fullscreen.style.translate = new Translate(-45 * scale, -35 * scale);
            fullscreen.style.scale = new Scale(new Vector3(scale, scale, 1));
            fullscreen.EnableInClassList("creation-windowed", !Screen.fullScreen);
        }
        static TElement Required<TElement>(VisualElement parent, string role) where TElement : VisualElement =>
            FFView.RequireClass<TElement>(parent, BindingPrefix + role);

        Label Label(string role, string text)
        {
            var label = Required<Label>(body, role);
            label.text = FFStyle.Caption(text);
            FFWidgets.AutoFit(label);
            return label;
        }

        void Action(string role, string caption, System.Action action)
        {
            var button = Required<Button>(body, role);
            button.text = FFStyle.Caption(caption);
            button.clicked += action;
            FFWidgets.AutoFit(button);
        }

        void BindButton(string role, System.Action action)
        {
            var button = Required<Button>(body, role);
            if (role.EndsWith("-previous", StringComparison.Ordinal) || role.EndsWith("-next", StringComparison.Ordinal))
                button.AddToClassList("ff-pad-repeat");
            button.clicked += action;
        }

        void NameTab(string name, string title, bool ownName)
        {
            var tab = Required<Button>(body, name); tab.text = FFStyle.Caption(title);
            FFWidgets.AutoFit(tab);
            tab.clicked += () => {
                if (custom == ownName) return;
                PlaySound("tab_click01"); custom = ownName; NameScreen();
            };
        }

        void NameScreen()
        {
            appearanceOpen = false;
            ClearBody(custom ? "custom-name-panel" : "generated-name-panel");

            Label("creation-title", T("ui.character_create.title", "CHARACTER CREATION"));
            NameTab("generated-name", T("ui.character_create.generate_name", "GENERATE A NAME"), false);
            NameTab("custom-name", T("ui.character_create.custom_name", "CREATE YOUR OWN"), true);
            Label("name-or", T("ui.common.or", "-OR-"));

            Label("selected-name-tab", T(custom ? "ui.character_create.custom_name" : "ui.character_create.generate_name", custom ? "CREATE YOUR OWN" : "GENERATE A NAME"));

            if (custom)
            {
                Label("name-question", T("ui.character_create.custom_name_question", "WANT TO CREATE YOUR OWN NAME?"));

                Required<Label>(body, "name-help").text = T("ui.character_create.custom_name_help",
                    "Type in the name you wish to use and\nclick 'CONTINUE'. Your custom name\nwill be submitted for review.");
                var customName = Required<TextField>(body, "custom-character-name");
                customName.value = customNameText;
                customName.RegisterValueChangedCallback(e => { customNameText = e.newValue; SaveCustomName(e.newValue); });
                customName.Focus();
            }
            else
            {
                UpdateGeneratedName();

                generatedName = Label("name-display", draft.FirstName + " " + draft.LastName);

                var columns = body.Query<TemplateContainer>(className: "creation-wheel").ToList();
                if (columns.Count != nameWheels.Length) throw new InvalidOperationException("Character creation UXML requires three name wheels.");
                for (int part = 0; part < 3; part++)
                {
                    var column = columns[part];
                    NameScrollButton(column, part, -1);
                    var wheel = nameWheels[part] = column.Q(className: "creation-wheel-rows");
                    nameRows[part] = wheel.Query<Label>(className: "creation-label").ToList().ToArray();
                    if (nameRows[part].Length != 7) throw new InvalidOperationException("A name wheel requires seven rows.");
                    foreach (var label in nameRows[part]) FFWidgets.AutoFit(label);
                    RefreshNameWheel(part);
                    NameScrollButton(column, part, 1);
                }
                body.schedule.Execute(TickNameWheels).Every(16);

                Action("random-name", T("ui.character_create.random", "RANDOM"), () => {
                    PlaySound("randomize"); RandomName();
                    for (int part = 0; part < 3; part++)
                    {
                        nameDirections[part] = 0; nameHeld[part] = false; nameOffsets[part] = 0;
                        RefreshNameWheel(part);
                        foreach (var row in nameRows[part]) row.style.translate = StyleKeyword.Null;
                    }
                });
            }
            error = Required<Label>(body, "creation-error");
            error.text = custom ? T("ui.character_create.custom_name_rules", "Names must have at least two parts.\nFirst name: up to 8 characters. Last name: up to 16.") :
                T("ui.character_create.generated_name_rules", "Names must have at least two parts.\nSingle-word names will not be accepted.");
            Action("creation-exit", T("ui.common.exit", "EXIT"), Close);
            Action("name-continue", T("ui.common.continue", "CONTINUE"), ContinueName);
            FFWidgets.FitTree(body);
        }
        void NameScrollButton(VisualElement column, int part, int direction)
        {
            var button = column.Q<Button>(className: direction < 0 ? "creation-wheel-up" : "creation-wheel-down");
            button.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0 || !button.enabledInHierarchy) return;
                if (!StartNameScroll(part, direction)) return;
                nameHeld[part] = true; button.CapturePointer(evt.pointerId);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(evt => { nameHeld[part] = false; button.ReleasePointer(evt.pointerId); }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCaptureOutEvent>(_ => nameHeld[part] = false);
            button.AddToClassList("ff-pad-repeat");
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
                var offset = new Translate(0, -direction * nameOffsets[part]);
                foreach (var row in nameRows[part]) row.style.translate = offset;
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

            Label("creation-title", T("ui.character_create.title", "CHARACTER CREATION"));
            Label("choose-body", T("ui.character_create.step.body", "1. CHOOSE BODY"));

            GenderButton("gender-boy", T("ui.character_create.boy", "BOY"), 1);
            GenderButton("gender-girl", T("ui.character_create.girl", "GIRL"), 2);
            Stepper("height", SizeLabel(draft.Style.Height, true), delta => { PlaySound(delta < 0 ? "height_down" : "height_up"); draft.Style.Height = (draft.Style.Height + delta + 5) % 5; Appearance(); });
            Stepper("body", SizeLabel(draft.Style.Body, false), delta => { PlaySound(delta < 0 ? "girth_narrow" : "girth_wide"); draft.Style.Body = (draft.Style.Body + delta + 3) % 3; Appearance(); });
            Label("skin-label", T("ui.character_create.skin", "SKIN"));

            Palette("skin", 12);
            Label("choose-features", T("ui.character_create.step.features", "2. CHOOSE FEATURES"));

            Stepper("hair", ChoiceLabel("hair"), delta => Step("hair", delta));
            Label("face-label", T("ui.character_create.face_label", "FACE"));
            Stepper("face", ChoiceLabel("face"), delta => Step("face", delta));

            Palette("hair", 18);
            Label("eye-label", T("ui.character_create.eye_color", "EYE COLOR"));

            Palette("eye", 5);
            Label("choose-clothes", T("ui.character_create.step.clothes", "3. CHOOSE CLOTHES"));

            Clothes("shirt"); Clothes("pants"); Clothes("shoes");
            var avatarImage = Required<Image>(body, "creation-avatar");
            FFWidgets.Hold(Required<Button>(body, "rotate-left"), dt => preview.Rotate(-100 * dt));
            FFWidgets.Hold(Required<Button>(body, "rotate-right"), dt => preview.Rotate(100 * dt));
            FFWidgets.Hold(Required<Button>(body, "zoom-in"), dt => preview.Zoom(-.4f * dt));
            FFWidgets.Hold(Required<Button>(body, "zoom-out"), dt => preview.Zoom(.4f * dt));
            Action("random-appearance", T("ui.character_create.random", "RANDOM"), Randomize);
            Action("save-character", T("ui.common.continue", "CONTINUE"), Submit);
            Action("appearance-exit", T("ui.common.exit", "EXIT"), () => { PlayButtonSound(); Close(); });
            error = Required<Label>(body, "creation-error");
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
            var button = Required<Button>(body, name);
            button.clicked += () => Gender(gender);
            button.EnableInClassList("creation-gender-selected", draft.Style.Gender == gender);
            var label = FFView.RequireClass<Label>(button, "creation-gender-caption");
            label.text = FFStyle.Caption(text);
            FFWidgets.AutoFit(label);
        }
        void Stepper(string name, string value, Action<int> action)
        {
            BindButton(name + "-previous", () => action(-1));
            BindButton(name + "-next", () => action(1));
            Label(name + "-value", value);
        }
        void Palette(string category, int count)
        {
            var colors = catalog.Palette(category);
            if (!colorPages.TryGetValue(category, out var page)) page = 0;
            var swatches = body.Query<TemplateContainer>(className: "creation-swatch-" + category).ToList();
            if (swatches.Count != count) throw new InvalidOperationException($"Character creation UXML requires {count} {category} swatches.");
            for (int i = 0; i < count; i++)
            {
                int selected = page * count + i;
                var swatch = swatches[i];
                bool available = selected < colors.Length;
                int current = category == "skin" ? draft.Style.SkinColor : category == "hair" ? draft.Style.HairColor : draft.Style.EyeColor;
                swatch.EnableInClassList("creation-swatch-unavailable", !available);
                swatch.EnableInClassList("creation-swatch-active", available && current == selected + 1);
                if (!available) continue;
                var outline = FFView.RequireClass<VisualElement>(swatch, "creation-swatch-outline");
                var marker = FFView.RequireClass<VisualElement>(swatch, "creation-swatch-selected");
                var button = FFView.RequireClass<Button>(swatch, "creation-swatch-color");
                outline.name = category + "-outline-" + selected;
                marker.name = category + "-selected-" + selected;
                button.name = category + "-color-" + selected;
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
            var left = Required<Button>(body, category + "-page-left");
            var right = Required<Button>(body, category + "-page-right");
            left.EnableInClassList("creation-page-unavailable", pages <= 1);
            right.EnableInClassList("creation-page-unavailable", pages <= 1);
            if (pages <= 1) return;
            left.clicked += () => TurnPage(-1);
            right.clicked += () => TurnPage(1);

            void TurnPage(int delta)
            {
                PlaySound("select_color");
                colorPages[category] = (page + delta + pages) % pages;
                Appearance();
            }
        }
        void Clothes(string category)
        {
            var choices = catalog.Choices(category, draft.Style.Gender);
            BindButton(category + "-previous", () => Step(category, -1));
            BindButton(category + "-next", () => Step(category, 1));
            for (int i = 0; i < 5; i++)
            {
                int delta = i - 2;
                int index = (choiceIndices[category] + delta + choices.Count) % choices.Count;
                var button = Required<VisualElement>(body, category + "-item-" + i);
                if (delta != 0) ((Button)button).clicked += () => Step(category, delta);
                var icon = CreationCatalog.Icon(choices[index]);
                button.style.backgroundImage = icon == null ? new StyleBackground(StyleKeyword.None) : new StyleBackground(FFWidgets.Texture(icon));
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
            if (IsOpen && !busy) frontend.PlayUiSound(name);
        }
        void PlayButtonSound() => PlaySound("button");
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
                await frontend.ShowLoadingAsync(FrontendLoadingStage.CharacterCreation);
                operation.Token.ThrowIfCancellationRequested();
                RollAppearance(true);
                await NativeAvatarPreview.PreloadAsync(NativeAvatarPreview.Snapshot(CreationCatalog.Preview(draft)), operation.Token,
                    value => { if (this && operation == creationLifetime) frontend.SetLoadingProgress(value, value); });
                if (this == null || operation.IsCancellationRequested || operation != creationLifetime) return;
                Appearance();
                body.SetEnabled(false);
                frontend.SetLoadingResourceProgressVisible(false);
                await WaitForDraftAsync(operation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (this != null && operation == creationLifetime && error != null) error.text = ex.Message;
            }
            finally
            {
                if (this != null && operation == creationLifetime) { frontend.HideLoading(); busy = false; body?.SetEnabled(true); }
            }
        }
        async Task WaitForDraftAsync(CancellationToken cancellation)
        {
            float deadline = Time.realtimeSinceStartup + 120;
            while (!preview.DraftReady)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(preview.DraftError)) throw new InvalidOperationException(preview.DraftError);
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Character creator did not render its avatar.");
                await Task.Yield();
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
                busy = false; Close(false);
                await frontend.EnterCreatedCharacterAsync(createdSlot);
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
        public void Close() => Close(true);
        void Close(bool returnToSelection)
        {
            if (busy) return;
            creationLifetime?.Cancel(); creationLifetime?.Dispose(); creationLifetime = null;
            frontend?.StopCreationMusic();
            overlay?.RemoveFromHierarchy(); overlay = null; panelHost = null; body = null;
            if (frontend != null)
            {
                frontend.HideLoading();
                if (frontend.Session.IsLoggedIn && !returnToSelection) frontend.RefreshRoster();
            }
            if (preview != null) preview.EndDraft();
            if (returnToSelection && frontend != null && frontend.Session.IsLoggedIn)
                frontend.ShowSelection();
        }
        void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose();
            creationLifetime?.Cancel(); creationLifetime?.Dispose();
            if (frontend != null)
            {
                frontend.CreateCharacterRequested -= Open;
                frontend.NetworkEventReceived -= OnNetworkEvent;
                frontend.Root.UnregisterCallback<GeometryChangedEvent>(ViewportChanged);
            }
        }
    }
}
