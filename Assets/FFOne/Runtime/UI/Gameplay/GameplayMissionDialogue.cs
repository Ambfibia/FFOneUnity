using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using FFOne.Content;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        sealed class MissionLine { public int Npc, Text, Guide, FirstUse; public bool GuideLevel; }
        internal Action<int> GuideComVoice;
        internal Action<string> FirstUseVoice;
        Func<int, System.Threading.Tasks.Task> recordFirstUse;
        readonly ulong[] seenFirstUse = new ulong[2];
        bool firstUseHintsEnabled = true;

        internal void ConfigureFirstUse(WorldEntry entry, Func<int, System.Threading.Tasks.Task> record)
        {
            seenFirstUse[0] = unchecked((ulong)Protocol0104.Read64(entry.RawLoadData, 2540));
            seenFirstUse[1] = unchecked((ulong)Protocol0104.Read64(entry.RawLoadData, 2548));
            recordFirstUse = record;
        }

        internal void CheckFirstUse(int condition)
        {
            if (!firstUseHintsEnabled || recordFirstUse == null || session.Snapshot.IsTutorial || condition < 1 || condition > 128) return;
            int index = (condition - 1) / 64;
            ulong mask = 1UL << ((condition - 1) % 64);
            if ((seenFirstUse[index] & mask) != 0 || string.IsNullOrEmpty(GameplayUiCatalog.Load().FirstUseMessage(condition, language).message)) return;
            seenFirstUse[index] |= mask;
            _ = RecordFirstUse(condition, index, mask);
            missionLines.Enqueue(new MissionLine { Npc = 730, FirstUse = condition });
            ShowMissionLine();
        }

        async System.Threading.Tasks.Task RecordFirstUse(int condition, int index, ulong mask)
        {
            try { await recordFirstUse(condition); }
            catch (Exception ex) when (!disposed)
            {
                seenFirstUse[index] &= ~mask;
                if (ex is not OperationCanceledException) Error?.Invoke(ex.Message);
            }
            catch (Exception) when (disposed) { }
        }
        internal Func<int,WorldEntity> MissionVoiceSpeaker;
        internal void QueueGuideMessage(int guide, bool levelUp)
        {
            int npc = guide == 5 ? 730 : GuideMailNpc(guide);
            if (npc <= 0 || string.IsNullOrWhiteSpace(GameplayUiCatalog.Load().MentorMessage(guide, levelUp, language))) return;
            if (missionLines.Count >= 128) missionLines.Dequeue();
            missionLines.Enqueue(new MissionLine { Npc = npc, Guide = guide, GuideLevel = levelUp });
            ShowMissionLine();
        }
        internal void ShowNanoAcquisition(int id)
        {
            var item = GameplayUiCatalog.Load().Nano(id, language);
            if (item == null) return;
            playSound?.Invoke("nano_creation");
            if (session.Snapshot.Nanos.Any(nano => nano.Id == id && nano.SkillId == 0)) StartNanoReward(id);
            if (showNpcMessagesInChat) AppendNpcChat("", T("Nano acquired: ", "Р В Р’В Р вЂ™Р’В Р В Р Р‹Р РЋРЎСџР В Р’В Р вЂ™Р’В Р В Р Р‹Р Р†Р вЂљРЎС›Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В»Р В Р’В Р В Р вЂ№Р В Р Р‹Р Р†Р вЂљРЎС™Р В Р’В Р В Р вЂ№Р В Р вЂ Р В РІР‚С™Р В Р вЂ№Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’ВµР В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В¦Р В Р’В Р вЂ™Р’В Р В Р Р‹Р Р†Р вЂљРЎС› Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В¦Р В Р’В Р вЂ™Р’В Р В РІР‚в„ўР вЂ™Р’В°Р В Р’В Р вЂ™Р’В Р В Р’В Р Р†Р вЂљР’В¦Р В Р’В Р вЂ™Р’В Р В Р Р‹Р Р†Р вЂљРЎС›: ") + item.Name);
        }
        readonly Queue<MissionLine> missionLines = new();
        VisualElement missionDialogue;
        VisualElement conversationFade;
        WorldEntity conversationNpc;
        bool missionMessageVisible;

        void UpdateNpcConversation()
        {
            if (disposed) return;
            WorldEntity next = Windows.Current switch {
                GameplayWindow.NpcServices => serviceMenuNpc,
                GameplayWindow.NpcMissions => missionMenuNpc,
                GameplayWindow.Travel => travelNpc,
                GameplayWindow.Service => serviceNpc,
                GameplayWindow.Barber => barberNpc,
                GameplayWindow.Journal => missionNpc,
                GameplayWindow.Mentor => conversationNpc,
                _ => null
            };
            if (next?.Id == conversationNpc?.Id) return;
            var previous = conversationNpc;
            conversationNpc = next;
            if (Windows.Current == GameplayWindow.NpcMissions ||
                next == null && previousWindow is GameplayWindow.NpcMissions or GameplayWindow.Journal)
                FadeNpcConversation();
            if (next == null)
            {
                if (previous != null)
                {
                    if (previousWindow is not (GameplayWindow.Service or GameplayWindow.Travel))
                        playSound?.Invoke("close_screen");
                    ServiceVoice?.Invoke(previous, "Farewell");
                }
                if (missionDialogue != null) missionDialogue.style.display = DisplayStyle.Flex;
                ShowMissionLine();
                return;
            }
            if (missionDialogue != null) missionDialogue.style.display = DisplayStyle.None;
            if (Windows.Current != GameplayWindow.Service) playSound?.Invoke("open_screen");
            var rules = WorldMissionCatalog.Load();
            var state = session.Snapshot;
            bool quest = rules.Tasks.Any(r => r.GiverNpc == next.NpcType && MissionOffered(r, rules, state)) ||
                state.RunningQuests.Any(q => rules.Find(q.TaskId)?.EndNpc == next.NpcType);
            var speaker = WorldNpcCatalog.Load().Find(next.NpcType);
            if (!string.IsNullOrWhiteSpace(speaker?.Greeting))
                PublishNpcSpeech(next, SpeechText("content.tabledata.npc.npc_string." + speaker.GreetingId + ".str_comment", speaker.Greeting));
            ServiceVoice?.Invoke(next, quest ? "QGreeting" : "Greeting");
        }

        void FadeNpcConversation()
        {
            conversationFade?.RemoveFromHierarchy();
            var view = FFView.OptionalClass(NpcMissionTree,"npc-conversation-fade");
            if (view == null) return;
            conversationFade = view;
            layers.Add(view,UiLayer.Cinematic);
            float age = 0;
            float previousTime = Time.unscaledTime;
            view.schedule.Execute(() => {
                // A stalled frame during NPC asset loading must still show the fade's peak.
                float now = Time.unscaledTime;
                age += Mathf.Min(now - previousTime, .045f);
                previousTime = now;
                view.style.opacity = Mathf.Clamp01(Mathf.Min(age / .16f, (.48f - age) / .22f));
                if (age < .48f) return;
                view.RemoveFromHierarchy();
                if (conversationFade == view) conversationFade = null;
            }).Every(10);
        }
        static readonly string[] BarkFields = { "str_name", "str_comment", "str_comment1", "str_comment2" };
        readonly Dictionary<string,double> spokenBarks = new(StringComparer.Ordinal);
        readonly Dictionary<int, float> nextBark = new();
        float nextBarkScan, nextMissionBark;

        string SpeechText(string key, string fallback)
        {
            string localized = LocalizationCatalog.Lookup(key, language);
            return localized == key ? fallback : localized;
        }

        void PublishNpcSpeech(WorldEntity speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (showBalloons) GetWorldAnnotations().ShowNpcBubble(speaker, text);
            if (showNpcMessagesInChat) AppendNpcChat(NpcSpeaker(speaker.NpcType), text);
        }

        internal void TickAmbientBarks(NetworkWorldController world)
        {
            if (!world || !world.IsReady || !world.Player || !world.Replicas || serviceClient == null ||
                disposed || session.Snapshot.Hp <= 0 || Time.unscaledTime < nextBarkScan) return;
            float now = Time.unscaledTime;
            nextBarkScan = now + 1;
            if (now >= nextMissionBark)
            {
                nextMissionBark = now + 20;
                var completed = session.Snapshot.CompletedMissions;
                if (completed.Count > 0)
                {
                    var tasks = WorldMissionCatalog.Load().Tasks.Where(task => completed.Contains(task.MissionId) &&
                        task.BarkerTextIds.Any(text => text > 0)).ToArray();
                    if (tasks.Length > 0)
                    {
                        var task = tasks[UnityEngine.Random.Range(0, tasks.Length)];
                        var npc = world.Replicas.Actors.FirstOrDefault(actor => actor.Definition?.BarkerType is >= 1 and <= 4 &&
                            !actor.Entity.Hidden && actor.Entity.Hp > 0 && task.BarkerTextIds[actor.Definition.BarkerType-1] > 0 &&
                            (actor.transform.position-world.Player.position).sqrMagnitude < 2500);
                        if (npc) _ = SendBark(npc.Entity.Id, task.TaskId, true);
                    }
                }
            }
            foreach (var actor in world.Replicas.Actors)
            {
                var entity = actor.Entity;
                var sets = actor.Definition?.BarkSets;
                if (sets == null || sets.Length == 0 || entity.Hidden || entity.Hp <= 0 ||
                    (world.Player.position - actor.transform.position).sqrMagnitude >= 100) continue;
                if (!nextBark.TryGetValue(entity.Id, out float due))
                {
                    nextBark[entity.Id] = now + UnityEngine.Random.Range(0f, 15f);
                    continue;
                }
                if (now < due) continue;
                nextBark[entity.Id] = now + 15;
                if (UnityEngine.Random.Range(0, 100) < 10)
                    _ = SendBark(entity.Id, sets[UnityEngine.Random.Range(0, sets.Length)]);
            }
        }

        async System.Threading.Tasks.Task SendBark(int npc, int number, bool mission = false)
        {
            try { await (mission ? serviceClient.RequestMissionBarkAsync(number, npc, serviceToken) :
                serviceClient.RequestAmbientBarkAsync(npc, number, serviceToken)); }
            catch (OperationCanceledException) when (serviceToken.IsCancellationRequested) { }
            catch (Exception e) { if (!disposed) Debug.LogWarning("Could not request NPC speech: " + e.Message); }
        }

        internal void ReceiveNpcSpeech(ProtocolFrame frame)
        {
            if (frame.PacketType is not (FusionClient.AmbientBarkReply or FusionClient.MissionBarkReply)) return;
            bool ambient = frame.PacketType == FusionClient.AmbientBarkReply;
            if (frame.Payload.Length != (ambient ? 12 : 8)) throw new ProtocolException("Invalid NPC speech payload.");
            int id = Protocol0104.Read32(frame.Payload,0), number = Protocol0104.Read32(frame.Payload,4);
            var actor = ResolveServiceNpc?.Invoke(id);
            if (!actor || actor.Entity.Hidden || actor.Entity.Hp <= 0 || number <= 0) return;
            if (!ambient)
            {
                PublishNpcSpeech(actor.Entity, GameplayUiCatalog.Load().MissionLine(number, language));
                return;
            }
            int line = Protocol0104.Read32(frame.Payload,8);
            if (line < 0 || line >= BarkFields.Length || !actor.Definition.BarkSets.Contains(number)) return;
            string source = WorldNpcCatalog.Load().BarkLine(number,line);
            if (string.IsNullOrWhiteSpace(source)) return;
            string key = string.Join(" ", source.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            double now = Time.realtimeSinceStartupAsDouble;
            if (spokenBarks.TryGetValue(key,out double last) && now - last < 600) return;
            spokenBarks[key] = now;
            PublishNpcSpeech(actor.Entity, SpeechText("content.tabledata.npc.npc_barker." + number + "." + BarkFields[line], source));
        }

        public Func<int, bool> MissionSpeakerAvailable { get; set; }
        public void QueueMissionDialogue(int task, GameplayMissionEdge edge)
        {
            var definition = WorldMissionCatalog.Load().Find(task);
            var dialogue = edge == GameplayMissionEdge.Start ? definition?.StartDialogue : edge == GameplayMissionEdge.Failure ? definition?.FailureDialogue : definition?.SuccessDialogue;
            if (dialogue == null) return;
            // cnMissionNode.SetMissionMessage selects passive NanoCom with bit2.
            if (dialogue.MessageNpc > 0 && dialogue.MessageString > 0 && (dialogue.MessageType & 2) != 0)
                Enqueue(dialogue.MessageNpc, dialogue.MessageString);
            // Explicit bubble ownership is separate from the NanoCom speaker.
            if (dialogue.BubbleNpc > 0 && dialogue.BubbleString > 0 && MissionSpeakerAvailable?.Invoke(dialogue.BubbleNpc) == true)
            {
                var message = GameplayUiCatalog.Load().MissionLine(dialogue.BubbleString, language);
                if (message.Length > 1)
                {
                    if (showBalloons) GetWorldAnnotations().ShowQuestBubble(dialogue.BubbleNpc, message);
                    if (showNpcMessagesInChat) AppendNpcChat(NpcSpeaker(dialogue.BubbleNpc), message);
                }
            }
            if (dialogue.BubbleNpc > 0 && dialogue.BubbleString > 0 && MissionSpeakerAvailable?.Invoke(dialogue.BubbleNpc) != true) return;
            int voiceNpc = edge == GameplayMissionEdge.Start ? definition.GiverNpc : edge == GameplayMissionEdge.Complete ? definition.EndNpc : 0;
            if (voiceNpc > 0)
            {
                var actor = MissionVoiceSpeaker?.Invoke(voiceNpc);
                if (actor != null) ServiceVoice?.Invoke(actor, edge == GameplayMissionEdge.Complete ? "NiceJob" : definition.Type == 6 ? "Laugh" : "GoodLuck");
            }
            ShowMissionLine();
        }
        void Enqueue(int npc, int text)
        {
            if (GameplayUiCatalog.Load().MissionLine(text, language).Length <= 1) return;
            if (missionLines.Count >= 128) missionLines.Dequeue();
            missionLines.Enqueue(new MissionLine { Npc = npc, Text = text });
        }
        void ShowMissionLine()
        {
            if (disposed || rewardView != null || conversationNpc != null || missionDialogue != null || missionLines.Count == 0 ||
                currentInvitation != null || interactionSuspended || prompt != null) return;
            var line = missionLines.Dequeue();
            var catalog = GameplayUiCatalog.Load();
            if (line.FirstUse > 0)
            {
                var hint = catalog.FirstUseMessage(line.FirstUse, language);
                if (!string.IsNullOrEmpty(hint.cue)) FirstUseVoice?.Invoke(hint.cue);
                ShowMissionMessage(NpcSpeaker(line.Npc), hint.message, catalog.NpcPortrait(line.Npc), hint.duration);
                return;
            }
            string message = line.Guide > 0 ? catalog.MentorMessage(line.Guide, line.GuideLevel, language) : catalog.MissionLine(line.Text, language);
            ShowMissionMessage(NpcSpeaker(line.Npc), message, catalog.NpcPortrait(line.Npc));
            if (line.Guide > 0) GuideComVoice?.Invoke(line.Npc);
        }

        string NpcSpeaker(int npc)
        {
            string key = "content.npc." + npc + ".name";
            string speaker = LocalizationCatalog.Lookup(key, language);
            return speaker != key ? speaker : WorldNpcCatalog.Load().Find(npc)?.Name ?? "";
        }

        void AppendNpcChat(string speaker, string message)
        {
            AppendChat(ChatChannel.All, string.IsNullOrEmpty(speaker) ? message : speaker + ": " + message);
            RefreshChat(chatView);
        }

        void ShowMissionMessage(string speaker, string message, string icon, float duration = GameplayNanoComMessage.NpcLifetime)
        {
            missionMessageVisible = true;
            var card = GameplayNanoComMessage.Create(layers.Host(UiLayer.Notification), speaker, message, GameplayNanoComMessage.NpcFrame, icon);
            missionDialogue = card;
            card.name = "mission-nanocom-message";
            if (showNpcMessagesInChat) AppendNpcChat(speaker, message);
            playSound?.Invoke("comm_slidein");
            float opened = Time.unscaledTime, previousTick = opened;
            GameplayNanoComMessage.Place(card, parent, 0);
            card.schedule.Execute(() => {
                if (missionDialogue != card) return;
                float now = Time.unscaledTime;
                if (rewardView != null) opened += now - previousTick;
                previousTick = now;
                if (rewardView != null) return;
                float age = now - opened;
                if (age >= duration)
                {
                    playSound?.Invoke("comm_slideout");
                    AdvanceMissionLine();
                }
                else GameplayNanoComMessage.Place(card, parent, age);
            }).Every(16);
        }

        void AdvanceMissionLine()
        {
            missionDialogue?.RemoveFromHierarchy();
            missionDialogue = null;
            missionMessageVisible = false;
            ShowMissionLine();
        }

        void DisposeMissionDialogue()
        {
            conversationFade?.RemoveFromHierarchy();
            conversationFade = null;
            missionDialogue?.RemoveFromHierarchy();
            missionDialogue = null;
            missionMessageVisible = false;
            missionLines.Clear();
            MissionSpeakerAvailable = null;
            MissionTimeRemaining = null;
        }
    }
}
