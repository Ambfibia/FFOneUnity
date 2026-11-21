using System.Collections;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class GameplayUiViewTests
    {
        GameObject owner;
        PanelSettings settings;
        RenderTexture target;
        GameplayUiController ui;

        [TestCase("en", "ui/en/gameplay/mission/npc/current_mission")]
        [TestCase("ru", "ui/ru/gameplay/mission/npc/current_mission")]
        public void NpcAndTravelServiceMenusUseThePublishedMissionIconForTheTextLocale(string locale, string address)
        {
            var expected = ContentAssets.Load<Texture2D>(address);
            Assert.That(expected, Is.Not.Null);
            Assert.That(GameplayUiController.LoadServiceIcon(GameplayUiController.MissionServiceIcon, locale), Is.SameAs(expected));
        }

        [UnityTest]
        public IEnumerator SourceGameplayViewsRenderBindAndKeepHitTargetsAcrossResolutions()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            yield return Exercise();
        }

        [UnityTest]
        public IEnumerator QuickChatColumnsFitLocalizedCaptionsWithoutShrinking()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            yield return ExerciseQuickChat();
        }

        IEnumerator ExerciseQuickChat()
        {
            var document = CreateOffscreenDocument();
            yield return null;
            var root = document.rootVisualElement;
            root.style.width = 1264;
            root.style.height = 681;
            foreach (string locale in new[] { "en", "ru" })
            {
                ui = new GameplayUiController(root, GameplayUiValidationData.Session(), locale, () => { }, () => { });
                ui.Windows.Open(GameplayWindow.NanoCom);
                yield return null;
                const string place = "KND Training Ground Infected Zone";
                root.Q<Label>("hud-minimap-name").text = place;

                foreach (var (control, category) in new[] { ("chat-menu", 12), ("chat-emotes", 18) })
                {
                    Submit(root.Q<Button>(control));
                    yield return null;
                    AssertCaptionWidths(root.Q("quick-root"));
                    Submit(root.Q<Button>($"quick-item-0-{category}"));
                    yield return null; yield return null;
                    var submenu = root.Q("quick-submenu");
                    AssertCaptionWidths(root.Q("quick-root"));
                    AssertCaptionWidths(submenu);
                    if (category == 12)
                        Assert.That(submenu.Query<Label>().ToList().Any(label => label.text.Contains(place)), Is.True);
                    var rootButton = root.Q<Button>($"quick-item-0-{category}");
                    Assert.That(submenu.worldBound.xMin, Is.GreaterThan(rootButton.worldBound.xMax));
                    var item = submenu.Q<Button>();
                    Assert.That(root.panel.Pick(item.worldBound.center), Is.SameAs(item));
                    SavePreview(control + "-" + locale);
                    Submit(root.Q<Button>(control));
                }
                ui.Dispose(); ui = null;
            }
        }

        static void AssertCaptionWidths(VisualElement column)
        {
            Assert.That(column, Is.Not.Null);
            var rows = column.Children().OfType<Button>().ToArray();
            Assert.That(rows, Is.Not.Empty);
            float width = rows[0].resolvedStyle.width;
            float longestWithPadding = 0;
            foreach (var row in rows)
            {
                var label = row.Q<Label>(className: "ff-quick-choice-copy");
                Assert.That(label.resolvedStyle.fontSize, Is.EqualTo(12), label.text);
                float textWidth = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined).x;
                longestWithPadding = Mathf.Max(longestWithPadding, textWidth + row.resolvedStyle.paddingLeft + row.resolvedStyle.paddingRight);
                Assert.That(textWidth, Is.LessThanOrEqualTo(label.contentRect.width + 1), label.text);
                Assert.That(row.resolvedStyle.width, Is.EqualTo(width).Within(1), label.text);
            }
            Assert.That(width, Is.EqualTo(longestWithPadding).Within(1), "Rows fit the widest actual caption and its padding.");
        }

        UIDocument CreateOffscreenDocument()
        {
            target = new RenderTexture(1920, 1080, 0); target.Create();
            settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(ContentPaths.Resolve("Assets/UI/MainPanel.asset")));
            settings.targetTexture = target; settings.scaleMode = PanelScaleMode.ConstantPixelSize; settings.scale = 1;
            settings.clearColor = true; settings.colorClearValue = Color.clear;
            owner = new GameObject("Gameplay UI acceptance"); owner.SetActive(false);
            var document = owner.AddComponent<UIDocument>(); document.panelSettings = settings; owner.SetActive(true);
            return document;
        }

        IEnumerator Exercise()
        {
            var document = CreateOffscreenDocument();
            yield return null;
            var root = document.rootVisualElement;
            var session = GameplayUiValidationData.Session();
            int settingsRequests = 0, quitRequests = 0, gateChanges = 0;
            ui = new GameplayUiController(root, session, "ru", () => settingsRequests++, () => quitRequests++);
            var layers = UiLayers.For(root);
            var dialog = new VisualElement();
            var lateWindow = new VisualElement();
            var loading = new VisualElement();
            dialog.StretchToParentSize();
            lateWindow.StretchToParentSize();
            loading.StretchToParentSize();
            layers.Add(dialog, UiLayer.Dialog);
            layers.Add(loading, UiLayer.Loading);
            layers.Add(lateWindow, UiLayer.Window);
            yield return null;
            yield return null;
            Assert.That(root.panel.Pick(new Vector2(30, 30)), Is.SameAs(loading));
            loading.RemoveFromHierarchy();
            Assert.That(root.panel.Pick(new Vector2(30, 30)), Is.SameAs(dialog));
            dialog.RemoveFromHierarchy();
            lateWindow.RemoveFromHierarchy();
            Assert.That(root.Q("hud-exp-frame").style.display.value, Is.EqualTo(DisplayStyle.None), "The ordinary HUD has no optional EXP strip.");
            ui.InputGateChanged += () => gateChanges++;
            foreach (var size in new[] { new Vector2(1264,681), new Vector2(1280,720), new Vector2(1920,1080) })
            {
                root.style.width = size.x; root.style.height = size.y;
                ui.Hud.SetExperienceVisible(true);
                yield return null; yield return null;
                float readyBy = Time.realtimeSinceStartup + 20;
                while (Enumerable.Range(0,3).Any(i => !(root.Q<Image>($"nano-{i}-portrait").image is RenderTexture)) && Time.realtimeSinceStartup < readyBy)
                    yield return null;
                yield return null; yield return null;
                var chat = FFView.RequireClass<VisualElement>(root, "ff-chat-window");
                Assert.That(chat.resolvedStyle.width, Is.EqualTo(440));
                Assert.That(chat.resolvedStyle.height, Is.EqualTo(150));
                Assert.That(root.Q("chat-friend").resolvedStyle.left, Is.EqualTo(95));
                Assert.That(root.Q("chat-group").resolvedStyle.left, Is.EqualTo(230));
                float scale = RetrobutionLayout.Scale(size.y);
                Assert.That(root.Q<Label>("hud-name").text, Is.EqualTo("UI Validation"));
                Assert.That(root.Q("hud-hp").worldBound.width, Is.EqualTo(186 * .7f * scale).Within(.8), "Allow UI Toolkit's logical pixel rounding before the source transform.");
                Assert.That(root.Q("hud-exp-frame").worldBound.width, Is.EqualTo(size.x * scale).Within(.2));
                ui.Hud.SetExperienceVisible(false);
                yield return null; yield return null;
                for (int i = 0; i < 3; i++)
                {
                    var portrait = root.Q<Image>($"nano-{i}-portrait");
                    var clip = root.Q($"nano-{i}-portrait-clip");
                    Assert.That(portrait.image, Is.TypeOf<RenderTexture>(), "HUD Nano must be a model camera render, never an inventory icon.");
                    Assert.That(clip.resolvedStyle.height, Is.EqualTo(72).Within(.1), "Animated Nano uses its full source camera viewport.");
                    Assert.That(portrait.worldBound.width, Is.EqualTo(72 * scale).Within(.8));
                    Assert.That(portrait.worldBound.height, Is.EqualTo(72 * scale).Within(.8));
                    Assert.That(clip.worldBound.height, Is.GreaterThan(0), "A bound texture inside a zero-height clip is still invisible.");
                }
                if (size.x == 1264) yield return VerifyNanoPortraitPixels(root);
                Assert.That(ui.Windows.Open(GameplayWindow.NanoCom), Is.True);
                yield return null; yield return null;
                var inventory = FFView.Require<Button>(root, "inventory");
                Assert.That(root.panel.Pick(inventory.worldBound.center), Is.SameAs(inventory));
                Submit(inventory);
                yield return null; yield return null;
                Assert.That(ui.Windows.Current, Is.EqualTo(GameplayWindow.Inventory));
                Assert.That(ui.InputBlocked, Is.True);
                var slot = FFView.Require<Button>(root, "inventory-slot-0");
                Assert.That(((GameplayItemState)slot.userData).ItemId, Is.EqualTo(1));
                Assert.That(slot.Q<Image>("inventory-slot-0-image").image, Is.Not.Null, "The exact catalog icon must resolve.");
                Assert.That(root.panel.Pick(slot.worldBound.center), Is.SameAs(slot));
                Assert.That(root.Q<Button>("inventory-slot-1").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("trash").enabledSelf, Is.False, "No item mutation before a server adapter exists.");
                Assert.That(root.Q("inventory-slot-49"), Is.Not.Null);
                Assert.That(root.Q("equipment-type-10"), Is.Not.Null);
                if (size.x == 1280) SavePreview("inventory");
                if (size.x == 1920) SavePreview("inventory-reference");
                Submit(FFView.Require<Button>(root, "tab-nanos"));
                yield return null; yield return null;
                Assert.That(root.Q("nano-inventory-slots").childCount, Is.EqualTo(GameplayUiCatalog.Load().NanoIds.Count()));
                Assert.That(root.Q("nano-inventory-grid").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                if (size.x == 1920) SavePreview("nanos-reference");
                Submit(FFView.Require<Button>(root, "close"));
                Assert.That(ui.InputBlocked, Is.False);
                ui.Windows.Open(GameplayWindow.Mail);
                yield return null; yield return null;
                var cabinet = root.Q("inventory-stuff"); var letterPanel = root.Q("mail-content");
                Assert.That(cabinet.worldBound.xMin, Is.GreaterThan(letterPanel.worldBound.xMin));
                Assert.That(cabinet.worldBound.xMax, Is.LessThanOrEqualTo(size.x + 1));
                if (size.x == 1920) SavePreview("mail-guide-reference");
                Submit(root.Q<Button>("mail-player-tab"));
                yield return null; yield return null;
                if (size.x == 1920) SavePreview("mail-empty-reference");
                var inbox = new byte[1024]; inbox[0] = 1;
                FFOne.Network.Protocol0104.Write64(inbox, 4, 1); FFOne.Network.Protocol0104.Write64(inbox, 12, 123);
                FFOne.Network.Protocol0104.WriteUtf16(inbox, 20, 9, "Alice"); FFOne.Network.Protocol0104.WriteUtf16(inbox, 38, 17, "Unity");
                FFOne.Network.Protocol0104.WriteUtf16(inbox, 72, 32, "2x4 Technology");
                ui.ReceiveSocial(new FFOne.Network.ProtocolFrame(FFOne.Network.ProtocolMail.ListOk, inbox));
                typeof(GameplayUiController).GetField("mailSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(ui, 1L);
                var letter = new byte[1084]; FFOne.Network.Protocol0104.Write64(letter, 0, 1);
                FFOne.Network.Protocol0104.WriteUtf16(letter, 8, 512, "После прыжка во времени детали машины Декстера были повреждены. Поговори с Номером Два на платформе домика на дереве.\n\nПроверка оформления письма.");
                ui.ReceiveSocial(new FFOne.Network.ProtocolFrame(FFOne.Network.ProtocolMail.ReadOk, letter));
                yield return null; yield return null;
                if (size.x == 1920) SavePreview("mail-read-reference");
                Submit(root.Q<Button>("mail-compose"));
                yield return null; yield return null;
                if (size.x == 1920) SavePreview("mail-compose-reference");
                ui.CloseWindow();
                ui.Windows.Open(GameplayWindow.Journal);
                yield return null; yield return null;
                var row = FFView.Require<Button>(root, "select-mission");
                Assert.That(root.panel.Pick(row.worldBound.center), Is.SameAs(row));
                Submit(row);
                Assert.That(root.Q("journal-details").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<Label>("mission-title-label").text, Is.Not.Empty.And.Not.EqualTo("2248"));
                if (size.x == 1280) { yield return null; SavePreview("journal"); }
                var completed = FFView.RequireClass<Button>(root, "ff-tab-completed");
                Assert.That(root.panel.Pick(completed.worldBound.center), Is.SameAs(completed));
                Submit(completed);
                yield return null; yield return null;
                var activeCaption = FFView.RequireClass<Label>(root, "ff-tab-active-label");
                var completedCaption = FFView.RequireClass<Label>(root, "ff-tab-completed-label");
                var activeText = activeCaption.MeasureTextSize(activeCaption.text, 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined);
                Assert.That(activeText.x, Is.LessThanOrEqualTo(activeCaption.contentRect.width),
                    "The inactive Russian tab must show both ends of its caption.");
                Assert.That(activeCaption.worldBound.xMax, Is.LessThan(completedCaption.worldBound.xMin),
                    "Each tab must have its own text rectangle.");
                if (size.x == 1280) SavePreview("journal-ru-completed-tab");
                Submit(FFView.RequireClass<Button>(root, "ff-tab-active"));
                ui.CloseWindow();
            }
            session.SetTutorialHealth(300, 1000);
            ui.Hud.Tick(.5f); ui.Hud.Tick(.1f);
            yield return null;
            Assert.That(root.Q("hud-hp-damage").resolvedStyle.width, Is.GreaterThan(root.Q("hud-hp").resolvedStyle.width));
            ui.Windows.Open(GameplayWindow.NanoCom);
            Submit(FFView.Require<Button>(root, "settings"));
            Assert.That(settingsRequests, Is.EqualTo(1)); Assert.That(ui.InputBlocked, Is.False);
            ui.Windows.Open(GameplayWindow.NanoCom);
            Submit(FFView.Require<Button>(root, "quit"));
            Assert.That(quitRequests, Is.EqualTo(1)); Assert.That(ui.Windows.Current, Is.EqualTo(GameplayWindow.NanoCom), "Exit is an overlay above NanoCom.");
            Assert.That(gateChanges, Is.GreaterThan(6));
            ui.Dispose(); ui = null;
            Assert.That(root.Q("hud-root"), Is.Null);
            Assert.That(root.Q("nanocom-root"), Is.Null);
        }

        static void Submit(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }

        IEnumerator VerifyNanoPortraitPixels(VisualElement root)
        {
            var portraits = Enumerable.Range(0, 3).Select(i => root.Q<Image>($"nano-{i}-portrait")).ToArray();
            var bounds = portraits.Select(image => image.worldBound).ToArray();
            var visiblePixels = ReadTargetPixels();
            SavePreview("nano-belt");
            try
            {
                foreach (var portrait in portraits) portrait.style.visibility = Visibility.Hidden;
                yield return null; yield return null;
                var hiddenPixels = ReadTargetPixels();
                for (int slot = 0; slot < portraits.Length; slot++)
                {
                    Rect rect = bounds[slot];
                    int x0 = Mathf.Clamp(Mathf.CeilToInt(rect.xMin), 0, target.width);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt(rect.xMax), 0, target.width);
                    int y0 = Mathf.Clamp(Mathf.CeilToInt(target.height - rect.yMax), 0, target.height);
                    int y1 = Mathf.Clamp(Mathf.FloorToInt(target.height - rect.yMin), 0, target.height);
                    int changed = 0;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int index = y * target.width + x;
                            var a = visiblePixels[index]; var b = hiddenPixels[index];
                            if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 12) changed++;
                        }
                    Assert.That(changed, Is.GreaterThan(32), "Nano " + slot + " must visibly contribute portrait pixels through its source frame, not merely hold a Texture reference.");
                }
            }
            finally
            {
                foreach (var portrait in portraits) portrait.style.visibility = Visibility.Visible;
            }
            yield return null; yield return null;
        }

        Color32[] ReadTargetPixels()
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        void SavePreview(string name)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                Assert.That(image.GetPixels32().Where((_,i) => i % 64 == 0).Select(c => (c.r,c.g,c.b,c.a)).Distinct().Count(), Is.GreaterThan(20));
                Directory.CreateDirectory("Logs/GameplayUiPreview");
                File.WriteAllBytes("Logs/GameplayUiPreview/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            ui?.Dispose(); ui = null;
            if (owner) Object.DestroyImmediate(owner);
            if (settings) Object.DestroyImmediate(settings);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
