using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string ChatArt = "ui/en/gameplay/chat/";
        static readonly string[] ChatEmoteIcons = { "hello", "goodbye", "yes", "no", "okay", "thanks", "happy", "sad", "angry", "scared",
            "love", "laugh", "taunt", "cheer", "clap", "flex", "dance", "chill-out", "extras" };
        VisualElement quickMenu;
        bool emoteMenu;
        internal Func<int, Task> RequestEmote { private get; set; }
        internal Func<string, int, Task> RequestMenuChat { private get; set; }

        void ToggleQuickChat(bool emotes)
        {
            if (gameGuide != null || interactionSuspended || prompt != null || session.Snapshot.IsTutorial || !session.Snapshot.IsActive) return;
            bool close = quickMenu != null && emoteMenu == emotes;
            CloseQuickChat();
            if (close) return;
            if (!Windows.IsOpen) Windows.Open(GameplayWindow.NanoCom);
            if (Windows.Current != GameplayWindow.NanoCom) return;
            emoteMenu = emotes;
            var chatWindow = FFView.RequireClass<VisualElement>(chatView, "ff-chat-window");
            quickMenu = Group(chatWindow, "quick-chat");
            quickMenu.AddToClassList("ff-quick-menu");
            quickMenu.EnableInClassList("ff-quick-menu--emotes", emotes);
            PaintQuickChat();
        }

        string QuickChatText(QuickChatItem item)
        {
            string key = emoteMenu ? $"ui.hud.chat.emote.{item.Id}" : $"ui.hud.chat.quick.{item.Parent}.{item.Id}";
            string text = LocalizationCatalog.Lookup(key, language);
            if (text == key) text = item.English;
            if (text.Contains("{location}")) text = text.Replace("{location}", minimap.PlaceName.text);
            if (text.Contains("{mission}"))
            {
                var state = session.Snapshot;
                var quest = state.RunningQuests.FirstOrDefault(q => WorldMissionCatalog.Load().Find(q.TaskId)?.MissionId == state.CurrentMissionId);
                text = text.Replace("{mission}", quest == null ? "" : GameplayUiCatalog.Load().Mission(quest.TaskId, language)?.Title ?? "");
            }
            return text;
        }

        void PaintQuickChat(int selected = -1)
        {
            var menu = quickMenu; if (menu == null) return;
            menu.Clear();
            var rows = emoteMenu ? QuickChatItem.Emotes : QuickChatItem.Messages;
            var top = rows.Where(item => item.Parent == 0).ToArray();
            void Column(QuickChatItem[] items, bool first, float bottom)
            {
                var column = Group(menu, first ? "quick-root" : "quick-submenu");
                column.AddToClassList("ff-quick-choice-column");
                column.EnableInClassList("ff-quick-choice-column--submenu", !first);
                if (!first) column.style.marginBottom = bottom;
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    string caption = QuickChatText(item);
                    bool open = item.Submenu && selected == item.Id;
                    var button = new Button(() => {
                        if (item.Submenu) { PaintQuickChat(open ? -1 : item.Id); return; }
                        _ = SubmitQuickChat(item, caption);
                    }) { name = $"quick-item-{item.Parent}-{item.Id}" };
                    button.AddToClassList("ff-button");
                    button.AddToClassList("ff-quick-choice");
                    button.EnableInClassList("ff-quick-choice--category", item.Submenu);
                    button.EnableInClassList("ff-quick-choice--open", open);
                    button.EnableInClassList("ff-quick-choice--emote", emoteMenu && first);
                    column.Add(button);
                    var label = new Label(caption) { name = "quick-label", pickingMode = PickingMode.Ignore, enableRichText = false };
                    label.AddToClassList("ff-label");
                    label.AddToClassList("ff-quick-choice-copy");
                    button.Add(label);
                    if (emoteMenu && first)
                    {
                        var icon = Texture(ChatArt + "emotes/" + ChatEmoteIcons[i]);
                        if (icon) { var img = Image(button, "quick-emote-icon", null, 3 + (26 - icon.width) / 2f, 6, icon.width, icon.height); img.image = icon; }
                    }
                    if (item.Submenu && !open)
                    {
                        var arrow = new Image { name = "quick-arrow", image = Texture(ChatArt + "next-arrow"),
                            scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                        arrow.AddToClassList("ff-quick-choice-arrow");
                        button.Add(arrow);
                    }
                }
            }
            Column(top, true, 0);
            if (selected < 0) return;
            var children = rows.Where(item => item.Parent == selected).ToArray();
            if (children.Length == 0) return;
            int row = Array.FindIndex(top, item => item.Id == selected);
            float bottom = Mathf.Max(0, (top.Length - children.Length) * 18 - row * (emoteMenu ? 18 : 9) - 18);
            Column(children, false, bottom);
        }

        async Task SubmitQuickChat(QuickChatItem item, string caption)
        {
            if (quickMenu == null || interactionSuspended || prompt != null || chatPending) return;
            bool emote = emoteMenu; CloseQuickChat();
            if (emote && session.Snapshot.VehicleMounted) { playSound?.Invoke("action_failure01"); return; }
            chatPending = true;
            try
            {
                if (emote) { if (RequestEmote != null) await RequestEmote(item.Emote); }
                else if (chatChannel == ChatChannel.Group && GroupChatAvailable) await socialClient.SendGroupMenuChatAsync(caption, socialToken);
                else if (chatChannel == ChatChannel.Friend && SelectedChatFriend is { } target) await socialClient.SendFriendMenuChatAsync(target.Slot, target.Uid, caption, socialToken);
                else if (chatChannel == ChatChannel.All && RequestMenuChat != null) await RequestMenuChat(caption, item.Emote);
            }
            catch (OperationCanceledException) when (socialToken.IsCancellationRequested) { }
            catch (Exception e) { if (!disposed) Error?.Invoke(e.Message); }
            finally { chatPending = false; if (!disposed) UpdateChatSend(); }
        }
        void CloseQuickChat() { quickMenu?.RemoveFromHierarchy(); quickMenu = null; }
    }
}
