using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        IReadOnlyList<MailSummary> mailSummaries = Array.Empty<MailSummary>();
        MailUpdate mailDetail;
        int mailPage = 1, mailUnread;
        long mailSelected;
        uint mailPending;
        float mailDeadline;
        bool mailFaulted, mailCompose, mailGuideFolder = true;
        string mailStatus = "";
        readonly List<MailAttachment> mailAttachments = new();
        public bool MailBusy => mailPending != 0 || mailFaulted;
        public int UnreadMail => mailUnread;
        public void OpenMail()
        {
            if (!SocialAvailable) return;
            mailCompose = false; Windows.Open(GameplayWindow.Mail);
            if (!mailGuideFolder) _ = LoadMailPage(mailPage);
        }
        Task LoadMailPage(int page)
        { mailPage = page; mailDetail = null; mailSelected = 0; return BeginMail(ProtocolMail.ListOk, () => socialClient.ListMailAsync(page, socialToken)); }
        async Task BeginMail(uint expected, Func<Task> action)
        {
            if (MailBusy || TravelBusy || disposed || inventoryPending || TradeWindowActive || ServiceActive) return;
            mailPending = expected; mailDeadline = Time.unscaledTime + 12; mailStatus = T("Waiting for server…", "Ожидание сервера…"); RefreshMail();
            try { await action(); }
            catch (OperationCanceledException) { if (!disposed) MailFault(T("Connection closed.", "Соединение закрыто.")); }
            catch (Exception error) { if (!disposed) MailFault(error.Message); }
        }
        void MailFault(string message)
        {
            mailFaulted = true; mailPending = 0;
            mailStatus = message + T(" Re-enter the world to synchronize.", " Войдите в мир заново для синхронизации."); RefreshMail(); Error?.Invoke(mailStatus);
        }
        void TickMail()
        { if (mailPending != 0 && Time.unscaledTime >= mailDeadline) MailFault(T("Mail action was not acknowledged.", "Нет подтверждения почтовой операции.")); }
        bool ReceiveMail(ProtocolFrame frame)
        {
            if (!ProtocolMail.TryDecode(frame, out var u)) return false;
            if (u.Type == ProtocolMail.New) { mailUnread = u.Unread; return true; }
            if (u.Type == ProtocolMail.ListOk) mailSummaries = u.Summaries;
            if (u.Type == ProtocolMail.ReadOk)
            {
                mailDetail = u;
                mailSummaries = mailSummaries.Select(m => m.Index != u.Index ? m : new MailSummary { Index = m.Index, SenderUid = m.SenderUid,
                    Sender = m.Sender, Subject = m.Subject, Read = true, HasAttachments = u.Taros > 0 || u.Items.Any(i => i.ItemId > 0) }).ToArray();
            }
            if (mailPending != 0 && !mailFaulted && (u.Type == mailPending || u.Type == mailPending + 1))
            {
                uint expected = mailPending; mailPending = 0;
                mailStatus = u.Failed ? u.Error == 9 ? T("Attachments cannot cross time periods.", "Нельзя отправлять вложения между эпохами.")
                    : T("Server rejected the mail action: ", "Сервер отклонил операцию: ") + u.Error : "";
                if (!u.Failed)
                {
                    if (expected == ProtocolMail.SendOk)
                    { mailAttachments.Clear(); mailCompose = false; RebuildMail(); _ = LoadMailPage(mailPage); return true; }
                    if (expected == ProtocolMail.DeleteOk) { _ = LoadMailPage(mailPage); return true; }
                    // Follow-up read is ordered after the server's give-item post-states, and also
                    // reports remaining taros when a claim only partially fits the currency cap.
                    if (expected == ProtocolMail.ItemOk || expected == ProtocolMail.AllOk || expected == ProtocolMail.TarosOk)
                    { _ = BeginMail(ProtocolMail.ReadOk, () => socialClient.ReadMailAsync(mailSelected, socialToken)); return true; }
                    if (expected == ProtocolMail.ReadOk) _ = SocialSend(() => socialClient.CheckMailAsync(socialToken));
                }
            }
            RefreshMail(); return true;
        }
        void RebuildMail()
        { if (Windows.Current == GameplayWindow.Mail) { Windows.Close(); Windows.Open(GameplayWindow.Mail); } }
        VisualElement BuildMailView() => BuildMailPresentation();
        void RefreshMailCost()
        {
            var root = Windows.Current == GameplayWindow.Mail ? Windows.View : null; var label = root?.Q<Label>("mail-cost"); if (label == null) return;
            bool valid = int.TryParse(root.Q<TextField>("mail-taros").value, out int amount) && amount >= 0 && amount <= int.MaxValue - 130;
            label.text = valid ? T("Total incl. postage: ", "Всего с пересылкой: ") + ProtocolMail.Cost(amount, mailAttachments.Count) + " Taros" : T("Invalid Taros amount", "Неверная сумма Taros");
        }
        void RefreshMail()
        {
            if (Windows.Current != GameplayWindow.Mail || Windows.View == null) return; var root = Windows.View; bool ready = !MailBusy;
            root.Q<Label>("mail-status").text = mailStatus;
            foreach (var b in root.Query<Button>().ToList())
                if (b.name.StartsWith("mail-") && b.name != "mail-close") b.SetEnabled(ready);
            root.Q<Button>("mail-refresh").SetEnabled(ready && !mailGuideFolder);
            root.Q<Button>("mail-guide-tab").EnableInClassList("mail-tab-selected", mailGuideFolder);
            root.Q<Button>("mail-player-tab").EnableInClassList("mail-tab-selected", !mailGuideFolder);
            RefreshMailTabArt(root);
            RefreshMailInventory(root, ready);
            root.Query<TextField>().ForEach(field => field.SetEnabled(ready));
            root.Query<DropdownField>().ForEach(field => field.SetEnabled(ready));
            RefreshMailRows(root, ready);
            if (mailCompose)
            {
                root.Q<Button>("mail-delete").SetEnabled(false);
                var items = root.Q<ScrollView>("mail-compose-items"); items.Clear();
                foreach (var item in session.Snapshot.Inventory.Where(i => !i.IsEmpty))
                {
                    int slot = item.Slot; bool selected = mailAttachments.Any(a => a.InventorySlot == slot);
                    string name = GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.Name ?? $"{item.ItemType}:{item.ItemId}";
                    var b = new Button(() => SelectMailAttachment(item)) { name = "mail-inventory-" + slot, text = (selected ? "✓ " : "") + name + " ×" + item.Quantity };
                    b.style.height = 29; b.style.flexShrink = 0; b.style.backgroundColor = new Color(.025f, .14f, .21f); b.style.color = Color.white;
                    FFWidgets.Font(b, true, 12); b.SetEnabled(ready); items.Add(b);
                }
                RefreshMailCost(); return;
            }
            var guideMessage = mailGuideFolder ? guideMails.FirstOrDefault(mail => mail.Index == mailSelected) : null;
            bool playerRead = !mailGuideFolder && mailDetail != null && mailDetail.Index == mailSelected;
            bool read = guideMessage != null || playerRead;
            var selectedMessage = mailSummaries.FirstOrDefault(m => m.Index == mailSelected);
            root.Q<Label>("mail-detail-from").text = guideMessage?.Sender ?? selectedMessage?.Sender ?? "";
            root.Q<Label>("mail-detail-subject").text = guideMessage?.Subject ?? selectedMessage?.Subject ?? "";
            root.Q("mail-message").style.display = read ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("mail-no-selection").style.display = read ? DisplayStyle.None : DisplayStyle.Flex;
            bool hasItems = playerRead && mailDetail.Items.Any(item => item.ItemId > 0);
            root.Q("mail-read-body").style.height = hasItems ? 117 : 215;
            root.Q("mail-paper-art").style.height = hasItems ? 140 : 238;
            root.Q("mail-attachments").style.display = hasItems ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("mail-claims").style.display = playerRead && (hasItems || mailDetail.Taros > 0) ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q("mail-guide-note").style.display = guideMessage != null ? DisplayStyle.Flex : DisplayStyle.None;

            var portrait = root.Q<Image>("mail-sender-portrait");
            portrait.image = guideMessage == null ? null : FFWidgets.Texture(GameplayUiCatalog.Load().NpcPortrait(guideMessage.Npc));
            portrait.style.display = guideMessage != null && portrait.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            // Player mail has no NPC portrait in Panel_EmailList.OnSub.
            FFWidgets.Rect(root.Q("mail-from-caption"), mailGuideFolder ? 89 : 10, 20, 60, 20);
            FFWidgets.Rect(root.Q("mail-subject-caption"), mailGuideFolder ? 89 : 10, 45, 80, 20);
            FFWidgets.Rect(root.Q("mail-detail-from"), mailGuideFolder ? 149 : 70, 17, 220, 20);
            FFWidgets.Rect(root.Q("mail-detail-subject"), mailGuideFolder ? 174 : 95, 42, 350, 20);

            root.Q<Label>("mail-detail-body").text = guideMessage?.Body ?? (playerRead ? mailDetail.Body : "");
            root.Q<Label>("mail-detail-body").style.color = new Color(.1f, .1f, .1f);
            for (int i = 0; i < 4; i++)
            {
                var b = root.Q<Button>("mail-attachment-" + (i + 1)); var item = playerRead ? mailDetail.Items[i] : default;
                b.text = item.ItemId == 0 ? "—" : (GameplayUiCatalog.Load().Item(item.ItemType, item.ItemId, language)?.Name ?? $"{item.ItemType}:{item.ItemId}") + " ×" + (item.Option & 0xffff);
                b.SetEnabled(ready && playerRead && item.ItemId > 0 && session.Snapshot.Inventory.Any(v => v.IsEmpty));
            }
            root.Q<Button>("mail-take-all").SetEnabled(ready && playerRead && mailDetail.Items.Any(i => i.ItemId > 0) && session.Snapshot.Inventory.Count(i => i.IsEmpty) >= mailDetail.Items.Count(i => i.ItemId > 0));
            root.Q<Button>("mail-take-taros").text = T("TAKE ", "ЗАБРАТЬ ") + (playerRead ? mailDetail.Taros : 0) + " Taros";
            root.Q<Button>("mail-take-taros").SetEnabled(ready && playerRead && mailDetail.Taros > 0); root.Q<Button>("mail-delete").SetEnabled(ready && playerRead);
        }
        void SelectMailAttachment(GameplayItemState item)
        {
            if (MailBusy) return; int existing = mailAttachments.FindIndex(a => a.InventorySlot == item.Slot);
            if (existing >= 0) { mailAttachments.RemoveAt(existing); RefreshMail(); return; }
            if (mailAttachments.Count >= 4) return;
            ShowPrompt(T("ATTACH ITEM", "ПРИКРЕПИТЬ ПРЕДМЕТ"), T("Quantity (up to ", "Количество (до ") + item.Quantity + ")", "1", value => {
                if (!int.TryParse(value, out int count) || count < 1 || count > item.Quantity) throw new ArgumentException(T("Invalid quantity.", "Неверное количество."));
                mailAttachments.Add(new MailAttachment(item.Slot, new EquipmentItem { ItemId = item.ItemId, ItemType = item.ItemType, Option = (item.Option & unchecked((int)0xffff0000)) | count, TimeLimit = item.TimeLimit })); RefreshMail(); return Task.CompletedTask;
            }, null);
        }
        void TakeMailAttachment(int slot)
        {
            int destination = session.Snapshot.Inventory.FirstOrDefault(i => i.IsEmpty).Slot;
            if (!session.Snapshot.Inventory.Any(i => i.IsEmpty)) return;
            _ = BeginMail(ProtocolMail.ItemOk, () => socialClient.TakeMailItemAsync(mailSelected, destination, slot, socialToken));
        }
        void PromptSendMail()
        {
            if (MailBusy) return; var root = Windows.View;
            if (!long.TryParse(root.Q<TextField>("mail-recipient").value, out long uid) || uid <= 0 || uid == session.Snapshot.CharacterUid ||
                !int.TryParse(root.Q<TextField>("mail-taros").value, out int amount) || amount < 0 || amount > int.MaxValue - 130) { mailStatus = T("Invalid recipient or amount.", "Неверный получатель или сумма."); RefreshMail(); return; }
            int cost = ProtocolMail.Cost(amount, mailAttachments.Count);
            if (cost > session.Snapshot.Taros) { mailStatus = T("Not enough Taros for postage and attachments.", "Недостаточно Taros для вложений и пересылки."); RefreshMail(); return; }
            var items = mailAttachments.ToArray();
            foreach (var a in items)
            {
                var real = session.Snapshot.Inventory[a.InventorySlot];
                if (real.ItemId != a.Item.ItemId || real.ItemType != a.Item.ItemType || real.Quantity < (a.Item.Option & 0xffff)) { mailAttachments.Clear(); mailStatus = T("Inventory changed. Select attachments again.", "Инвентарь изменился. Выберите вложения заново."); RefreshMail(); return; }
            }
            string subject = root.Q<TextField>("mail-subject").value, body = root.Q<TextField>("mail-body").value;
            ShowPrompt(T("SEND MAIL", "ОТПРАВИТЬ ПИСЬМО"), cost + T(" Taros including postage?", " Taros с учётом пересылки?"), null,
                _ => BeginMail(ProtocolMail.SendOk, () => socialClient.SendMailAsync(uid, subject, body, amount, items, socialToken)), null);
        }
    }
}
