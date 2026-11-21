#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task MentorChange(int port)
        {
            const uint accepted = 0x31000093, rejected = 0x31000094;
            const int previousGuide = 2, targetGuide = 4, guideChanger = 3035;
            bool expectRejection = Environment.GetCommandLineArgs().Contains("--expect-mentor-rejection");
            var start = frontend.LiveState.Snapshot;
            Check(start.Level == (expectRejection ? 3 : 4) && start.Guide == previousGuide && start.GuideCount == 1,
                "fixture starts with Dexter as its existing guide at the requested level");
            var world = frontend.LiveWorld;
            world.Replicas.ContentError += contentErrors.Add;
            var document = frontend.GetComponent<UIDocument>();
            int width = UiSmokeLaunch.Dimension("-screen-width", 1280);
            int height = UiSmokeLaunch.Dimension("-screen-height", 720);
            uiTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            uiTexture.Create();
            panel = Instantiate(document.panelSettings);
            panel.targetTexture = uiTexture;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.clearColor = true;
            panel.colorClearValue = Color.clear;
            document.panelSettings = panel;

            NetworkEntityView npc = null;
            await Wait(() => (npc = world.Replicas.Actors.FirstOrDefault(actor => actor.Entity.Kind == WorldEntityKind.Npc
                && actor.Entity.NpcType == guideChanger)) && world.IsReady, "the authored Guide Changer enters live server interest");
            var ui = frontend.LiveUi;
            Check(ui.OpenNpcTravel(npc.Entity, npc.Definition), "Guide Changer opens its normal interaction menu");
            await Frames(4);
            PressVisible(ui.Windows.View.Q<Button>("npc-mentor"));
            await Wait(() => ui.Windows.Current == GameplayWindow.Mentor, "NPC action opens the mentor window");
            var mentor = ui.Windows.View;
            var dexter = mentor.Q(className: "mentor-card-dexter");
            var ben = mentor.Q(className: "mentor-card-ben");
            Check(dexter.ClassListContains("mentor-card--current"), "mentor window marks the server's existing guide");
            await Frames(6);
            Capture(uiTexture, "mentor-current-dexter");
            PressVisible(ben.Q<Button>(className: "mentor-card-toggle"));
            PressVisible(mentor.Q<Button>(className: "mentor-primary"));
            await Frames(6);
            Check(mentor.Q(className: "mentor-confirm-art").ClassListContains("mentor-confirm-art--ben"),
                "confirmation displays the selected Ben variant");
            Capture(uiTexture, "mentor-confirm-ben");
            PressVisible(mentor.Q<Button>(className: "mentor-no"));
            await Frames(6);
            Check(frontend.LiveState.Snapshot.Guide == previousGuide && !received.Contains(accepted) && !received.Contains(rejected),
                "cancelling leaves the guide unchanged and receives no settlement");

            ProtocolFrame reply = null;
            int replyCount = 0;
            void ObserveMentorReply(NetworkEvent update)
            {
                if (update.IsShard && update.Kind == NetworkEventKind.Packet && update.Frame.PacketType is accepted or rejected)
                {
                    reply = update.Frame;
                    replyCount++;
                }
            }
            frontend.NetworkEventReceived += ObserveMentorReply;
            try
            {
                PressVisible(mentor.Q<Button>(className: "mentor-primary"));
                await Frames(4);
                PressVisible(mentor.Q<Button>(className: "mentor-yes"));
                Check(ui.Windows.Current == GameplayWindow.Mentor && frontend.LiveState.Snapshot.Guide == previousGuide,
                    "confirmation keeps the current guide until the shard acknowledges the request");
                await Wait(() => replyCount > 0, "RustyFusion replies to the mentor change");
                if (expectRejection)
                {
                    var status = frontend.Root.Q<Label>("connection-status");
                    string requiredLevel = frontend.Language == "ru" ? "Сменить наставника можно с 4-го уровня." : "Reach level 4 to change your guide.";
                    Check(replyCount == 1 && reply.PacketType == rejected && reply.Payload.Length == 8 &&
                        Protocol0104.Read16(reply.Payload, 0) == targetGuide && frontend.LiveState.Snapshot.Guide == previousGuide &&
                        frontend.LiveState.Snapshot.GuideCount == start.GuideCount && frontend.LiveState.Snapshot.FusionMatter == start.FusionMatter,
                        "RustyFusion rejects the request below level four without changing the guide or currency");
                    await Wait(() => status?.text == requiredLevel && status.resolvedStyle.display != DisplayStyle.None &&
                        ui.Windows.Current == GameplayWindow.Mentor && mentor.Q<Button>(className: "mentor-primary").enabledInHierarchy,
                        "the localized level requirement appears and the mentor window accepts input again");
                    await Frames(6);
                    Capture(uiTexture, "mentor-level-required");
                    contentErrors.AddRange(world.Replicas.ContentFailures);
                    await frontend.LiveExit();
                    return;
                }
                Check(replyCount == 1 && reply.PacketType == accepted && reply.Payload.Length == 8 &&
                    Protocol0104.Read16(reply.Payload, 0) == targetGuide &&
                    Protocol0104.Read16(reply.Payload, 2) == start.GuideCount + 1 &&
                    Protocol0104.Read32(reply.Payload, 4) == start.FusionMatter,
                    "one 0104 success reply settles Ben, increments the guide count and preserves Fusion Matter");
                Check(!ui.Windows.IsOpen && frontend.LiveState.Snapshot.Guide == targetGuide &&
                    frontend.LiveState.Snapshot.GuideCount == start.GuideCount + 1 &&
                    frontend.LiveState.Snapshot.FusionMatter == start.FusionMatter,
                    "the Unity window closes after applying the authoritative settlement");
                contentErrors.AddRange(world.Replicas.ContentFailures);
                await frontend.LiveExit();
                Check(!frontend.Session.IsInWorld, "world exit acknowledges the save before reconnect");
                await frontend.LiveLogin(port);
                var rejoined = frontend.LiveState.Snapshot;
                Check(frontend.Session.IsWorldLoaded && rejoined.Guide == targetGuide && rejoined.FusionMatter == start.FusionMatter,
                    "a fresh login and world entry rehydrate the persisted mentor and Fusion Matter");
                Debug.Log($"LIVE MENTOR COUNTS: accepted={start.GuideCount + 1}, relog={rejoined.GuideCount}");
                contentErrors.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                Check(replyCount == 1, "reconnect did not issue another mentor request");
            }
            finally
            {
                frontend.NetworkEventReceived -= ObserveMentorReply;
            }

            void PressVisible(Button button)
            {
                if (button == null || button.worldBound.width <= 0 || button.worldBound.height <= 0 ||
                    !ReferenceEquals(button.panel?.Pick(button.worldBound.center), button))
                    throw new InvalidOperationException("Mentor control is missing or covered: " + button?.name);
                Submit(button);
            }
        }
    }
}
#endif
