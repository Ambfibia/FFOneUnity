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
        Button MailButton(VisualElement parent, string name, string text, float x, float y, float width, Action action, string css = "mail-button")
        {
            var button = new Button(action) { name = name, text = text };
            FFWidgets.Rect(button, x, y, width, 25);
            foreach (string className in css.Split(' ')) button.AddToClassList(className);
            FFWidgets.Font(button, false, 12);
            MailClearBox(button);
            FFStyle.Button(button, "ui/en/gameplay/chat/blue_button_normal", "ui/en/gameplay/chat/blue_button_over");
            button.style.color = new Color(.8f, 1, 1);
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            parent.Add(button); return button;
        }

        static void MailClearBox(VisualElement element)
        {
            element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = 0;
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = 0;
            element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = 0;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = 0;
            element.style.backgroundColor = Color.clear;
        }

        static VisualElement MailSurface(VisualElement parent, string name, float x, float y, float width, float height,
            string art = null, Vector4? border = null)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            FFWidgets.Rect(element, x, y, width, height); MailClearBox(element);
            if (art != null) FFStyle.Background(element, "ui/en/email/" + art, border);
            parent.Add(element); return element;
        }

        Button MailTab(VisualElement parent, string name, string text, float x, float width, Action action)
        {
            var tab = new Button(action) { name = name, text = text };
            FFWidgets.Rect(tab, x, 5, width, 15); MailClearBox(tab); FFStyle.Background(tab, null);
            FFWidgets.Font(tab, false, 12); tab.style.color = new Color(.8f, 1, 1);
            tab.style.unityTextAlign = TextAnchor.MiddleLeft; parent.Add(tab); return tab;
        }

        void RefreshMailTabArt(VisualElement root)
        {
            var listArt = root.Q("mail-table-art"); var inactive = root.Q("mail-inactive-tab");
            var fill = root.Q("mail-tab-fill");
            FFWidgets.Rect(inactive, mailGuideFolder ? 146 : 0, 0, mailGuideFolder ? 209 : 181, mailGuideFolder ? 29 : 25);
            FFStyle.Background(inactive, "ui/en/email/" + (mailGuideFolder ? "player-tab" : "guide-tab"), Vector4.zero);
            fill.style.display = mailGuideFolder ? DisplayStyle.None : DisplayStyle.Flex;
            // DoListBut draws the inactive PLAYER before the list; the inactive GUIDE
            // and PLAYER silhouette are drawn after the list in the other folder.
            inactive.PlaceInFront(listArt);
            if (mailGuideFolder) inactive.PlaceBehind(listArt);
            root.Q<Button>("mail-guide-tab").style.color = mailGuideFolder ? Color.white : new Color(.8f, 1, 1);
            root.Q<Button>("mail-player-tab").style.color = !mailGuideFolder ? Color.white : new Color(.8f, 1, 1);
        }

        VisualElement BuildMailPresentation()
        {
            var root = FFView.Instantiate("ui/views/Retrobution/Inventory", "inventory-root");
            BindInventory(root, false, () => Windows.Close(), true);
            root.name = "mail-root"; root.AddToClassList("mail-screen");
            var panel = root.Q("inventory-window"); panel.name = "mail-window";
            foreach (var child in panel.Children().ToArray())
                if (child.name != "inventory-background" && child.name != "inventory-stuff") child.RemoveFromHierarchy();
            root.Q<Button>("close").name = "mail-close";
            root.Q<Button>("trash").RemoveFromHierarchy();
            root.Q<Button>("tab-nanos").style.display = DisplayStyle.None;
            root.Q<Button>("tab-equipment").SetEnabled(false);
            root.Q<Button>("hammer").style.display = DisplayStyle.None;
            MailSurface(panel, "mail-frame", 0, 0, 585, 653, "frame", new Vector4(5, 0, 5, 0));
            var email = MailSurface(panel, "mail-content", 8, 7.5f, 567, 634);
            MailButton(email, "mail-compose", mailCompose ? T("INBOX", "ВХОДЯЩИЕ") : T("SEND MAIL", "НАПИСАТЬ"), 390, 0, 160, () => {
                if (MailBusy) return; mailCompose = !mailCompose; mailAttachments.Clear(); RebuildMail();
            });
            var refresh = MailButton(email, "mail-refresh", "↻", 350, 0, 30, () => _ = LoadMailPage(mailPage));
            refresh.tooltip = T("Refresh inbox", "Обновить входящие"); refresh.style.display = DisplayStyle.None;
            var content = MailSurface(email, "mail-inner", 6, 3, 561, 631);
            var table = MailSurface(content, "mail-table", 0, 15, 561, 229);
            MailSurface(table, "mail-inactive-tab", 146, 0, 209, 29, "player-tab", Vector4.zero);
            MailSurface(table, "mail-table-art", 0, 0, 561, 229, "list", Vector4.zero);
            MailSurface(table, "mail-tab-fill", 0, 0, 343, 30, "inactive-tab-fill", Vector4.zero);
            MailTab(table, "mail-guide-tab", T("GUIDE EMAIL", "ПИСЬМА ГИДА"), 13, 130, () => SelectMailFolder(true));
            MailTab(table, "mail-player-tab", T("PLAYER EMAIL", "ПИСЬМА ИГРОКОВ"), 180, 135, () => SelectMailFolder(false));
            InventoryText(table, "mail-heading-from", T("FROM", "ОТ"), 1, 36, 203, 20, 12).style.unityTextAlign = TextAnchor.MiddleCenter;
            InventoryText(table, "mail-heading-subject", T("SUBJECT", "ТЕМА"), 204, 36, 219, 20, 12).style.unityTextAlign = TextAnchor.MiddleCenter;
            InventoryText(table, "mail-heading-day", T("DAY", "ДЕНЬ"), 421, 36, 100, 20, 12).style.unityTextAlign = TextAnchor.MiddleCenter;
            InventoryArt(table, "mail-heading-attachment", "ui/en/email/attachment", 530, 40, 19, 13);
            var list = new ScrollView { name = "mail-list", horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            FFWidgets.Rect(list, 1, 58, 559, 140); table.Add(list);
            InventoryText(table, "mail-empty-folder", T("There are no messages in this folder.", "В этой папке нет писем."), 0, 58, 560, 115, 12, true).style.unityTextAlign = TextAnchor.MiddleCenter;
            var previous = MailButton(table, "mail-previous", T("PREVIOUS 5", "ПРЕДЫДУЩИЕ 5"), 1, 202, 120, () => ChangeMailPage(-1));
            previous.style.height = 26; FFWidgets.Font(previous, false, 14);
            FFStyle.Button(previous, "ui/en/email/previous", "ui/en/email/previous-hover");
            var next = MailButton(table, "mail-next", T("NEXT 5", "СЛЕДУЮЩИЕ 5"), 440, 202, 120, () => ChangeMailPage(1));
            next.style.height = 26; FFWidgets.Font(next, false, 14);
            FFStyle.Button(next, "ui/en/email/next", "ui/en/email/next-hover");
            InventoryText(table, "mail-page", "", 131, 205, 297, 20, 12, true).style.unityTextAlign = TextAnchor.MiddleCenter;
            var detail = MailSurface(content, "mail-detail", 0, 248, 560, 382, "data-back", new Vector4(11, 11, 8, 11));
            InventoryText(detail, "mail-no-selection", T("There is no message selected.", "Письмо не выбрано."), 20, 102, 525, 215, 12, true).style.unityTextAlign = TextAnchor.MiddleCenter;
            var message = new VisualElement { name = "mail-message" }; message.StretchToParentSize(); detail.Add(message);
            InventoryText(message, "mail-from-caption", T("FROM:", "ОТ:"), 89, 20, 60, 20, 12).style.color = Color.yellow;
            InventoryText(message, "mail-subject-caption", T("SUBJECT:", "ТЕМА:"), 89, 45, 80, 20, 12).style.color = Color.yellow;
            InventoryText(message, "mail-detail-from", "", 149, 17, 220, 20, 12, true);
            InventoryText(message, "mail-detail-subject", "", 174, 42, 350, 20, 12, true);
            InventoryArt(message, "mail-sender-portrait", null, 10, 10, 79, 68);
            MailSurface(message, "mail-paper-art", 10, 92, 538, 238, "data-box", new Vector4(4, 4, 4, 4));
            var body = new ScrollView { name = "mail-read-body", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            FFWidgets.Rect(body, 20, 102, 525, 215); message.Add(body);
            var text = new Label { name = "mail-detail-body", enableRichText = false }; text.AddToClassList("mail-body-copy"); FFWidgets.Font(text, false, 12); body.Add(text);
            var attachments = new VisualElement { name = "mail-attachments" }; FFWidgets.Rect(attachments, 10, 292, 539, 25); message.Add(attachments);
            for (int i = 0; i < 4; i++) { int slot = i + 1; MailButton(attachments, "mail-attachment-" + slot, "", i * 135, 0, 132, () => TakeMailAttachment(slot)); }
            var claims = new VisualElement { name = "mail-claims" }; FFWidgets.Rect(claims, 10, 334, 539, 25); message.Add(claims);
            MailButton(claims, "mail-take-all", T("TAKE ALL ITEMS", "ЗАБРАТЬ ПРЕДМЕТЫ"), 0, 0, 260, () => _ = BeginMail(ProtocolMail.AllOk, () => socialClient.TakeAllMailItemsAsync(mailSelected, socialToken)));
            MailButton(claims, "mail-take-taros", "", 276, 0, 263, () => _ = BeginMail(ProtocolMail.TarosOk, () => socialClient.TakeMailTarosAsync(mailSelected, socialToken)));
            InventoryText(detail, "mail-guide-note", T("Note: Guide emails are automatically deleted after the related mission is complete.", "Письма гида удаляются после завершения связанного задания."), 23, 335, 520, 20, 12, true);
            var trash = MailButton(root.Q("inventory-stuff"), "mail-delete", "", 390, 555, 32, () => ShowPrompt(T("DELETE MAIL", "УДАЛИТЬ ПИСЬМО"), T("Delete this message and any remaining attachments?", "Удалить письмо вместе с оставшимися вложениями?"), null,
                _ => BeginMail(ProtocolMail.DeleteOk, () => socialClient.DeleteMailAsync(mailSelected, socialToken)), null), "mail-trash"); trash.style.height = 32;
            FFStyle.Button(trash, "ui/en/user-equip/trash", "ui/en/user-equip/trash-hover");
            InventoryText(panel, "mail-status", mailStatus, 22, 637, 543, 14, 10, true);
            RefreshMailTabArt(root);
            if (mailCompose) BuildMailComposer(email);
            root.RegisterCallback<GeometryChangedEvent>(_ => PlaceWindow()); return root;
        }

        void SelectMailFolder(bool guide)
        {
            if (MailBusy) return;
            mailGuideFolder = guide; guideMailPage = 0; mailSelected = 0; mailDetail = null; mailCompose = false; RebuildMail();
            if (!guide && SocialAvailable) _ = LoadMailPage(mailPage);
        }

        void BuildMailComposer(VisualElement content)
        {
            foreach (string name in new[] { "mail-list", "mail-table-art", "mail-empty-folder", "mail-detail", "mail-heading-from", "mail-heading-subject", "mail-heading-day", "mail-heading-attachment", "mail-previous", "mail-next", "mail-page" }) content.Q(name).style.display = DisplayStyle.None;
            var composer = new VisualElement { name = "mail-composer" }; FFWidgets.Rect(composer, 14, 76, 561, 564); composer.AddToClassList("mail-detail"); content.Add(composer);
            SocialField(composer, "mail-recipient", T("To UID", "UID получателя"), 14, 16, 531, 20);
            var recipients = social.Friends.Where(f => f != null && !f.Blocked).ToArray();
            if (recipients.Length > 0)
            {
                var names = recipients.Select(f => f.Name).ToList(); var picker = new DropdownField(T("Friend", "Друг"), names, 0) { name = "mail-friend" };
                FFWidgets.Rect(picker, 14, 52, 531, 28); FFWidgets.Font(picker, true, 13); composer.Add(picker);
                composer.Q<TextField>("mail-recipient").SetValueWithoutNotify(recipients[0].Uid.ToString(CultureInfo.InvariantCulture));
                picker.RegisterValueChangedCallback(e => composer.Q<TextField>("mail-recipient").value = recipients[names.IndexOf(e.newValue)].Uid.ToString(CultureInfo.InvariantCulture));
            }
            SocialField(composer, "mail-subject", T("Subject", "Тема"), 14, 88, 531, 31);
            var body = SocialField(composer, "mail-body", "", 14, 126, 531, 511, true, 190);
            var bodyInput = body.Q<VisualElement>("unity-text-input");
            bodyInput.style.backgroundColor = new Color(.62f, .98f, .98f); bodyInput.style.color = new Color(.07f, .21f, .22f);
            SocialField(composer, "mail-taros", "Taros", 14, 328, 531, 10).SetValueWithoutNotify("0");
            InventoryText(composer, "mail-cost", "", 14, 363, 531, 24, 13, true);
            var items = new ScrollView { name = "mail-compose-items", horizontalScrollerVisibility = ScrollerVisibility.Hidden }; FFWidgets.Rect(items, 14, 394, 531, 113); composer.Add(items);
            MailButton(composer, "mail-send", T("SEND", "ОТПРАВИТЬ"), 14, 524, 255, PromptSendMail);
            MailButton(composer, "mail-clear-items", T("CLEAR ITEMS", "УБРАТЬ ВЛОЖЕНИЯ"), 282, 524, 263, () => { mailAttachments.Clear(); RefreshMail(); });
            composer.Q<TextField>("mail-taros").RegisterValueChangedCallback(_ => RefreshMailCost());
            composer.Query<TextField>().ForEach(field => field.labelElement.style.color = new Color(.8f, 1, 1));
            composer.Query<DropdownField>().ForEach(field => field.labelElement.style.color = new Color(.8f, 1, 1));
        }

        void RefreshMailRows(VisualElement root, bool ready)
        {
            guideMails = BuildGuideMail();
            if (guideMailPage * 5 >= guideMails.Count) guideMailPage = Math.Max(0, (guideMails.Count - 1) / 5);
            if (mailGuideFolder && !guideMails.Any(mail => mail.Index == mailSelected)) mailSelected = guideMails.Skip(guideMailPage * 5).FirstOrDefault()?.Index ?? 0;
            var summaries = mailGuideFolder ? guideMails.Skip(guideMailPage * 5).Take(5).Select(mail =>
                new MailSummary { Index = mail.Index, Sender = mail.Sender, Subject = mail.Subject, Read = true }).ToArray() : mailSummaries.ToArray();
            int first = summaries.Length == 0 ? 0 : (mailGuideFolder ? guideMailPage : mailPage - 1) * 5 + 1;
            int last = summaries.Length == 0 ? 0 : first + summaries.Length - 1;
            root.Q<Label>("mail-page").text = first + " - " + last + T("   of   ", "   из   ") + (mailGuideFolder ? guideMails.Count : mailPage) + T("   Emails", "   писем");
            root.Q<Button>("mail-previous").SetEnabled(ready && (mailGuideFolder ? guideMailPage > 0 : mailPage > 1));
            root.Q<Button>("mail-next").SetEnabled(ready && (mailGuideFolder ? (guideMailPage + 1) * 5 < guideMails.Count : mailPage < 127 && mailSummaries.Count == 5));
            root.Q("mail-empty-folder").style.display = !mailCompose && summaries.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var list = root.Q<ScrollView>("mail-list"); list.Clear();
            foreach (var summary in summaries)
            {
                var row = new Button(() => {
                    if (MailBusy || TravelBusy || mailCompose) return; mailSelected = summary.Index; mailDetail = null;
                    if (mailGuideFolder) { RefreshMail(); return; }
                    _ = BeginMail(ProtocolMail.ReadOk, () => socialClient.ReadMailAsync(summary.Index, socialToken));
                }) { name = "mail-row-" + summary.Index };
                MailClearBox(row); FFStyle.Background(row, null); row.style.height = row.style.minHeight = 28;
                row.style.flexShrink = 0; row.style.width = 559; row.tooltip = summary.Subject;
                if (summary.Index == mailSelected) MailSurface(row, "mail-row-selection", 1, 0, 556, 23, "list-selection", Vector4.zero);
                row.EnableInClassList("mail-row-selected", summary.Index == mailSelected);
                InventoryText(row, "mail-row-from", summary.Sender, 0, 0, 203, 28, 12, true);
                InventoryText(row, "mail-row-subject", summary.Subject?.Length > 24 ? summary.Subject.Substring(0, 21) + "..." : summary.Subject, 203, 0, 219, 28, 12, true);
                if (summary.Index == mailSelected) row.Query<Label>().ForEach(label => label.style.color = (Color)new Color32(61, 145, 244, 255));
                if (summary.HasAttachments) InventoryArt(row, "mail-row-attachment", "ui/en/email/attachment", 530, 7, 19, 13);
                row.SetEnabled(ready && !mailCompose); list.Add(row);
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
                var slot = root.Q<Button>("inventory-slot-" + item.Slot); BindItem(root, slot.name, item, catalog, false);
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
