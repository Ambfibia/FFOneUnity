#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Integration;
using FFOne.Network;
using FFOne.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        internal GameplayUiSession LiveState => gameplayState;
        internal GameplayUiController LiveUi => gameplayUi;
        internal NetworkWorldController LiveWorld => networkWorld;
        internal VisualElement LiveRevivePrompt => revivePrompt?.View;
        internal int? LiveReviveCountdown => revivePrompt?.SecondsRemaining;
        internal bool LiveFrontendReady => frontendInitialized;
        internal async Task LiveMoveTo(int x, int y, int z)
        {
            ApplyServerRelocation(networkWorld?.MapNumber ?? 0, x, y, z);
            await Session.SendMovementAsync(x, y, z, 0, 0, 0, 0, 0, 0, true);
        }
        internal async Task LiveLogin(int port)
        {
            await Session.LoginAsync("127.0.0.1", port, "unityalice", "test-only-password", lifetime.Token);
            await EnterLiveFixtureWorld();
        }

        // The hidden standalone has no swapchain presentation. Drive the actual preview and world
        // cameras while the normal roster/world entry awaits their first rendered frames.
        internal async Task EnterLiveFixtureWorld()
        {
            async Task Enter()
            {
                await ShowSelectionAsync(); SelectRosterSlot(1); await EnterSelectedWorldAsync();
            }
            var destination = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            destination.Create();
            try
            {
                var entry = Enter();
                while (!entry.IsCompleted)
                {
                    await Task.Yield();
                    if (networkWorld && networkWorld.View && !networkWorld.View.targetTexture) networkWorld.View.targetTexture = destination;
                    foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                    {
                        if (!camera.enabled || !camera.targetTexture) continue;
                        var draw = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                        if (RenderPipeline.SupportsRenderRequest(camera, draw)) RenderPipeline.SubmitRenderRequest(camera, draw);
                    }
                }
                await entry;
            }
            finally
            {
                if (networkWorld && networkWorld.View && networkWorld.View.targetTexture == destination) networkWorld.View.targetTexture = null;
                destination.Release(); Destroy(destination);
            }
        }
        internal async Task LiveExit()
        { await Session.LeaveWorldAsync(); DisposeWorldFlow(); SetWorldPresentation(false); }
    }

    /// <summary>Explicit local-only acceptance of the real frontend/world/UI path against an isolated server.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed partial class LiveGameplayPlayerSmoke : MonoBehaviour
    {
        FrontendController frontend;
        FusionClient peer;
        RenderTexture worldTexture, uiTexture;
        PanelSettings panel;
        bool finished;
        string directory;
        readonly List<string> checks = new(), contentErrors = new();
        readonly HashSet<uint> received = new();
        readonly List<ProtocolFrame> peerFrames = new();
        readonly SocialSession peerSocial = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            if (DevelopmentConnection.Enabled && (args.Contains("--live-gameplay-smoke") ||
                args.Contains("--live-mentor-smoke") || args.Contains("--live-actor-animation-smoke") ||
                args.Contains("--live-zipline-smoke")))
                new GameObject("Live RustyFusion player acceptance").AddComponent<LiveGameplayPlayerSmoke>();
        }
        async void Start()
        {
            directory = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
            try
            {
                Directory.CreateDirectory(directory);
                var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable)).Endpoint;
                if (!endpoint.IsLoopback) throw new InvalidOperationException("Live acceptance requires loopback.");
                await Wait(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.LiveFrontendReady, "frontend ready");
                await frontend.LiveLogin(endpoint.Port);
                frontend.NetworkEventReceived += Observe;
                Check(frontend.Session.IsWorldLoaded && frontend.LiveState.Snapshot.IsActive, "real login and world load drive the gameplay HUD");
                if (Environment.GetCommandLineArgs().Contains("--live-zipline-smoke"))
                {
                    await Zipline(endpoint.Port);
                    await frontend.LiveExit();
                    Write(true, null); Debug.Log("LIVE ZIPLINE SMOKE PASSED"); finished = true; Application.Quit(0);
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--live-actor-animation-smoke"))
                {
                    await ActorAnimationCombat();
                    contentErrors.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                    await frontend.LiveExit();
                    Write(true, null); Debug.Log("LIVE ACTOR ANIMATION SMOKE PASSED"); finished = true; Application.Quit(0);
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--live-mentor-smoke"))
                {
                    await MentorChange(endpoint.Port);
                    Write(true, null); Debug.Log("LIVE MENTOR SMOKE PASSED"); finished = true; Application.Quit(0);
                    return;
                }
                frontend.LiveWorld.Replicas.ContentError += contentErrors.Add;
                peer = new FusionClient(); var roster = await peer.LoginAsync("127.0.0.1", endpoint.Port, "unitybobby", "test-only-password");
                var entry = await peer.EnterWorldAsync(roster.Single().Uid); await peer.CompleteLoadingAsync();
                NetworkEntityView remote = null;
                await Wait(() => (remote = frontend.LiveWorld.Replicas.GetComponentsInChildren<NetworkEntityView>().FirstOrDefault(v => v.Entity.Kind == WorldEntityKind.Player && v.Entity.Id == entry.PlayerId)) && remote.Avatar && remote.Avatar.IsReady,
                    "remote player is built from the server appearance");
                await Wait(() => frontend.LiveWorld.Replicas.GetComponentsInChildren<NetworkEntityView>().Count(v => v.Entity.Kind == WorldEntityKind.Npc && v.GetComponentsInChildren<Renderer>().Any()) >= 5,
                    "streamed NPC models have real renderers");
                await Wait(() => frontend.LiveWorld.Replicas.GetComponentsInChildren<NetworkEntityView>().Any(v => v.Definition?.HnpcAppearance >= 0 && v.Avatar && v.Avatar.IsReady),
                    "server HNPC appearance is assembled on the shared player rig");
                int x = entry.X + 100;
                await peer.SendMovementAsync(x, entry.Y, entry.Z, 90, 1, 0, 0, 100, 3, false);
                await Wait(() => remote && remote.Entity.X == x && Vector3.Distance(remote.transform.position, WorldCoordinates.FromServer(x, entry.Y, entry.Z)) < .2f,
                    "remote movement reaches its rendered transform");
                Check(remote.Entity.Character.DisplayName == "Bobby Unity", "remote social name uses the aligned 0104 UTF-16 appearance fields");
                await peer.SendMovementAsync(x, entry.Y, entry.Z, 90, 0, 0, 0, 0, 0, true);
                var document = frontend.GetComponent<UIDocument>();
                int width = UiSmokeLaunch.Dimension("-screen-width", 1280);
                int height = UiSmokeLaunch.Dimension("-screen-height", 720);
                uiTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); uiTexture.Create();
                UiSmokeLaunch.VerifyLocale(frontend, uiTexture);
                panel = Instantiate(document.panelSettings); panel.targetTexture = uiTexture; panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1; panel.clearColor = true; panel.colorClearValue = Color.clear; document.panelSettings = panel;
                worldTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); worldTexture.Create();
                frontend.LiveWorld.View.targetTexture = worldTexture;
                if (Environment.GetCommandLineArgs().Contains("--live-player-interaction-input"))
                {
                    await PlayerInteractionInput(entry, remote);
                    contentErrors.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                    await peer.LeaveWorldAsync();
                    await frontend.LiveExit();
                    Write(true, null); Debug.Log("LIVE PLAYER INTERACTION INPUT PASSED"); finished = true; Application.Quit(0);
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--automatic-revival"))
                {
                    await Combat();
                    await peer.LeaveWorldAsync();
                    await frontend.LiveExit();
                    Write(true, null); Debug.Log("LIVE AUTOMATIC REVIVAL PASSED"); finished = true; Application.Quit(0);
                    return;
                }
                await Frames(8); Capture(worldTexture, "world"); Capture(uiTexture, "hud");
                frontend.LiveUi.Windows.Open(GameplayWindow.Inventory); await Frames(8); Capture(uiTexture, "inventory");
                frontend.LiveUi.Windows.Open(GameplayWindow.Journal); await Frames(8); Capture(uiTexture, "journal");
                frontend.LiveUi.Windows.Open(GameplayWindow.Map);
                await Wait(() => frontend.LiveUi.Windows.View?.Q("map-markers")?.Children()
                    .Any(m => m.resolvedStyle.display == DisplayStyle.Flex && m is Image image && image.image) == true, "standalone world map projects authoritative RustyFusion NPCs");
                Capture(uiTexture, "world-map");
                Check(frontend.LiveState.Snapshot.Taros == 1010, "HUD reads the persisted trade and mail balance from the real world entry");
                Check(frontend.LiveState.Snapshot.RunningQuests.Any(q => q.TaskId == 5214), "journal reads the persisted running mission");
                frontend.LiveUi.Windows.Close();
                await Services();
                await SocialAndMail(entry);
                await RedeemCode();
                await Travel();
                await Sliders();
                await PersonalVehicles();
                await Combat();
                contentErrors.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                await peer.LeaveWorldAsync();
                await Wait(() => !remote || !remote.gameObject.activeInHierarchy, "remote actor is removed on peer exit");
                await frontend.LiveExit();
                Check(frontend.LiveWorld == null && !frontend.Session.IsInWorld, "world teardown follows acknowledged server save");
                Write(true, null); Debug.Log("LIVE GAMEPLAY PLAYER SMOKE PASSED"); finished = true; Application.Quit(0);
            }
            catch (Exception error)
            { Write(false, error.ToString()); Debug.LogError("LIVE GAMEPLAY PLAYER SMOKE FAILED: " + error); finished = true; Application.Quit(1); }
            finally { peer?.Dispose(); }
        }
        void Observe(NetworkEvent value)
        { if (value.IsShard && value.Kind == NetworkEventKind.Packet) received.Add(value.Frame.PacketType); }
        void Submit(Button button)
        {
            if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("Acceptance button is missing or disabled.");
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
        }

        async Task PlayerInteractionInput(WorldEntry remoteEntry, NetworkEntityView remote)
        {
            Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 99, "player interaction uses a normal account");
            var world = frontend.LiveWorld;
            var state = frontend.LiveState;
            var ui = frontend.LiveUi;
            var mouse = InputSystem.AddDevice<Mouse>();
            bool movement = world.MovementEnabled;
            world.MovementEnabled = false; // Hold the authored world and the staged camera steady while exercising Fire1.
            try
            {
                Debug.Log($"PLAYER INTERACTION PRECHECK: focus={Application.isFocused}, batch={Application.isBatchMode}, " +
                    $"window={ui.Windows.Current}, uiBlocked={ui.InputBlocked}, ready={world.IsReady}, worldError={world.LastError}");
                await Wait(() => world.IsReady, "player interaction world colliders are admitted");
                await Wait(() => !ui.InputBlocked, "no gameplay window blocks player interaction");
                await StagePlayerInteractionAim(world, remote);

                foreach (var fixture in new[] { (Slot: 38, Id: 706, Projectile: false, Name: "ordinary"), (Slot: 39, Id: 781, Projectile: true, Name: "launcher") })
                {
                    Check(state.Snapshot.Inventory[fixture.Slot].ItemId == fixture.Id &&
                        WorldWeaponCatalog.Load().Find(fixture.Id)?.Projectile == fixture.Projectile,
                        "fixture contains " + fixture.Name + " weapon");
                    await ui.RequestItemMove(ItemContainer.Inventory, fixture.Slot, ItemContainer.Equipment, 0);
                    await Wait(() => state.Snapshot.Equipment[0].ItemId == fixture.Id &&
                        !ui.WeaponChangePending, fixture.Name + " weapon equips through the server");
                    Check(!ui.InputBlocked && world.IsReady, fixture.Name + " equipped with gameplay input unobstructed");
                    Check(world.Target() == remote,
                        fixture.Name + " crosshair resolves the expected player: " + PlayerInteractionRay(world));
                    Check(Vector3.Distance(world.Player.position, remote.transform.position) <= 8,
                        fixture.Name + " target is within interaction range");
                    await Wait(() => Application.isFocused, fixture.Name + " player window is in the foreground");
                    await Frames(4);
                    Capture(worldTexture, "player-target-" + fixture.Name);
                    peerFrames.Clear();
                    received.Clear();
                    InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                    await Frames(2);
                    InputSystem.QueueStateEvent(mouse, new MouseState());
                    await Wait(() => ui.Windows.Current == GameplayWindow.Contacts &&
                        ui.Windows.View?.ClassListContains("player-interaction") == true,
                        fixture.Name + " Fire1 opens the player interaction menu");
                    float until = Time.realtimeSinceStartup + .5f;
                    while (Time.realtimeSinceStartup < until) await Task.Yield();
                    Check(!peerFrames.Any(packet => packet.PacketType is ProtocolTrade.Offer or ProtocolProjectiles.RocketPeer) &&
                        !received.Contains(ProtocolProjectiles.RocketSuccess),
                        fixture.Name + " Fire1 sends no trade offer or rocket");
                    Check(FFView.RequireClass<Label>(ui.Windows.View, "player-name").text == FFStyle.Caption(remote.Entity.Character.DisplayName),
                        fixture.Name + " menu names the targeted player");
                    Capture(uiTexture, "player-menu-" + fixture.Name);

                    if (!fixture.Projectile)
                    {
                        Submit(FFView.RequireClass<Button>(ui.Windows.View, "player-close"));
                        Check(!ui.Windows.IsOpen, "close returns from the player menu");
                        continue;
                    }
                    Submit(FFView.RequireClass<Button>(ui.Windows.View, "player-trade"));
                    int localId = state.Snapshot.PlayerId;
                    await Wait(() => peerFrames.Any(packet => packet.PacketType == ProtocolTrade.Offer &&
                        ProtocolTrade.TryDecode(packet, out var offer) && offer.FromId == localId && offer.ToId == remoteEntry.PlayerId),
                        "explicit Trade action sends one offer to the selected player");
                    Check(peerFrames.Count(packet => packet.PacketType == ProtocolTrade.Offer) == 1,
                        "selected player receives exactly one trade offer");
                    await Frames(4);
                    Capture(uiTexture, "player-menu-explicit-trade");
                    await peer.SendTradeRefuseAsync(localId, remoteEntry.PlayerId);
                    await Wait(() => received.Contains(ProtocolTrade.Refused), "selected player can decline the offer");
                }
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                world.MovementEnabled = movement;
            }
        }

        async Task StagePlayerInteractionAim(NetworkWorldController world, NetworkEntityView remote)
        {
            Physics.SyncTransforms();
            Vector3 aim = remote.GetComponent<Collider>().bounds.center;
            var feet = remote.transform.position;
            for (int step = 0; step < 8; step++)
            {
                var side = Quaternion.Euler(0, step * 45, 0) * Vector3.forward;
                var candidate = feet + side * 2.5f;
                var floor = Physics.RaycastAll(candidate + Vector3.up * 2, Vector3.down, 4, ~(1 << 31), QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.normal.y >= .6f && !hit.collider.transform.IsChildOf(world.Player))
                    .OrderBy(hit => hit.distance).FirstOrDefault();
                if (!floor.collider) continue;
                var stance = floor.point + Vector3.up * .05f;
                var cameraPosition = stance + Vector3.up * 1.55f + side * .25f;
                world.View.transform.position = cameraPosition;
                world.View.transform.LookAt(aim);
                if (world.Target() != remote)
                {
                    Debug.Log($"PLAYER INTERACTION RAY: candidate {step}, stance={stance}, {PlayerInteractionRay(world)}");
                    continue;
                }
                var coordinate = WorldCoordinates.ToServer(stance);
                await frontend.LiveMoveTo(coordinate.x, coordinate.y, coordinate.z);
                world.View.transform.position = cameraPosition;
                world.View.transform.LookAt(remote.GetComponent<Collider>().bounds.center);
                Physics.SyncTransforms();
                Check(world.Target() == remote, "authored floor and camera provide a clear player interaction ray");
                return;
            }
            throw new InvalidOperationException("No nearby clear authored floor and camera ray for player interaction. " + PlayerInteractionRay(world));
        }

        static string PlayerInteractionRay(NetworkWorldController world)
        {
            var ray = world.View.ViewportPointToRay(new Vector3(.5f, .5f));
            var colliders = Physics.RaycastAll(ray, 10, ~(1 << 31), QueryTriggerInteraction.Collide)
                .OrderBy(hit => hit.distance).Take(6).Select(hit => $"{hit.collider.name} at {hit.distance:F2}");
            var target = world.Target();
            return $"camera={world.View.transform.position}, player={world.Player.position}, target={target?.name ?? "none"}, hits={string.Join(", ", colliders)}";
        }

        void ConfirmQuantity()
        {
            var value = frontend.Root.Q<TextField>("prompt-value"); if (value != null) value.value = "1";
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
        }
        async Task RedeemCode()
        {
            int before = frontend.LiveState.Snapshot.Inventory.Count(item => item.ItemType == 7 && item.ItemId == 119);
            frontend.LiveUi.Windows.Open(GameplayWindow.Inventory);
            Submit(frontend.Root.Q<Button>("inventory-redeem"));
            frontend.Root.Q<TextField>("prompt-value").value = "StudioUiSmoke";
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.Root.Q("gameplay-prompt") == null &&
                frontend.LiveState.Snapshot.Inventory.Count(item => item.ItemType == 7 && item.ItemId == 119) == before + 1,
                "redeem dialog closes on server response and inventory receives the award");
            frontend.LiveUi.Windows.Close();
        }
        async Task OpenService(int type)
        {
            var npc = frontend.LiveWorld.Replicas.Session.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == type);
            await frontend.LiveMoveTo(npc.X, npc.Y, npc.Z);
            frontend.LiveUi.OpenNpcService(npc, WorldNpcCatalog.Load().Find(type));
        }
        async Task Services()
        {
            await OpenService(651);
            await Wait(() => frontend.LiveUi.Windows.View?.Q<Button>("vendor-row-0")?.enabledInHierarchy == true,
                "native vendor window waits for the real server catalog");
            await Frames(8); Capture(uiTexture, "vendor");
            int money = frontend.LiveState.Snapshot.Taros;
            Submit(frontend.LiveUi.Windows.View.Q<Button>("vendor-row-0")); ConfirmQuantity();
            await Wait(() => frontend.LiveState.Snapshot.Taros < money && frontend.LiveState.Snapshot.Inventory.Any(i => i.ItemType == 0 && i.ItemId == 366),
                "native purchase confirmation applies the server item and currency");
            int slot = frontend.LiveState.Snapshot.Inventory.First(i => i.ItemType == 0 && i.ItemId == 366).Slot;
            int afterBuy = frontend.LiveState.Snapshot.Taros;
            Submit(frontend.LiveUi.Windows.View.Q<Button>("inventory-slot-" + slot)); ConfirmQuantity();
            await Wait(() => frontend.LiveState.Snapshot.Inventory[slot].IsEmpty && frontend.LiveState.Snapshot.Taros > afterBuy,
                "native sale confirmation applies ItemStay and server currency");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("service-tab-restore"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("buyback-row-0")); ConfirmQuantity();
            await Wait(() => frontend.LiveState.Snapshot.Taros == afterBuy && frontend.LiveState.Snapshot.Inventory[slot].ItemId == 366,
                "native buyback restores the accepted item and original sale cost");
            int battery = frontend.LiveState.Snapshot.WeaponBattery;
            Submit(frontend.LiveUi.Windows.View.Q<Button>("service-weapon-battery")); ConfirmQuantity();
            await Wait(() => frontend.LiveState.Snapshot.Taros == afterBuy - 100 && frontend.LiveState.Snapshot.WeaponBattery == battery + 100,
                "native battery purchase applies both authoritative counters");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("close"));
            await Wait(() => !frontend.LiveUi.Windows.IsOpen, "native vendor close restores world input");
            await OpenService(686);
            await Wait(() => frontend.LiveState.Snapshot.BankNumber == 0 && frontend.LiveUi.Windows.View?.Q<Button>("bank-slot-199")?.enabledInHierarchy == true,
                "native main bank renders all two hundred accepted slots");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("inventory-slot-" + slot));
            await Wait(() => frontend.LiveState.Snapshot.Bank[0].ItemId == 366 && frontend.LiveState.Snapshot.Inventory[slot].IsEmpty,
                "native bank deposit commits both server post-states");
            await Frames(8); Capture(uiTexture, "bank");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("bank-slot-0"));
            await Wait(() => frontend.LiveState.Snapshot.Bank[0].IsEmpty && frontend.LiveState.Snapshot.Inventory[slot].ItemId == 366,
                "native bank withdrawal commits both server post-states");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("close"));
            await Wait(() => frontend.LiveState.Snapshot.BankNumber == -1 && !frontend.LiveUi.Windows.IsOpen,
                "native bank close waits for the server acknowledgment");
            await OpenService(2586);
            string accessMessage = frontend.Language == "ru" ? "карта доступа" : "membership card";
            await Wait(() => frontend.LiveUi.Windows.View?.Q<Label>("service-status")?.text.Contains(accessMessage) == true,
                "native membership bank explains the server access-required rejection");
            Check(frontend.LiveState.Snapshot.BankNumber == -1, "rejected membership bank never exposes a stale bank snapshot");
            await Frames(8); Capture(uiTexture, "bank-locked");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("close"));
        }
        async Task SocialAndMail(WorldEntry peerEntry)
        {
            frontend.LiveUi.OpenSocial();
            await Wait(() => frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid), "native contacts list loads the persisted real friendship");
            await frontend.LiveMoveTo(peerEntry.X, peerEntry.Y, peerEntry.Z);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("social-refresh"));
            await Wait(() => frontend.LiveUi.Windows.View.Q<Button>("group-invite-" + peerEntry.PlayerId)?.enabledInHierarchy == true, "native social controls wait for the pending refresh write");
            frontend.LiveUi.Windows.Close();
            var selected=frontend.LiveWorld.Replicas.Actors.First(a=>a.Entity.Kind==WorldEntityKind.Player && a.Entity.Id==peerEntry.PlayerId);
            frontend.LiveUi.OpenPlayer(selected.Entity);
            await Frames(4);Capture(uiTexture,"player-menu");
            peerFrames.Clear(); Submit(frontend.LiveUi.Windows.View.Q<Button>("player-group"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.Invite), "native group invite button reaches the real peer");
            await peer.AnswerGroupAsync(frontend.LiveState.Snapshot.PlayerId, true);
            frontend.LiveUi.OpenSocial();
            await Wait(() => frontend.LiveUi.Social.Members.Count == 2 && frontend.LiveUi.Windows.View.Q<Label>("group-member-" + peerEntry.PlayerId) != null &&
                frontend.LiveUi.Windows.View.Q<Button>("social-send").enabledInHierarchy,
                "native group panel renders the authoritative roster");
            peerFrames.Clear(); frontend.LiveUi.Windows.View.Q<TextField>("social-input").value = "Unity native group message";
            Submit(frontend.LiveUi.Windows.View.Q<Button>("social-send"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.GroupChatOk), "native group chat button delivers to the peer");
            await Frames(8); Capture(uiTexture, "group");

            frontend.LiveUi.Windows.Open(GameplayWindow.NanoCom);
            var chat = frontend.Root.Q(className: "ff-chat-root");
            Submit(FFView.RequireClass<Button>(chat, "ff-chat-group"));
            await Frames(8); Capture(uiTexture, "group-chat-tab");
            const string tabMessage = "Unity main Group tab message";
            peerFrames.Clear();
            FFView.RequireClass<TextField>(chat, "ff-chat-input").value = tabMessage;
            Submit(FFView.RequireClass<Button>(chat, "ff-chat-send"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.GroupChatOk &&
                    ProtocolSocial.TryDecode(f, out var message) && message.Text == tabMessage) &&
                chat.Query<Label>(className: "ff-chat-message").ToList().Any(label => label.text.EndsWith(tabMessage, StringComparison.Ordinal)),
                "main Group tab sends to the peer and displays authoritative echo");
            await Frames(8); Capture(uiTexture, "group-chat-tab-sent");
            frontend.LiveUi.OpenSocial();
            var buddy = frontend.LiveUi.Social.Friends.First(f => f?.Uid == peerEntry.CharacterUid);
            await Wait(() => frontend.LiveUi.Windows.View?.Q<Button>("friend-chat-" + buddy.Slot)?.enabledInHierarchy == true,
                "native friend chat action is ready after reopening Contacts");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("friend-chat-" + buddy.Slot)); peerFrames.Clear();
            frontend.LiveUi.Windows.View.Q<TextField>("social-input").value = "Unity native private message";
            Submit(frontend.LiveUi.Windows.View.Q<Button>("social-send"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.WhisperOk), "native friend chat button delivers a private message");
            await Frames(8); Capture(uiTexture, "friends");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("group-leave"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 0 && peerSocial.Members.Count == 0, "native leave-group button clears both rosters");
            await peer.InviteGroupAsync(frontend.LiveState.Snapshot.PlayerId);
            await Wait(() => frontend.Root.Q<Button>(className: "nanocom-message-open")?.resolvedStyle.display == DisplayStyle.Flex,
                "group invitation appears on NanoCom");
            await Frames(6); Capture(uiTexture,"group-invitation");
            Submit(frontend.Root.Q<Button>(className: "nanocom-message-open"));
            await Wait(() => frontend.Root.Q<Button>("prompt-confirm") != null, "native incoming group request opens a confirmation");
            await Frames(4); Capture(uiTexture,"group-invitation-open");
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 2, "native group confirmation joins the peer group");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("group-leave"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 0 && peerSocial.Members.Count == 0, "native incoming group can be left through the same UI");
            await Wait(() => frontend.LiveUi.Windows.View.Q<Button>("friend-block-" + buddy.Slot)?.enabledInHierarchy == true, "native friend actions are ready after group exit");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("friend-block-" + buddy.Slot));
            await Wait(() => frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid && f.Blocked), "native block button applies the server blocked entry");
            frontend.OpenSettings();
            Submit(frontend.Root.Q<Button>(className: "tab-social"));
            await Wait(() => frontend.Root.Q<Button>(className: "option-blocked-player")?.resolvedStyle.display == DisplayStyle.Flex,
                "Settings social tab presents the acknowledged block");
            var ignored = frontend.Root.Q<Button>(className: "option-blocked-player");
            Submit(ignored);
            Submit(frontend.Root.Q<Button>("unignore"));
            await Wait(() => !frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid), "Settings unignore applies the acknowledged removal");
            Submit(frontend.Root.Q<Button>("settings-close"));
            frontend.LiveUi.Windows.Close();
            await peer.RequestFriendByNameAsync("Alice", "Unity");
            await Wait(() => frontend.Root.Q<Button>(className: "nanocom-message-open")?.resolvedStyle.display == DisplayStyle.Flex,
                "named buddy request appears on NanoCom");
            Submit(frontend.Root.Q<Button>(className: "nanocom-message-open"));
            await Wait(() => frontend.Root.Q<Button>("prompt-confirm") != null, "native named friend request opens its confirmation");
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid && !f.Blocked), "native friend confirmation restores the real friendship");
            frontend.LiveUi.Windows.Close(); frontend.LiveUi.Windows.Open(GameplayWindow.NanoCom);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-player-tab"));
            await Wait(() => !frontend.LiveUi.MailBusy, "native inbox waits for its real page");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-compose"));
            var view = frontend.LiveUi.Windows.View;
            view.Q<TextField>("mail-subject").value = "Unity native mail";
            view.Q<TextField>("mail-body").value = "Проверка кнопок Unity и вложений.";
            view.Q<TextField>("mail-taros").value = "10";
            Submit(view.Q<Button>("mail-inventory-1")); ConfirmQuantity();
            await Frames(8); Capture(uiTexture, "mail-compose");
            int money = frontend.LiveState.Snapshot.Taros; peerFrames.Clear();
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-send")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveState.Snapshot.Taros == money - 80 && frontend.LiveState.Snapshot.Inventory[1].IsEmpty,
                "native send button commits postage, attached Taros and the sender slot");
            await peer.ListMailAsync(1);
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolMail.ListOk), "native outgoing mail is visible in the peer inbox");
            ProtocolMail.TryDecode(peerFrames.Last(f => f.PacketType == ProtocolMail.ListOk), out var page);
            long index = page.Summaries.Single().Index; peerFrames.Clear(); await peer.TakeAllMailItemsAsync(index);
            await Wait(() => peerFrames.Any(f => f.PacketType == 0x31000061), "peer receives the native-composed item through give-item acknowledgment");
            var given = peerFrames.Last(f => f.PacketType == 0x31000061); int slot = Protocol0104.Read32(given.Payload, 4); var item = ProtocolServices.Item(given.Payload, 8);
            Check(item.ItemId == 67, "native-composed attachment keeps its accepted item identity");
            peerFrames.Clear(); await peer.SendMailAsync(frontend.LiveState.Snapshot.CharacterUid, "Ответ RustyFusion", "Предмет возвращён.", 5, new[] { new MailAttachment(slot, item) });
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolMail.SendOk), "peer sends an item and Taros back to the native client");
            await Wait(() => frontend.LiveUi.UnreadMail > 0, "native mailbox receives the new-mail notification");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-refresh"));
            await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveUi.Windows.View.Query<Button>().ToList().Any(b => b.name.StartsWith("mail-row-")), "native inbox renders the incoming email row");
            Submit(frontend.LiveUi.Windows.View.Query<Button>().ToList().First(b => b.name.StartsWith("mail-row-")));
            await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveUi.Windows.View.Q<Button>("mail-attachment-1").enabledInHierarchy, "native read action exposes the returned item and UTF-16 body");
            await Frames(8); Capture(uiTexture, "mail-read");
            int free = frontend.LiveState.Snapshot.Inventory.First(i => i.IsEmpty).Slot;
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-take-all"));
            await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveState.Snapshot.Inventory[free].ItemId == 67, "native take-all button commits the server-selected inventory destination");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-take-taros"));
            await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveState.Snapshot.Taros == money - 75, "native Taros claim updates the actual HUD wallet");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-delete")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => !frontend.LiveUi.MailBusy && !frontend.LiveUi.Windows.View.Query<Button>().ToList().Any(b => b.name.StartsWith("mail-row-")), "native delete confirmation removes the acknowledged email from the inbox");
            frontend.LiveUi.Windows.Close();
        }
        async Task Combat()
        {
            var mob = frontend.LiveWorld.Replicas.Session.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1 && e.Hp > 0);
            int nanoId = frontend.LiveState.Snapshot.NanoSlots[0];
            int stamina = frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == nanoId).Stamina;
            await frontend.Session.ActivateNanoAsync(0);
            await Wait(() => frontend.LiveState.Snapshot.ActiveNanoSlot == 0, "native HUD applies the server Nano activation");
            await frontend.Session.UseNanoSkillAsync(new[] { mob.Id }, mob.Id);
            await Wait(() => received.Contains(ProtocolNanoSkill.Success) && frontend.LiveState.Snapshot.Nanos.Single(n => n.Id == nanoId).Stamina < stamina,
                "native HUD applies the real Nano skill stamina result");
            await frontend.LiveMoveTo(mob.X - 100, mob.Y, mob.Z);
            int hp = mob.Hp;
            await frontend.Session.AttackNpcsAsync(new[] { mob.Id });
            await Wait(() => received.Contains(ProtocolGameplay.AttackSuccess) && frontend.LiveWorld.Replicas.Session.TryGet(WorldEntityKind.Npc, mob.Id, out var changed) && changed.Hp < hp,
                "native world applies the authoritative basic attack target HP");
            await ObserveMobAttack(mob.Id);
            await Wait(() => frontend.LiveState.Snapshot.Hp == 0 && frontend.LiveRevivePrompt != null,
                "native NPC combat reaches zero HP and opens the real revival dialog");
            await Frames(8); Capture(uiTexture, "death");
            bool automatic = Environment.GetCommandLineArgs().Contains("--automatic-revival");
            if (automatic)
            {
                await Wait(() => frontend.LiveReviveCountdown == 1,
                    "automatic revival keeps the countdown visible near its end");
                Capture(uiTexture, "revival-countdown");
            }
            else
            {
                var confirm = frontend.LiveRevivePrompt.Q<Button>("prompt-confirm");
                Check(confirm != null && confirm.enabledInHierarchy, "revival dialog exposes its enabled confirmation");
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = confirm; confirm.SendEvent(submit);
            }
            await Wait(() => received.Contains(ProtocolGameplay.RegenSuccess) && frontend.LiveState.Snapshot.Hp > 0 && frontend.LiveRevivePrompt == null,
                automatic ? "automatic revival reaches the living server post-state without submitting a button"
                    : "revival dialog sends the request and native HUD applies the living server post-state");
            Check(Vector3.Distance(frontend.LiveWorld.Player.position, frontend.LiveState.Snapshot.WorldPosition) < .2f,
                "revived world position agrees with the HUD after authoritative relocation");
            await Frames(8); Capture(uiTexture, "revived");
        }

        async Task ActorAnimationCombat()
        {
            var world = frontend.LiveWorld;
            WorldEntity mob = null;
            await Wait(() => (mob = world.Replicas.Session.Entities.FirstOrDefault(entity =>
                entity.Kind == WorldEntityKind.Npc && entity.NpcType == 1 && entity.Hp > 0)) != null,
                "the fixture mob enters real server interest");
            await frontend.LiveMoveTo(mob.X - 100, mob.Y, mob.Z);
            await frontend.Session.AttackNpcsAsync(new[] { mob.Id });
            await Wait(() => received.Contains(ProtocolGameplay.AttackSuccess) &&
                world.Replicas.Session.TryGet(WorldEntityKind.Npc, mob.Id, out var postState) && postState.Hp < mob.Hp,
                "a real player attack updates the live mob's health");
            await ObserveMobAttack(mob.Id);
        }

        async Task ObserveMobAttack(int id)
        {
            NetworkEntityView replica = null;
            await Wait(() => (replica = frontend.LiveWorld.Replicas.Actors.FirstOrDefault(actor =>
                actor.Entity.Kind == WorldEntityKind.Npc && actor.Entity.Id == id)) && replica.NpcAnimator &&
                replica.NpcAnimator.Animator.runtimeAnimatorController, "the live mob loaded its authored Animator Controller");
            await Wait(() => replica && (replica.NpcAnimator.CurrentClip is "melee1" or "melee2" or "attack1") &&
                replica.NpcAnimator.MotionTime > .02f, "a real server attack starts and advances the native mob attack clip");
        }

        void Update()
        {
            if (peer == null) return;
            while (peer.TryDequeueEvent(out var e))
            {
                if (e.Kind == NetworkEventKind.Disconnected && !finished) Debug.LogWarning("Acceptance peer: " + e.Message);
                if (e.IsShard && e.Kind == NetworkEventKind.Packet)
                { peerFrames.Add(e.Frame); if (ProtocolSocial.TryDecode(e.Frame, out var u)) peerSocial.Apply(u); }
            }
        }
        void LateUpdate()
        {
            if (finished || !worldTexture) return;
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!camera.enabled || !camera.targetTexture) continue;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = camera.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            }
        }
        async Task Wait(Func<bool> condition, string description)
        {
            float until = Time.realtimeSinceStartup + 120;
            while (!condition()) { if (Time.realtimeSinceStartup > until) throw new TimeoutException(description); await Task.Yield(); }
            Check(true, description);
        }
        static async Task Frames(int count) { for (int i = 0; i < count; i++) await Task.Yield(); }
        void Check(bool valid, string description)
        { if (!valid) throw new InvalidOperationException(description); checks.Add(description); Debug.Log("LIVE CHECK: " + description); }
        void Capture(RenderTexture texture, string name)
        {
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false); var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture; image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                int colors = image.GetPixels32().Where((_, i) => i % 32 == 0).Select(c => (c.r, c.g, c.b)).Distinct().Count();
                Check(colors > 20, name + " has rendered pixels (" + colors + " colors)");
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }
        void Write(bool passed, string error)
        {
            if (directory == null) return;
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonConvert.SerializeObject(new { passed, checks,
                contentComplete = contentErrors.Count == 0, contentErrors = contentErrors.Distinct().ToArray(), error }, Formatting.Indented));
        }
        void OnDestroy()
        {
            if (frontend) frontend.NetworkEventReceived -= Observe;
            peer?.Dispose();
            if (worldTexture) { worldTexture.Release(); Destroy(worldTexture); }
            if (uiTexture) { uiTexture.Release(); Destroy(uiTexture); }
            if (panel) Destroy(panel);
        }
    }
}
#endif
