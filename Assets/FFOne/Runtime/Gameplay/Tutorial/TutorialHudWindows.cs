using FFOne.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        const string TutorialNpcArt = "ui/en/gameplay/mission/npc/";
        internal VisualElement GamepadWindow => tutorialWindow;
        bool TutorialMissionSelect => Stage.Chapter == 3 && (Stage.Step == 3 || Stage.Step == 16);
        bool TutorialMissionAccept => Stage.Chapter == 3 && Stage.Step == 101 || Stage.Chapter == 4 && (Stage.Step == 2 || Stage.Step == 109);
        bool TutorialMissionReward => Stage.Chapter == 3 && Stage.Step == 17;
        bool TutorialWindowCanClose => interacting == 0 ? Stage.Chapter != 3 || Stage.Step >= 12
            : Stage.Chapter == 3 && (Stage.Step == 102 || Stage.Step == 18) || Stage.Chapter == 4 && Stage.Step == 102;
        int TutorialMissionTask => currentTask > 0 ? currentTask : Stage.Chapter == 3 ? 2248 : 2250;

        void RebuildTutorialWindow()
        {
            if (tutorialMinimap != null && tutorialMinimap.parent != tutorialHudRoot) tutorialHudRoot.Add(tutorialMinimap);
            tutorialWindow?.RemoveFromHierarchy(); tutorialWindow = tutorialWindowPanel = null;
            if (interacting != 0)
            {
                if (!actors.TryGetValue(interacting, out var actor) || !actor) { interacting = 0; return; }
                if (interacting == 1009 || interacting == 1012 || interacting == 4001) BuildTutorialWarp(actor.DisplayName);
                else if (TutorialMissionSelect) BuildTutorialNpcMission(actor.DisplayName);
                else BuildTutorialJournal(true);
            }
            else if (journal) BuildTutorialJournal(false);
            else if (mainMenu) BuildTutorialNanoCom();
            if (tutorialWindow != null)
            {
                tutorialHudRoot.Add(tutorialWindow);
                if (tutorialWindowPanel.ClassListContains("tutorial-npc-window"))
                    tutorialWindow.Query<TextElement>().ForEach(text => { text.enableRichText = false; FFWidgets.Font(text, false, 12); });
                tutorialWindow.Query<ScrollView>().ForEach(scroll => scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden);
                tutorialWindow.RegisterCallback<PointerDownEvent>(_ => hudInputFrame = Time.frameCount, TrickleDown.TrickleDown);
                LayoutTutorialHud();
            }
        }

        void BuildTutorialNanoCom()
        {
            tutorialWindow = GameplayNanoComView.Instantiate();
            tutorialWindowPanel = GameplayNanoComView.Panel(tutorialWindow);
            GameplayNanoComView.Localize(tutorialWindow, Language);
            GameplayNanoComView.BindZoom(tutorialWindow, delta => TutorialUiAction(() =>
            {
                tutorialMapRatio = Mathf.Clamp(tutorialMapRatio + delta, 4, 16);
                TickTutorialMinimap();
            }), Stage.Allows(8));
            GameplayNanoComView.AttachMinimap(tutorialWindow, tutorialMinimap);
            foreach (string name in new[] { "inventory", "mail", "map", "settings", "guide", "quit" })
                GameplayNanoComView.Action(tutorialWindow, name).SetEnabled(false);
            var journalButton = GameplayNanoComView.Action(tutorialWindow, "journal");
            journalButton.SetEnabled(Stage.Allows(11));
            journalButton.clicked += () => TutorialUiAction(() => { if (Stage.Allows(11)) { journal = true; mainMenu = false; } });
            GameplayNanoComView.Action(tutorialWindow, "close").clicked += () => TutorialUiAction(() => mainMenu = false);

        }

        void NewTutorialNpcPanel(float width, float height)
        {
            tutorialWindow = new VisualElement { name = "tutorial-npc-root", pickingMode = PickingMode.Ignore };
            tutorialWindow.style.position = Position.Absolute; tutorialWindow.style.left = tutorialWindow.style.top = tutorialWindow.style.right = tutorialWindow.style.bottom = 0;
            tutorialWindowPanel = new VisualElement { name = "tutorial-npc-window", userData = new Vector2(width, height) };
            tutorialWindowPanel.AddToClassList("tutorial-npc-window");
            FFWidgets.Rect(tutorialWindowPanel, 0, 0, width, height); tutorialWindow.Add(tutorialWindowPanel);
        }

        void TutorialNpcBackground(string role, Rect rect)
        {
            var artwork = FFWidgets.Group(tutorialWindowPanel, role);
            FFWidgets.Rect(artwork, rect);
            artwork.AddToClassList("ff-artwork");
            artwork.AddToClassList(role);
        }

        void BuildTutorialNpcMission(string npcName)
        {
            NewTutorialNpcPanel(332, 183);
            TutorialNpcBackground("tutorial-npc-mission-top", new Rect(0, 0, 332, 50));
            TutorialNpcBackground("tutorial-npc-mission-body", new Rect(0, 50, 332, 57));
            TutorialNpcBackground("tutorial-npc-mission-bottom", new Rect(0, 107, 332, 76));
            FFWidgets.Label(tutorialWindowPanel, "tutorial-npc-name", npcName, 15, 10, 277, 40);
            FFWidgets.Label(tutorialWindowPanel, "tutorial-mission-heading", Russian ? "Задания:" : "Missions:", 7, 50, 312, 20);
            var mission = GameplayUiCatalog.Load().Mission(TutorialMissionTask, Language);
            var button = FFWidgets.Button(tutorialWindowPanel, "tutorial-select-mission", mission?.Title ?? "", 7, 75, 312, 32,
                null, null,
                () => TutorialUiAction(() => { if (TutorialMissionSelect) missionSelected = true; }));
            button.AddToClassList("tutorial-npc-mission-button");
        }

        void BuildTutorialWarp(string npcName)
        {
            NewTutorialNpcPanel(242, 171);
            TutorialNpcBackground("npc-service-window", new Rect(0, 0, 242, 171));
            FFWidgets.Label(tutorialWindowPanel, "tutorial-npc-name", npcName, 15, 5, 212, 38);
            FFWidgets.Image(tutorialWindowPanel, "tutorial-warp-icon", TutorialNpcArt + "npcicon_warp", 20, 53, 36, 36);
            var warp = FFWidgets.Button(tutorialWindowPanel, "tutorial-warp", Russian ? "ПЕРЕМЕСТИТЬСЯ" : "WARP", 59, 53, 160, 38,
                null, null, () => TutorialUiAction(() => { if (pendingWarp < 0) RequestWarp(); }));
            warp.AddToClassList("npc-service-action");
            warp.SetEnabled(pendingWarp < 0);
        }

        void BuildTutorialJournal(bool npc)
        {
            tutorialWindow = FFView.InstantiateClass("ui/views/Retrobution/MissionJournal", "ff-journal-root");
            tutorialWindowPanel = FFView.RequireClass<VisualElement>(tutorialWindow, "ff-journal-window");
            tutorialWindowPanel.userData = new Vector2(1036, 654);
            var content = GameplayUiCatalog.Load().Mission(TutorialMissionTask, Language);
            GameplayJournalView.Localize(tutorialWindow, Language);
            bool offering = npc && (TutorialMissionAccept || Stage.Chapter == 3 && Stage.Step == 100);
            string description = offering ? content?.OfferDescription
                : TutorialMissionReward ? content?.CompletionDescription : content?.ActiveDescription;
            var rule = FFOne.World.WorldMissionCatalog.Load().Find(TutorialMissionTask);
            GameplayJournalView.ShowDetails(tutorialWindow, rule, content?.Title ?? "",
                TutorialMissionDescription(content, description), Language, offering, TutorialMissionReward);
            foreach (string role in new[] { "ff-tab-active", "ff-tab-completed", "ff-journal-action-help" }) FFView.RequireClass<Button>(tutorialWindow, role).SetEnabled(false);
            var close = FFView.RequireClass<Button>(tutorialWindow, "ff-journal-action-close"); close.SetEnabled(TutorialWindowCanClose);
            close.clicked += () => TutorialUiAction(() => { if (!TutorialWindowCanClose) return; if (npc) interacting = 0; else journal = false; });
            GameplayJournalView.AddRow(tutorialWindow, content?.Title ?? "", content?.Objective ?? "");
            if (npc)
            {
                string caption = TutorialMissionReward ? (Russian ? "ПОЛУЧИТЬ НАГРАДУ" : "CLAIM REWARD") : (Russian ? "ПРИНЯТЬ" : "ACCEPT");
                var accept = GameplayJournalView.PrimaryAction(tutorialWindow, caption, () => TutorialUiAction(() => {
                        if (TutorialMissionAccept) accepted = true;
                        else if (TutorialMissionReward) rewardClaimed = true;
                    }), completion: TutorialMissionReward);
                accept.SetEnabled(TutorialMissionAccept || TutorialMissionReward);
                accept.style.display = TutorialMissionAccept || TutorialMissionReward || Stage.Chapter == 3 && Stage.Step == 100 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public static string TutorialMissionDescription(GameplayMissionPresentation content, string description)
        {
            string text = string.IsNullOrEmpty(description) ? content?.Objective ?? "" : description;
            if (!string.IsNullOrEmpty(content?.Objective) && text != content.Objective) text += "\n\n" + content.Objective;
            if (!string.IsNullOrEmpty(content?.RewardDescription)) text += "\n\n" + content.RewardDescription;
            return text;
        }
    }
}
