using System;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal static class GameplayJournalView
    {
        static readonly string[] MissionBanners = {
            "mission/guide-banners/edd", "mission/guide-banners/dexter", "mission/guide-banners/mojo",
            "mission/guide-banners/ben", "mission/guide-banners/computress", "journal/nano_banner", "journal/world_banner"
        };

        internal static void Localize(VisualElement root, string language)
        {
            root.Query<TextElement>().ForEach(text => text.enableRichText = false);
            root.Query<ScrollView>().ForEach(scroll => scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden);
            if (language != "ru") return;

            FFView.RequireClass<Label>(root, "ff-journal-frame-label").text = "ЖУРНАЛ ЗАДАНИЙ";
            FFView.RequireClass<Label>(root, "ff-journal-empty-label").text = "Задание не выбрано";
            FFView.RequireClass<Label>(root, "ff-mission-details-heading-label").text = "ПОДРОБНОСТИ ЗАДАНИЯ:";
            FFView.RequireClass<Label>(root, "ff-journal-active-background-label").text =
                FFView.RequireClass<Label>(root, "ff-tab-active-label").text = "Активные";
            FFView.RequireClass<Label>(root, "ff-journal-completed-background-label").text =
                FFView.RequireClass<Label>(root, "ff-tab-completed-label").text = "Завершённые";
        }

        internal static void AddRow(VisualElement root, string title, string objective, Action select = null, Action track = null, bool tracked = false)
        {
            var row = FFView.InstantiateClass("ui/views/Retrobution/MissionRow", "ff-mission-row");
            row.Query<TextElement>().ForEach(text => text.enableRichText = false);
            FFView.RequireClass<Label>(row, "ff-row-title-label").text = title;
            FFView.RequireClass<Label>(row, "ff-row-objective-label").text = objective;
            var selection = FFView.RequireClass<Button>(row, "ff-select-mission");
            if (select != null) selection.clicked += select;
            var tracking = FFView.RequireClass<Button>(row, "ff-track-mission");
            tracking.SetEnabled(track != null);
            tracking.EnableInClassList("retro-on", tracked);
            if (track != null) tracking.clicked += track;
            FFView.RequireClass<ScrollView>(root, "ff-journal-list").Add(row);
        }

        internal static void ShowDetails(VisualElement root, WorldMissionDefinition rule, string title,
            string description, string language, bool offered = false, bool completed = false)
        {
            FFView.RequireClass<VisualElement>(root, "ff-journal-empty").style.display = DisplayStyle.None;
            var details = FFView.RequireClass<VisualElement>(root, "ff-journal-details");
            details.style.display = DisplayStyle.Flex;
            FFView.RequireClass<Label>(details, "ff-mission-title-label").text = title;
            FFView.RequireClass<Label>(details, "ff-mission-level-label").text = rule != null
                ? (language == "ru" ? "УРОВЕНЬ " : "LEVEL ") + rule.MinimumLevel.ToString("D2")
                : "";
            var scroll = details.Q<ScrollView>(className: "mission-description");
            scroll.Q<Label>(className: "mission-description-copy").text = description;
            scroll.scrollOffset = Vector2.zero;
            details.Q<Label>(className: "mission-clock").style.display = DisplayStyle.None;
            details.EnableInClassList("ff-journal-details--offer", offered);
            details.EnableInClassList("ff-journal-details--complete", completed);
            SetBanner(details, rule);
        }

        // cnMissionJournal.DoWindowLeft, primary component 1618, GuideBannerTex[0..6].
        // Slot 3 uses the accepted earlier Ben banner from the native retention recipe.
        static void SetBanner(VisualElement details, WorldMissionDefinition rule)
        {
            var banner = details.Q<Image>(className: "mission-banner");
            int index = rule == null ? -1 : rule.MissionType == 1 ? rule.RequiredGuide - 1 : rule.MissionType == 2 ? 5 : 6;
            banner.image = index >= 0 && index < MissionBanners.Length ? FFWidgets.Texture("ui/en/gameplay/" + MissionBanners[index]) : null;
        }

        internal static Button PrimaryAction(VisualElement root, string caption, Action action, bool completion = false)
        {
            var button = root.Q<Button>(className: "mission-primary");
            button.style.display = DisplayStyle.Flex;
            button.EnableInClassList("mission-primary--complete", completion);
            var label = button.Q<Label>(className: "mission-primary-copy");
            label.text = caption;
            FFWidgets.AutoFit(label);
            if (button.userData is Action previous) button.clicked -= previous;
            button.userData = action;
            button.clicked += action;
            return button;
        }
    }
}
