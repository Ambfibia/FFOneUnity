using System;
using System.Collections.Generic;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        sealed class MissionLine { public int Npc, Text; public bool Message; }
        readonly Queue<MissionLine> missionLines = new();
        VisualElement missionDialogue;
        public Func<int, bool> MissionSpeakerAvailable { get; set; }
        public void QueueMissionDialogue(int task, GameplayMissionEdge edge)
        {
            var definition = WorldMissionCatalog.Load().Find(task);
            var dialogue = edge == GameplayMissionEdge.Start ? definition?.StartDialogue : edge == GameplayMissionEdge.Failure ? definition?.FailureDialogue : definition?.SuccessDialogue;
            if (dialogue == null) return;
            // cnMissionNode.SetMissionMessage selects passive NanoCom with bit2.
            if (dialogue.MessageNpc > 0 && dialogue.MessageString > 0 && (dialogue.MessageType & 2) != 0)
                Enqueue(dialogue.MessageNpc, dialogue.MessageString, true);
            // Explicit bubble ownership is separate from the NanoCom speaker.
            if (dialogue.BubbleNpc > 0 && dialogue.BubbleString > 0 && MissionSpeakerAvailable?.Invoke(dialogue.BubbleNpc) == true)
                Enqueue(dialogue.BubbleNpc, dialogue.BubbleString, false);
            ShowMissionLine();
        }
        void Enqueue(int npc, int text, bool message)
        {
            if (GameplayUiCatalog.Load().MissionLine(text, language).Length <= 1) return;
            if (missionLines.Count >= 128) missionLines.Dequeue();
            missionLines.Enqueue(new MissionLine { Npc = npc, Text = text, Message = message });
        }
        void ShowMissionLine()
        {
            if (disposed || missionDialogue != null || missionLines.Count == 0) return;
            var line = missionLines.Dequeue();
            missionDialogue = new VisualElement { name = line.Message ? "mission-nanocom-message" : "mission-npc-dialogue" };
            missionDialogue.style.position = Position.Absolute; missionDialogue.style.left = 30; missionDialogue.style.bottom = 155;
            missionDialogue.style.width = 620; missionDialogue.style.height = 180; missionDialogue.style.backgroundColor = new Color(.025f, .08f, .14f, .96f);
            missionDialogue.style.paddingLeft = missionDialogue.style.paddingRight = 14;
            missionDialogue.style.paddingTop = 10; missionDialogue.style.paddingBottom = 8;
            var title = new Label((line.Message ? "NANOCOM · " : "") + (WorldNpcCatalog.Load().Find(line.Npc)?.Name ?? line.Npc.ToString()));
            title.enableRichText = false; FFWidgets.Font(title, false, 14); title.style.color = new Color(.48f, .94f, 1);
            missionDialogue.Add(title);
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1;
            var body = new Label(GameplayUiCatalog.Load().MissionLine(line.Text, language)); body.enableRichText = false;
            body.style.whiteSpace = WhiteSpace.Normal; FFWidgets.Font(body, true, 14); body.style.color = Color.white;
            scroll.Add(body); missionDialogue.Add(scroll);
            var next = new Button(() => { missionDialogue.RemoveFromHierarchy(); missionDialogue = null; ShowMissionLine(); }) {
                name = "mission-dialogue-next", text = language == "ru" ? "ДАЛЕЕ" : "CONTINUE" };
            FFWidgets.Font(next, false, 12); missionDialogue.Add(next); parent.Add(missionDialogue);
        }
        void DisposeMissionDialogue()
        { missionDialogue?.RemoveFromHierarchy(); missionDialogue = null; missionLines.Clear(); MissionSpeakerAvailable = null; MissionTimeRemaining = null; }
    }
}
