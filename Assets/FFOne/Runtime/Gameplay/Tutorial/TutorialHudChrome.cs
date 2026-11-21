using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        VisualElement tutorialChat, tutorialNpcMessage;
        GameplayHudStatus tutorialHudStatus;
        float tutorialLastCombatResult = float.NegativeInfinity;
        string tutorialChatMessage;
        readonly Queue<(int Npc, int Line, bool Nano)> missionCalls = new();
        VisualElement missionCall, missionBubble;
        TutorialActor bubbleSpeaker;
        float missionCallStarted, bubbleUntil;
        GameObject tutorialMovementMarker;
        Vector3 tutorialMovementMarkerPosition;

        public static async Task PreloadTutorialHudAsync(TutorialContent content, CancellationToken cancellation)
        {
            await ContentAssets.GetAsync<NativeDataDocument>(NativeData.Resource("data/tables/xdt"), cancellation);
            var catalog = GameplayUiCatalog.Load();
            string[] shared = { "mop_info", "npc_info", "target_icon1a", "target_icon2", "nano_win", "nano_draw", "nano_lose" };
            string[] affinity = { "blue_icon", "red_icon", "yellow_icon", "a_icon", "b_icon", "c_icon" };
            string[] instructions = { "tut_mouse", "tut_right", "tut_left", "tut_up", "tut_down", "tut_lmouse", "tut_rmouse", "tut_move", "tut_move_w", "tut_move_s", "tut_jump", "tut_one" };
            var addresses = shared.Select(value => GameplayTargetHud.Art + value)
                .Concat(new[] { "ui/ru/gameplay/shared/mop_info", "ui/ru/gameplay/player/character_info" })
                .Append(GameplayNanoComMessage.NpcFrame)
                .Concat(affinity.Select(value => GameplayTargetHud.AffinityArt + value))
                .Concat(instructions.Select(value => "ui/en/gameplay/tutorial/" + value))
                .Concat(new[] { "ui/en/gameplay/group/hp_bar", "ui/en/gameplay/player/DiskFront", "ui/en/gameplay/nano/active/nano_info", "ui/en/gameplay/nano/stamina/nano_st_bar", "ui/en/gameplay/shared/combat-frame", "ui/en/gameplay/shared/danger", "ui/ru/gameplay/shared/danger", catalog.SkillIcon(1) })
                .Concat((content?.Actors ?? Array.Empty<TutorialActorDefinition>()).Select(actor => catalog.NpcPortrait(actor.NpcType)))
                .Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal);
            await Task.WhenAll(addresses.Select(value => ContentAssets.GetAsync<Texture2D>(value, cancellation)));
            await ContentAssets.GetAsync<AudioClip>("audio/sfx/ui/mission_completed", cancellation);
        }

        void CreateTutorialChrome()
        {
            tutorialHudStatus = new GameplayHudStatus(tutorialHudRoot, "tutorial", Language);

            tutorialChat = FFView.InstantiateClass("ui/views/Retrobution/Chat", "ff-chat-root");
            tutorialChat.name = "tutorial-chat";
            GameplayChatPresentation.Configure(tutorialChat);
            // The tutorial never unlocks Social/FreeChat input. Keep the original
            // receiving surface and menu lesson without dispatching shard messages.
            tutorialChat.Query<Button>().ForEach(button => button.SetEnabled(false));
            FFView.RequireClass<VisualElement>(tutorialChat, "ff-chat-entry-row").RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button == 0 && Stage.Allows(8)) TutorialUiAction(() => mainMenu = true);
            });
            GameplayChatPresentation.Localize(tutorialChat, Language);
            tutorialHudRoot.Add(tutorialChat);
            tutorialChatMessage = null;
        }

        void AdvanceTutorialTask(int next, bool beginsCinematic = false)
        {
            if (currentTask == next) return;
            var missions = FFOne.World.WorldMissionCatalog.Load();
            var finished = missions.Find(currentTask)?.SuccessDialogue;
            QueueTutorialCall(finished, false);
            if (!beginsCinematic && finished?.BubbleString > 0 && tutorialHudRoot != null)
            {
                bubbleSpeaker = actors.Values.FirstOrDefault(actor => actor && actor.NpcType == finished.BubbleNpc);
                if (bubbleSpeaker)
                {
                    missionBubble?.RemoveFromHierarchy();
                    missionBubble = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "world-speech");
                    missionBubble.AddToClassList("world-speech--quest");
                    FFView.RequireClass<Label>(missionBubble, "world-speech-copy").text =
                        GameplayUiCatalog.Load().MissionLine(finished.BubbleString, Language);
                    tutorialHudRoot.Add(missionBubble);
                    bubbleUntil = Time.unscaledTime + 7;
                }
            }
            currentTask = next;
            var started = missions.Find(next);
            QueueTutorialCall(started?.StartDialogue, next == 2250);
        }

        void QueueTutorialCall(FFOne.World.WorldMissionDialogue row, bool nano)
        {
            if (row?.MessageNpc > 0 && row.MessageString > 0 && (row.MessageType & 2) != 0)
                missionCalls.Enqueue((row.MessageNpc, row.MessageString, nano));
        }

        void TickTutorialMissionDialogue()
        {
            if (missionBubble != null)
            {
                if (!bubbleSpeaker || Time.unscaledTime >= bubbleUntil)
                { missionBubble.RemoveFromHierarchy(); missionBubble = null; }
                else
                {
                    var point = ViewCamera.WorldToViewportPoint(bubbleSpeaker.transform.position + Vector3.up * bubbleSpeaker.Height);
                    missionBubble.EnableInClassList("world-annotation--visible", tutorialBalloons && point.z > 0 &&
                        point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1);
                    missionBubble.style.left = point.x * tutorialHudRoot.resolvedStyle.width;
                    missionBubble.style.top = (1 - point.y) * tutorialHudRoot.resolvedStyle.height;
                }
            }
            if (missionCall != null && Time.unscaledTime - missionCallStarted >= GameplayNanoComMessage.NpcLifetime)
            { missionCall.RemoveFromHierarchy(); missionCall = null; }
            if (missionCall == null && missionCalls.Count > 0 && !journal && !mainMenu && interacting == 0)
            {
                var item = missionCalls.Dequeue();
                var catalog = GameplayUiCatalog.Load();
                string title = Localized(item.Nano ? "ui.nanocom.nano_mission.title" : "content.npc." + item.Npc + ".name");
                string line = catalog.MissionLine(item.Line, Language);
                missionCall = GameplayNanoComMessage.Create(tutorialHudRoot, title, line, GameplayNanoComMessage.NpcFrame, catalog.NpcPortrait(item.Npc));
                missionCall.AddToClassList("tutorial-mission-call");
                missionCallStarted = Time.unscaledTime;
                if (tutorialNpcMessagesInChat)
                {
                    var history = FFView.RequireClass<ScrollView>(tutorialChat, "ff-chat-history");
                    GameplayChatPresentation.AddLine(history, title + ": " + line);
                    GameplayChatPresentation.ScrollToLatest(history);
                }
            }
            if (missionCall != null)
            {
                missionCall.style.display = !mainMenu && !journal && interacting == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                GameplayNanoComMessage.Place(missionCall, tutorialHudRoot, Time.unscaledTime - missionCallStarted);
            }
        }

        void TickTutorialChrome(bool equipped)
        {
            if (tutorialHudStatus == null) return;
            bool combat = playerHp > 0 && TutorialCombatHudPresentation.IsActive(tutorialLastCombatResult, Time.realtimeSinceStartup);
            tutorialHudStatus.ShowCombat(combat, tutorialTargetHud?.Visible == true, eventScene);
            tutorialHudStatus.BindNano(equipped && nanoActive ? 1 : 0, 1, 1);
            // cntutorialscript's Social lock keeps chat read-only, including when
            // Enter opens the NanoCom during the journal lesson.
            tutorialChat.style.display = interacting == 0 && !journal ? DisplayStyle.Flex : DisplayStyle.None;
            // CnGuiChat.DoChatWindow(false) always draws its inactive hint.
            // The tutorial's permission mask gates the Enter/menu action only.
            if (auxiliaryMessageTime > 0 && !string.IsNullOrEmpty(auxiliaryMessage) &&
                (tutorialChatMessage != auxiliaryMessage || tutorialNpcMessage == null))
            {
                tutorialNpcMessage?.RemoveFromHierarchy();
                string speaker = Localized("content.npc.2671.name");
                tutorialNpcMessage = GameplayNanoComMessage.Create(tutorialHudRoot, speaker, auxiliaryMessage,
                    GameplayNanoComMessage.NpcFrame, GameplayUiCatalog.Load().NpcPortrait(2671));
                bool newMessage = tutorialChatMessage != auxiliaryMessage;
                tutorialChatMessage = auxiliaryMessage;
                if (newMessage && tutorialNpcMessagesInChat)
                {
                    var history = FFView.RequireClass<ScrollView>(tutorialChat, "ff-chat-history");
                    GameplayChatPresentation.AddLine(history, speaker + ": " + auxiliaryMessage);
                    GameplayChatPresentation.ScrollToLatest(history);
                }
            }
            if (tutorialNpcMessage != null)
            {
                if (auxiliaryMessageTime <= 0) { tutorialNpcMessage.RemoveFromHierarchy(); tutorialNpcMessage = null; }
                else
                {
                    tutorialNpcMessage.style.display = !mainMenu && !journal && interacting == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    GameplayNanoComMessage.Place(tutorialNpcMessage, tutorialHudRoot, GameplayNanoComMessage.NpcLifetime - auxiliaryMessageTime);
                }
            }
        }

        void LayoutTutorialChrome()
        {
            if (tutorialHudStatus == null) return;
            float width = tutorialHudRoot.resolvedStyle.width, height = tutorialHudRoot.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            tutorialHudStatus.Layout(width, height);
            GameplayChatPresentation.Place(tutorialChat, height);
        }

        void TickTutorialMovementMarker()
        {
            if (!marker.HasValue || !effects || !IsReady || ending || IsCinematic || hiddenHud > 0)
            {
                if (tutorialMovementMarker && effects) effects.DestroyNamed("Tutorial movement marker");
                tutorialMovementMarker = null;
                return;
            }
            if (tutorialMovementMarker && tutorialMovementMarkerPosition == marker.Value) return;
            tutorialMovementMarkerPosition = marker.Value;
            tutorialMovementMarker = effects.Spawn(750, marker.Value, 2, false, "Tutorial movement marker");
        }

        void ReleaseTutorialChrome()
        {
            missionCalls.Clear();
            missionBubble = missionCall = null;
            bubbleSpeaker = null;
            if (tutorialMovementMarker && effects) effects.DestroyNamed("Tutorial movement marker");
            tutorialMovementMarker = null; tutorialChat = tutorialNpcMessage = null; tutorialHudStatus = null;
            tutorialChatMessage = null; tutorialLastCombatResult = float.NegativeInfinity;
        }

        // Primary GameFrame.SendCombatMode refreshes condition 1 with realtime,
        // even when it was already set. EndCombatMode clears it only after >5s.
        void RecordTutorialCombatResult() => tutorialLastCombatResult = Time.realtimeSinceStartup;
    }

    public static class TutorialCombatHudPresentation
    {
        public static bool IsActive(float lastResult, float now) => GameplayHudStatus.CombatActive(lastResult, now);
        public static float FrameAlpha(float realtime) => GameplayHudStatus.FrameAlpha(realtime);
    }
}
