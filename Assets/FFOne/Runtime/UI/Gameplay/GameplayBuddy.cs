using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        VisualElement buddyPanel, buddyEntry;
        ScrollView buddyList;
        Button buddyWarp, buddyDelete, buddyAdd;
        long warpBuddyUid;
        float nextBuddyWarp, buddyWarpDeadline;
        bool BuddyVisible => Windows.Current == GameplayWindow.NanoCom && chatChannel == ChatChannel.Friend &&
            !interactionSuspended && !session.Snapshot.IsTutorial && gameGuide == null;
        string BuddyText(string key) => LocalizationCatalog.Lookup("ui.buddy." + key, language);

        void BuildBuddy(VisualElement root)
        {
            buddyPanel = FFView.InstantiateClass(SocialViewAddress, "buddy-list-window");
            root.Add(buddyPanel);
            SocialRole<Label>(buddyPanel, "buddy-heading").text = FFStyle.Caption(BuddyText("title"));
            buddyList = SocialRole<ScrollView>(buddyPanel, "buddy-rows");
            buddyDelete = BindSocialButton(buddyPanel, "buddy-delete", T("DELETE", "УДАЛИТЬ"), () => {
                var buddy = SelectedChatFriend; if (buddy == null || !SocialAvailable) return;
                ShowPrompt(T("DELETE BUDDY?", "УДАЛИТЬ ДРУГА?"), buddy.Name, null,
                    _ => SelectedChatFriend?.Uid == buddy.Uid ? SocialSend(() => socialClient.RemoveFriendAsync(buddy.Slot, buddy.Uid, false, socialToken)) : Task.CompletedTask,
                    null, "ui/en/gameplay/chat/icons/buddy");
            });
            buddyWarp = BindSocialButton(buddyPanel, "buddy-warp", BuddyText("warp"), () => {
                var buddy = SelectedChatFriend;
                if (buddy == null || buddy.State == 0 || !SocialAvailable || warpBuddyUid != 0 || Time.unscaledTime < nextBuddyWarp) return;
                ShowPrompt(T("WARP TO BUDDY?", "ПЕРЕМЕСТИТЬСЯ К ДРУГУ?"), buddy.Name, null, async _ => {
                    if (!SocialAvailable || SelectedChatFriend?.Uid != buddy.Uid || SelectedChatFriend.State == 0 || warpBuddyUid != 0) return;
                    warpBuddyUid = buddy.Uid; buddyWarpDeadline = Time.unscaledTime + 12;
                    try { await socialClient.WarpToBuddyAsync(buddy.Slot, buddy.Uid, socialToken); }
                    catch { warpBuddyUid = 0; throw; }
                    RefreshBuddy();
                }, null, "ui/en/gameplay/chat/icons/buddy");
            });
            buddyAdd = BindSocialButton(buddyPanel, "buddy-add", BuddyText("add"), OpenBuddyEntry);
            SocialRole<VisualElement>(root, "ff-chat-window").RegisterCallback<GeometryChangedEvent>(_ => PlaceChat());
        }

        void RefreshBuddy()
        {
            if (buddyPanel == null) return;
            buddyPanel.EnableInClassList("social-hidden", !BuddyVisible);
            if (!BuddyVisible) return;
            buddyList.Clear();
            foreach (var buddy in social.Friends.Where(f => f != null && !f.Blocked))
            {
                var row = CreateSocialRow<Button>("social-buddy-row");
                row.name = "buddy-" + buddy.Slot;
                row.text = buddy.Name;
                row.EnableInClassList("social-buddy-row--selected", buddy.Uid == whisperTarget?.Uid);
                row.EnableInClassList("social-buddy-row--online", buddy.State != 0);
                row.clicked += () => { whisperTarget = buddy; playSound?.Invoke("button"); RefreshBuddy(); UpdateChatSend(); };
                buddyList.Add(row);
            }
            var selected = SelectedChatFriend;
            buddyDelete.SetEnabled(selected != null && SocialAvailable && !socialSending);
            buddyWarp.SetEnabled(selected != null && selected.State != 0 && SocialAvailable && warpBuddyUid == 0 && Time.unscaledTime >= nextBuddyWarp);
            buddyAdd.SetEnabled(!socialSending && social.Friends.Count(f => f != null) < 50);
            PlaceChat();
        }

        void OpenBuddyEntry()
        {
            if (buddyEntry != null || !BuddyVisible || prompt != null) return;
            playSound?.Invoke("button");
            buddyEntry = FFView.InstantiateClass(SocialViewAddress, "buddy-add-dialog");
            var panel = SocialRole<VisualElement>(buddyEntry, "buddy-add-window");
            layers.Add(buddyEntry, UiLayer.Dialog);
            buddyEntry.RegisterCallback<GeometryChangedEvent>(_ => {
                if (buddyEntry == null) return;
                float w = buddyEntry.resolvedStyle.width, h = buddyEntry.resolvedStyle.height;
                ScaledRect(panel, new Rect((w - 526) / 2, (h - 164) / 2, 526, 164), RetrobutionLayout.Scale(h), new Vector2(w / 2, h / 2));
            });
            SocialRole<Label>(panel, "buddy-add-title").text = FFStyle.Caption(BuddyText("add_title"));
            var instruction = SocialRole<Label>(panel, "buddy-add-instruction");
            instruction.text = BuddyText("add_instruction");
            var field = SocialRole<TextField>(panel, "buddy-full-name");
            BindSocialButton(panel, "buddy-add-cancel", T("CANCEL", "ОТМЕНА"),
                () => { playSound?.Invoke("no_button"); CloseBuddyEntry(); });
            void Submit()
            {
                if (socialClient == null || !SocialAvailable || socialSending || buddyEntry == null) return;
                var parts = field.value.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || parts[0].Length > 8 || parts[1].Trim().Length == 0 || parts[1].Trim().Length > 16) {
                    instruction.text = BuddyText("add_name_error"); field.Focus(); return;
                }
                CloseBuddyEntry(); playSound?.Invoke("yes_button");
                _ = SocialSend(() => socialClient.RequestFriendByNameAsync(parts[0], parts[1].Trim(), socialToken));
            }
            var submit = BindSocialButton(panel, "buddy-add-submit", BuddyText("add"), Submit);
            submit.SetEnabled(socialClient != null);
            field.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Submit(); e.StopPropagation(); } });
            chatView.SetEnabled(false); Windows.View.SetEnabled(false); field.Focus();
        }

        void CloseBuddyEntry()
        {
            if (buddyEntry == null) return;
            buddyEntry.RemoveFromHierarchy(); buddyEntry = null;
            bool enabled = !interactionSuspended && gameGuide == null;
            chatView?.SetEnabled(enabled); Windows.View?.SetEnabled(enabled);
            if (enabled && BuddyVisible) buddyAdd.Focus();
        }

        bool ReceiveBuddyWarp(ProtocolFrame frame)
        {
            if (frame.PacketType != 0x310000c9 && frame.PacketType != 0x310000db && frame.PacketType != 0x31000101) return false;
            bool local = frame.PacketType == 0x31000101;
            Protocol0104.RequireSize(frame.Payload, local ? 1 : frame.PacketType == 0x310000db ? 16 : 12);
            if (warpBuddyUid == 0 || !local && Protocol0104.Read64(frame.Payload, 0) != warpBuddyUid) return true;
            warpBuddyUid = 0;
            if (local) { nextBuddyWarp = Time.unscaledTime + 60; Windows.Close(); }
            else Error?.Invoke(frame.PacketType == 0x310000db ? T("Your buddy is on a different shard.", "Ваш друг находится на другом сервере.") :
                T("Unable to warp to this buddy.", "Не удалось переместиться к другу."));
            RefreshBuddy(); return true;
        }
    }
}
