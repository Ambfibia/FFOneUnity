#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Creation;
using FFOne.Integration;
using FFOne.Network;
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
            target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            Require(target.Create(), "Unable to create gameplay validation render target.");
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
            ui = new GameplayUiController(container, session, "en", () => { }, () => { });
            ui.Error += error => Fail(new InvalidOperationException("Gameplay UI action failed: " + error));
            ui.SetChatSender(text => { chatSends++; sentChat = text; ProtocolChat.FreeChatPayload(text); return Task.CompletedTask; });
            ui.SetItemMover((from, fromSlot, to, toSlot) => { itemSends++; return Task.CompletedTask; });
            trade = new GameplayTradeSession(session.Snapshot.PlayerId);
            ui.ConfigureTrade(trade, new GameplayTradeActions {
                Accept = (from, to) => { acceptSends++; ProtocolTrade.Participants(90, from, to); return Task.CompletedTask; },
                Refuse = (from, to) => Task.CompletedTask,
                Cancel = (from, to) => Task.CompletedTask,
                CancelOffer = (from, to) => Task.CompletedTask,
                Confirm = (from, to) => { confirmSends++; return Task.CompletedTask; },
                Cash = (from, to, amount) => Task.CompletedTask,
                AddItem = (from, to, item) => Task.CompletedTask,
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
            ui.Hud.SetExperienceVisible(true);
            yield return null; yield return new WaitForEndOfFrame();
            Require(Mathf.Abs(container.Q("hud-exp-fill").resolvedStyle.width - (container.resolvedStyle.width - 14) * .25f) < 1,
                "HUD optional EXP geometry differs from source fraction.");
            Capture("hud-exp-enabled");
            ui.Hud.SetExperienceVisible(false);

            Require(ui.Windows.Open(GameplayWindow.NanoCom), "NanoCom unavailable.");
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("nanocom");
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
            Require(ui.Windows.View.Q("inventory-nano-52") == null && ui.Windows.View.Q("inventory-nano-72") != null, "Extended accepted Nano identities must preserve late additions and the retired slot.");
            var nanoGrid = ui.Windows.View.Q<ScrollView>("nano-inventory-grid"); nanoGrid.scrollOffset = new Vector2(0, 10000);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("nanos-extended"); ui.CloseWindow();
            // Accepted task462 supplies an actual type4 Computress email; retain the
            // first running task for the journal checks below. No fabricated sender/body.
            var guideEntry = GameplayUiValidationData.Entry();
            Protocol0104.Write32(guideEntry.RawLoadData, 2032 + 52, 462);
            session.SetWorld(guideEntry);
            ui.Windows.Open(GameplayWindow.Mail);
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
            SubmitHit(ui.Windows.View.Q<Button>("mail-compose"));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Capture("mail-compose"); ui.CloseWindow();

            ui.Windows.Open(GameplayWindow.Journal);
            yield return null; yield return null;
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "select-mission"));
            yield return null; yield return new WaitForEndOfFrame();
            string title = FFView.Require<Label>(ui.Windows.View, "mission-title-label").text;
            Require(!string.IsNullOrWhiteSpace(title) && title != "2248", "Journal lacks real localized mission title.");
            Capture("journal");
            ui.CloseWindow();

            session.SetLocalPosition(new Vector3(4000, 0, 4000));
            ui.Windows.Open(GameplayWindow.Map);
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            var map = FFView.Require<Image>(ui.Windows.View, "map-image");
            Require(map.image != null, "Primary world map texture missing from bundle.");
            Texture originalMap = map.image; Rect initialUv = map.uv;
            Capture("map-world");
            SubmitHit(FFView.Require<Button>(ui.Windows.View, "zoom-in"));
            for (int i = 0; i < 12; i++) yield return null;
            yield return new WaitForEndOfFrame();
            Require(map.image != null && map.image != originalMap && map.uv.width < initialUv.width, "Map zoom did not change source texture and UV camera.");
            Capture("map-zoom");
            ui.CloseWindow();

            ui.Windows.Open(GameplayWindow.NanoCom);
            yield return null; yield return null;
            var input = FFView.Require<TextField>(container, "chat-input");
            var send = FFView.Require<Button>(container, "chat-send");
            var history = FFView.Require<ScrollView>(container, "chat-history");
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
            ui.ReceiveChat(ChatEcho(sentChat));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(history.contentContainer.childCount == 1 && history.Q<Label>("chat-message").text == "UI Validation: Offline UI validation",
                "Authoritative chat echo/name binding failed.");
            Capture("chat-echo");

            ui.ReceiveTrade(new ProtocolFrame(ProtocolTrade.Offer, ProtocolTrade.Participants(91, 91, 90)));
            yield return null; yield return null; yield return new WaitForEndOfFrame();
            Require(trade.Snapshot.Phase == GameplayTradePhase.IncomingOffer, "Incoming trade fixture was not decoded.");
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

            Require(!finished, "An earlier action failed during validation.");
            Debug.Log("GAMEPLAY UI PLAYER SMOKE PASSED: bundled source HUD and Nano belt/batteries, NanoCom, inventory icons/avatar, journal, map textures/zoom, normal/trade chat echoes, trade invitation/accept/item/cash/confirm/cancel, hit targets, modal input guards, and no optimistic mutations. All send delegates were offline fixtures.");
            finished = true;
            Application.Quit(0);
        }

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
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "GameplayUiValidation");
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
                string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "GameplayUiValidation");
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
