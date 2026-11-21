using System;
using System.Globalization;
using System.Linq;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class GameplayUiController
    {
        const string MailViewAddress = "ui/views/Retrobution/Inventory";

        static TElement MailRole<TElement>(VisualElement root, string role) where TElement : VisualElement =>
            FFView.RequireClass<TElement>(root, role);

        static void SetMailVisibility(VisualElement element, bool visible) =>
            element.EnableInClassList("mail-hidden", !visible);

        VisualElement BuildMailPresentation()
        {
            var root = FFView.InstantiateClass(MailViewAddress, "inventory-screen");
            BindInventory(root, false, () => Windows.Close(), true);
            root.name = "mail-root";
            root.AddToClassList("mail-screen");
            var panel = FFView.RequireClass<VisualElement>(root, "inventory-window");
            panel.name = "mail-window";
            var cabinet = FFView.RequireClass<VisualElement>(panel, "inventory-stuff");
            var cabinetBackground = FFView.RequireClass<VisualElement>(panel, "inventory-background");
            foreach (var child in panel.Children().ToArray())
                if (child != cabinetBackground && child != cabinet) child.RemoveFromHierarchy();
            FFView.RequireClass<Button>(root, "inventory-close").name = "mail-close";
            FFView.RequireClass<Button>(root, "inventory-trash").RemoveFromHierarchy();
            FFView.RequireClass<Button>(root, "inventory-nanos-tab").AddToClassList("mail-hidden");
            FFView.RequireClass<Button>(root, "inventory-equipment-tab").SetEnabled(false);
            FFView.RequireClass<Button>(root, "inventory-hammer").AddToClassList("mail-hidden");

            var layout = FFView.InstantiateClass(MailViewAddress, "mail-layout-template");
            layout.EnableInClassList("mail-inbox--guide", mailGuideFolder);
            layout.EnableInClassList("mail-inbox--player", !mailGuideFolder);
            layout.EnableInClassList("mail-inbox--compose", mailCompose);
            layout.EnableInClassList("mail-inbox--read", !mailCompose);
            panel.Add(layout);
            void Text(string role, string caption) => MailRole<Label>(layout, role).text = caption;
            void Button(string role, string caption, Action action) => BindMailButton(layout, role, caption, action);

            Button("mail-compose", mailCompose ? T("INBOX", "ВХОДЯЩИЕ") : T("SEND MAIL", "НАПИСАТЬ"), () =>
            {
                if (MailBusy) return;
                mailCompose = !mailCompose;
                mailAttachments.Clear();
                RebuildMail();
            });
            Button("mail-refresh", "↻", () => _ = LoadMailPage(mailPage));
            MailRole<Button>(layout, "mail-refresh").tooltip = T("Refresh inbox", "Обновить входящие");
            Button("mail-guide-tab", T("GUIDE EMAIL", "ПИСЬМА ГИДА"), () => SelectMailFolder(true));
            Button("mail-player-tab", T("PLAYER EMAIL", "ПИСЬМА ИГРОКОВ"), () => SelectMailFolder(false));
            var guideTab = MailRole<Button>(layout, "mail-guide-tab");
            var playerTab = MailRole<Button>(layout, "mail-player-tab");
            var rim = MailRole<VisualElement>(layout, "mail-table-art");
            void PlaceGuideTab(bool highlighted)
            {
                if (mailGuideFolder || highlighted) guideTab.PlaceInFront(rim);
                else guideTab.PlaceBehind(playerTab);
            }
            bool pointerOverGuide = false;
            guideTab.RegisterCallback<PointerEnterEvent>(_ => { pointerOverGuide = true; PlaceGuideTab(true); });
            guideTab.RegisterCallback<PointerLeaveEvent>(_ => { pointerOverGuide = false; PlaceGuideTab(guideTab.focusController?.focusedElement == guideTab); });
            guideTab.RegisterCallback<FocusInEvent>(_ => PlaceGuideTab(true));
            guideTab.RegisterCallback<FocusOutEvent>(_ => PlaceGuideTab(pointerOverGuide));
            PlaceGuideTab(false);
            Text("mail-heading-from", T("FROM", "ОТ"));
            Text("mail-heading-subject", T("SUBJECT", "ТЕМА"));
            Text("mail-heading-day", T("DAY", "ДЕНЬ"));
            Text("mail-empty-folder", T("There are no messages in this folder.", "В этой папке нет писем."));
            Button("mail-previous", T("PREVIOUS 5", "ПРЕДЫДУЩИЕ 5"), () => ChangeMailPage(-1));
            Button("mail-next", T("NEXT 5", "СЛЕДУЮЩИЕ 5"), () => ChangeMailPage(1));
            Text("mail-no-selection", T("There is no message selected.", "Письмо не выбрано."));
            Text("mail-from-caption", T("FROM:", "ОТ:"));
            Text("mail-subject-caption", T("SUBJECT:", "ТЕМА:"));
            Text("mail-guide-note", T("Note: Guide emails are automatically deleted after the related mission is complete.", "Письма гида удаляются после завершения связанного задания."));
            Text("mail-status", mailStatus);

            var attachments = MailRole<VisualElement>(layout, "mail-attachments");
            for (int i = 1; i <= 4; i++)
            {
                int slot = i;
                var attachment = (Button)FFView.InstantiateClass(MailViewAddress, "mail-attach-template");
                attachment.name = "mail-attachment-" + slot;
                attachment.clicked += () => TakeMailAttachment(slot);
                attachments.Add(attachment);
                FFWidgets.AutoFit(attachment);
            }
            Button("mail-take-all", T("TAKE ALL ITEMS", "ЗАБРАТЬ ПРЕДМЕТЫ"),
                () => _ = BeginMail(ProtocolMail.AllOk, () => socialClient.TakeAllMailItemsAsync(mailSelected, socialToken)));
            Button("mail-take-taros", "",
                () => _ = BeginMail(ProtocolMail.TarosOk, () => socialClient.TakeMailTarosAsync(mailSelected, socialToken)));

            var trash = (Button)FFView.InstantiateClass(MailViewAddress, "mail-delete");
            trash.clicked += () => ShowPrompt(T("DELETE MAIL", "УДАЛИТЬ ПИСЬМО"),
                T("Delete this message and any remaining attachments?", "Удалить письмо вместе с оставшимися вложениями?"), null,
                _ => BeginMail(ProtocolMail.DeleteOk, () => socialClient.DeleteMailAsync(mailSelected, socialToken)), null);
            cabinet.Add(trash);
            if (mailCompose) BindMailComposer(layout);
            root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow());
            return root;
        }

        static void BindMailButton(VisualElement parent, string role, string caption, Action action)
        {
            var button = MailRole<Button>(parent, role);
            button.text = caption;
            button.clicked += action;
            FFWidgets.AutoFit(button);
        }

        void SelectMailFolder(bool guide)
        {
            if (MailBusy) return;
            mailGuideFolder = guide; guideMailPage = 0; mailSelected = 0; mailDetail = null; mailCompose = false; RebuildMail();
            if (!guide && SocialAvailable) _ = LoadMailPage(mailPage);
        }

        void BindMailComposer(VisualElement layout)
        {
            var composer = MailRole<VisualElement>(layout, "mail-form-template");
            var recipient = MailRole<TextField>(composer, "mail-recipient");
            recipient.label = T("To UID", "UID получателя");
            MailRole<TextField>(composer, "mail-subject").label = T("Subject", "Тема");
            var taros = MailRole<TextField>(composer, "mail-taros");
            taros.label = "Taros";
            taros.RegisterValueChangedCallback(_ => RefreshMailCost());

            var picker = MailRole<DropdownField>(composer, "mail-friend");
            var recipients = social.Friends.Where(friend => friend != null && !friend.Blocked).ToArray();
            SetMailVisibility(picker, recipients.Length > 0);
            if (recipients.Length > 0)
            {
                picker.label = T("Friend", "Друг");
                picker.choices = recipients.Select(friend => friend.Name).ToList();
                picker.index = 0;
                recipient.SetValueWithoutNotify(recipients[0].Uid.ToString(CultureInfo.InvariantCulture));
                picker.RegisterValueChangedCallback(_ =>
                {
                    if (picker.index >= 0) recipient.value = recipients[picker.index].Uid.ToString(CultureInfo.InvariantCulture);
                });
            }
            BindMailButton(composer, "mail-send", T("SEND", "ОТПРАВИТЬ"), PromptSendMail);
            BindMailButton(composer, "mail-clear-items", T("CLEAR ITEMS", "УБРАТЬ ВЛОЖЕНИЯ"), () =>
            {
                mailAttachments.Clear();
                RefreshMail();
            });
        }

        void RefreshMailRows(VisualElement root, bool ready)
        {
            if (socialClient == null) guideMails = BuildGuideMail();
            if (guideMailPage * 5 >= guideMails.Count) guideMailPage = Math.Max(0, (guideMails.Count - 1) / 5);
            if (mailGuideFolder && !guideMails.Any(mail => mail.Index == mailSelected)) mailSelected = guideMails.Skip(guideMailPage * 5).FirstOrDefault()?.Index ?? 0;
            var summaries = mailGuideFolder ? guideMails.Skip(guideMailPage * 5).Take(5).Select(mail =>
                new MailSummary { Index = mail.Index, Sender = mail.Sender, Subject = mail.Subject, Read = true }).ToArray() : mailSummaries.ToArray();
            int first = summaries.Length == 0 ? 0 : (mailGuideFolder ? guideMailPage : mailPage - 1) * 5 + 1;
            int last = summaries.Length == 0 ? 0 : first + summaries.Length - 1;
            MailRole<Label>(root, "mail-page").text = first + " - " + last + T("   of   ", "   из   ") + (mailGuideFolder ? guideMails.Count : mailPage) + T("   Emails", "   писем");
            MailRole<Button>(root, "mail-previous").SetEnabled(ready && (mailGuideFolder ? guideMailPage > 0 : mailPage > 1));
            MailRole<Button>(root, "mail-next").SetEnabled(ready && (mailGuideFolder ? (guideMailPage + 1) * 5 < guideMails.Count : mailPage < 127 && mailSummaries.Count == 5));
            SetMailVisibility(MailRole<VisualElement>(root, "mail-empty-folder"), !mailCompose && summaries.Length == 0);
            var list = MailRole<ScrollView>(root, "mail-list"); list.Clear();
            foreach (var summary in summaries)
            {
                var row = (Button)FFView.InstantiateClass(MailViewAddress, "mail-row-template");
                row.name = "mail-row-" + summary.Index;
                row.tooltip = summary.Subject;
                bool selected = summary.Index == mailSelected;
                row.EnableInClassList("mail-row-selected", selected);
                row.EnableInClassList("mail-row-unselected", !selected);
                row.EnableInClassList("mail-row-no-attachment", !summary.HasAttachments);
                var sender = MailRole<Label>(row, "mail-row-from");
                var subject = MailRole<Label>(row, "mail-row-subject");
                sender.text = summary.Sender;
                subject.text = summary.Subject?.Length > 24 ? summary.Subject.Substring(0, 21) + "..." : summary.Subject;
                FFWidgets.AutoFit(sender);
                FFWidgets.AutoFit(subject);
                row.clicked += () =>
                {
                    if (MailBusy || TravelBusy || mailCompose) return;
                    mailSelected = summary.Index;
                    mailDetail = null;
                    if (mailGuideFolder) { RefreshMail(); return; }
                    _ = BeginMail(ProtocolMail.ReadOk, () => socialClient.ReadMailAsync(summary.Index, socialToken));
                };
                row.SetEnabled(ready && !mailCompose);
                list.Add(row);
            }
        }

        void ChangeMailPage(int delta)
        {
            if (MailBusy) return;
            if (mailGuideFolder) { guideMailPage = Math.Max(0, guideMailPage + delta); mailSelected = 0; RefreshMail(); }
            else _ = LoadMailPage(mailPage + delta);
        }

        void RefreshMailInventory(VisualElement root, bool ready)
        {
            var catalog = GameplayUiCatalog.Load();
            foreach (var item in session.Snapshot.Inventory)
            {
                var slot = InventoryBagSlot(root, item.Slot);
                BindInventoryBagItem(root, item, catalog, false);
                slot.SetEnabled(ready && !item.IsEmpty);
                slot.EnableInClassList("mail-item-attached", mailAttachments.Any(a => a.InventorySlot == item.Slot));
                if (!slot.ClassListContains("mail-inventory-bound"))
                {
                    int index = item.Slot;
                    slot.clicked += () => { if (mailCompose) SelectMailAttachment(session.Snapshot.Inventory[index]); };
                }
                slot.AddToClassList("mail-inventory-bound");
            }
            RefreshInventoryPresentation(root);
        }
    }
}
