using System;
using FFOne.Content;
using FFOne.Integration;
using FFOne.Network;
using FFOne.UI;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        VisualElement tutorialHudRoot, tutorialWindow, tutorialWindowPanel;
        GameplayHudController tutorialHud;
        GameplayNanoHudController tutorialNanoHud;
        GameplayUiSession tutorialHudSession;
        GameplayUiState tutorialHudEntry;
        PreviewStage tutorialPortraitStage;
        EventCallback<GeometryChangedEvent> tutorialHudGeometry;
        int hudInputFrame = -1, hudHp = -1, hudAnimationFrame = -1;
        bool hudNanoEquipped, hudNanoActive;
        string hudWindowKey;
        public Image GameplayHudPortrait => tutorialHud?.Portrait;
        public bool HudConsumedInput => hudInputFrame == Time.frameCount;
        public bool HasGameplayHud => tutorialHudRoot != null;

        /// <summary>
        /// The tutorial owns local combat and mission edges. Only decode the accepted entry here;
        /// no gameplay service, chat or combat network command is attached to these controls.
        /// </summary>
        public void AttachHud(VisualElement parent, WorldEntry entry, CharacterInfo character, PreviewStage portraitStage = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            DetachHud();
            try
            {
                tutorialHudSession = new GameplayUiSession();
                tutorialHudSession.SetWorld(entry, character);
                tutorialHudEntry = tutorialHudSession.Snapshot;
                tutorialHudRoot = new VisualElement { name = "tutorial-gameplay-ui", pickingMode = PickingMode.Ignore };
                tutorialHudRoot.style.position = Position.Absolute;
                tutorialHudRoot.style.left = tutorialHudRoot.style.top = tutorialHudRoot.style.right = tutorialHudRoot.style.bottom = 0;
                var style = ContentAssets.Load<StyleSheet>("ui/styles/Gameplay");
                if (style) tutorialHudRoot.styleSheets.Add(style);
                UiLayers.For(parent).Add(tutorialHudRoot, UiLayer.Hud);
                tutorialHud = new GameplayHudController(tutorialHudRoot, Language);
                tutorialNanoHud = new GameplayNanoHudController(tutorialHudRoot);
                tutorialNanoHud.SetAnimated(tutorialAnimatedNanos);
                CreateTutorialMinimap();
                CreateTutorialTargetHud();
                CreateTutorialChrome();
                tutorialPortraitStage = portraitStage;
                if (tutorialPortraitStage)
                {
                    tutorialPortraitStage.RefreshGameplayAppearance(character);
                    tutorialPortraitStage.SetGameplayPortrait(tutorialHud.Portrait);
                }
                tutorialHudGeometry = _ => LayoutTutorialHud();
                tutorialHudRoot.RegisterCallback(tutorialHudGeometry);
                hudHp = -1; hudWindowKey = null;
                TickHud(); LayoutTutorialHud();
                AttachTutorialOverlay(parent);
            }
            catch { DetachHud(); throw; }
        }

        bool TutorialHudCanInteract => IsReady && !ending && !paused && !IsCinematic && hiddenHud == 0 && !ExternalInputBlocked;

        void TickHud()
        {
            if (tutorialHudRoot == null) return;
            TickTutorialMovementMarker();
            bool visible = IsReady && !ending && !paused && !IsCinematic && hiddenHud == 0;
            tutorialHudRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // TutorialUiAction gates input without changing presentation.
            // Disabling the root also applies UI Toolkit's disabled tint to
            // every portrait, map tile and bar, washing out the source HUD.
            if (!visible) return;
            // Equip/call in NanoPowerA2 is temporary. Do not publish it into the server session.
            bool equipped = Stage.Chapter == 5 && Stage.Step >= 3;
            if (hudHp != playerHp || equipped != hudNanoEquipped || hudNanoActive != nanoActive)
            {
                hudHp = playerHp; hudNanoEquipped = equipped; hudNanoActive = nanoActive;
                int stamina = GameplayUiCatalog.Load().Nano(1)?.NanoMaxStamina ?? 150;
                var state = ProjectTutorialHud(tutorialHudEntry, playerHp, equipped, nanoActive, stamina);
                tutorialHud.Bind(state); tutorialNanoHud.Bind(state);
            }
            // UpdateTutorial binds before input and again after combat/progression.
            // CnGuiCharacter_info.Update advances the damage trail only once.
            if (hudAnimationFrame != Time.frameCount)
            {
                hudAnimationFrame = Time.frameCount;
                tutorialHud.Tick(Time.unscaledDeltaTime);
            }
            TickTutorialMinimap();
            TickTutorialTargetHud();
            TickTutorialChrome(equipped);
            TickTutorialMissionDialogue();
            string key = $"{Stage.Chapter}:{Stage.Step}:{interacting}:{journal}:{mainMenu}:{currentTask}";
            if (hudWindowKey != key) { hudWindowKey = key; RebuildTutorialWindow(); }
        }

        public static GameplayUiState ProjectTutorialHud(GameplayUiState entry, int hp, bool equipped, bool active, int stamina)
        {
            if (entry == null || !entry.IsActive) throw new ArgumentException("An accepted entry is required.", nameof(entry));
            var local = entry.WithTutorialHealth(Mathf.Clamp(hp, 0, 1000), 1000);
            // The authored tutorial supplies Nano 1 / skill 1 in slot zero only.
            return local.WithNanos(equipped ? new[] { new GameplayNanoState(1, 1, 1, stamina) } : Array.Empty<GameplayNanoState>(),
                equipped ? new[] { 1, 0, 0 } : new[] { 0, 0, 0 }, equipped && active ? 0 : -1);
        }

        void TutorialUiAction(Action action)
        {
            if (!TutorialHudCanInteract) return;
            hudInputFrame = Time.frameCount; action(); hudWindowKey = null;
        }

        void LayoutTutorialHud()
        {
            if (tutorialHudRoot == null) return;
            tutorialHud?.Layout(); tutorialNanoHud?.Layout(); LayoutTutorialMinimap();
            LayoutTutorialTargetHud();
            LayoutTutorialChrome();
            if (tutorialWindowPanel == null) return;
            float width = tutorialHudRoot.resolvedStyle.width, height = tutorialHudRoot.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            if (tutorialWindowPanel.ClassListContains("nanocom-window"))
            {
                GameplayNanoComView.Place(tutorialWindow, width, height);
                return;
            }
            var size = tutorialWindowPanel.userData is Vector2 value ? value : new Vector2(1036, 654);
            float scale = Mathf.Min(RetrobutionLayout.Scale(height), width / size.x);
            tutorialWindowPanel.style.transformOrigin = new TransformOrigin(0, 0);
            tutorialWindowPanel.style.scale = new Scale(new Vector3(scale, scale, 1));
            if (tutorialWindowPanel.ClassListContains("tutorial-npc-window"))
            {
                // NpcIconMode uses the right quarter-screen pivot.
                tutorialWindowPanel.style.left = width * .75f - size.x * scale / 2;
                tutorialWindowPanel.style.top = height / 2 - size.y * scale / 2;
            }
            else
            { tutorialWindowPanel.style.left = (width - size.x * scale) / 2; tutorialWindowPanel.style.top = (height - size.y * scale) / 2; }
        }

        public void DetachHud()
        {
            DetachTutorialOverlay();
            if (tutorialPortraitStage) tutorialPortraitStage.SetGameplayPortrait(null);
            tutorialPortraitStage = null;
            tutorialHud?.Dispose(); tutorialHud = null;
            tutorialNanoHud?.Dispose(); tutorialNanoHud = null;
            if (tutorialHudRoot != null)
            {
                if (tutorialHudGeometry != null) tutorialHudRoot.UnregisterCallback(tutorialHudGeometry);
                tutorialHudRoot.RemoveFromHierarchy();
            }
            tutorialHudRoot = tutorialWindow = tutorialWindowPanel = null;
            tutorialHudGeometry = null; tutorialHudSession = null; tutorialHudEntry = null;
            hudWindowKey = null; hudHp = -1; hudAnimationFrame = -1;
            ReleaseTutorialMinimap();
            ReleaseTutorialTargetHud();
            ReleaseTutorialChrome();
        }
    }
}
