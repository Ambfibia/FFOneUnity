using System;
using System.Linq;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using FFOne.Content;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayMailSocialTests
    {
        static ProtocolFrame Frame(uint type, byte[] p) => new ProtocolFrame(type, p);
        static GameplayUiSession Session()
        {
            var p = new byte[2700]; Protocol0104.Write32(p, 0, 51); Protocol0104.Write64(p, 8, 1234); Protocol0104.Write16(p, 1676, -1);
            var s = new GameplayUiSession(_ => null); s.SetWorld(Protocol0104.DecodeWorldEntry(p, 1234)); return s;
        }
        static byte[] Friend(long uid)
        { var p = new byte[76]; p[0] = 7; Protocol0104.Write64(p, 8, uid); Protocol0104.WriteUtf16(p, 20, 9, "Alice"); Protocol0104.WriteUtf16(p, 38, 17, "Unity"); return p; }
        [Test] public void ComposePreserves64BitRecipientUtf16AndSparseInventorySlots()
        {
            var p = ProtocolMail.Compose(9876543210, "Привет", "Первая\nВторая", 20, new[] { new MailAttachment(49, new EquipmentItem { ItemType = 1, ItemId = 67, Option = 3 }) });
            Assert.AreEqual(1164, p.Length); Assert.AreEqual(9876543210, Protocol0104.Read64(p, 0)); Assert.AreEqual("Привет", Protocol0104.ReadUtf16(p, 8, 32));
            Assert.AreEqual("Первая\nВторая", Protocol0104.ReadUtf16(p, 72, 512)); Assert.AreEqual(49, Protocol0104.Read32(p, 1096)); Assert.AreEqual(20, Protocol0104.Read32(p, 1160));
            Assert.AreEqual(90, ProtocolMail.Cost(20, 1));
        }
        [Test] public void MailIntentRejectsDuplicatesOverlongTextAndZeroBasedAttachmentSlot()
        {
            var a = new MailAttachment(4, new EquipmentItem { ItemType = 1, ItemId = 67, Option = 1 });
            Assert.Throws<ArgumentException>(() => ProtocolMail.Compose(2, "", "", 0, new[] { a, a }));
            Assert.Throws<ArgumentException>(() => ProtocolMail.Compose(2, new string('x', 32), "", 0, Array.Empty<MailAttachment>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolMail.ItemPayload(1, 0, 0));
            Assert.AreEqual(4, Protocol0104.Read32(ProtocolMail.ItemPayload(1, 49, 4), 12));
            Assert.Throws<ArgumentOutOfRangeException>(() => ProtocolMail.Page(0));
        }
        [Test] public void InboxHeaderIsAlignedAndSummaryRetainsItsServerIndex()
        {
            var p = new byte[1024]; p[0] = 2; Protocol0104.Write64(p, 4, 123); Protocol0104.Write64(p, 12, 777);
            Protocol0104.WriteUtf16(p, 20, 9, "Alice"); Protocol0104.WriteUtf16(p, 38, 17, "Unity"); Protocol0104.WriteUtf16(p, 72, 32, "Тема"); Protocol0104.Write32(p, 204, 1);
            ProtocolMail.TryDecode(Frame(ProtocolMail.ListOk, p), out var u); Assert.AreEqual(2, u.Page); var m = u.Summaries.Single();
            Assert.AreEqual(123, m.Index); Assert.AreEqual(777, m.SenderUid); Assert.AreEqual("Alice Unity", m.Sender); Assert.AreEqual("Тема", m.Subject); Assert.True(m.HasAttachments);
            Protocol0104.Write64(p, 208, 123); Assert.Throws<ProtocolException>(() => ProtocolMail.TryDecode(Frame(ProtocolMail.ListOk, p), out _));
        }
        [Test] public void MailFinancialFailureDoesNotPublishAndReadDoesNotChangeWallet()
        {
            var s = Session(); var old = s.Snapshot; var fail = new byte[12]; Protocol0104.Write64(fail, 0, 44);
            s.ApplyFrame(Frame(ProtocolMail.SendOk + 1, fail)); Assert.AreSame(old, s.Snapshot);
            var read = new byte[1084]; Protocol0104.Write64(read, 0, 44); Protocol0104.Write32(read, 1080, 999);
            s.ApplyFrame(Frame(ProtocolMail.ReadOk, read)); Assert.AreSame(old, s.Snapshot);
            var take = new byte[12]; Protocol0104.Write64(take, 0, 44); Protocol0104.Write32(take, 8, 50);
            s.ApplyFrame(Frame(ProtocolMail.TarosOk, take)); Assert.AreEqual(50, s.Snapshot.Taros);
        }
        [Test] public void GiveItemIsAnAuthoritativePostStateIncludingEmptySlotAndExpiry()
        {
            var s = Session(); var p = new byte[20]; Protocol0104.Write32(p, 0, 1); Protocol0104.Write32(p, 4, 49); GameplayInventoryTests.Item(p, 8, 1, 67, 1); Protocol0104.Write32(p, 16, 1800000000);
            s.ApplyFrame(Frame(0x31000061, p)); Assert.AreEqual(67, s.Snapshot.Inventory[49].ItemId); Assert.AreEqual(1800000000, s.Snapshot.Inventory[49].TimeLimit);
            var before = s.Snapshot; Protocol0104.Write32(p, 4, 50); Assert.Throws<ProtocolException>(() => s.ApplyFrame(Frame(0x31000061, p))); Assert.AreSame(before, s.Snapshot);
            Array.Clear(p, 8, 12); Protocol0104.Write32(p, 4, 49); s.ApplyFrame(Frame(0x31000061, p)); Assert.True(s.Snapshot.Inventory[49].IsEmpty); Assert.AreEqual(67, before.Inventory[49].ItemId);
        }
        [Test] public void FriendSlotsAndPackedPresenceSurviveHolesWithoutInventingUidFromPlayerId()
        {
            var s = new SocialSession(); ProtocolSocial.TryDecode(Frame(ProtocolSocial.Added, Friend(777)), out var added); s.Apply(added);
            var p = new byte[252]; Protocol0104.Write32(p, 0, 25); p[200] = 1; ProtocolSocial.TryDecode(Frame(ProtocolSocial.States, p), out var states); s.Apply(states);
            Assert.AreEqual(777, s.Friends[7].Uid); Assert.AreEqual(25, s.Friends[7].PlayerId); Assert.AreEqual(1, s.Friends[7].State); Assert.Null(s.Friends[0]);
            var remove = ProtocolSocial.Slot(7, 778); ProtocolSocial.TryDecode(Frame(ProtocolSocial.Removed, remove), out var removed); s.Apply(removed); Assert.NotNull(s.Friends[7]);
            Protocol0104.Write64(remove, 0, 777); ProtocolSocial.TryDecode(Frame(ProtocolSocial.Removed, remove), out removed); s.Apply(removed); Assert.Null(s.Friends[7]);
        }
        [Test] public void MalformedFriendPageCannotPartiallyReplaceTheProjection()
        {
            var s = new SocialSession(); ProtocolSocial.TryDecode(Frame(ProtocolSocial.Added, Friend(777)), out var added); s.Apply(added);
            var p = new byte[16]; p[13] = 2; Assert.Throws<ProtocolException>(() => ProtocolSocial.TryDecode(Frame(ProtocolSocial.List, p), out _)); Assert.AreEqual(777, s.Friends[7].Uid);
            p[12] = 49; p[13] = 2; Assert.Throws<ProtocolException>(() => ProtocolSocial.TryDecode(Frame(ProtocolSocial.List, p), out _));
        }
        [Test] public void GroupRosterChecksBothCountsAndAllRecordsBeforePublishing()
        {
            var p = new byte[12 + 112]; Protocol0104.Write32(p, 4, 1); Protocol0104.Write32(p, 12, 51); Protocol0104.Write64(p, 16, 1234);
            Protocol0104.WriteUtf16(p, 26, 9, "Alice"); Protocol0104.WriteUtf16(p, 44, 17, "Unity"); Protocol0104.Write16(p, 80, 7); Protocol0104.Write32(p, 84, 300); Protocol0104.Write32(p, 88, 700);
            ProtocolSocial.TryDecode(Frame(ProtocolSocial.Join, p), out var u); var s = new SocialSession(); s.Apply(u); var before = s.Members;
            Assert.AreEqual("Alice Unity", before[0].Name); Assert.AreEqual(7, before[0].Level); Assert.AreEqual(300, before[0].Hp); Assert.AreEqual(700, before[0].MaxHp);
            Protocol0104.Write32(p, 8, 1); Assert.Throws<ProtocolException>(() => ProtocolSocial.TryDecode(Frame(ProtocolSocial.Join, p), out _)); Assert.AreSame(before, s.Members);
            ProtocolSocial.TryDecode(Frame(ProtocolSocial.Left, new byte[1]), out u); s.Apply(u); Assert.IsEmpty(s.Members);
        }
        [Test] public void SocialIntentLayoutsKeepFullUidsAndRequiredPadding()
        {
            var identity = ProtocolSocial.Identity(25, 9876543210); Assert.AreEqual(12, identity.Length); Assert.AreEqual(9876543210, Protocol0104.Read64(identity, 4));
            var accept = ProtocolSocial.Accept(25, true); Assert.AreEqual(16, accept.Length); Assert.AreEqual(1, accept[0]); Assert.AreEqual(25, Protocol0104.Read32(accept, 4));
            var whisper = ProtocolSocial.Whisper(49, 9876543210, "Привет"); Assert.AreEqual(272, whisper.Length); Assert.AreEqual(9876543210, Protocol0104.Read64(whisper, 260)); Assert.AreEqual(49, whisper[268]);
        }
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator NewWindowsExposeConcreteActionsAndShareTheWindowLifetime()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new UnityEngine.TestTools.EnterPlayMode();
            var settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                ContentPaths.Resolve("Assets/UI/MainPanel.asset")));
            var target = new RenderTexture(1280, 720, 0);
            target.Create();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            var owner = new GameObject("Mail acceptance");
            owner.SetActive(false);
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = settings;
            owner.SetActive(true);
            yield return null;
            var root = document.rootVisualElement;
            var session = Session();
            using var ui = new GameplayUiController(root, session, "ru", null, null);
            Assert.True(ui.Windows.Open(GameplayWindow.Contacts)); Assert.NotNull(ui.Windows.View.Q<Button>("friend-add-name")); Assert.NotNull(ui.Windows.View.Q<Button>("group-leave"));
            yield return null; yield return null;
            var input = ui.Windows.View.Q<TextField>("friend-first").Q<VisualElement>(className: "unity-base-text-field__input");
            Assert.NotNull(input);
            Assert.That(input.resolvedStyle.backgroundColor, Is.EqualTo((Color)new Color32(6, 26, 41, 255)),
                "The contacts stylesheet must provide a contrasting input background.");
            Assert.True(ui.Windows.Open(GameplayWindow.Mail)); Assert.NotNull(ui.Windows.View.Q<Button>("mail-compose")); Assert.NotNull(ui.Windows.View.Q<Button>("mail-take-all"));
            Assert.NotNull(ui.Windows.View.Q<Button>("inventory-slot-49"), "Mail keeps the shared 50-slot equipment cabinet.");
            Assert.IsNull(ui.Windows.View.Q("inventory-avatar"), "Mail reserves the entire left panel for letters.");
            yield return null; yield return null;
            Assert.AreEqual(DisplayStyle.None, ui.Windows.View.Q("mail-message").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.Flex, ui.Windows.View.Q("mail-empty-folder").resolvedStyle.display);
            typeof(GameplayUiController).GetMethod("SelectMailFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(ui, new object[] { false });
            var summary = new byte[1024]; summary[0] = 1;
            Protocol0104.Write64(summary, 4, 1); Protocol0104.Write64(summary, 12, 777);
            Protocol0104.WriteUtf16(summary, 20, 9, "Alice"); Protocol0104.WriteUtf16(summary, 72, 32, "Тема письма");
            ui.ReceiveSocial(Frame(ProtocolMail.ListOk, summary));
            Assert.AreEqual("Тема письма", ui.Windows.View.Q<Button>("mail-row-1").Q<Label>("mail-row-subject").text);
            typeof(GameplayUiController).GetField("mailSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(ui, 1L);
            var letter = new byte[1084]; Protocol0104.Write64(letter, 0, 1); Protocol0104.WriteUtf16(letter, 8, 512, "Текст <b>письма</b>\nВторая строка.");
            ui.ReceiveSocial(Frame(ProtocolMail.ReadOk, letter));
            yield return null; yield return null;
            Assert.AreEqual(DisplayStyle.Flex, ui.Windows.View.Q("mail-message").resolvedStyle.display);
            Assert.False(ui.Windows.View.Q<Label>("mail-detail-body").enableRichText);
            Assert.AreEqual("Текст <b>письма</b>\nВторая строка.", ui.Windows.View.Q<Label>("mail-detail-body").text);
            Assert.AreEqual(DisplayStyle.None, ui.Windows.View.Q("mail-claims").resolvedStyle.display, "Letters without attachments keep the full reading area.");
            Assert.True(ui.Windows.Open(GameplayWindow.Nanos));
            Assert.AreEqual(GameplayUiCatalog.Load().NanoIds.Count(), ui.Windows.View.Q("nano-inventory-slots").childCount);
            Assert.Greater(ui.Windows.View.Q("nano-inventory-slots").childCount, 36);
            Assert.NotNull(ui.Windows.View.Q<Image>("nano-slot-image").image, "Locked Nanos use their original silhouette artwork.");
            foreach (var image in ui.Windows.View.Q("nano-inventory-slots").Query<Image>().ToList())
                Assert.NotNull(image.image, "Every catalog Nano, including later atlas numbers, needs its silhouette.");
            Assert.NotNull(ui.Windows.View.Q<Label>("inventory-player-name"));
            Assert.AreEqual(session.Snapshot.Taros.ToString("D9"), ui.Windows.View.Q<Label>("inventory-taros").text);
            Assert.True(ui.InputBlocked); ui.Windows.Close(); Assert.False(ui.InputBlocked);
            ui.Dispose();
            UnityEngine.Object.Destroy(owner);
            UnityEngine.Object.Destroy(settings);
            target.Release();
            UnityEngine.Object.Destroy(target);
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }
    }
}
