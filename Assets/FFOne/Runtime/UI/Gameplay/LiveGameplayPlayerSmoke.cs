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
        internal async Task LiveMoveTo(int x, int y, int z)
        {
            ApplyServerRelocation(networkWorld?.MapNumber ?? 0, x, y, z);
            await Session.SendMovementAsync(x, y, z, 0, 0, 0, 0, 0, 0, true);
        }
        internal async Task LiveLogin(int port)
        {
            await Session.LoginAsync("127.0.0.1", port, "unityalice", "test-only-password", lifetime.Token);
            ShowSelection(); SelectRosterSlot(1); await EnterSelectedWorldAsync();
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
            if (Environment.GetCommandLineArgs().Contains("--live-gameplay-smoke") && DevelopmentConnection.Enabled)
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
                await Wait(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.Root?.panel != null, "frontend ready");
                await frontend.LiveLogin(endpoint.Port);
                frontend.NetworkEventReceived += Observe;
                Check(frontend.Session.IsWorldLoaded && frontend.LiveState.Snapshot.IsActive, "real login and world load drive the gameplay HUD");
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
                uiTexture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); uiTexture.Create();
                panel = Instantiate(document.panelSettings); panel.targetTexture = uiTexture; panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.scale = 1; panel.clearColor = true; panel.colorClearValue = Color.clear; document.panelSettings = panel;
                worldTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); worldTexture.Create();
                frontend.LiveWorld.View.targetTexture = worldTexture;
                await Frames(8); Capture(worldTexture, "world"); Capture(uiTexture, "hud");
                frontend.LiveUi.Windows.Open(GameplayWindow.Inventory); await Frames(8); Capture(uiTexture, "inventory");
                frontend.LiveUi.Windows.Open(GameplayWindow.Journal); await Frames(8); Capture(uiTexture, "journal");
                Check(frontend.LiveState.Snapshot.Taros == 1010, "HUD reads the persisted trade and mail balance from the real world entry");
                Check(frontend.LiveState.Snapshot.RunningQuests.Any(q => q.TaskId == 5214), "journal reads the persisted running mission");
                frontend.LiveUi.Windows.Close();
                await Services();
                await SocialAndMail(entry);
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
        void ConfirmQuantity()
        {
            var value = frontend.Root.Q<TextField>("prompt-value"); if (value != null) value.value = "1";
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
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
            await Wait(() => frontend.LiveUi.Windows.View?.Q<Label>("service-status")?.text.Contains("карта") == true,
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
            peerFrames.Clear(); Submit(frontend.LiveUi.Windows.View.Q<Button>("group-invite-" + peerEntry.PlayerId));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.Invite), "native group invite button reaches the real peer");
            await peer.AnswerGroupAsync(frontend.LiveState.Snapshot.PlayerId, true);
            await Wait(() => frontend.LiveUi.Social.Members.Count == 2 && frontend.LiveUi.Windows.View.Q<Label>("group-member-" + peerEntry.PlayerId) != null,
                "native group panel renders the authoritative roster");
            peerFrames.Clear(); frontend.LiveUi.Windows.View.Q<TextField>("social-input").value = "Unity native group message";
            Submit(frontend.LiveUi.Windows.View.Q<Button>("social-send"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.GroupChatOk), "native group chat button delivers to the peer");
            await Frames(8); Capture(uiTexture, "group");
            var buddy = frontend.LiveUi.Social.Friends.First(f => f?.Uid == peerEntry.CharacterUid);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("friend-chat-" + buddy.Slot)); peerFrames.Clear();
            frontend.LiveUi.Windows.View.Q<TextField>("social-input").value = "Unity native private message";
            Submit(frontend.LiveUi.Windows.View.Q<Button>("social-send"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.WhisperOk), "native friend chat button delivers a private message");
            await Frames(8); Capture(uiTexture, "friends");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("group-leave"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 0 && peerSocial.Members.Count == 0, "native leave-group button clears both rosters");
            await peer.InviteGroupAsync(frontend.LiveState.Snapshot.PlayerId);
            await Wait(() => frontend.Root.Q<Button>("prompt-confirm") != null, "native incoming group request opens a confirmation");
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 2, "native group confirmation joins the peer group");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("group-leave"));
            await Wait(() => frontend.LiveUi.Social.Members.Count == 0 && peerSocial.Members.Count == 0, "native incoming group can be left through the same UI");
            await Wait(() => frontend.LiveUi.Windows.View.Q<Button>("friend-block-" + buddy.Slot)?.enabledInHierarchy == true, "native friend actions are ready after group exit");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("friend-block-" + buddy.Slot));
            await Wait(() => frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid && f.Blocked), "native block button applies the server blocked entry");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("friend-remove-" + buddy.Slot));
            await Wait(() => !frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid), "native unblock button removes the acknowledged blocked slot");
            await peer.RequestFriendByNameAsync("Alice", "Unity");
            await Wait(() => frontend.Root.Q<Button>("prompt-confirm") != null, "native named friend request opens its confirmation");
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveUi.Social.Friends.Any(f => f?.Uid == peerEntry.CharacterUid && !f.Blocked), "native friend confirmation restores the real friendship");
            frontend.LiveUi.Windows.Close(); frontend.LiveUi.OpenMail();
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
            await Wait(() => frontend.LiveState.Snapshot.Hp == 0 && frontend.LiveRevivePrompt != null,
                "native NPC combat reaches zero HP and opens the real revival dialog");
            await Frames(8); Capture(uiTexture, "death");
            var confirm = frontend.LiveRevivePrompt.Q<Button>("prompt-confirm");
            Check(confirm != null && confirm.enabledInHierarchy, "revival dialog exposes its enabled confirmation");
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = confirm; confirm.SendEvent(submit); }
            await Wait(() => received.Contains(ProtocolGameplay.RegenSuccess) && frontend.LiveState.Snapshot.Hp > 0 && frontend.LiveRevivePrompt == null,
                "revival dialog sends the request and native HUD applies the living server post-state");
            Check(Vector3.Distance(frontend.LiveWorld.Player.position, frontend.LiveState.Snapshot.WorldPosition) < .2f,
                "revived world position agrees with the HUD after authoritative relocation");
            await Frames(8); Capture(uiTexture, "revived");
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
