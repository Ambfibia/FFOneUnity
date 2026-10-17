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
        Button contactsButton, mailButton;
        float socialRefreshAt;
        public SocialSession Social => social;
        public void ConfigureSocial(FusionClient client, CancellationToken token, Func<IEnumerable<WorldEntity>> peers, IEnumerable<ProtocolFrame> bootstrap)
        {
            socialClient = client; socialToken = token; socialPeers = peers;
            contactsButton = ServiceButton(parent, "contacts-open", T("FRIENDS / GROUP", "ДРУЗЬЯ / ГРУППА"), 18, 110, 200, () => OpenSocial());
            mailButton = ServiceButton(parent, "mail-open", T("MAIL", "ПОЧТА"), 18, 140, 200, () => OpenMail());
            foreach (var frame in bootstrap ?? Array.Empty<ProtocolFrame>()) ReceiveSocial(frame);
            _ = SocialSend(() => client.CheckMailAsync(token));
        }
        string T(string en, string ru) => language == "ru" ? ru : en;
        bool SocialAvailable => socialClient != null && !disposed && !interactionSuspended && !TradeWindowActive && !ServiceActive && !inventoryPending && !MailBusy && !TravelBusy && !serviceFaulted;
        public void OpenSocial()
        {
            if (!SocialAvailable) return; Windows.Open(GameplayWindow.Contacts);
            _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); socialRefreshAt = Time.unscaledTime + 10;
        }
        void TickSocial()
        {
            bool visible = session.Snapshot.IsActive && !session.Snapshot.IsTutorial && session.Snapshot.Hp > 0 && !Windows.IsOpen && !interactionSuspended && prompt == null;
            if (contactsButton != null) { contactsButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; contactsButton.text = T("FRIENDS / GROUP", "ДРУЗЬЯ / ГРУППА"); }
            if (mailButton != null) { mailButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; mailButton.text = T("MAIL", "ПОЧТА") + (mailUnread > 0 ? $" ({mailUnread})" : ""); }
            if (Windows.Current == GameplayWindow.Contacts && Time.unscaledTime >= socialRefreshAt)
            { socialRefreshAt = Time.unscaledTime + 10; _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); RefreshSocial(); }
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
            if (ReceiveMail(frame)) return;
            if (!ProtocolSocial.TryDecode(frame, out var u)) return;
            social.Apply(u);
            if (u.Failed) socialStatus = T("Server rejected the social action: ", "Сервер отклонил действие: ") + u.Error;
            if (u.Type == 0x310000f0 || u.Type == 0x310000f2) travelStatus = socialStatus;
            if (u.Type == ProtocolSocial.Request || u.Type == ProtocolSocial.NamedRequest || u.Type == ProtocolSocial.Invite)
            {
                bool group = u.Type == ProtocolSocial.Invite;
                Task Answer(bool accept) => group ? socialClient.AnswerGroupAsync(u.Id, accept, socialToken)
                    : u.Type == ProtocolSocial.NamedRequest ? socialClient.AcceptNamedFriendAsync(u.Uid, u.First, u.Last, accept, socialToken)
                    : socialClient.AcceptFriendAsync(u.Id, accept, socialToken);
                if (prompt != null || TradeWindowActive || ServiceActive || MailBusy || TravelBusy || interactionSuspended)
                { _ = SocialSend(() => Answer(false)); return; }
                string host = (socialPeers?.Invoke() ?? Enumerable.Empty<WorldEntity>()).FirstOrDefault(e => e.Kind == WorldEntityKind.Player && e.Id == u.Id)?.Character?.DisplayName
                    ?? social.Friends.FirstOrDefault(f => f?.PlayerId == u.Id)?.Name ?? T("a player", "игрока");
                ShowPrompt(group ? T("GROUP", "ГРУППА") : T("FRIEND REQUEST", "ЗАПРОС ДРУЖБЫ"),
                    group ? T("Join the group of ", "Вступить в группу ") + host + "?" : (u.First + " " + u.Last).Trim(), null,
                    _ => Answer(true), () => Answer(false));
            }
            if (u.Type == ProtocolSocial.Added) { socialStatus = T("Friend added.", "Друг добавлен."); _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); }
            if (u.Type == ProtocolSocial.Join) socialStatus = T("Group joined.", "Группа создана.");
            if (u.Type == 0x31000088) socialStatus = T("Group invitation declined.", "Приглашение в группу отклонено.");
            if (u.Type == ProtocolSocial.WhisperOk || u.Type == ProtocolSocial.GroupChatOk)
            {
                string name = u.Type == ProtocolSocial.GroupChatOk ? social.Members.FirstOrDefault(m => m.Id == u.Id)?.Name ?? u.Id.ToString()
                    : u.Uid == session.Snapshot.CharacterUid ? session.Snapshot.DisplayName : social.Friends.FirstOrDefault(f => f?.Uid == u.Uid)?.Name ?? u.Uid.ToString();
                socialHistory.Add((u.Type == ProtocolSocial.GroupChatOk ? T("[Group] ", "[Группа] ") : T("[Friend] ", "[Друг] ")) + name + ": " + u.Text);
                if (socialHistory.Count > 50) socialHistory.RemoveAt(0);
            }
            RefreshSocial(); RefreshTravel();
        }
        VisualElement SocialRoot(string name, float width, float height, string title)
        {
            var root = new VisualElement { name = name + "-root", pickingMode = PickingMode.Ignore }; root.style.position = Position.Absolute; root.StretchToParentSize();
            var panel = new VisualElement { name = name + "-window" }; FFWidgets.Rect(panel, 0, 0, width, height);
            panel.style.backgroundColor = new Color(.015f, .055f, .09f, .99f); panel.style.borderBottomWidth = panel.style.borderTopWidth = panel.style.borderLeftWidth = panel.style.borderRightWidth = 2;
            panel.style.borderBottomColor = panel.style.borderTopColor = panel.style.borderLeftColor = panel.style.borderRightColor = new Color(.16f, .5f, .65f);
            root.Add(panel); FFWidgets.Label(panel, name + "-title", title, 18, 12, width - 100, 30, true, 18, false);
            ServiceButton(panel, name + "-close", "×", width - 48, 12, 32, () => Windows.Close());
            root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow()); return root;
        }
        TextField SocialField(VisualElement view, string name, string label, float x, float y, float width, int max = 127, bool multiline = false, float height = 28)
        {
            var field = new TextField(label) { name = name, maxLength = max, multiline = multiline };
            FFWidgets.Rect(field, x, y, width, height); FFWidgets.Font(field, true, 13); field.labelElement.style.minWidth = 0;
            field.style.marginTop = field.style.marginBottom = 0;
            field.style.paddingTop = field.style.paddingBottom = 0;
            var input = field.Q<VisualElement>("unity-text-input");
            if (input != null)
            {
                input.style.height = height; input.style.marginTop = input.style.marginBottom = 0;
                input.style.paddingTop = input.style.paddingBottom = multiline ? 6 : 0;
                input.style.paddingLeft = input.style.paddingRight = 6;
                input.style.unityTextAlign = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
                input.style.backgroundColor = new Color(.025f, .10f, .16f); input.style.color = Color.white;
                input.style.borderBottomColor = input.style.borderTopColor = input.style.borderLeftColor = input.style.borderRightColor = new Color(.16f, .5f, .65f);
                input.style.borderBottomWidth = input.style.borderTopWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 1;
            }
            view.Add(field); return field;
        }
        VisualElement BuildSocialView()
        {
            var root = SocialRoot("social", 880, 630, T("FRIENDS AND GROUP", "ДРУЗЬЯ И ГРУППА")); var p = root.Q("social-window");
            FFWidgets.Label(p, "social-status", socialStatus, 18, 48, 830, 24, true, 12, false);
            SocialField(p, "friend-first", T("First", "Имя"), 18, 77, 190, 8); SocialField(p, "friend-last", T("Last", "Фамилия"), 216, 77, 240, 16);
            ServiceButton(p, "friend-add-name", T("ADD FRIEND", "ДОБАВИТЬ"), 464, 77, 180, () => _ = SocialSend(() => socialClient.RequestFriendByNameAsync(p.Q<TextField>("friend-first").value, p.Q<TextField>("friend-last").value, socialToken)));
            ServiceButton(p, "social-refresh", T("REFRESH", "ОБНОВИТЬ"), 656, 77, 200, () => { RefreshSocial(); _ = SocialSend(() => socialClient.RefreshFriendsAsync(socialToken)); });
            var list = new ScrollView { name = "friend-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; FFWidgets.Rect(list, 18, 116, 450, 282); p.Add(list);
            FFWidgets.Label(p, "group-heading", T("GROUP", "ГРУППА"), 488, 113, 360, 25, true, 15, false);
            var group = new ScrollView { name = "group-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; FFWidgets.Rect(group, 488, 143, 365, 206); p.Add(group);
            ServiceButton(p, "group-leave", T("LEAVE GROUP", "ПОКИНУТЬ ГРУППУ"), 488, 363, 365, () => _ = SocialSend(() => socialClient.LeaveGroupAsync(socialToken)));
            FFWidgets.Label(p, "social-channel", "", 18, 404, 830, 24, true, 13, false);
            var history = new ScrollView { name = "social-history", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; FFWidgets.Rect(history, 18, 434, 835, 122); p.Add(history);
            SocialField(p, "social-input", "", 18, 574, 570);
            ServiceButton(p, "social-send", T("SEND", "ОТПРАВИТЬ"), 600, 574, 120, () => _ = SocialSend(async () => {
                var field = p.Q<TextField>("social-input"); string text = field.value;
                if (whisperTarget != null) await socialClient.SendFriendChatAsync(whisperTarget.Slot, whisperTarget.Uid, text, socialToken);
                else await socialClient.SendGroupChatAsync(text, socialToken); field.SetValueWithoutNotify("");
            }));
            ServiceButton(p, "social-group-channel", T("GROUP", "ГРУППА"), 732, 574, 120, () => { whisperTarget = null; RefreshSocial(); });
            root.RegisterCallback<AttachToPanelEvent>(_ => RefreshSocial()); return root;
        }
        void RefreshSocial()
        {
            if (Windows.Current != GameplayWindow.Contacts || Windows.View == null) return; var root = Windows.View;
            root.Q<Label>("social-status").text = socialStatus; var list = root.Q<ScrollView>("friend-list"); list.Clear();
            bool ready = !socialSending;
            root.Q<Button>("friend-add-name").SetEnabled(ready); root.Q<Button>("social-refresh").SetEnabled(ready);
            foreach (var f in social.Friends.Where(f => f != null))
            {
                var row = new VisualElement(); row.style.height = 69; row.style.flexShrink = 0; list.Add(row);
                FFWidgets.Label(row, "name", f.Name + (f.Blocked ? T(" [blocked]", " [заблокирован]") : f.State != 0 ? T(" [online]", " [в сети]") : ""), 0, 0, 425, 25, true, 13, false);
                ServiceButton(row, "friend-chat-" + f.Slot, T("CHAT", "ЧАТ"), 0, 29, 100, () => { whisperTarget = f; RefreshSocial(); }).SetEnabled(ready && !f.Blocked);
                ServiceButton(row, "friend-block-" + f.Slot, T("BLOCK", "БЛОК"), 108, 29, 100, () => _ = SocialSend(() => socialClient.RemoveFriendAsync(f.Slot, f.Uid, true, socialToken))).SetEnabled(ready && !f.Blocked);
                ServiceButton(row, "friend-remove-" + f.Slot, f.Blocked ? T("UNBLOCK", "РАЗБЛОК.") : T("REMOVE", "УДАЛИТЬ"), 216, 29, 120, () => _ = SocialSend(() => socialClient.RemoveFriendAsync(f.Slot, f.Uid, false, socialToken))).SetEnabled(ready);
            }
            foreach (var peer in (socialPeers?.Invoke() ?? Enumerable.Empty<WorldEntity>()).Where(e => e.Kind == WorldEntityKind.Player && e.Character != null && !e.Hidden).Take(20))
            {
                var row = new VisualElement(); row.style.height = 65; row.style.flexShrink = 0; list.Add(row);
                FFWidgets.Label(row, "name", T("Nearby: ", "Рядом: ") + peer.Character.DisplayName, 0, 0, 425, 25, true, 13, false);
                ServiceButton(row, "friend-nearby-" + peer.Id, T("ADD FRIEND", "В ДРУЗЬЯ"), 0, 28, 150, () => _ = SocialSend(() => socialClient.RequestFriendAsync(peer.Id, peer.Character.Uid, socialToken))).SetEnabled(ready);
                ServiceButton(row, "group-invite-" + peer.Id, T("INVITE GROUP", "В ГРУППУ"), 158, 28, 180, () => _ = SocialSend(() => socialClient.InviteGroupAsync(peer.Id, socialToken))).SetEnabled(ready);
            }
            var group = root.Q<ScrollView>("group-list"); group.Clear();
            foreach (var m in social.Members)
            { var label = new Label($"{m.Name}   Lv {m.Level}   HP {m.Hp}/{m.MaxHp}") { name = "group-member-" + m.Id }; FFWidgets.Font(label, true, 14); group.Add(label); }
            foreach (var npc in social.Npcs)
            {
                var row = new VisualElement(); row.style.height = 55; row.style.flexShrink = 0; group.Add(row);
                FFWidgets.Label(row, "group-npc-" + npc.Id, (FFOne.World.WorldNpcCatalog.Load().Find(npc.Type)?.Name ?? npc.Type.ToString()) + $"   HP {npc.Hp}", 0, 0, 300, 24, true, 13, false);
                ServiceButton(row, "group-npc-kick-" + npc.Id, T("REMOVE NPC", "УБРАТЬ NPC"), 0, 25, 170, () => _ = SocialSend(() => socialClient.KickNpcAsync(npc.Id, socialToken))).SetEnabled(!socialSending && !TravelBusy);
            }
            root.Q<Button>("group-leave").SetEnabled(social.Members.Count > 0 && !socialSending);
            if (whisperTarget != null) whisperTarget = social.Friends.FirstOrDefault(f => f?.Uid == whisperTarget.Uid && !f.Blocked);
            root.Q<Label>("social-channel").text = whisperTarget != null ? T("To friend: ", "Друг: ") + whisperTarget.Name : T("Group chat", "Чат группы");
            var history = root.Q<ScrollView>("social-history"); history.Clear();
            foreach (string text in socialHistory) { var label = new Label(text); FFWidgets.Font(label, true, 13); label.style.whiteSpace = WhiteSpace.Normal; history.Add(label); }
            root.Q<Button>("social-send").SetEnabled(!socialSending && (whisperTarget != null || social.Members.Count > 0));
        }
        void DisposeSocial() { socialClient = null; contactsButton?.RemoveFromHierarchy(); mailButton?.RemoveFromHierarchy(); }
    }
}
