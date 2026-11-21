#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Creation;
using FFOne.Integration;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    /// <summary>Opt-in shipped-player acceptance using explicit local protocol fixtures, without login or sockets.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class GameplayUiSmoke : MonoBehaviour
    {
        FrontendController frontend;
        PreviewStage preview;
        VisualElement root, container;
        GameplayUiController ui;
        GameplayUiSession session;
        GameplayTradeSession trade;
        RenderTexture target;
        PanelSettings panel;
        bool finished;
        float deadline;
        int chatSends, itemSends, acceptSends, confirmSends, tradeChatSends;
        string sentChat, sentTradeChat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            if (Environment.GetCommandLineArgs().Contains("--gameplay-ui-smoke"))
                new GameObject("Gameplay UI player validation").AddComponent<GameplayUiSmoke>();
        }

        IEnumerator Start()
        {
            deadline = Time.realtimeSinceStartup + 120;
            // Drive the iterator explicitly: Unity otherwise logs a coroutine exception
            // without returning a failing process exit code to build validation.
            var exercise = Exercise();
            while (!finished)
            {
                object next;
                try
                {
                    if (!exercise.MoveNext()) break;
                    next = exercise.Current;
                }
                catch (Exception error) { Fail(error); yield break; }
                yield return next;
            }
        }

        void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartup > deadline)
                Fail(new TimeoutException("Gameplay UI player smoke exceeded 120 seconds."));
        }

        bool renderRequestLogged;
        void LateUpdate()
        {
            if (finished || ui == null) return;
            try
            {
                // A hidden standalone window can suppress automatic camera rendering
                // while UI Toolkit still updates its offscreen panel. Submit the same
                // URP cameras explicitly for this opt-in GPU validation only.
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include))
                {
                    if (!camera.targetTexture) continue;
                    if (!renderRequestLogged)
                        Debug.Log($"GAMEPLAY EXPLICIT CAMERA: name={camera.name}, active={camera.gameObject.activeInHierarchy}, enabled={camera.enabled}, target={camera.targetTexture.name}, pipelineAsset={GraphicsSettings.currentRenderPipeline?.name}, pipeline={RenderPipelineManager.currentPipeline?.GetType().Name}");
                    if (!camera.enabled || !camera.gameObject.activeInHierarchy) continue;
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                    Require(RenderPipeline.SupportsRenderRequest(camera, request), "Active pipeline does not support explicit UI preview rendering.");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                renderRequestLogged = true;
            }
            catch (Exception error) { Fail(error); }
        }

        IEnumerator Exercise()
        {
            float readyBy = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < readyBy)
            {
                frontend = FindFirstObjectByType<FrontendController>();
                if (frontend && frontend.Root?.Q<Button>("create-character") != null) break;
                yield return null;
            }
            Require(frontend && frontend.Root != null, "Frontend did not initialize.");
            var document = frontend.GetComponent<UIDocument>();
            Require(document && document.panelSettings, "Frontend has no runtime UI panel.");
            target = new RenderTexture(UiSmokeLaunch.Dimension("-screen-width", Screen.width),
                UiSmokeLaunch.Dimension("-screen-height", Screen.height), 24, RenderTextureFormat.ARGB32);
            Require(target.Create(), "Unable to create gameplay validation render target.");
            UiSmokeLaunch.VerifyLocale(frontend, target);
            string locale = UiSmokeLaunch.Locale ?? "en";
            panel = Instantiate(document.panelSettings);
            panel.targetTexture = target;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize; panel.scale = 1;
            panel.clearColor = true; panel.colorClearValue = Color.clear;
            document.panelSettings = panel;
            root = frontend.Root;
            foreach (var view in root.Children().ToArray()) view.style.display = DisplayStyle.None;
            container = new VisualElement { name = "gameplay-smoke-container" };
            container.style.position = Position.Absolute;
            container.style.left = container.style.top = container.style.right = container.style.bottom = 0;
            root.Add(container);
            session = GameplayUiValidationData.Session();
            var cues = new System.Collections.Generic.List<string>();
            ui = new GameplayUiController(container, session, locale, () => { }, () => { }, cue => { cues.Add(cue); frontend.PlayUiSound(cue); });
            ui.Error += error => Fail(new InvalidOperationException("Gameplay UI action failed: " + error));
            ui.SetChatSender(text => { chatSends++; sentChat = text; ProtocolChat.FreeChatPayload(text); return Task.CompletedTask; });
            ui.SetItemMover((from, fromSlot, to, toSlot) => { itemSends++; return Task.CompletedTask; });
            int deleted = -1;
            ui.DeleteItem = id => { deleted = id; return Task.CompletedTask; };
            trade = new GameplayTradeSession(session.Snapshot.PlayerId);
            TradeItem offeredItem = default;
            ui.ConfigureTrade(trade, new GameplayTradeActions {
                Accept = (from, to) => { acceptSends++; ProtocolTrade.Participants(90, from, to); return Task.CompletedTask; },
                Refuse = (from, to) => Task.CompletedTask,
                Cancel = (from, to) => Task.CompletedTask,
                CancelOffer = (from, to) => Task.CompletedTask,
                Confirm = (from, to) => { confirmSends++; return Task.CompletedTask; },
                Cash = (from, to, amount) => Task.CompletedTask,
                AddItem = (from, to, item) => { offeredItem = item; return Task.CompletedTask; },
                RemoveItem = (from, to, item) => Task.CompletedTask,
                Chat = (from, to, text) => {
                    ProtocolTradeChat.Payload(90, from, to, text);
                    tradeChatSends++; sentTradeChat = text; return Task.CompletedTask;
                }
            });
            preview = frontend.GetComponent<PreviewStage>();
            Require(preview, "Missing native avatar preview owner.");
            ui.InventoryAvatarChanged += preview.SetGameplayAvatar;
            ui.InventoryRotationRequested += preview.Rotate;
            // Use the production gameplay owner with separate portrait/inventory views.
            // Preview uses audited creator equipment; item-icon fixtures also include
            // catalog items whose actor skin remap has not been accepted yet.
            var creation = new CreationCatalog();
            int First(string category) => (int)creation.Choices(category, 1)[0]["value"];
            var avatar = CreationCatalog.Preview(new CharacterCreation {
                Style = new CharacterStyle { Gender = 1, FaceStyle = First("face"), HairStyle = First("hair"),
                    EyeColor = 1, SkinColor = 1, HairColor = 1, Height = 2, Body = 1 },
                UpperBodyId = First("shirt"), LowerBodyId = First("pants"), FootId = First("shoes")
            });
            avatar.Uid = 9001; avatar.Slot = 1;
            preview.RefreshGameplayAppearance(avatar);
            preview.SetGameplayPortrait(ui.Hud.Portrait);
            Debug.Log("GAMEPLAY UI PLAYER SMOKE STARTED (offline fixtures; no login, chat or trade network requests)");
            yield return null; yield return null;
            float avatarBy = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < avatarBy && ui.Hud.Portrait.image == null) yield return null;
            Require(ui.Hud.Portrait.image != null, "HUD avatar preview texture did not bind.");
            for (int frame = 0; frame < 6; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            for (int i = 0; i < 3; i++)
            {
                var nanoPortrait = container.Q<Image>($"nano-{i}-portrait");
                if (nanoPortrait.image is RenderTexture) AvatarPixelEvidence(nanoPortrait, "nano-model-" + i);
            }
            AvatarPixelEvidence(ui.Hud.Portrait, "hud-avatar");
            var withPortrait = ReadPixels(target);
            ui.Hud.Portrait.style.visibility = Visibility.Hidden;
            yield return null; yield return new WaitForEndOfFrame();
            AssertCompositedAvatar(ui.Hud.Portrait, withPortrait, ReadPixels(target), "HUD portrait");
            ui.Hud.Portrait.style.visibility = Visibility.Visible;
            yield return null; yield return new WaitForEndOfFrame();
            Require(container.Q<Label>("hud-name").text == "UI Validation", "HUD name binding differs from fixture.");
            Require(container.Q<Label>("hud-level").text == "01", "HUD level lost original two-digit formatting.");
            float scale = RetrobutionLayout.Scale(container.resolvedStyle.height);
            Require(Mathf.Abs(container.Q("hud-hp").worldBound.width - 186 * .7f * scale) < 1, "HUD health geometry differs from source fraction.");
            ValidateNanoBelt();
            var nanoFrames = Enumerable.Range(0, 3).Select(i => ReadPixels((RenderTexture)container.Q<Image>($"nano-{i}-portrait").image)).ToArray();
            yield return new WaitForSeconds(.45f); yield return new WaitForEndOfFrame();
            for (int i = 0; i < 3; i++)
            {
                var frame = ReadPixels((RenderTexture)container.Q<Image>($"nano-{i}-portrait").image);
                int changed = frame.Where((pixel, index) => Mathf.Abs(pixel.r - nanoFrames[i][index].r) +
                    Mathf.Abs(pixel.g - nanoFrames[i][index].g) + Mathf.Abs(pixel.b - nanoFrames[i][index].b) +
                    Mathf.Abs(pixel.a - nanoFrames[i][index].a) > 24).Count();
                Require(changed > 8, "Nano model did not animate in its camera target: " + i);
                Debug.Log($"GAMEPLAY NANO ANIMATION {i}: changed pixels={changed}");
            }
            Require(container.Q("hud-exp-frame").resolvedStyle.display == DisplayStyle.None,
                "The ordinary HUD unexpectedly shows the optional EXP strip.");
            Capture("hud");
            ui.ReceiveCombat(new ProtocolFrame(ProtocolGameplay.AttackSuccess, ProtocolTravel.Words(1, 0)));
            ui.UpdateTarget(null, null, null, false);
            yield return null; yield return new WaitForEndOfFrame();
            var dangerImage = FFView.RequireClass<Image>(container, "ff-combat-danger");
            Require(dangerImage.resolvedStyle.display == DisplayStyle.Flex &&
                dangerImage.image && dangerImage.image == FFWidgets.Texture("ui/" + locale + "/gameplay/shared/danger"),
                "Combat danger must display the artwork for the active language.");
            Capture("combat-danger");
            ui.UpdateTarget(null, null, null, true);
            ui.Hud.SetExperienceVisible(true);
            yield return null; yield return new WaitForEndOfFrame();
            Require(Mathf.Abs(container.Q("hud-exp-fill").resolvedStyle.width - (container.resolvedStyle.width - 14) * .25f) < 1,
                "HUD optional EXP geometry differs from source fraction.");
            Capture("hud-exp-enabled");
            ui.Hud.SetExperienceVisible(false);

            Require(ui.Windows.Open(GameplayWindow.NanoCom), "NanoCom unavailable.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("nanocom");
            var tile = FFView.Require<Image>(container, "hud-minimap-tile-0");
            Require(tile.image != null && tile.uv.width > 0, "NanoCom map did not bind its atlas.");
            var previousUv = tile.uv;
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "minimap-zoom-in"));
            Require(tile.uv != previousUv, "Minimap zoom did not resample the atlas.");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "guide"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var guide = container.Q("game-guide");
            Require(guide != null && !ui.Windows.View.enabledInHierarchy && guide.Q<Image>("guide-screenshot").image != null, "Game Guide failed to open above NanoCom.");
            string guideFirstHeading = guide.Q<Label>("guide-heading").text;
            Require(!string.IsNullOrWhiteSpace(guideFirstHeading), "Game Guide heading is empty.");
            Capture("game-guide");
            SubmitHit(guide.Q<Button>("guide-next"));
            Require(guide.Q<ScrollView>("guide-content").contentContainer.childCount > 4, "Next section did not change the page.");
            SubmitHit(guide.Q<Button>("guide-topic-7"));
            SubmitHit(guide.Q<Button>("guide-next"));
            SubmitHit(guide.Q<Button>("guide-next"));
            Require(guide.Q<Label>("guide-heading").text == guideFirstHeading, "Guide navigation did not wrap back to the first topic.");
            SubmitHit(guide.Q<Button>("guide-close"));
            Require(ui.Windows.Current == GameplayWindow.NanoCom && ui.Windows.View.enabledInHierarchy, "Closing help lost the underlying NanoCom menu.");
            Require(cues.SequenceEqual(new[] { "open_screen", "button", "button", "open_screen", "close_screen" }), "NanoCom control cue sequence changed.");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "inventory"));
            yield return null; yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.Current == GameplayWindow.Inventory, "NanoCom inventory action did not navigate.");
            var inventory = ui.Windows.View;
            var slot = FFView.Require<Button>(inventory, "inventory-slot-0");
            Require(slot.Q<Image>("inventory-slot-0-image").image != null, "Inventory catalog icon missing from bundle.");
            Require(FFView.Require<Image>(inventory, "inventory-avatar").image != null, "Inventory avatar callback did not bind preview.");
            float inventoryModelsBy = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < inventoryModelsBy && Enumerable.Range(0, 3).Any(i => !(inventory.Q<Image>("inventory-nano-portrait-" + i).image is RenderTexture))) yield return null;
            for (int frame = 0; frame < 6; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            for (int i = 0; i < 3; i++) AvatarPixelEvidence(inventory.Q<Image>("inventory-nano-portrait-" + i), "inventory-nano-model-" + i);
            var inventoryAvatar = FFView.Require<Image>(inventory, "inventory-avatar");
            Require(inventoryAvatar.image != ui.Hud.Portrait.image, "HUD and inventory must retain independent preview textures.");
            AvatarPixelEvidence(inventoryAvatar, "inventory-avatar");
            AvatarPixelEvidence(ui.Hud.Portrait, "hud-avatar-with-inventory");
            var withAvatars = ReadPixels(target);
            inventoryAvatar.style.visibility = ui.Hud.Portrait.style.visibility = Visibility.Hidden;
            yield return null; yield return new WaitForEndOfFrame();
            var withoutAvatars = ReadPixels(target);
            AssertCompositedAvatar(inventoryAvatar, withAvatars, withoutAvatars, "Inventory avatar");
            AssertCompositedAvatar(ui.Hud.Portrait, withAvatars, withoutAvatars, "HUD portrait with inventory open");
            inventoryAvatar.style.visibility = ui.Hud.Portrait.style.visibility = Visibility.Visible;
            yield return null; yield return new WaitForEndOfFrame();
            Hit(slot);
            Capture("inventory");
            var before = session.Snapshot;
            ui.SetInteractionSuspended(true);
            Require(!slot.enabledInHierarchy, "External modal did not disable inventory controls.");
            var blockedMove = ui.RequestItemMove(ItemContainer.Inventory, 0, ItemContainer.Inventory, 1);
            Require(blockedMove.IsCompletedSuccessfully && itemSends == 0, "External modal leaked item move request.");
            ui.SetInteractionSuspended(false);
            var move = ui.RequestItemMove(ItemContainer.Inventory, 0, ItemContainer.Inventory, 1);
            Require(move.IsCompletedSuccessfully && itemSends == 1, "Local item mover was not invoked exactly once.");
            Require(ReferenceEquals(before, session.Snapshot) && session.Snapshot.Inventory[1].IsEmpty, "Item send optimistically mutated inventory.");
            ui.ReceiveInventory(MoveAcknowledgement());
            Require(session.Snapshot.Inventory[0].IsEmpty && session.Snapshot.Inventory[1].ItemId == 1, "Authoritative move reply did not update inventory.");
            ui.ConfirmInventoryDelete(1);
            yield return null; yield return null;
            SubmitHit(container.Q<Button>("prompt-cancel"));
            Require(deleted == -1, "Cancelling trash sent a delete request.");
            ui.ConfirmInventoryDelete(1);
            yield return null; yield return null;
            Require(container.Q<Image>(className: "gameplay-prompt-icon")?.image, "Missing deletion item preview.");
            Capture("inventory-delete");
            SubmitHit(container.Q<Button>("prompt-confirm"));
            Require(deleted == 1 && !session.Snapshot.Inventory[1].IsEmpty, "Inventory delete failed to wait for the server.");
            ui.ReceiveInventory(new ProtocolFrame(ProtocolInventory.DeleteSuccess, ProtocolInventory.DeletePayload(1)));
            Require(session.Snapshot.Inventory[1].IsEmpty, "Delete acknowledgement failed to clear its slot.");
            panel.scale = 1.5f;
            for (int frame = 0; frame < 8; frame++) yield return null;
            yield return new WaitForEndOfFrame();
            AvatarPixelEvidence(inventoryAvatar, "inventory-avatar-scaled-panel");
            AvatarPixelEvidence(inventory.Q<Image>("inventory-nano-portrait-0"), "inventory-nano-scaled-panel");
            panel.scale = 1;
            for (int frame = 0; frame < 8; frame++) yield return null;
            ui.CloseWindow();

            ui.Windows.Open(GameplayWindow.Nanos);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.View.Q("nano-inventory-slots").childCount == GameplayUiCatalog.Load().NanoIds.Count(), "Nano collection must retain locked catalog entries.");
            Capture("nanos");
            Require(Enumerable.Range(1, 68).All(id => ui.Windows.View.Q("inventory-nano-" + id) != null),
                "Every published Nano identity must be represented in the collection.");
            var nanoGrid = ui.Windows.View.Q<ScrollView>("nano-inventory-grid"); nanoGrid.scrollOffset = new Vector2(0, 10000);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("nanos-extended"); ui.CloseWindow();
            // Accepted task462 supplies an actual type4 Computress email; retain the
            // first running task for the journal checks below. No fabricated sender/body.
            var guideEntry = GameplayUiValidationData.Entry();
            Protocol0104.Write32(guideEntry.RawLoadData, 2032 + 52, 462);
            session.SetWorld(guideEntry);
            int mailOpenedAt = cues.Count;
            ui.Windows.Open(GameplayWindow.Mail);
            Require(cues.Skip(mailOpenedAt).SequenceEqual(new[] { "open_screen" }), "Opening mail must play one window cue.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.View.Q<Image>("mail-sender-portrait").image != null &&
                !string.IsNullOrWhiteSpace(ui.Windows.View.Q<Label>("mail-detail-body").text), "Mission guide mail must resolve its actual NPC portrait and message.");
            Capture("mail-guide");
            session.SetWorld(GameplayUiValidationData.Entry());
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.View.Q("mail-message").resolvedStyle.display == DisplayStyle.None,
                "Guide email remained selected after its running mission was removed.");
            Capture("mail-guide-empty");
            SubmitHit(ui.Windows.View.Q<Button>("mail-player-tab"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("mail-empty");
            int beforeMail = cues.Count;
            ui.ReceiveSocial(new ProtocolFrame(ProtocolMail.New, ProtocolTravel.Words(0)));
            ui.ReceiveSocial(new ProtocolFrame(ProtocolMail.New, ProtocolTravel.Words(1)));
            Require(ui.UnreadMail == 1 && cues.Skip(beforeMail).SequenceEqual(new[] { "email_arrived", "email_arrived" }),
                "New mail did not play both source arrival sounds for the positive notice.");
            var inbox = new byte[1024]; inbox[0] = 1;
            Protocol0104.Write64(inbox, 4, 1); Protocol0104.Write64(inbox, 12, 123);
            Protocol0104.WriteUtf16(inbox, 20, 9, "Alice"); Protocol0104.WriteUtf16(inbox, 38, 17, "Unity");
            Protocol0104.WriteUtf16(inbox, 72, 32, "2x4 Technology");
            ui.ReceiveSocial(new ProtocolFrame(ProtocolMail.ListOk, inbox));
            typeof(GameplayUiController).GetField("mailSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(ui, 1L);
            var letter = new byte[1084]; Protocol0104.Write64(letter, 0, 1);
            Protocol0104.WriteUtf16(letter, 8, 512, "After your timejump, parts of Dexter's time machine were damaged. Speak to Numbuh Two on the tree house platform.\n\nOffline visual validation fixture.");
            ui.ReceiveSocial(new ProtocolFrame(ProtocolMail.ReadOk, letter));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.View.Q("mail-message").resolvedStyle.display == DisplayStyle.Flex, "Selected mail must reveal its reading panel.");
            Capture("mail-read");
            float shortLetterFont = ui.Windows.View.Q<Label>("mail-detail-body").resolvedStyle.fontSize;
            string longText = string.Join("\n", Enumerable.Range(1, 23).Select(i => "Строка письма " + i));
            Protocol0104.WriteUtf16(letter, 8, 512, longText);
            ui.ReceiveSocial(new ProtocolFrame(ProtocolMail.ReadOk, letter));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var letterBody = FFView.Require<ScrollView>(ui.Windows.View, "mail-read-body");
            var letterText = FFView.Require<Label>(ui.Windows.View, "mail-detail-body");
            Require(letterText.text == longText && Mathf.Approximately(shortLetterFont, letterText.resolvedStyle.fontSize)
                && letterBody.verticalScroller.highValue > 0, "Long mail must overflow at the ordinary letter size.");
            Capture("mail-long-top");
            ScrollWheel(letterText, 8);
            yield return new WaitForSecondsRealtime(.25f); yield return new WaitForEndOfFrame();
            Require(letterBody.scrollOffset.y > 0, "Long mail did not scroll under the mouse.");
            Capture("mail-long-scrolled");
            SubmitHit(ui.Windows.View.Q<Button>("mail-compose"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("mail-compose");
            Require(cues.Skip(mailOpenedAt + 1).All(cue => cue != "open_screen" && cue != "close_screen"),
                "Changing mail folders or composing a message replayed a window cue.");
            SubmitHit(ui.Windows.View.Q<Button>("mail-close"));
            Require(ui.Windows.Current != GameplayWindow.Mail && cues.Last() == "close_screen", "Closing mail must play its close cue.");
            yield return null; yield return new WaitForEndOfFrame();
            Capture("mail-closed");

            ui.Windows.Open(GameplayWindow.Journal);
            yield return null; yield return null;
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "select-mission"));
            yield return null; yield return new WaitForEndOfFrame();
            string title = FFView.Require<Label>(ui.Windows.View, "mission-title-label").text;
            Require(!string.IsNullOrWhiteSpace(title) && title != "2248", "Journal lacks real localized mission title.");
            Capture("journal");
            var benMission = FFOne.World.WorldMissionCatalog.Load().Tasks.First(r => r.MissionType == 1 && r.RequiredGuide == 4);
            var journalEntry = GameplayUiValidationData.Entry();
            Protocol0104.Write32(journalEntry.RawLoadData, 2032, benMission.TaskId);
            session.SetWorld(journalEntry); yield return null; yield return null;
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "select-mission"));
            yield return null; yield return new WaitForEndOfFrame();
            var benBanner = ui.Windows.View.Q<Image>(className: "mission-banner");
            Require(benBanner?.image && benBanner.image == FFWidgets.Texture("ui/en/gameplay/mission/guide-banners/ben"),
                "Ben guide mission did not show the retained banner.");
            Capture("journal-ben");
            ui.CloseWindow();

            session.SetLocalPosition(new Vector3(4000, 0, 4000));
            ui.Windows.Open(GameplayWindow.Map);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var map = FFView.Require<Image>(ui.Windows.View, "map-image");
            Require(map.image != null, "Primary world map texture missing from bundle.");
            ui.ReceiveTravel(new ProtocolFrame(0x31000137, ProtocolTravel.Words(1, 1, 964)));
            ui.ReceiveTravel(new ProtocolFrame(0x3100013b, ProtocolTravel.Words(3, 1, 27, 964, 425000, 277000, 0)));
            yield return new WaitForSecondsRealtime(.12f);
            var filters = ui.Windows.View.Q("map-filters");
            var markers = ui.Windows.View.Q("map-markers");
            Require(filters.childCount == 12 && markers.Children().Any(m => m.resolvedStyle.display == DisplayStyle.Flex && m is Image icon && icon.image), "Map did not project the server NPC at Candy Cove.");
            ui.ReceiveTravel(new ProtocolFrame(0x31000137, ProtocolTravel.Words(1, 3, 964, 653, 54)));
            ui.ReceiveTravel(new ProtocolFrame(0x3100013b, ProtocolTravel.Words(3, 4,
                27, 964, 425000, 277000, 0, 28, 964, 400100, 400100, 0,
                2592, 653, 334976, 755183, 0, 30, 54, 400150, 400100, 0)));
            yield return new WaitForSecondsRealtime(.15f);
            int NpcMarkerCount() => markers.Children().Count(marker => marker.userData is int waypoint && waypoint == -1 &&
                marker.resolvedStyle.display == DisplayStyle.Flex);
            Require(NpcMarkerCount() == 2, "The world map must show both on-map operators and exclude the out-of-bounds Ranger Sam and mob.");
            Capture("map-server-placements");
            SubmitHit(ui.Windows.View.Q<Button>("show-filters"));
            yield return null;
            Require(filters.resolvedStyle.display == DisplayStyle.None, "Map filter strip did not collapse.");
            SubmitHit(ui.Windows.View.Q<Button>("show-filters"));
            yield return null; yield return null;
            for (int i = 0; i < filters.childCount; i++) SubmitHit((Button)filters[i]);
            yield return new WaitForSecondsRealtime(.12f);
            Require(!markers.Children().Any(m => m.resolvedStyle.display == DisplayStyle.Flex), "Disabled filters still displayed an NPC.");
            for (int i = 0; i < filters.childCount; i++) SubmitHit((Button)filters[i]);
            var viewport = ui.Windows.View.Q("map-viewport");
            void RightClick(Vector2 position)
            {
                var point = viewport.LocalToWorld(position);
                var element = container.panel.Pick(point);
                Require(element == viewport || viewport.Contains(element), "Map waypoint surface obstructed.");
                using var evt = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1, mousePosition = point });
                evt.target = element; element.SendEvent(evt);
            }
            RightClick(new Vector2(525, 298));
            yield return null; yield return null;
            var pin = markers.Children().Single(m => m.userData is int color && color == 0 && m.resolvedStyle.display == DisplayStyle.Flex);
            Require(((Image)pin).image, "Custom waypoint was missing its colored source texture.");
            Texture originalMap = map.image; Rect initialUv = map.uv;
            Capture("map-world");
            RightClick(pin.layout.center);
            yield return null;
            Require(pin.resolvedStyle.display == DisplayStyle.None, "Right-click on the same marker did not remove the waypoint.");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "my-view"));
            yield return new WaitForSecondsRealtime(.2f); yield return new WaitForEndOfFrame();
            Require(filters.resolvedStyle.display == DisplayStyle.None, "My View exposed world filters.");
            Debug.Log($"GAMEPLAY MAP LOCAL player={session.Snapshot.WorldPosition} uv={map.uv} items={string.Join(",", markers.Children().Select(m => m.tooltip + "/" + m.userData + "/" + m.resolvedStyle.display))}");
            Require(NpcMarkerCount() == 1, "Local view must show the nearby operator and exclude the distant NPC and non-radar mob.");
            Capture("map-nearby-only");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "world-view"));
            yield return null; yield return null;
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "zoom-in"));
            for (int i = 0; i < 12; i++) yield return null;
            yield return new WaitForEndOfFrame();
            Require(map.image != null && map.image != originalMap && map.uv.width < initialUv.width, "Map zoom did not change source texture and UV camera.");
            Capture("map-zoom");
            ui.CloseWindow();

            Require(ui.Windows.Open(GameplayWindow.Contacts), "Contacts unavailable.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            string contactsTitle = ui.Windows.View.Q<Label>(className: "social-title").text;
            Require(contactsTitle == (locale == "ru" ? "ДРУЗЬЯ И ГРУППА" : "FRIENDS AND GROUP"), "Contacts did not bind the requested language.");
            Capture("contacts");
            ui.CloseWindow();

            ui.Windows.Open(GameplayWindow.NanoCom);
            yield return null; yield return null;
            var input = FFView.Require<TextField>(container, "chat-input");
            var send = FFView.Require<Button>(container, "chat-send");
            var history = FFView.Require<ScrollView>(container, "chat-history");
            SubmitHit(container.Q<Button>("chat-group"));
            input.value = "Do not send to All";
            yield return null; yield return new WaitForEndOfFrame();
            Require(container.Q(className: "ff-chat-root").ClassListContains("ff-chat-channel-group") &&
                container.Q(className: "ff-chat-empty-channel").resolvedStyle.display == DisplayStyle.Flex &&
                container.Q<Label>(className: "ff-chat-empty-text").text == LocalizationCatalog.Lookup("ui.hud.chat.empty.group", locale) &&
                !send.enabledInHierarchy, "The solo Group tab did not keep its own empty state and prevent sending.");
            SendNavigation(send);
            Require(chatSends == 0, "A Group draft was sent to All.");
            input.value = "";
            Capture("chat-group-empty");
            SubmitHit(container.Q<Button>("chat-friend"));
            Require(container.Q(className: "ff-chat-root").ClassListContains("ff-chat-channel-empty"), "The empty friends tab did not show its source message.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(container.Q("buddy-list-window").resolvedStyle.display == DisplayStyle.Flex && !container.Q<Button>("buddy-warp").enabledSelf,
                "Buddy channel did not expose the native list with its empty-state controls.");
            Capture("buddy-list");
            SubmitHit(container.Q<Button>("buddy-add"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(container.Q<TextField>("buddy-full-name") != null && !container.Q<Button>("chat-all").enabledInHierarchy, "Add Buddy did not own input.");
            Capture("buddy-add");
            SubmitHit(container.Q<Button>("buddy-add-cancel"));
            SubmitHit(container.Q<Button>("chat-all"));
            string menuCaption = null; int menuEmote = -1, avatarEmote = -1;
            ui.RequestMenuChat = (caption, code) => { menuCaption = caption; menuEmote = code; return Task.CompletedTask; };
            ui.RequestEmote = code => { avatarEmote = code; return Task.CompletedTask; };
            SubmitHit(container.Q<Button>("chat-menu")); yield return null; yield return null;
            SubmitHit(container.Q<Button>("quick-item-0-7")); yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            Capture("quick-chat");
            var readyMessage = container.Q<Button>("quick-item-7-61");
            string readyText = readyMessage.Q<Label>("quick-label").text;
            SubmitHit(readyMessage);
            Require(!string.IsNullOrWhiteSpace(readyText) && menuCaption == readyText && menuEmote == 0 &&
                history.contentContainer.childCount == 0, "Quick message was not sent with its localized text and emote.");
            SubmitHit(container.Q<Button>("chat-emotes")); yield return null; yield return null;
            SubmitHit(container.Q<Button>("quick-item-0-18")); yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("emote-menu");
            SubmitHit(container.Q<Button>("quick-item-18-330"));
            Require(avatarEmote == 44, "Retrobution Idol Pose did not send the native emote code.");
            input.value = "Offline UI validation";
            ui.SetInteractionSuspended(true);
            Require(!send.enabledInHierarchy, "External modal did not disable chat action.");
            SendNavigation(send); // Deliberate disabled-action attempt: must not dispatch the sender.
            Require(chatSends == 0 && history.contentContainer.childCount == 0, "External modal leaked chat send/history.");
            ui.SetInteractionSuspended(false);
            yield return null; yield return null;
            SubmitHit(send);
            Require(chatSends == 1 && sentChat == "Offline UI validation", "Chat did not call local sender once.");
            Require(history.contentContainer.childCount == 0, "Chat fabricated an optimistic server echo.");
            Require(!ui.Windows.IsOpen, "Source ChatModify=0 submit did not close NanoCom.");
            int beforeChatEcho = cues.Count;
            ui.ReceiveChat(ChatEcho(sentChat));
            Require(cues.Skip(beforeChatEcho).SequenceEqual(new[] { "outgoing_chat" }), "Own chat echo must play its outgoing sound once.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(history.contentContainer.childCount == 1 && history.Q<Label>("chat-message").text == "UI Validation: Offline UI validation",
                "Authoritative chat echo/name binding failed.");
            Capture("chat-echo");
            ui.Windows.Open(GameplayWindow.NanoCom);
            for (int i = 0; i < 38; i++) ui.ReceiveChat(ChatEcho("Scroll " + i));
            yield return new WaitForSecondsRealtime(.35f); yield return new WaitForEndOfFrame();
            float chatBottom = history.scrollOffset.y;
            ScrollWheel(history.contentViewport, -8);
            yield return new WaitForSecondsRealtime(.3f); yield return new WaitForEndOfFrame();
            Require(chatBottom > 0 && history.scrollOffset.y < chatBottom, "Mouse wheel did not scroll populated chat history.");
            Capture("chat-wheel");
            var thumb = history.verticalScroller.slider.Q(className: "unity-base-slider__dragger");
            Require(thumb != null && thumb.worldBound.height > 0 && history.verticalScroller.resolvedStyle.display == DisplayStyle.Flex,
                "Active chat must expose a draggable history scrollbar.");
            float beforeThumb = history.scrollOffset.y;
            var thumbStart = thumb.worldBound.center;
            var thumbEnd = thumbStart + Vector2.down * 16;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = thumbStart }))
            { down.target = thumb; thumb.SendEvent(down); }
            yield return null;
            using (var motion = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = thumbEnd, delta = thumbEnd - thumbStart }))
            { motion.target = thumb; thumb.SendEvent(motion); }
            yield return null;
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = thumbEnd }))
            { up.target = thumb; thumb.SendEvent(up); }
            yield return null; yield return new WaitForEndOfFrame();
            Require(Mathf.Abs(history.scrollOffset.y - beforeThumb) > 1, "Dragging the chat scrollbar did not scroll the history.");
            Capture("chat-thumb-dragged");
            input.value="/cashmall";
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            SubmitHit(send);
            Require(ui.Windows.Current==GameplayWindow.CashMall && chatSends==1,"Cash Mall command reached the shop without sending public chat.");
            yield return new WaitForSecondsRealtime(1.15f); yield return new WaitForEndOfFrame();
            for(int tab=0;tab<5;tab++)SubmitHit(ui.Windows.View.Q<Button>("cash-tab-"+tab));
            Capture("cash-mall"); SubmitHit(ui.Windows.View.Q<Button>("cash-stuff"));
            Require(ui.Windows.Current==GameplayWindow.Inventory,"My Stuff opens the player's inventory.");
            ui.Windows.Close();

            int beforeTradeInvitation = cues.Count;
            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.Offer, ProtocolTrade.Participants(91, 91, 90)));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(trade.Snapshot.Phase == GameplayTradePhase.IncomingOffer, "Incoming trade fixture was not decoded.");
            Require(cues.Skip(beforeTradeInvitation).SequenceEqual(new[] { "affirmation_screen_popup" }),
                "Opening an incoming trade invitation must play its popup cue once.");
            Capture("trade-invitation");
            SubmitHit(FFView.Require<Button>(container, "prompt-confirm"));
            Require(acceptSends == 1 && trade.Snapshot.Phase == GameplayTradePhase.IncomingOffer, "Trade acceptance fabricated server acknowledgement.");
            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.Accepted, ProtocolTrade.Participants(90, 91, 90)));
            ui.ReceiveTrade(TradeItemAcknowledgement());
            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.CashRegistered, ProtocolTrade.CashPayload(91, 91, 90, 123)));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(ui.Windows.Current == GameplayWindow.Trade, "Accepted trade did not open source panel.");
            Require(FFView.Require<Label>(ui.Windows.View, "trade-other-taros-label").text == "123", "Trade peer cash binding failed.");
            Require(FFView.Require<Image>(ui.Windows.View, "trade-other-slot-0-image").image != null, "Trade peer item icon missing.");
            Capture("trade-active");
            var tradeSource = FFView.Require<Button>(ui.Windows.View, "inventory-slot-0");
            var tradeDestination = FFView.Require<Button>(ui.Windows.View, "trade-own-slot-0");
            Vector2 fromPoint = tradeSource.worldBound.center, toPoint = tradeDestination.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = fromPoint }))
            { down.target = tradeSource; tradeSource.SendEvent(down); }
            yield return null;
            using (var moveOffer = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = toPoint, delta = toPoint - fromPoint }))
            { moveOffer.target = tradeSource; tradeSource.SendEvent(moveOffer); }
            yield return null;
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = toPoint }))
            { up.target = tradeSource; tradeSource.SendEvent(up); }
            Require(!offeredItem.IsEmpty && offeredItem.ItemId == session.Snapshot.Inventory[0].ItemId &&
                offeredItem.InventorySlot == 0 && offeredItem.TradeSlot == 0 && trade.Snapshot.OwnItems[0].IsEmpty,
                "Dropping an inventory item into the trade window must send its offer and wait for acknowledgement.");
            var tradeInput = FFView.Require<TextField>(ui.Windows.View, "trade-chat-input");
            var tradeSend = FFView.Require<Button>(ui.Windows.View, "trade-chat-send");
            var tradeHistory = FFView.Require<ScrollView>(ui.Windows.View, "trade-chat-history");
            tradeInput.value = "Offline <b>trade</b> validation";
            ui.SetInteractionSuspended(true);
            Require(!tradeSend.enabledInHierarchy, "External modal did not disable trade chat.");
            SendNavigation(tradeSend);
            Require(tradeChatSends == 0, "External modal leaked trade chat send.");
            ui.SetInteractionSuspended(false);
            yield return null; yield return null;
            SubmitHit(tradeSend);
            Require(tradeChatSends == 1 && sentTradeChat == "Offline <b>trade</b> validation", "Trade chat did not invoke local sender exactly once.");
            Require(tradeHistory.contentContainer.childCount == 0, "Trade chat fabricated an optimistic server echo.");
            ui.ReceiveTrade(TradeChatEcho(sentTradeChat));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(tradeHistory.contentContainer.childCount == 1, "Authoritative trade chat echo did not produce exactly one row.");
            var tradeLine = tradeHistory.contentContainer[0].Q<Label>("chat-message");
            Require(tradeLine != null && !tradeLine.enableRichText && tradeLine.text == "UI Validation: Offline <b>trade</b> validation",
                "Trade chat must preserve sender and render user markup as plain text.");
            Capture("trade-chat-echo");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "trade-submit"));
            Require(confirmSends == 1 && !trade.Snapshot.OwnConfirmed, "Trade submit optimistically confirmed offer.");
            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.Confirmed, ProtocolTrade.Participants(90, 91, 90)));
            Require(trade.Snapshot.OwnConfirmed, "Trade confirmation reply was not applied.");
            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.Cancelled, ProtocolTrade.Participants(91, 91, 90)));
            Require(!ui.Windows.IsOpen && trade.Snapshot.Phase == GameplayTradePhase.Cancelled, "Server trade cancel did not close panel.");

            int requestedMentor = 0;
            ui.RequestMentor = id => { requestedMentor = id; return Task.CompletedTask; };
            ui.OpenMentor(23, 1);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("mentor-warning");
            SubmitHit(ui.Windows.View.Q<Button>("mentor-yes"));
            yield return null; yield return null;
            var mentor = ui.Windows.View;
            Require(!mentor.Q<Button>("mentor-primary").enabledSelf, "Guide selection is enabled before a mentor was chosen.");
            foreach (string mentorArt in new[] { "ben", "dexter", "mojo", "edd" })
                Require(HasArtwork(mentor.Q(className: mentorArt + "-portrait")), "Initial guide choice is missing portrait: " + mentorArt);
            SubmitHit(mentor.Q<Button>("mentor-toggle-ben"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("mentor-selection");
            SubmitHit(mentor.Q<Button>("mentor-primary"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(BackgroundTexture(mentor.Q(className: "mentor-confirm-art"))?.name == "ben_confirm", "Ben confirmation artwork changed.");
            Capture("mentor-confirm-ben");
            SubmitHit(mentor.Q<Button>("mentor-no"));
            yield return null; yield return null;
            Require(requestedMentor == 0 && session.Snapshot.Guide == 5, "Cancelling confirmation sent a guide change.");
            SubmitHit(mentor.Q<Button>("mentor-primary"));
            yield return null; yield return null;
            SubmitHit(mentor.Q<Button>("mentor-yes"));
            Require(requestedMentor == 4 && ui.CloseWindow() && ui.Windows.IsOpen && session.Snapshot.Guide == 5, "Guide changed before a server acknowledgement.");
            var reply = new byte[8]; Protocol0104.Write16(reply, 0, 4); Protocol0104.Write16(reply, 2, 1); Protocol0104.Write32(reply, 4, 2345);
            ui.ReceiveTravel(new ProtocolFrame(0x31000093, reply));
            Require(!ui.Windows.IsOpen && session.Snapshot.Guide == 4 && session.Snapshot.GuideCount == 1 && session.Snapshot.FusionMatter == 2345, "Guide acknowledgement did not settle the authoritative state.");

            int regeneration = 0, attempts = 0;
            var defeated = session.Snapshot.WithHealth(0).WithCondition(1 << 4);
            using (var revival = new GameplayResurrection(container, locale, (type, _) => { regeneration = type; attempts++; return Task.CompletedTask; }, FailMessage, _ => { }))
            {
                revival.Tick(defeated, true, false, 0);
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                Require(HasArtwork(revival.View.Q(className: "ff-resurrection-grim")) && revival.View.Q<Label>("resurrection-countdown").text.Contains("60"), "Resurrection art or initial countdown missing.");
                Capture("resurrection");
                SubmitHit(revival.View.Q<Button>("revive-phoenix"));
                revival.Tick(defeated, true, false, 61);
                Require(regeneration == 3 && attempts == 1, "Phoenix choice leaked a second automatic request.");
            }
            using (var revival = new GameplayResurrection(container, locale, (type, _) => { regeneration = type; attempts++; return Task.CompletedTask; }, FailMessage, _ => { }))
            {
                revival.Tick(defeated, true, false, 59);
                revival.Tick(defeated, false, false, 2);
                Require(attempts == 1, "Automatic revival fired before the source timeout.");
                revival.Tick(defeated, true, false, 1);
                revival.Tick(defeated, true, false, 1);
                Require(regeneration == 1 && attempts == 2, "Automatic revival did not choose the nearest Resurrect 'Em.");
                Require(revival.View.Q<Label>("resurrection-countdown").text.Contains("0") &&
                    !revival.View.Q<Label>("resurrection-countdown").text.Contains("-1"), "Automatic revival countdown did not stop at zero.");
            }
            void FailMessage(string message) => throw new InvalidOperationException(message);
            using (var local = new FusionClient())
            {
                var bag = GameplayUiValidationData.Entry();
                Buffer.BlockCopy(bag.RawLoadData, 244, bag.RawLoadData, 256, 12);
                Protocol0104.Write16(bag.RawLoadData, 244 + 2 * 12, 1);
                Protocol0104.Write16(bag.RawLoadData, 244 + 2 * 12 + 2, short.MaxValue);
                session.SetWorld(bag); ui.ConfigureTravel(local, default, bag); ui.OpenCroc();
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                var croc = ui.Windows.View;
                Require(HasArtwork(croc.Q(className: "croc-background")) && !croc.Q<Button>("croc-combine").enabledSelf, "Croc Pot did not start with an empty recipe.");
                var slotWithoutIcon = croc.Q<Button>("inventory-slot-2");
                var fallbackCaption = FFView.RequireClass<Label>(slotWithoutIcon, "inventory-item-count");
                Require(fallbackCaption.text == "#32767" && fallbackCaption.resolvedStyle.display == DisplayStyle.Flex,
                    "Croc Pot did not mark the occupied slot without a catalog icon.");
                SubmitHit(croc.Q<Button>("inventory-slot-0")); SubmitHit(croc.Q<Button>("croc-looks"));
                SubmitHit(croc.Q<Button>("inventory-slot-1")); SubmitHit(croc.Q<Button>("croc-stats"));
                yield return null; yield return new WaitForEndOfFrame();
                Require(croc.Q<Image>("looks-selected-icon").image && croc.Q<Image>("stats-selected-icon").image && croc.Q<Button>("croc-combine").enabledSelf, "Croc Pot failed to accept compatible style and stat items.");
                Capture("croc-pot");
                SubmitHit(croc.Q<Button>("croc-combine")); yield return null; yield return null; yield return new WaitForEndOfFrame();
                Capture("croc-confirm");
                SubmitHit(container.Q<Button>("prompt-cancel")); SubmitHit(croc.Q<Button>("croc-clear"));
                Require(!croc.Q<Button>("croc-combine").enabledSelf && !session.Snapshot.Inventory[0].IsEmpty, "Clearing a Croc Pot recipe mutated inventory.");
                var sourceSlot = croc.Q<Button>("inventory-slot-0");
                var dropSlot = croc.Q<Button>("croc-looks");
                Vector2 start = sourceSlot.worldBound.center;
                Vector2 end = dropSlot.worldBound.center;
                yield return null; yield return new WaitForEndOfFrame();
                Capture("croc-before-drag");
                using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = start }))
                { down.target = sourceSlot; sourceSlot.SendEvent(down); }
                yield return null;
                using (var dragMove = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseDrag, button = 0, mousePosition = end, delta = end - start }))
                { dragMove.target = sourceSlot; sourceSlot.SendEvent(dragMove); }
                yield return null; yield return new WaitForEndOfFrame();
                Capture("croc-drag-over-style");
                using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = end }))
                { up.target = sourceSlot; sourceSlot.SendEvent(up); }
                yield return null; yield return new WaitForEndOfFrame();
                Require(croc.Q<Image>("looks-selected-icon").image, "A bag item could not be dropped on the Croc Pot style slot.");
                Capture("croc-after-drag");
                ui.ReceiveInventory(MoveAcknowledgement());
                yield return null; yield return new WaitForEndOfFrame();
                Require(!croc.Q<Image>("inventory-slot-0-image").image && croc.Q<Image>("inventory-slot-1-image").image,
                    "The open Croc Pot kept stale inventory slot icons after a move acknowledgement.");
                Capture("croc-pot-inventory-updated");
                ui.CloseWindow();

                var mentorNpc = new WorldEntity { Kind = WorldEntityKind.Npc, NpcType = 1175, Id = 503, Hp = 1 };
                Require(ui.OpenNpcTravel(mentorNpc, WorldNpcCatalog.Load().Find(mentorNpc.NpcType)),
                    "Travel service menu for the guide NPC did not open.");
                yield return null; yield return new WaitForEndOfFrame();
                var missionRow = ui.Windows.View.Q<Button>("travel-missions");
                Require(missionRow != null, "Guide NPC service menu omitted its mission action.");
                var missionImage = FFView.RequireClass<Image>(missionRow, "npc-service-icon").image;
                string missionArt = "ui/" + (locale == "ru" ? "ru" : "en") + "/gameplay/mission/npc/current_mission";
                Require(missionImage && missionImage == ContentAssets.Load<Texture2D>(missionArt),
                    "Guide NPC mission action did not bind its localized artwork.");
                Capture("npc-mission-service");
                ui.CloseWindow();
            }

            var missionFlow = CaptureMissionFlow(locale);
            while (missionFlow.MoveNext()) yield return missionFlow.Current;
            var rewardCounters = CaptureRewardCounters(locale);
            while (rewardCounters.MoveNext()) yield return rewardCounters.Current;
            var minimapDistrict = CaptureMinimapDistrict(locale);
            while (minimapDistrict.MoveNext()) yield return minimapDistrict.Current;
            Require(!finished, "An earlier action failed during validation.");
            Debug.Log("GAMEPLAY UI PLAYER SMOKE PASSED: HUD, NanoCom, guide pages, control cues, mentor selection/confirmation with retained Ben, inventory, journal, map, chat, trade and modal input. Server replies were offline fixtures.");
            finished = true;
            Application.Quit(0);
        }

        IEnumerator CaptureMinimapDistrict(string locale)
        {
            ui.CloseWindow();
            var caption = FFView.Require<Label>(container, "hud-minimap-name");
            var entrance = CharacterLocations.Resolve(385000, 410000);
            string entranceName = CharacterLocations.District(entrance, locale);
            string exitName = CharacterLocations.District(CharacterLocations.Resolve(220000, 220000), locale);
            Require(entranceName.Length > 0 && exitName.Length > 0 && entranceName != exitName,
                "Dungeon minimap fixture needs two distinct named overworld districts.");

            session.Relocate(0, 385000, 410000, 0);
            Require(caption.text == entranceName, "Minimap did not show the overworld entrance district.");
            yield return null; yield return new WaitForEndOfFrame();
            Capture("minimap-dungeon-entrance");
            session.Relocate(0, 0, 0, 0);
            Require(caption.text.Length == 0, "Minimap named an unlabelled overworld tile.");
            session.Relocate(1, 220000, 220000, 0);
            Require(caption.text == entranceName, "Dungeon minimap forgot the last named overworld district.");
            Require(!ui.Windows.Open(GameplayWindow.Map), "A dungeon admitted the world map.");
            ui.Windows.Open(GameplayWindow.NanoCom);
            Require(!GameplayNanoComView.Action(ui.Windows.View, "map").enabledSelf, "The dungeon menu enabled its world-map button.");
            yield return null; yield return new WaitForEndOfFrame();
            Capture("dungeon-map-disabled");
            ui.CloseWindow();
            yield return null; yield return new WaitForEndOfFrame();
            Capture("minimap-dungeon-instance");

            session.Relocate(2, 0, 0, 0);
            Require(caption.text == entranceName, "Nested instance changed the dungeon minimap district.");
            string alternateLocale = locale == "ru" ? "en" : "ru";
            ui.SetLanguage(alternateLocale);
            Require(caption.text == CharacterLocations.District(entrance, alternateLocale),
                "Minimap did not relocalize the remembered district in an instance.");
            ui.SetLanguage(locale);
            session.Relocate(0, 220000, 220000, 0);
            Require(caption.text == exitName, "Dungeon exit did not display the new overworld district.");
            Require(ui.Windows.Supports(GameplayWindow.Map), "The dungeon exit did not restore world map access.");
            yield return null; yield return new WaitForEndOfFrame();
            Capture("minimap-dungeon-exit");

            var entry = GameplayUiValidationData.Entry();
            entry.MapNumber = 1;
            entry.PlayerId++;
            session.SetWorld(entry);
            Require(caption.text.Length == 0, "Minimap carried an overworld district into a different player's instance.");
            session.Relocate(0, 220000, 220000, 0);
            session.Clear();
            session.SetWorld(entry);
            Require(caption.text.Length == 0, "Minimap carried an overworld district into a new session after logout.");
            yield return null; yield return new WaitForEndOfFrame();
            Capture("minimap-dungeon-unknown");
        }

        IEnumerator CaptureRewardCounters(string locale)
        {
            var fm = FFView.RequireClass<VisualElement>(container, "reward-counter--fusion");
            var taros = FFView.RequireClass<VisualElement>(container, "reward-counter--taros");
            Require(fm.parent.parent == UiLayers.For(container).Host(UiLayer.Notification),
                "Reward counters must render above the chat and other HUD elements.");
            Require(!fm.ClassListContains("reward-counter--visible") && !taros.ClassListContains("reward-counter--visible"),
                "World entry or unrelated balance updates exposed a reward counter.");
            var before = session.Snapshot;
            var packet = new byte[56];
            Protocol0104.Write32(packet, 0, before.Taros + 12);
            Protocol0104.Write32(packet, 4, before.FusionMatter + 8);
            Protocol0104.Write32(packet, 8, before.NanoBattery + 3);
            Protocol0104.Write32(packet, 12, before.WeaponBattery + 4);
            packet[16] = 1;
            Protocol0104.Write16(packet, 36, 9);
            Protocol0104.Write16(packet, 38, 9);
            Protocol0104.Write32(packet, 40, 1);
            Protocol0104.Write32(packet, 48, 1);
            Protocol0104.Write32(packet, 52, before.Inventory.First(item => item.IsEmpty).Slot);
            Require(session.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Reward, packet)), "Currency reward packet was not applied.");
            Require(session.ApplyFrame(new ProtocolFrame(ProtocolGameplay.Reward, packet)), "Repeated reward post-state was not accepted.");
            for (int frame = 0; frame < 15; frame++) ui.Tick(.11f, null, false);
            yield return null; yield return new WaitForEndOfFrame();

            string artLocale = locale == "ru" ? "ru" : "en";
            void Check(VisualElement lane, string art, int expectedTotal)
            {
                var image = FFView.RequireClass<Image>(lane, "reward-counter-art").image;
                Require(image && image == ContentAssets.Load<Texture2D>(GameplayRewardCounters.ArtAddress(artLocale, art)),
                    "Reward counter did not bind the published " + artLocale + " artwork for " + art + ".");
                Require(lane.resolvedStyle.display == DisplayStyle.Flex && lane.worldBound.width > 0,
                    "Reward lane is absent or has no rendered bounds: " + art);
                string digits = string.Concat(lane.Query<Label>(className: "reward-counter-digit").ToList().Select(label => label.text));
                Require(int.TryParse(digits, out var total) && total == expectedTotal, "Reward counter did not reach the authoritative " + art + " total.");
            }
            Check(fm, "fusion-matter", session.Snapshot.FusionMatter);
            Check(taros, "taros", session.Snapshot.Taros);
            var drops = new[] { "crate", "potion", "boost" }
                .Select(role => FFView.RequireClass<VisualElement>(container, "reward-drop--" + role)).ToArray();
            Require(drops.All(drop => drop.resolvedStyle.display == DisplayStyle.Flex &&
                (drop.resolvedStyle.backgroundImage.texture || drop.resolvedStyle.backgroundImage.sprite) &&
                drop.Q<Label>(className: "reward-drop-text").text.StartsWith("+", StringComparison.Ordinal)),
                "Crate, potion and boost reward icons must each show their artwork and a localized count.");
            var fullBag = FFView.RequireClass<VisualElement>(container, "reward-inventory-full");
            Require(fullBag.resolvedStyle.display == DisplayStyle.None,
                "The full inventory warning appeared with free slots.");
            Capture("reward-counters");
            for (int frame = 0; frame < 24; frame++) ui.Tick(.11f, null, false);
            Require(!fm.ClassListContains("reward-counter--visible") && !taros.ClassListContains("reward-counter--visible"),
                "Reward counters remained visible after the two-second hold and fade.");
            Require(drops.All(drop => !drop.ClassListContains("reward-drop--visible")), "Repeated reward post-state queued duplicate drops.");
        }

        IEnumerator CaptureMissionFlow(string locale)
        {
            // This authored level-four task has distinct giver/receiver NPCs,
            // a reward and no kill or item objectives or follow-up task.
            var mission = WorldMissionCatalog.Load().Find(2092);
            Require(mission is { IsFirst: true, IsFinal: true, NextTask: 0, RewardId: > 0 },
                "The journal visual fixture no longer describes a complete mission.");
            var entry = GameplayUiValidationData.Entry();
            entry.Level = mission.MinimumLevel;
            Protocol0104.Write32(entry.RawLoadData, 2032, 0);
            Protocol0104.Write32(entry.RawLoadData, 2500, 0);
            session.SetWorld(entry);

            var giver = new WorldEntity { Kind = WorldEntityKind.Npc, NpcType = mission.GiverNpc, Id = 501, Hp = 1 };
            var receiver = new WorldEntity { Kind = WorldEntityKind.Npc, NpcType = mission.EndNpc, Id = 502, Hp = 1 };
            int accepted = 0, completed = 0;
            ui.ConfigureMissions((task, npc) => {
                if (npc == giver.Id) accepted = task;
                return Task.CompletedTask;
            }, (task, npc) => {
                if (npc == receiver.Id) completed = task;
                return Task.CompletedTask;
            }, null, null);
            ui.OpenNpcMissions(giver);
            yield return null; yield return null;
            var journal = ui.Windows.View;
            string title = GameplayUiCatalog.Load().Mission(mission.TaskId, locale)?.Title;
            Require(!string.IsNullOrWhiteSpace(title), "The journal fixture has no localized mission title.");
            Button MissionRow() => journal.Query<Button>("select-mission").ToList()
                .Single(button => button.parent.Q<Label>(className: "ff-row-title-label")?.text == title);
            SubmitHit(MissionRow());
            yield return null; yield return new WaitForEndOfFrame();
            var action = journal.Q<Button>(className: "mission-primary");
            Require(journal.Q(className: "ff-journal-details--offer") != null, "Journal did not show a mission offer.");
            Capture("journal-offer");
            SubmitHit(action);
            Require(accepted == mission.TaskId, "Mission offer did not dispatch the giver and task.");
            Require(session.ApplyFrame(new ProtocolFrame(0x3100001c, ProtocolTravel.Words(mission.TaskId, 0))),
                "Mission start fixture was not applied.");

            ui.OpenMissionReward(mission.TaskId, receiver);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(action.ClassListContains("mission-primary--complete"), "Journal did not show the turn-in action.");
            Capture("journal-turn-in");
            SubmitHit(action);
            Require(completed == mission.TaskId, "Turn-in did not dispatch the receiver and task.");
            Require(session.ApplyFrame(new ProtocolFrame(0x3100001e, ProtocolTravel.Words(mission.TaskId))) &&
                session.Snapshot.CompletedMissions.Contains(mission.MissionId), "Mission completion fixture did not settle.");

            SubmitHit(journal.Q<Button>("tab-completed"));
            yield return null; yield return null;
            SubmitHit(MissionRow());
            yield return null; yield return new WaitForEndOfFrame();
            Require(journal.Q(className: "ff-journal-details--complete") != null, "Completed mission details did not render.");
            Capture("journal-completed");
            ui.CloseWindow();
            float fadeDeadline = Time.realtimeSinceStartup + 4;
            while (container.Q(className: "npc-conversation-fade") != null)
            {
                Require(Time.realtimeSinceStartup < fadeDeadline, "NPC exit fade did not clear after leaving the journal.");
                yield return null;
            }
        }

        static Texture2D BackgroundTexture(VisualElement element)
        {
            var background = element.resolvedStyle.backgroundImage;
            return background.texture != null ? background.texture : background.sprite?.texture;
        }

        static bool HasArtwork(VisualElement element) =>
            element.worldBound.width > 0 && element.worldBound.height > 0 && BackgroundTexture(element) != null;

        static ProtocolFrame MoveAcknowledgement()
        {
            var bytes = new byte[40];
            Protocol0104.Write32(bytes, 0, (int)ItemContainer.Inventory);
            Protocol0104.Write32(bytes, 4, 0);
            Protocol0104.Write32(bytes, 20, (int)ItemContainer.Inventory);
            Protocol0104.Write32(bytes, 24, 1);
            Protocol0104.Write16(bytes, 28, 1); Protocol0104.Write16(bytes, 30, 1);
            return new ProtocolFrame(ProtocolInventory.MoveSuccess, bytes);
        }

        static ProtocolFrame ChatEcho(string text)
        {
            var bytes = new byte[264]; Protocol0104.Write32(bytes, 0, 90);
            Protocol0104.WriteUtf16(bytes, 4, 128, text);
            return new ProtocolFrame(ProtocolChat.Success, bytes);
        }

        static ProtocolFrame TradeChatEcho(string text)
        {
            // Success has no request-only freechat byte or failure error code: exact size272.
            var bytes = new byte[272];
            Array.Copy(ProtocolTrade.Participants(90, 91, 90), bytes, 12);
            Protocol0104.WriteUtf16(bytes, 12, 128, text);
            return new ProtocolFrame(ProtocolTradeChat.Success, bytes);
        }

        static void ScrollWheel(VisualElement target, float amount)
        {
            using var wheel = WheelEvent.GetPooled(new Event { type = EventType.ScrollWheel,
                mousePosition = target.worldBound.center, delta = new Vector2(0, amount) });
            wheel.target = target;
            target.SendEvent(wheel);
        }

        void ValidateNanoBelt()
        {
            var belt = ui.NanoHud.View;
            Require(FFView.Require<Image>(belt, "nano-belt-background").image != null, "Source Nano belt texture missing from bundle.");
            for (int slot = 0; slot < 3; slot++)
            {
                Require(session.Snapshot.NanoSlots[slot] == slot + 1, "Fixture Nano equipment slot was not decoded.");
                Require(FFView.Require<VisualElement>(belt, "nano-slot-" + slot).resolvedStyle.display == DisplayStyle.Flex,
                    "Equipped Nano slot is hidden: " + slot);
                foreach (string part in new[] { "portrait", "skill", "skill-back", "key" })
                    Require(FFView.Require<Image>(belt, $"nano-{slot}-{part}").image != null, "Nano source/catalog texture missing: " + slot + " " + part);
                foreach (string part in new[] { "skill", "key" })
                {
                    var control = FFView.Require<Image>(belt, $"nano-{slot}-{part}");
                    var bounds = control.worldBound;
                    Require(bounds.width > 0 && bounds.height > 0, "Nano control has no rendered area: " + slot + " " + part);
                    var originalPickingMode = control.pickingMode;
                    control.pickingMode = PickingMode.Position;
                    try
                    {
                        var lowerEdge = new Vector2(bounds.center.x, bounds.yMax - 1);
                        Require(control.panel.Pick(lowerEdge) == control,
                            "The lower edge of a Nano control is clipped or covered: " + slot + " " + part);
                    }
                    finally { control.pickingMode = originalPickingMode; }
                    Require(bounds.xMin >= container.worldBound.xMin && bounds.xMax <= container.worldBound.xMax &&
                        bounds.yMin >= container.worldBound.yMin &&
                        bounds.yMax <= container.worldBound.yMax - 9 * RetrobutionLayout.Scale(container.resolvedStyle.height),
                        "Nano control has lost its lower screen margin: " + slot + " " + part);
                }
                var portrait = FFView.Require<Image>(belt, $"nano-{slot}-portrait");
                var clip = FFView.Require<VisualElement>(belt, $"nano-{slot}-portrait-clip");
                float sourceScale = RetrobutionLayout.Scale(container.resolvedStyle.height);
                Require(Mathf.Abs(clip.resolvedStyle.height - 72) < .1f && clip.worldBound.height > 0,
                    "Nano portrait clip lost its source height before first layout: " + slot);
                Require(Mathf.Abs(portrait.worldBound.width - 72 * sourceScale) < .8f &&
                    Mathf.Abs(portrait.worldBound.height - 72 * sourceScale) < .8f,
                    "Nano portrait has a texture but no source-sized rendered geometry: " + slot);
                AvatarPixelEvidence(portrait, "nano-model-" + slot);
                int selectedStyles = 0;
                for (int style = 0; style < 3; style++)
                {
                    var front = FFView.Require<Image>(belt, $"nano-{slot}-front-{style}");
                    if (front.resolvedStyle.display != DisplayStyle.Flex) continue;
                    selectedStyles++;
                    Require(front.image != null && FFView.Require<Image>(belt, $"nano-{slot}-back-{style}").image != null &&
                        FFView.Require<Image>(belt, $"nano-{slot}-style-{style}").image != null, "Visible Nano type frame has missing texture.");
                }
                Require(selectedStyles == 1, "Nano type must select exactly one original frame family.");
            }
            Require(FFView.Require<Label>(belt, "nano-battery-count").text == "0050" &&
                FFView.Require<Label>(belt, "weapon-battery-count").text == "0025", "Battery counts do not retain source four-digit formatting.");
            foreach (string kind in new[] { "nano", "weapon" })
                Require(FFView.Require<VisualElement>(belt, kind + "-battery").resolvedStyle.display == DisplayStyle.Flex &&
                    FFView.Require<Image>(belt, kind + "-battery-background").image != null, "Known battery texture/count is hidden or missing: " + kind);
        }

        static ProtocolFrame TradeItemAcknowledgement()
        {
            var item = new TradeItem(1, 1, 0, 0, 0);
            var first = ProtocolTrade.ItemPayload(91, 91, 90, item);
            var bytes = new byte[44]; Array.Copy(first, bytes, first.Length);
            Array.Copy(first, 12, bytes, 28, 16);
            return new ProtocolFrame(ProtocolTrade.ItemRegistered, bytes);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static Color32[] ReadPixels(RenderTexture source)
        {
            var resolved = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var image = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                // Resolve MSAA before reading the camera target on Vulkan as well as D3D.
                Graphics.Blit(source, resolved); RenderTexture.active = resolved;
                image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; Destroy(image); RenderTexture.ReleaseTemporary(resolved); }
        }

        void AvatarPixelEvidence(Image binding, string name)
        {
            var texture = binding.image as RenderTexture;
            Require(texture && texture.IsCreated(), "Avatar has no live render texture: " + name);
            var physical = binding.worldBound.size * binding.panel.scaledPixelsPerPoint;
            float sampleScale = Mathf.Min(2, 4096 / Mathf.Max(physical.x, physical.y));
            Require(texture.width >= physical.x * sampleScale - 2 && texture.height >= physical.y * sampleScale - 2,
                $"Preview is undersized for its displayed pixels: {name}, texture={texture.width}x{texture.height}, display={physical}.");
            int supportedMsaa = SystemInfo.GetRenderTextureSupportedMSAASampleCount(new RenderTextureDescriptor(texture.width, texture.height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4 });
            Require(texture.antiAliasing >= supportedMsaa, "Preview did not enable supported MSAA: " + name);
            var pixels = ReadPixels(texture);
            int foreground = pixels.Count(c => c.a > 32 && (c.r < 240 || c.g < 240 || c.b < 240));
            int transparent = pixels.Count(c => c.a < 16);
            int colors = pixels.Where(c => c.a > 32).Select(c => (c.r / 8, c.g / 8, c.b / 8)).Distinct().Count();
            // Save even a failed target so a clear-only or all-white frame can be examined.
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                image.SetPixels32(pixels); image.Apply();
                string directory = UiSmokeLaunch.OutputDirectory("GameplayUiValidation");
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { Destroy(image); }
            Debug.Log($"GAMEPLAY AVATAR PIXELS {name}: {texture.width}x{texture.height}, foreground={foreground}, transparent={transparent}, colors={colors}");
            foreach (var camera in preview.GetComponentsInChildren<Camera>())
                if (camera.targetTexture == texture)
                    Debug.Log($"GAMEPLAY AVATAR CAMERA {name}: enabled={camera.enabled}, position={camera.transform.position}, near={camera.nearClipPlane}, far={camera.farClipPlane}, mask={camera.cullingMask}");
            Require(foreground > Mathf.Max(12, pixels.Length / 200) && transparent > pixels.Length / 100 && colors >= 12,
                $"Avatar pixels are blank, white, or lack a rendered character: {name} (foreground={foreground}, transparent={transparent}, colors={colors}).");
        }

        void AssertCompositedAvatar(Image binding, Color32[] shown, Color32[] hidden, string name)
        {
            var rect = binding.worldBound;
            int left = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, target.width), right = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, target.width);
            int top = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, target.height), bottom = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, target.height);
            int changed = 0;
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int index = (target.height - 1 - y) * target.width + x;
                    var a = shown[index]; var b = hidden[index];
                    if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 24) changed++;
                }
            Debug.Log($"GAMEPLAY AVATAR COMPOSITE {name}: changed={changed}, bounds={rect}");
            Require(changed > Mathf.Max(8, (right - left) * (bottom - top) / 500), "Rendered avatar did not contribute visible panel pixels: " + name);
        }

        void Hit(Button button)
        {
            Require(button.enabledInHierarchy, "Disabled hit target: " + button.name);
            Require(button.worldBound.width > 0 && button.worldBound.height > 0, "Empty hit target: " + button.name);
            Require(container.panel.Pick(button.worldBound.center) == button, "Covered or displaced hit target: " + button.name);
        }

        void SubmitHit(Button button) { Hit(button); button.Focus(); SendNavigation(button); }

        static void SendNavigation(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }

        void Capture(string name)
        {
            Require(target && target.IsCreated(), "UI render target was not initialized.");
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                Require(image.GetPixels32().Where((_, index) => index % 64 == 0).Select(c => (c.r, c.g, c.b, c.a)).Distinct().Count() > 20,
                    "Blank gameplay UI capture: " + name);
                string directory = UiSmokeLaunch.OutputDirectory("GameplayUiValidation");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                Debug.Log("GAMEPLAY UI CAPTURE: " + name);
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }

        void Fail(Exception error)
        {
            if (finished) return;
            finished = true; Debug.LogException(error);
            Debug.LogError("GAMEPLAY UI PLAYER SMOKE FAILED"); Application.Quit(1);
        }

        void OnDestroy()
        {
            if (preview) { preview.SetGameplayAvatar(null); preview.SetGameplayPortrait(null); }
            ui?.Dispose(); container?.RemoveFromHierarchy();
            if (panel) Destroy(panel);
            if (target) { target.Release(); Destroy(target); }
        }
    }
}
#endif
