using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        FusionClient socialClient;
        CancellationToken socialToken;
        Func<IEnumerable<WorldEntity>> socialPeers;
        readonly SocialSession social = new();
        readonly List<string> socialHistory = new();
        FriendInfo whisperTarget;
        bool socialSending;
        string socialStatus = "";
        float socialRefreshAt;
        float nextMailCheck;
        public SocialSession Social => social;
        public void ConfigureSocial(FusionClient client, CancellationToken token, Func<IEnumerable<WorldEntity>> peers, IEnumerable<ProtocolFrame> bootstrap)
        {
            socialClient = client; socialToken = token; socialPeers = peers;
            foreach (var frame in bootstrap ?? Array.Empty<ProtocolFrame>()) ReceiveSocial(frame);
            nextMailCheck = Time.unscaledTime + 60;
            UpdateGuideMailNotices();
        }
        string T(string en, string ru) => language == "ru" ? ru : en;
        bool SocialAvailable => socialClient != null && !disposed && !interactionSuspended && !TradeWindowActive && !ServiceActive && !inventoryPending && !MailBusy && !TravelBusy && !serviceFaulted;
        public void OpenSocial()
        {
            if (!SocialAvailable) return; selectedPeer=null; Windows.Open(GameplayWindow.Contacts);
            _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); socialRefreshAt = Time.unscaledTime + 10;
        }
        void TickSocial()
        {
            if (warpBuddyUid != 0 && Time.unscaledTime >= buddyWarpDeadline) { warpBuddyUid = 0; RefreshBuddy(); }
            if (socialClient != null && (Windows.Current == GameplayWindow.Contacts || BuddyVisible) && Time.unscaledTime >= socialRefreshAt)
            { socialRefreshAt = Time.unscaledTime + 10; _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); RefreshSocial(); }
            if (socialClient != null && !socialSending && Time.unscaledTime >= nextMailCheck)
            { nextMailCheck = Time.unscaledTime + 600; _ = SocialSend(() => socialClient.CheckMailAsync(socialToken)); }
            TickMail();
        }
        async Task SocialSend(Func<Task> action)
        {
            if (disposed || socialSending) return; socialSending = true; RefreshSocial();
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) { socialStatus = error.Message; Error?.Invoke(error.Message); } }
            finally { socialSending = false; if (!disposed) { RefreshSocial(); RefreshTravel(); } }
        }
        public void ReceiveSocial(ProtocolFrame frame)
        {
            if (ReceiveBuddyWarp(frame)) return;
            if (ReceiveMail(frame)) return;
            if(frame.PacketType is 0x310000cb or 0x310000cc)
            {
                Protocol0104.RequireSize(frame.Payload,16);
                if(frame.PacketType==0x310000cb)_=SocialSend(()=>socialClient.RefreshFriendsAsync(socialToken));
                else Error?.Invoke(T("Unable to ignore player.","Не удалось добавить игрока в список игнорирования."));
                return;
            }
            if (!ProtocolSocial.TryDecode(frame, out var u)) return;
            social.Apply(u);
            if(u.Members!=null || u.Npcs!=null)RefreshGroupHud();
            if (u.Failed) socialStatus = T("Server rejected the social action: ", "Сервер отклонил действие: ") + u.Error;
            if (u.Type == 0x310000f0 || u.Type == 0x310000f2) travelStatus = socialStatus;
            if (u.Type == ProtocolSocial.Request || u.Type == ProtocolSocial.NamedRequest || u.Type == ProtocolSocial.Invite)
            {
                bool group = u.Type == ProtocolSocial.Invite;
                Task Answer(bool accept) => group ? socialClient.AnswerGroupAsync(u.Id, accept, socialToken)
                    : u.Type == ProtocolSocial.NamedRequest ? socialClient.AcceptNamedFriendAsync(u.Uid, u.First, u.Last, accept, socialToken)
                    : socialClient.AcceptFriendAsync(u.Id, accept, socialToken);
                if ((!group && !allowBuddyInvites) || (group && !allowGroupInvites) || social.Friends.Any(f=>f!=null && f.Blocked && (u.Id>0 && f.PlayerId==u.Id || u.Uid>0 && f.Uid==u.Uid)))
                { _ = SendInvitationAnswer(Answer, false); return; }
                string host = (socialPeers?.Invoke() ?? Enumerable.Empty<WorldEntity>()).FirstOrDefault(e => e.Kind == WorldEntityKind.Player && e.Id == u.Id)?.Character?.DisplayName
                    ?? social.Friends.FirstOrDefault(f => f?.PlayerId == u.Id)?.Name ?? T("a player", "игрока");
                var sender=(u.First+" "+u.Last).Trim();
                QueueInvitation(group, sender.Length > 0 ? sender : host, Answer);
            }
            if (u.Type == ProtocolSocial.Added) { socialStatus = T("Friend added.", "Друг добавлен."); _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); }
            if (u.Type == ProtocolSocial.Join) socialStatus = T("Group joined.", "Группа создана.");
            if (u.Type == 0x31000088) socialStatus = T("Group invitation declined.", "Приглашение в группу отклонено.");
            bool groupChat = u.Type == ProtocolSocial.GroupChatOk || u.Type == ProtocolSocial.GroupMenuOk;
            if (groupChat || u.Type == ProtocolSocial.WhisperOk || u.Type == ProtocolSocial.WhisperMenuOk)
            {
                if (social.Friends.Any(f => f != null && f.Blocked && (groupChat ? f.PlayerId == u.Id : f.Uid == u.Uid || f.Uid == u.ToUid))) return;
                string name = groupChat ? social.Members.FirstOrDefault(m => m.Id == u.Id)?.Name ?? u.Id.ToString()
                    : u.Uid == session.Snapshot.CharacterUid ? session.Snapshot.DisplayName : social.Friends.FirstOrDefault(f => f?.Uid == u.Uid)?.Name ?? u.Uid.ToString();
                socialHistory.Add((groupChat ? T("[Group] ", "[Группа] ") : T("[Friend] ", "[Друг] ")) + name + ": " + u.Text);
                AppendChat(groupChat ? ChatChannel.Group : ChatChannel.Friend, name + ": " + u.Text);
                AppendChat(ChatChannel.All, socialHistory[socialHistory.Count - 1]);
                if (!groupChat || u.Id == session.Snapshot.PlayerId)
                    playSound?.Invoke(groupChat || u.Uid == session.Snapshot.CharacterUid ? "outgoing_chat" : "incoming_tell");
                if (socialHistory.Count > 50) socialHistory.RemoveAt(0);
            }
            RefreshChat(chatView); RefreshSocial(); RefreshTravel();
        }
        const string SocialViewAddress = "ui/views/Retrobution/Chat";
        VisualTreeAsset socialRowTemplates;

        static TElement SocialRole<TElement>(VisualElement view, string role) where TElement : VisualElement =>
            FFView.RequireClass<TElement>(view, role);

        TElement CreateSocialRow<TElement>(string role) where TElement : VisualElement
        {
            socialRowTemplates ??= ((TemplateContainer)FFView.InstantiateClass(SocialViewAddress, "social-row-prototypes")).templateSource;
            var row = SocialRole<TElement>(socialRowTemplates.Instantiate(), role);
            row.RemoveFromHierarchy();
            return row;
        }

        static Button BindSocialButton(VisualElement view, string role, string text, Action run, string diagnosticName = null)
        {
            var button = SocialRole<Button>(view, role);
            if (diagnosticName != null) button.name = diagnosticName;
            button.text = FFStyle.Caption(text);
            button.clicked += run;
            FFWidgets.AutoFit(button);
            return button;
        }

        VisualElement BuildSocialView()
        {
            if (selectedPeer != null) return BuildPlayerMenu();
            var view = FFView.InstantiateClass(SocialViewAddress, "social-root");
            var first = SocialRole<TextField>(view, "friend-first");
            var last = SocialRole<TextField>(view, "friend-last");
            var input = SocialRole<TextField>(view, "social-input");
            first.label = T("First", "Имя");
            last.label = T("Last", "Фамилия");
            SocialRole<Label>(view, "social-title").text = T("FRIENDS AND GROUP", "ДРУЗЬЯ И ГРУППА");
            SocialRole<Label>(view, "group-heading").text = T("GROUP", "ГРУППА");
            BindSocialButton(view, "social-close", "×", () => Windows.Close());
            var add = BindSocialButton(view, "friend-add-name", T("ADD FRIEND", "ДОБАВИТЬ"),
                () => _ = SocialSend(() => socialClient.RequestFriendByNameAsync(first.value, last.value, socialToken)));
            var refresh = BindSocialButton(view, "social-refresh", T("REFRESH", "ОБНОВИТЬ"),
                () => { RefreshSocial(); _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); });
            var leave = BindSocialButton(view, "group-leave", T("LEAVE GROUP", "ПОКИНУТЬ ГРУППУ"),
                () => _ = SocialSend(() => socialClient.LeaveGroupAsync(socialToken)));
            var send = BindSocialButton(view, "social-send", T("SEND", "ОТПРАВИТЬ"), () => _ = SocialSend(async () =>
            {
                if (whisperTarget != null) await socialClient.SendFriendChatAsync(whisperTarget.Slot, whisperTarget.Uid, input.value, socialToken);
                else await socialClient.SendGroupChatAsync(input.value, socialToken);
                input.SetValueWithoutNotify("");
            }));
            BindSocialButton(view, "social-group-channel", T("GROUP", "ГРУППА"), () => { whisperTarget = null; RefreshSocial(); });

            var status = SocialRole<Label>(view, "social-status");
            var list = SocialRole<ScrollView>(view, "friend-list");
            var group = SocialRole<ScrollView>(view, "group-list");
            var channel = SocialRole<Label>(view, "social-channel");
            var history = SocialRole<ScrollView>(view, "social-history");
            view.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
            view.userData = (Action)Refresh;
            view.RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            return view;

            void Refresh()
            {
                status.text = socialStatus;
                list.Clear();
                bool ready = !socialSending;
                add.SetEnabled(ready);
                refresh.SetEnabled(ready);
                foreach (var friend in social.Friends.Where(friend => friend != null))
                {
                    var row = CreateSocialRow<VisualElement>("social-friend-row");
                    SocialRole<Label>(row, "social-row-name").text = friend.Name +
                        (friend.Blocked ? T(" [blocked]", " [заблокирован]") : friend.State != 0 ? T(" [online]", " [в сети]") : "");
                    BindSocialButton(row, "social-row-chat", T("CHAT", "ЧАТ"), () => { whisperTarget = friend; RefreshSocial(); },
                        "friend-chat-" + friend.Slot).SetEnabled(ready && !friend.Blocked);
                    BindSocialButton(row, "social-row-block", T("BLOCK", "БЛОК"),
                        () => _ = SocialSend(() => socialClient.RemoveFriendAsync(friend.Slot, friend.Uid, true, socialToken)),
                        "friend-block-" + friend.Slot).SetEnabled(ready && !friend.Blocked);
                    BindSocialButton(row, "social-row-remove", friend.Blocked ? T("UNBLOCK", "РАЗБЛОК.") : T("REMOVE", "УДАЛИТЬ"),
                        () => _ = SocialSend(() => socialClient.RemoveFriendAsync(friend.Slot, friend.Uid, false, socialToken)),
                        "friend-remove-" + friend.Slot).SetEnabled(ready);
                    list.Add(row);
                }
                foreach (var peer in (socialPeers?.Invoke() ?? Enumerable.Empty<WorldEntity>())
                    .Where(entity => entity.Kind == WorldEntityKind.Player && entity.Character != null && !entity.Hidden).Take(20))
                {
                    var row = CreateSocialRow<VisualElement>("social-peer-row");
                    SocialRole<Label>(row, "social-peer-name").text = T("Nearby: ", "Рядом: ") + peer.Character.DisplayName;
                    BindSocialButton(row, "social-peer-buddy", T("ADD FRIEND", "В ДРУЗЬЯ"),
                        () => _ = SocialSend(() => socialClient.RequestFriendAsync(peer.Id, peer.Character.Uid, socialToken)),
                        "friend-nearby-" + peer.Id).SetEnabled(ready);
                    BindSocialButton(row, "social-peer-group", T("INVITE GROUP", "В ГРУППУ"),
                        () => _ = SocialSend(() => socialClient.InviteGroupAsync(peer.Id, socialToken)),
                        "group-invite-" + peer.Id).SetEnabled(ready);
                    list.Add(row);
                }

                group.Clear();
                foreach (var member in social.Members)
                {
                    var label = CreateSocialRow<Label>("social-member-row");
                    label.name = "group-member-" + member.Id;
                    label.text = $"{member.Name}   Lv {member.Level}   HP {member.Hp}/{member.MaxHp}";
                    group.Add(label);
                }
                foreach (var npc in social.Npcs)
                {
                    var row = CreateSocialRow<VisualElement>("social-group-npc-row");
                    var label = SocialRole<Label>(row, "social-group-npc-name");
                    label.name = "group-npc-" + npc.Id;
                    label.text = (FFOne.World.WorldNpcCatalog.Load().Find(npc.Type)?.Name ?? npc.Type.ToString()) + $"   HP {npc.Hp}";
                    BindSocialButton(row, "social-group-npc-remove", T("REMOVE NPC", "УБРАТЬ NPC"),
                        () => _ = SocialSend(() => socialClient.KickNpcAsync(npc.Id, socialToken)),
                        "group-npc-kick-" + npc.Id).SetEnabled(ready && !TravelBusy);
                    group.Add(row);
                }
                leave.SetEnabled(social.Members.Count > 0 && ready);
                if (whisperTarget != null) whisperTarget = social.Friends.FirstOrDefault(friend => friend?.Uid == whisperTarget.Uid && !friend.Blocked);
                channel.text = whisperTarget != null ? T("To friend: ", "Друг: ") + whisperTarget.Name : T("Group chat", "Чат группы");
                history.Clear();
                foreach (string text in socialHistory)
                {
                    var line = CreateSocialRow<Label>("social-history-row");
                    line.text = text;
                    history.Add(line);
                }
                send.SetEnabled(ready && (whisperTarget != null || social.Members.Count > 0));
            }
        }

        void RefreshSocial()
        {
            RefreshBuddy();
            if (Windows.Current == GameplayWindow.Contacts && Windows.View?.userData is Action refresh)
                refresh();
        }

        void DisposeSocial() { socialClient = null; invitations.Clear(); currentInvitation=null; messageRoot?.RemoveFromHierarchy(); CloseBuddyEntry(); }
    }
}
