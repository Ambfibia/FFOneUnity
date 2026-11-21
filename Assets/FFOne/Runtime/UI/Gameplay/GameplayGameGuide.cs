using System;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string GuideNanoComView = "ui/views/Retrobution/NanoCom";
        VisualElement gameGuide, guidePreviousFocus;

        void OpenGuide(int? firstUse = null)
        {
            if (gameGuide != null || interactionSuspended || prompt != null || !session.Snapshot.IsActive) return;
            var catalog = GameplayUiCatalog.Load();
            if (!catalog.GuideStart(firstUse, out int topic, out int page)) return;

            var view = FFView.InstantiateClass(GuideNanoComView, "game-guide");
            view.EnableInClassList("game-guide--russian", language == "ru");
            var topicTemplate = GuideTemplate(view, "guide-topic-template");
            var illustrationTemplate = GuideTemplate(view, "guide-illustration-template");
            var canvas = FFView.RequireClass<VisualElement>(view, "guide-canvas");
            var topics = FFView.RequireClass<VisualElement>(view, "guide-topics");
            var sections = FFView.RequireClass<VisualElement>(view, "guide-sections");
            var heading = FFView.RequireClass<Label>(view, "guide-heading");
            var contents = FFView.RequireClass<ScrollView>(view, "guide-content");
            var previous = FFView.RequireClass<Button>(view, "guide-previous");
            var next = FFView.RequireClass<Button>(view, "guide-next");
            var close = FFView.RequireClass<Button>(view, "guide-close");
            FFView.RequireClass<Label>(view, "guide-title").text = FFStyle.Caption(GuideCaption("title", "GAME GUIDE"));
            FFView.RequireClass<Label>(view, "guide-choose-topic").text = FFStyle.Caption(GuideCaption("choose_topic", "CHOOSE A TOPIC:"));
            previous.text = FFStyle.Caption(GuideCaption("previous_section", "< PREVIOUS SECTION"));
            next.text = FFStyle.Caption(GuideCaption("next_section", "NEXT SECTION >"));
            previous.clicked += () => Step(-1);
            next.clicked += () => Step(1);
            close.clicked += () => CloseGuide(true);

            for (int i = 1; i < catalog.GuideTopics.Length; i++)
            {
                int selected = i, first = catalog.GuideTopics[i].first;
                topics.Add(GuideButton(topicTemplate, "guide-topic-" + i, catalog.GuideTitle(first, false, language),
                    () => ShowPage(selected, first)));
            }
            ShowPage(topic, page);

            CloseQuickChat();
            gameGuide = view;
            guidePreviousFocus = parent.panel?.focusController.focusedElement as VisualElement;
            Windows.View?.SetEnabled(false);
            chatView.SetEnabled(false);
            view.RegisterCallback<GeometryChangedEvent>(evt => Place(evt.newRect.width, evt.newRect.height));
            layers.Add(view, UiLayer.Guide);
            if (parent.layout.height > 0)
                Place(parent.layout.width, parent.layout.height);
            view.Focus();
            playSound?.Invoke("open_screen");
            InputGateChanged?.Invoke();

            void Place(float width, float height)
            {
                if (!(height > 0 && width > 0)) return;
                float scale = RetrobutionLayout.Scale(height);
                canvas.style.scale = new Scale(new Vector2(scale, scale));
                // Preserve the legacy integer viewport pivot on odd-sized surfaces.
                canvas.style.translate = new Translate(Mathf.Floor(width / 2) - width / 2,
                    Mathf.Floor(height / 2) - height / 2);
            }

            void ShowPage(int nextTopic, int nextPage)
            {
                topic = nextTopic;
                page = nextPage;
                var range = catalog.GuideTopics[topic];
                heading.text = catalog.GuideTitle(range.first, false, language);
                sections.Clear();
                for (int id = range.first; id <= range.last; id++)
                {
                    int selected = id;
                    sections.Add(GuideButton(topicTemplate, "guide-section-" + id, catalog.GuideTitle(id, true, language),
                        () => ShowPage(topic, selected)));
                }

                contents.Clear();
                contents.scrollOffset = Vector2.zero;
                var entries = catalog.GuidePages[page];
                for (int i = entries.first; i <= entries.last; i++)
                {
                    var entry = catalog.GuideContent[i];
                    VisualElement row;
                    switch (entry.type)
                    {
                        case 1:
                        case 3:
                        case 4:
                            row = GuideLine(catalog.GuideText(entry.text, language), entry.type, entry.size);
                            break;
                        case 2:
                            var tree = illustrationTemplate.Instantiate();
                            row = FFView.RequireClass<VisualElement>(tree, "guide-illustration");
                            var image = FFView.RequireClass<Image>(row, "guide-screenshot");
                            string route = catalog.GuideScreenshot(entry.text);
                            if (route != null) image.image = FFWidgets.Texture(route);
                            row.RemoveFromHierarchy();
                            break;
                        default:
                            continue;
                    }
                    row.EnableInClassList("guide-row--first", contents.contentContainer.childCount == 0);
                    contents.Add(row);
                }
            }

            void Step(int direction)
            {
                var all = catalog.GuideTopics;
                int destination = page + direction;
                if (destination < all[1].first) destination = all[all.Length - 1].last;
                if (destination > all[all.Length - 1].last) destination = all[1].first;
                for (int i = 1; i < all.Length; i++)
                    if (destination >= all[i].first && destination <= all[i].last)
                    {
                        ShowPage(i, destination);
                        return;
                    }
            }
        }

        string GuideCaption(string key, string fallback)
        {
            string route = "ui.game_guide." + key;
            string value = LocalizationCatalog.Lookup(route, language);
            return value == route ? fallback : value;
        }

        static VisualTreeAsset GuideTemplate(VisualElement view, string role)
        {
            var instance = FFView.RequireClass<TemplateContainer>(view, role);
            var template = instance.templateSource;
            instance.RemoveFromHierarchy();
            return template;
        }

        static Button GuideButton(VisualTreeAsset template, string name, string caption, Action select)
        {
            var button = FFView.RequireClass<Button>(template.Instantiate(), "guide-topic-button");
            button.name = name;
            button.text = caption;
            button.clicked += select;
            button.RemoveFromHierarchy();
            return button;
        }

        static Label GuideLine(string caption, int type, int size)
        {
            var line = new Label(caption) { pickingMode = PickingMode.Ignore, enableRichText = false };
            line.AddToClassList("ff-copy");
            line.AddToClassList("guide-row");
            line.AddToClassList("guide-line");
            line.AddToClassList(type == 3 ? "guide-line--title" : type == 4 ? "guide-line--caption" : "guide-line--copy");
            line.EnableInClassList("guide-line--centered", type == 1 && size == 4);
            return line;
        }

        void CloseGuide(bool audible)
        {
            if (gameGuide == null) return;
            gameGuide.RemoveFromHierarchy();
            gameGuide = null;
            Windows.View?.SetEnabled(!interactionSuspended);
            chatView.SetEnabled(!interactionSuspended);
            if (guidePreviousFocus?.panel != null && guidePreviousFocus.enabledInHierarchy) guidePreviousFocus.Focus();
            guidePreviousFocus = null;
            if (audible) playSound?.Invoke("close_screen");
            lastWindowChangeFrame = Time.frameCount;
            if (!disposed) InputGateChanged?.Invoke();
        }
    }
}
