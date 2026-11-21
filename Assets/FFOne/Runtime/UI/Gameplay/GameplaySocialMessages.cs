using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        sealed class Invitation
        {
            public string Key, Sender, Icon;
            public Func<bool,Task> Answer;
        }
        readonly Queue<Invitation> invitations=new();
        VisualElement messageRoot, messageCard, groupHud;
        Label invitationExpiration;
        Invitation currentInvitation;
        float invitationStart;
        int invitationSeconds;
        bool expandedInvitation, allowBuddyInvites=true;
        WorldEntity selectedPeer;

        internal void OpenPlayer(WorldEntity peer)
        {
            if(!SocialAvailable || peer?.Character==null || peer.Kind!=WorldEntityKind.Player || peer.Hidden)return;
            selectedPeer=peer;
            Windows.Open(GameplayWindow.Contacts);
        }

        VisualElement BuildPlayerMenu()
        {
            var peer = selectedPeer;
            var root = FFView.InstantiateClass(SocialViewAddress, "player-interaction");
            var panel = SocialRole<VisualElement>(root, "player-menu");
            var name = SocialRole<Label>(panel, "player-name");
            name.text = FFStyle.Caption(peer.Character.DisplayName);
            AutoFit(name);
            BindSocialButton(panel, "player-dismiss", "", () => Windows.Close());
            var buddy = social.Friends.FirstOrDefault(friend => friend?.Uid == peer.Character.Uid);
            BindAction("trade", () => OfferTrade(peer.Id));
            BindAction("group", () => SocialSend(() => socialClient.InviteGroupAsync(peer.Id, socialToken)))
                .SetEnabled(!social.Members.Any(member => member.Id == peer.Id));
            BindAction("buddy", () => SocialSend(() => socialClient.RequestFriendAsync(peer.Id, peer.Character.Uid, socialToken)))
                .SetEnabled(buddy == null);
            BindAction("ignore", () => SocialSend(() => buddy == null
                ? socialClient.BlockNearbyAsync(peer.Id, peer.Character.Uid, socialToken)
                : socialClient.RemoveFriendAsync(buddy.Slot, buddy.Uid, true, socialToken))).SetEnabled(buddy?.Blocked != true);
            BindAction("close", () => Task.CompletedTask);

            Button BindAction(string key, Func<Task> command) => BindSocialButton(panel, "player-" + key,
                FFOne.Content.LocalizationCatalog.Lookup("ui.player_menu." + key, language), () =>
                {
                    playSound?.Invoke("button");
                    Windows.Close();
                    if ((socialPeers?.Invoke() ?? Enumerable.Empty<WorldEntity>()).Any(entity => !entity.Hidden &&
                        entity.Kind == WorldEntityKind.Player && entity.Id == peer.Id && entity.Character?.Uid == peer.Character.Uid))
                        _ = command();
                });
            return root;
        }

        string InvitationText(string suffix) => FFOne.Content.LocalizationCatalog.Lookup(
            currentInvitation.Key + suffix, language).Replace("{name}", currentInvitation.Sender)
                .Replace("{seconds}", invitationSeconds.ToString());

        void QueueInvitation(bool group, string sender, Func<bool,Task> answer)
        {
            invitations.Enqueue(new Invitation {
                Key = group ? "ui.nanocom.group." : "ui.nanocom.buddy.", Sender = sender, Answer = answer,
                Icon = group ? "ui/en/gameplay/system/messicon_group" : "ui/en/gameplay/nanocom/messicon_buddy"
            });
            TickInvitations();
        }

        void TickInvitations()
        {
            ShowMissionLine();
            if(currentInvitation==null)
            {
                if(invitations.Count==0 || interactionSuspended || prompt!=null || TradeWindowActive || missionMessageVisible) return;
                currentInvitation=invitations.Dequeue(); invitationStart=Time.unscaledTime; expandedInvitation=false;
                invitationSeconds=20;
                messageRoot=Group(layers.Host(UiLayer.Notification),"nanocom-invitation");
                messageRoot.StretchToParentSize();
                messageCard=GameplayNanoComMessage.Create(messageRoot, InvitationText("compact_title"),
                    InvitationText("compact_body"), GameplayNanoComMessage.InvitationFrame, currentInvitation.Icon, ExpandInvitation);
                playSound?.Invoke("comm_slidein");
            }
            float age=Time.unscaledTime-invitationStart;
            if(age>20) { _=AnswerInvitation(false,false);return; }
            int remainingSeconds = Mathf.CeilToInt(20-age);
            if (remainingSeconds != invitationSeconds)
            {
                invitationSeconds = remainingSeconds;
                if (expandedInvitation) invitationExpiration.text = InvitationText("expiration");
                else ((NanoComMessageCard)messageCard).Body.text = InvitationText("compact_body");
            }
            if(expandedInvitation)
            {
                float w=parent.resolvedStyle.width,h=parent.resolvedStyle.height;
                if(!float.IsFinite(w) || !float.IsFinite(h))return;
                float scale=FrontendLayout.UiScale(h);
                ScaledRect(messageCard,new Rect((w-520)/2,(h-164)/2,520,164),scale,new Vector2(w/2,h/2));
            }
            else GameplayNanoComMessage.Place(messageCard, parent, age);
        }

        void ExpandInvitation()
        {
            if (currentInvitation == null || interactionSuspended || expandedInvitation || gameGuide != null || buddyEntry != null || prompt != null) return;
            const string view = "ui/views/Retrobution/NanoCom";
            var dialog = FFView.InstantiateClass(view, "nanocom-invitation-dialog");
            var card = FFView.RequireClass<VisualElement>(dialog, "nanocom-invitation-panel");
            FFView.RequireClass<Image>(card, "nanocom-invitation-icon").image = Texture(currentInvitation.Icon);
            var title = FFView.RequireClass<Label>(card, "nanocom-invitation-title");
            title.text = FFStyle.Caption(InvitationText("expanded_title"));
            AutoFit(title);
            FFView.RequireClass<Label>(card, "nanocom-invitation-body").text = InvitationText("invitation");
            invitationExpiration = FFView.RequireClass<Label>(card, "nanocom-invitation-expiration");
            invitationExpiration.text = InvitationText("expiration");
            var decline = FFView.RequireClass<Button>(card, "nanocom-invitation-decline");
            var accept = FFView.RequireClass<Button>(card, "nanocom-invitation-accept");
            decline.text = T("DECLINE", "ОТКЛОНИТЬ");
            accept.text = T("ACCEPT", "ПРИНЯТЬ");
            decline.clicked += () => _ = AnswerInvitation(false, true);
            accept.clicked += () => _ = AnswerInvitation(true, true);
            AutoFit(decline);
            AutoFit(accept);
            messageRoot.RemoveFromHierarchy();
            messageRoot = dialog;
            messageCard = card;
            expandedInvitation = true;
            layers.Add(dialog, UiLayer.Dialog);
            decline.Focus();
            InputGateChanged?.Invoke(); TickInvitations();
        }

        async Task AnswerInvitation(bool accepted,bool clicked)
        {
            var request=currentInvitation;
            if(request==null)return;
            currentInvitation=null; expandedInvitation=false; messageRoot.RemoveFromHierarchy(); messageRoot=null;
            invitationExpiration = null;
            if(clicked)playSound?.Invoke(accepted?"yes_button":"no_button");
            playSound?.Invoke("comm_slideout"); InputGateChanged?.Invoke();
            await SendInvitationAnswer(request.Answer, accepted);
        }

        async Task SendInvitationAnswer(Func<bool, Task> answer, bool accepted)
        {
            try { await answer(accepted); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) Error?.Invoke(error.Message); }
        }

        void OnInvitationKey(KeyDownEvent evt)
        {
            if (currentInvitation == null || expandedInvitation || interactionSuspended || gameGuide != null || buddyEntry != null || prompt != null) return;
            if (evt.target is VisualElement element && (element is TextField || element.GetFirstAncestorOfType<TextField>() != null)) return;
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            ExpandInvitation();
            evt.StopImmediatePropagation();
        }

        void RefreshGroupHud()
        {
            if (social.Members.Count == 0 && social.Npcs.Count == 0)
            {
                groupHud?.RemoveFromHierarchy();
                groupHud = null;
                return;
            }
            if (groupHud == null)
            {
                groupHud = FFView.InstantiateClass(SocialViewAddress, "party-hud");
                Hud.View.Add(groupHud);
            }
            groupHud.Clear();

            int rows = 0;
            foreach (var member in social.Members.Where(member => member.Id != session.Snapshot.PlayerId).Take(3))
            {
                var row = CreateSocialRow<VisualElement>("party-member-row");
                SocialRole<Label>(row, "party-name").text = member.Name;
                SocialRole<Label>(row, "party-level").text = member.Level.ToString();
                SocialRole<VisualElement>(row, "party-member-health").style.width =
                    186 * Mathf.Clamp01((float)member.Hp / Mathf.Max(1, member.MaxHp));
                var nano = SocialRole<VisualElement>(row, "party-nano");
                nano.EnableInClassList("social-hidden", member.NanoId <= 0);
                if (member.NanoId > 0)
                {
                    var catalog = GameplayUiCatalog.Load();
                    SocialRole<Label>(nano, "party-nano-name").text = catalog.Nano(member.NanoId, language)?.Name ?? "";
                    SocialRole<VisualElement>(nano, "party-nano-health").style.width = 109 * Mathf.Clamp01(member.NanoStamina / 150f);
                    SocialRole<Image>(nano, "party-nano-skill").image = Texture(catalog.SkillIcon(member.NanoSkill));
                }
                groupHud.Add(row);
                rows++;
            }
            var npc = social.Npcs.FirstOrDefault();
            if (npc == null) return;
            for (; rows < social.Members.Count; rows++)
                groupHud.Add(CreateSocialRow<VisualElement>("party-gap"));
            var definition = WorldNpcCatalog.Load().Find(npc.Type);
            var npcRow = CreateSocialRow<VisualElement>("party-npc-row");
            SocialRole<Label>(npcRow, "party-npc-name").text = definition?.Name ?? "";
            SocialRole<VisualElement>(npcRow, "party-npc-hp").style.width =
                180 * Mathf.Clamp01((float)npc.Hp / Mathf.Max(1, definition?.MaxHp ?? npc.Hp));
            groupHud.Add(npcRow);
        }
    }
}
