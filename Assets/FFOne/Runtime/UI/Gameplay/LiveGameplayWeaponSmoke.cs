#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
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
        internal async Task LiveWeaponLogin(int port, string account = "unityweapa")
        {
            await Session.LoginAsync("127.0.0.1", port, account, "test-only-password", lifetime.Token);
            await EnterLiveFixtureWorld();
        }
        internal bool LiveFireWeapon(NetworkEntityView target) => TryFireWeapon(target, directProjectile: true);
        internal bool LiveWeaponCooldownReady => Time.unscaledTime >= nextWeaponAttack && !CombatUnavailable;
        internal bool LiveWeaponIntentReady(NetworkEntityView target)
        {
            var weapon = WorldWeaponCatalog.Load().Find(gameplayState.Snapshot.Equipment[0].ItemId);
            float extent = Mathf.Max(target.Definition?.Radius ?? .5f, (target.Definition?.Height ?? 2) * .5f);
            return characterAttackPermission && LiveWeaponCooldownReady &&
                Vector3.Distance(networkWorld.Player.position, target.transform.position) <= (weapon?.Range ?? 2) + extent;
        }
        internal string LiveWeaponIntentDiagnostics(NetworkEntityView target)
        {
            var weapon = WorldWeaponCatalog.Load().Find(gameplayState.Snapshot.Equipment[0].ItemId);
            float extent = Mathf.Max(target.Definition?.Radius ?? .5f, (target.Definition?.Height ?? 2) * .5f);
            return "player=" + WorldCoordinates.ToServer(networkWorld.Player.position) + " targetRendered=" + WorldCoordinates.ToServer(target.transform.position) +
                " targetServer=" + new Vector3Int(target.Entity.X, target.Entity.Y, target.Entity.Z) + " item=" + gameplayState.Snapshot.Equipment[0].ItemId +
                " range=" + (weapon?.Range ?? 2) + " extent=" + extent + " actualDistance=" + Vector3.Distance(networkWorld.Player.position, target.transform.position) +
                " permission=" + characterAttackPermission + " now=" + Time.unscaledTime + " nextAttack=" + nextWeaponAttack +
                " combatUnavailable=" + CombatUnavailable + " worldReady=" + networkWorld.IsReady;
        }
    }

    /// <summary>Normal combat intent, original projectile renderer, actual server damage and observer packets.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class LiveGameplayWeaponSmoke : MonoBehaviour
    {
        FrontendController frontend; FusionClient observer; RenderTexture image, uiImage; PanelSettings uiPanel;
        readonly List<string> checks = new(), failures = new();
        readonly List<ProtocolFrame> frames = new(), peerFrames = new();
        readonly List<object> screenshots = new();
        readonly List<object> projectileClosureVisuals = new();
        readonly List<object> projectileImpacts = new();
        readonly List<object> projectileTransactions = new();
        sealed class ProjectileAttemptEvidence
        {
            public int mode, attempt, targetId, targetHpBefore, batteryBefore, slot = -1;
            public int? acceptedBattery, batteryAfter;
            public object targetBefore, targetAtImpact;
            public bool impactRecorded;
            public int[] impactTargets;
            public string result = "awaiting-native-fire", expectedHitPayloadFromImpact;
            public readonly List<object> incomingPackets = new(), targetTrajectory = new();
        }
        readonly List<ProjectileAttemptEvidence> projectileAttemptResults = new();
        ProjectileAttemptEvidence activeProjectileAttempt;
        float lastProjectileTargetSample;
        readonly List<object> projectileFlightVisuals = new();
        readonly List<string> pvpIntentDiagnostics = new();
        readonly List<object> pvpReceipts = new();
        readonly List<object> peerHealthReceipts = new(), flightCameraDiagnostics = new();
        readonly List<object> projectileFlightAttempts = new();
        sealed class FlightAttemptImages
        {
            internal string AttemptName, SelectedName;
            internal Texture2D Visible, Hidden;
        }
        readonly List<FlightAttemptImages> pendingFlightAttempts = new();
        int pvpBaselineHp;
        readonly List<object> hudBatteryVisuals = new();
        readonly List<object> aimGeometry = new();
        string directory; int peerHp, peerId; WorldEntitySession peerWorld; GameplayUiSession peerHealth;
        Action captureFlightAfterNativeEffects;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            if (DevelopmentConnection.Enabled && (args.Contains("--live-weapon-smoke") ||
                args.Contains("--live-weapon-change-smoke") || args.Contains("--live-grenade-smoke")))
                new GameObject("Native weapon acceptance").AddComponent<LiveGameplayWeaponSmoke>();
        }
        async void Start()
        {
            directory = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
            try
            {
                Directory.CreateDirectory(directory);
                var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable)).Endpoint;
                if (!endpoint.IsLoopback) throw new InvalidOperationException("Weapon acceptance requires loopback.");
                await Wait(() => (frontend = FindFirstObjectByType<FrontendController>()) && frontend.LiveFrontendReady, "weapon frontend ready");
                await frontend.LiveWeaponLogin(endpoint.Port); frontend.NetworkEventReceived += Observe;
                await Wait(() => frontend.LiveWorld && frontend.LiveWorld.IsReady, "native weapon world completes content admission");
                bool weaponChange = Environment.GetCommandLineArgs().Contains("--live-weapon-change-smoke");
                bool grenadeOnly = Environment.GetCommandLineArgs().Contains("--live-grenade-smoke");
                if (!weaponChange) frontend.LiveWorld.MovementEnabled = false;
                Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 99, "native combat keeps normal account privilege 99");
                frontend.LiveWorld.Replicas.ContentError += failures.Add;
                frontend.LiveWorld.ProjectileImpacted += RecordProjectileImpact;
                observer = new FusionClient(); var roster = await observer.LoginAsync("127.0.0.1", endpoint.Port, "unityweapb", "test-only-password");
                var entry = await observer.EnterWorldAsync(roster.Single().Uid); await observer.CompleteLoadingAsync();
                peerHp = entry.Hp; peerId = entry.PlayerId;
                peerHealth = new GameplayUiSession(); peerHealth.SetWorld(entry, roster.Single()); peerHp = peerHealth.Snapshot.Hp;
                peerWorld = new WorldEntitySession(peerId);
                foreach (var packet in entry.BootstrapPackets ?? Array.Empty<ProtocolFrame>()) peerWorld.Apply(packet);
                await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == entry.PlayerId), "native weapon observer enters interest");
                image = new RenderTexture(1280, 720, 24); image.Create(); frontend.LiveWorld.View.targetTexture = image;
                if (weaponChange)
                {
                    await ExerciseWeaponChange();
                    await frontend.LiveExit();
                    await observer.LeaveWorldAsync();
                    Write(true, null); Application.Quit(0); return;
                }
                var document = frontend.GetComponent<UIDocument>();
                uiImage = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32); uiImage.Create();
                uiPanel = Instantiate(document.panelSettings); uiPanel.targetTexture = uiImage; uiPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                uiPanel.scale = 1; uiPanel.clearColor = true; uiPanel.colorClearValue = Color.clear; document.panelSettings = uiPanel;
                var playerTarget = frontend.LiveWorld.Replicas.Actors.First(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerId);
                int normalHp = peerHp; frames.Clear(); peerFrames.Clear();
                Check(!frontend.LiveFireWeapon(playerTarget), "normal99 frontend refuses player attack intent");
                await frontend.Session.AttackCharactersAsync(new[] { (WorldEntityKind.Player, peerId) }); await Pause(.5f);
                Check(peerHp == normalHp && !frames.Any(f => f.PacketType == 0x31000102) && !peerFrames.Any(f => f.PacketType == 0x31000103), "normal99 server independently denies forged PvP without damage or success");
                var weapons = new[] { (mode: 1, id: 706), (mode: 2, id: 621), (mode: 3, id: 612), (mode: 4, id: 625), (mode: 5, id: 781), (mode: 6, id: 22) };
                for (int i = grenadeOnly ? weapons.Length - 1 : 0; i < weapons.Length; i++)
                {
                    await FollowWeaponCaster("native weapon family " + weapons[i].mode + " restores mutual observer interest before equip");
                    var weapon = weapons[i]; frames.Clear(); peerFrames.Clear();
                    await frontend.Session.SendItemMoveAsync(ItemContainer.Inventory, 20 + i, ItemContainer.Equipment, 0);
                    await Wait(() => frontend.LiveState.Snapshot.Equipment[0].ItemId == weapon.id && peerFrames.Any(f => f.PacketType == ProtocolInventory.EquipmentChanged), "native weapon family " + weapon.mode + " applies real equip and observer appearance");
                    await Pause(2.1f);
                    var target = frontend.LiveWorld.Replicas.Actors.Where(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == 59 && a.Entity.Hp > 0).OrderBy(a => a.Entity.X).First();
                    int hp, battery = frontend.LiveState.Snapshot.WeaponBattery;
                    if (weapon.mode >= 5)
                        hp = await ExerciseProjectileWeapon(target, weapon.mode, weapon.id);
                    else
                    {
                        await PrepareWeaponAim(target, weapon.mode);
                        frames.Clear(); peerFrames.Clear(); hp = target.Entity.Hp;
                        Check(frontend.LiveFireWeapon(target), "native weapon family " + weapon.mode + " uses the normal gameplay attack path");
                    }
                    await Wait(() => frames.Any(f => ProtocolGameplay.TryCombat(f, out var c) && c.Hits.Any(h => h.Id == target.Entity.Id && h.Hp < hp && h.Damage > 0)) &&
                        peerFrames.Any(f => ProtocolGameplay.TryCombat(f, out var c) && c.Hits.Any(h => h.Id == target.Entity.Id && h.Hp < hp && h.Damage > 0)),
                        "native weapon family " + weapon.mode + " server damage and observer post-state", 15);
                    await Wait(() => frontend.LiveWorld.ProjectileCount == 0, "native weapon family " + weapon.mode + " clears completed projectile ownership");
                    FlushFlightAttempts();
                    Check(frontend.LiveState.Snapshot.WeaponBattery < battery, "native weapon family " + weapon.mode + " HUD applies authoritative cost");
                    await CaptureBatteryHud("weapon-" + weapon.mode + "-battery-hud", battery);
                    Check(target.Entity.Hp < hp, "native weapon family " + weapon.mode + " rendered target applies server health");
                    Capture("weapon-" + weapon.mode + "-damage");
                    Check(string.IsNullOrEmpty(frontend.LiveWorld.LastError), "native weapon family " + weapon.mode + " has no content failure");
                }
                failures.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                if (grenadeOnly)
                {
                    await ExerciseGrenadeFloor();
                    await observer.LeaveWorldAsync();
                    await frontend.LiveExit();
                    Write(true, null); Application.Quit(0); return;
                }
                await observer.SendMovementAsync(entry.X, entry.Y, entry.Z, 0, 0, 0, 0, 0, 0, true);
                await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerId &&
                    a.Entity.X == entry.X && a.Entity.Y == entry.Y && a.Entity.Z == entry.Z), "observer spawn return is confirmed by the real interest stream before GM admission");
                await frontend.LiveExit(); await Wait(() => frontend.LiveWorld == null, "normal weapon world teardown follows acknowledged save");
                frontend.LiveResetInstance(); await frontend.LiveWeaponLogin(endpoint.Port, "unityweapgm");
                await Wait(() => frontend.LiveWorld && frontend.LiveWorld.IsReady, "dedicated GM frontend completes real world admission");
                frontend.LiveWorld.MovementEnabled = false; frontend.LiveWorld.View.targetTexture = image;
                frontend.LiveWorld.Replicas.ContentError += failures.Add;
                frontend.LiveWorld.ProjectileImpacted += RecordProjectileImpact;
                Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 30, "only dedicated disposable frontend GM holds source PvP permission30");
                await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerId), "native GM frontend renders target player interest");
                pvpIntentDiagnostics.Add("before-replica-wait " + CurrentPeerIntentDiagnostics());
                await Wait(() => { var actor = CurrentPeerActor(); return actor && Vector3.Distance(actor.transform.position,
                    WorldCoordinates.FromServer(actor.Entity.X, actor.Entity.Y, actor.Entity.Z)) < .1f; },
                    "native PvP target rendered position converges to its authoritative bootstrap", 15);
                playerTarget = CurrentPeerActor();
                await frontend.LiveMoveTo(playerTarget.Entity.X - 100, playerTarget.Entity.Y, playerTarget.Entity.Z);
                pvpIntentDiagnostics.Add("before-intent-wait " + CurrentPeerIntentDiagnostics());
                await Wait(() => { var actor = CurrentPeerActor(); return actor && frontend.LiveWeaponIntentReady(actor) &&
                    peerWorld.TryGet(WorldEntityKind.Player, frontend.LiveState.Snapshot.PlayerId, out var gm) &&
                    gm.X == actor.Entity.X - 100 && gm.Y == actor.Entity.Y && gm.Z == actor.Entity.Z; },
                    "native PvP real range cooldown condition and observed caster position permit one frontend attack", 15);
                await Wait(() => { var actor = CurrentPeerActor(); return actor && peerHp > 0 && actor.Entity.Hp == peerHp; },
                    "native PvP baseline agrees between canonical owner health and current rendered target before the single attack", 15);
                pvpIntentDiagnostics.Add("before-single-native-attack " + CurrentPeerIntentDiagnostics());
                frames.Clear(); peerFrames.Clear(); int pvpHp = peerHp, gmBattery = frontend.LiveState.Snapshot.WeaponBattery; pvpBaselineHp = pvpHp;
                Check(frontend.LiveFireWeapon(CurrentPeerActor()), "source-permitted PvP uses the real frontend character attack path");
                await Wait(() => frames.Any(f => f.PacketType == 0x31000102) && peerFrames.Any(f => f.PacketType == 0x31000103),
                    "native PvP success and actual observer broadcast arrive", 15);
                await Wait(() => { var actor = CurrentPeerActor(); return actor && peerHp < pvpHp && actor.Entity.Hp < pvpHp; },
                    "native PvP ACK target damage rendered health and observer post-state agree", 15);
                pvpIntentDiagnostics.Add("after-native-damage peerHp=" + peerHp + " replicaHp=" + CurrentPeerActor().Entity.Hp + " baseline=" + pvpHp);
                Check(frontend.LiveState.Snapshot.WeaponBattery < gmBattery, "native PvP HUD uses authoritative source battery charge");
                await CaptureBatteryHud("weapon-pvp-gm-battery-hud", gmBattery);
                Capture("weapon-pvp-gm-damage");
                // Leave while an actual rocket is flying, then verify its owned
                // renderer/motor is destroyed with the acknowledged world exit.
                frames.Clear(); await frontend.Session.SendItemMoveAsync(ItemContainer.Inventory, 24, ItemContainer.Equipment, 0);
                await Wait(() => frontend.LiveState.Snapshot.Equipment[0].ItemId == 781, "cleanup fixture equips original rocket through inventory");
                await Pause(2.1f); frontend.LiveWorld.View.transform.rotation = Quaternion.LookRotation(Vector3.up);
                Check(frontend.LiveFireWeapon(null), "native cleanup launches through frontend free aim");
                await Wait(() => frontend.LiveWorld.ProjectileCount > 0, "native cleanup begins with owned in-flight projectile");
                var departingMotor = frontend.LiveWorld.GetComponentInChildren<NetworkProjectileMotor>();
                failures.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
                await frontend.LiveExit(); await Wait(() => frontend.LiveWorld == null && !departingMotor, "acknowledged exit destroys native projectile motors and world ownership");
                await observer.LeaveWorldAsync();
                await ValidateAllProjectileClosures(); Write(true, null); Application.Quit(0);
            }
            catch (Exception error)
            {
                try { FlushFlightAttempts(); }
                catch (Exception evidenceError) { error = new AggregateException(error, evidenceError); }
                Write(false, error.ToString()); Debug.LogError("NATIVE WEAPON ACCEPTANCE FAILED: " + error); Application.Quit(1);
            }
            finally { observer?.Dispose(); }
        }
        async Task ExerciseWeaponChange()
        {
            const int first = 706, second = 621, hand = 0, reserve = 7;
            var state = frontend.LiveState;
            bool Slots(int active, int spare) =>
                state.Snapshot.Equipment[hand].ItemId == active && state.Snapshot.Equipment[reserve].ItemId == spare;

            Check(Slots(0, 0) && state.Snapshot.Inventory[20].ItemId == first && state.Snapshot.Inventory[21].ItemId == second,
                "weapon change starts with two fixture weapons in the backpack and empty hands");
            await frontend.Session.SendItemMoveAsync(ItemContainer.Inventory, 20, ItemContainer.Equipment, hand);
            await Wait(() => Slots(first, 0), "fixture equips first weapon in the active hand");
            await frontend.Session.SendItemMoveAsync(ItemContainer.Inventory, 21, ItemContainer.Equipment, reserve);
            await Wait(() => Slots(first, second), "fixture equips second weapon in the reserve hand");

            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                await PressAndVerify("weapon-swap", reserve, hand, second, first);
                await PressAndVerify("weapon-swap-back", reserve, hand, first, second);
                await frontend.Session.SendItemMoveAsync(ItemContainer.Equipment, reserve, ItemContainer.Inventory, 21);
                await Wait(() => Slots(first, 0) && state.Snapshot.Inventory[21].ItemId == second,
                    "fixture returns second weapon to backpack for single-weapon input");
                await PressAndVerify("weapon-holster", hand, reserve, 0, first);
                await PressAndVerify("weapon-restore", reserve, hand, first, 0);
                Check(state.Snapshot.Inventory[20].IsEmpty && state.Snapshot.Inventory[21].ItemId == second,
                    "single weapon change leaves backpack slots untouched");
            }
            finally { InputSystem.RemoveDevice(keyboard); }

            async Task PressAndVerify(string name, int from, int to, int active, int spare)
            {
                float ready = Time.time + 1.1f;
                await Wait(() => Time.time >= ready && Application.isFocused && !frontend.LiveUi.InputBlocked,
                    name + " gameplay input is focused after cooldown", 30);
                frames.Clear(); peerFrames.Clear();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab));
                await Frames(2);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                int Expected(int slot) => slot == hand ? active : spare;
                bool OwnerAck(ProtocolFrame packet) => ProtocolInventory.TryDecode(packet, out var update) &&
                    update.Kind == InventoryUpdateKind.Move && update.FromContainer == ItemContainer.Equipment &&
                    update.ToContainer == ItemContainer.Equipment && update.FromSlot == from && update.ToSlot == to &&
                    update.FromItem.ItemId == Expected(from) && update.ToItem.ItemId == Expected(to);
                bool Observed(int slot) => peerFrames.Any(packet => ProtocolInventory.TryDecode(packet, out var update) &&
                    update.Kind == InventoryUpdateKind.EquipmentChanged && update.PlayerId == state.Snapshot.PlayerId &&
                    update.FromSlot == slot && update.FromItem.ItemId == Expected(slot));
                await Wait(() => Slots(active, spare) && frames.Any(OwnerAck) && Observed(hand) && Observed(reserve),
                    $"{name}: Tab confirms hand={active} reserve={spare} on owner and observer", 15);
                Check(frames.Count(OwnerAck) == 1, name + " sends one confirmed slot exchange");
                if (active > 0)
                {
                    var animator = frontend.LiveWorld.Player.GetComponentInChildren<FFOne.Content.NativePlayerAnimator>().Animator;
                    string prefix = WorldWeaponCatalog.AnimationPrefix(WorldWeaponCatalog.Load().Find(active).EquipType);
                    string readyState = FFOne.Content.NativePlayerAnimator.BaseLayer + "." + prefix + "ready";
                    await Wait(() => animator.GetCurrentAnimatorStateInfo(0).IsName(readyState),
                        name + " enters " + prefix + "ready on the rendered rig", 2);
                    Capture(name + "-ready");
                    await Wait(() => animator.GetCurrentAnimatorStateInfo(0).IsName(FFOne.Content.NativePlayerAnimator.BaseLayer + "." + prefix + "stand1"),
                        name + " completes the weapon change and returns to its armed idle", 8);
                }
                await Frames(3);
                Capture(name);
            }
        }

        async Task ExerciseGrenadeFloor()
        {
            await frontend.LiveMoveTo(631632, 187177, -5500);
            var world = frontend.LiveWorld;
            await Wait(() => world.IsReady && frontend.LiveWeaponCooldownReady, "ground grenade has its original cooldown and a loaded tile");
            await Pause(WorldWeaponCatalog.Load().Find(frontend.LiveState.Snapshot.Equipment[0].ItemId).Delay + .2f);
            var hits = Physics.RaycastAll(world.Player.position + Vector3.up * 15, Vector3.down, 35, ~(1 << 31), QueryTriggerInteraction.Ignore);
            var floor = hits.Where(h => world.Stream.Owns(h.collider) && h.normal.y > .9f)
                .OrderBy(h => Mathf.Abs(h.point.y - world.Player.position.y)).First();
            var launch = WorldCoordinates.ToServer(floor.point + Vector3.up * 2);
            await frontend.LiveMoveTo(launch.x, launch.y, launch.z);
            var muzzle = world.Player.position + Vector3.up * .8f;
            world.View.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            frames.Clear();
            Check(frontend.LiveFireWeapon(null), "ground-directed grenade uses the normal fire path");
            NetworkProjectileMotor motor = null;
            await Wait(() => frames.Any(f => f.PacketType == ProtocolProjectiles.GrenadeSuccess) &&
                (motor = world.GetComponentInChildren<NetworkProjectileMotor>()), "server accepts the ground-directed grenade", 10);
            bool bounced = false;
            float upward = 0, clearance = 0;
            void Record(RaycastHit hit, Vector3 velocity)
            {
                if (bounced || hit.collider != floor.collider || hit.normal.y < .9f) return;
                clearance = Vector3.Dot(motor.transform.position - hit.point, hit.normal);
                upward = Vector3.Dot(velocity, hit.normal);
                bounced = true;
            }
            motor.Bounced += Record;
            try
            {
                await Wait(() => bounced, "grenade collides with the authored " + floor.collider.name + " floor before expiry", 6);
                Check(upward > 0 && clearance >= NetworkProjectileMotor.GrenadeCollisionRadius - .02f,
                    $"ground collision sends grenade upward ({upward:F2} m/s), with {clearance:F3} m center clearance");
                await Wait(() => !motor, "ground grenade expires and releases its projectile motor", 6);
            }
            finally { if (motor) motor.Bounced -= Record; }
        }

        void Observe(NetworkEvent e) { if (e.IsShard && e.Frame != null) { frames.Add(e.Frame); RecordPvpReceipt("frontend", e.Frame); RecordProjectilePacket("frontend", e.Frame); } }
        void Update()
        {
            if (observer == null) return;
            while (observer.TryDequeueEvent(out var e)) if (e.IsShard && e.Frame != null)
            {
                peerFrames.Add(e.Frame); RecordPvpReceipt("observer", e.Frame); RecordProjectilePacket("observer", e.Frame); peerWorld?.Apply(e.Frame);
                int previous = peerHp;
                if (peerHealth != null) { peerHealth.ApplyFrame(e.Frame); peerHp = peerHealth.Snapshot.Hp; }
                if (peerHp != previous) peerHealthReceipts.Add(new { type = e.Frame.PacketType.ToString("x8"), previous, hp = peerHp,
                    payloadBase64 = Convert.ToBase64String(e.Frame.Payload), frame = Time.frameCount });
            }
        }
        void LateUpdate()
        {
            if (activeProjectileAttempt != null && activeProjectileAttempt.slot >= 0 && !activeProjectileAttempt.impactRecorded &&
                Time.realtimeSinceStartup - lastProjectileTargetSample >= .02f)
            {
                lastProjectileTargetSample = Time.realtimeSinceStartup;
                activeProjectileAttempt.targetTrajectory.Add(new { phase = "rendered-sample", frame = Time.frameCount,
                    time = Time.realtimeSinceStartup, deltaTime = Time.deltaTime, target = ProjectileTargetSnapshot(activeProjectileAttempt.targetId) });
            }
            // DefaultExecutionOrder(1000) places this after the native effect
            // playback's normal LateUpdate, before another motor Update.
            var capture = captureFlightAfterNativeEffects; captureFlightAfterNativeEffects = null;
            capture?.Invoke();
        }
        void RecordPvpReceipt(string recipient, ProtocolFrame frame)
        {
            if (frame.PacketType != 0x31000102 && frame.PacketType != 0x31000103) return;
            ProtocolGameplay.TryCombat(frame, out var combat);
            pvpReceipts.Add(new { recipient, type = frame.PacketType.ToString("x8"), payloadBase64 = Convert.ToBase64String(frame.Payload),
                combat.WeaponBattery, hits = combat.Hits.Select(h => new { kind = h.Kind.ToString(), h.Id, h.Damage, h.Hp }).ToArray() });
        }
        NetworkEntityView CurrentPeerActor() => frontend && frontend.LiveWorld && frontend.LiveWorld.Replicas ?
            frontend.LiveWorld.Replicas.Actors.FirstOrDefault(a => a && a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peerId) : null;
        string CurrentPeerIntentDiagnostics()
        {
            var actor = CurrentPeerActor();
            return actor ? frontend.LiveWeaponIntentDiagnostics(actor) : "target player " + peerId + " has no current rendered replica";
        }
        async Task FollowWeaponCaster(string description)
        {
            var position = WorldCoordinates.ToServer(frontend.LiveWorld.Player.position);
            await frontend.LiveMoveTo(position.x, position.y, position.z);
            await observer.SendMovementAsync(position.x, position.y + 2000, position.z, 0, 0, 0, 0, 0, 0, true);
            await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(actor => actor.Entity.Kind == WorldEntityKind.Player && actor.Entity.Id == peerId &&
                actor.Entity.X == position.x && actor.Entity.Y == position.y + 2000 && actor.Entity.Z == position.z) &&
                peerWorld.TryGet(WorldEntityKind.Player, frontend.LiveState.Snapshot.PlayerId, out var owner) && owner.X == position.x && owner.Y == position.y && owner.Z == position.z,
                description);
        }
        async Task<int> ExerciseProjectileWeapon(NetworkEntityView target, int mode, int itemId)
        {
            bool capturedFlight = false;
            try
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    await Wait(() => frontend.LiveWeaponCooldownReady, "native projectile family " + mode + " attempt " + attempt + " respects original cooldown and conditions", 15);
                    Check(target && target.Entity.Hp > 0, "native projectile family " + mode + " attempt " + attempt + " retains its living real target");
                    await PrepareWeaponAim(target, mode);
                    frames.Clear(); peerFrames.Clear();
                    var evidence = new ProjectileAttemptEvidence { mode = mode, attempt = attempt, targetId = target.Entity.Id,
                        targetHpBefore = target.Entity.Hp, batteryBefore = frontend.LiveState.Snapshot.WeaponBattery,
                        targetBefore = ProjectileTargetSnapshot(target.Entity.Id) };
                    projectileAttemptResults.Add(evidence); activeProjectileAttempt = evidence;
                    Check(frontend.LiveFireWeapon(target), "native weapon family " + mode + " uses the normal gameplay attack path");
                    uint fire = mode == 5 ? ProtocolProjectiles.RocketSuccess : ProtocolProjectiles.GrenadeSuccess;
                    await Wait(() => frames.Any(f => f.PacketType == fire) && frontend.LiveWorld.ProjectileCount > 0,
                        "native projectile family " + mode + " real fire ACK creates one in-flight motor");
                    var motor = frontend.LiveWorld.GetComponentInChildren<NetworkProjectileMotor>();
                    Check(motor && motor.GetComponentsInChildren<Renderer>(true).Length > 0, "native projectile family " + mode + " renders original bullet model or particle surfaces");
                    Check(ProtocolProjectiles.TryDecode(frames.Last(f => f.PacketType == fire), out var accepted) && accepted.Mode == mode &&
                        accepted.WeaponId == (mode == 6 ? 1 : itemId) && frontend.LiveState.Snapshot.Equipment[0].ItemId == itemId,
                        "native projectile family " + mode + " preserves native bullet marker and accepted equipment identity");
                    evidence.slot = accepted.Slot; evidence.acceptedBattery = accepted.WeaponBattery; evidence.result = "native-fire-accepted";
                    var renderedWeapon = WorldWeaponCatalog.Load().Find(accepted.WeaponId);
                    var equippedWeapon = WorldWeaponCatalog.Load().Find(itemId);
                    Check(renderedWeapon.Mode == equippedWeapon.Mode && renderedWeapon.Effect == equippedWeapon.Effect && renderedWeapon.BoostedEffect == equippedWeapon.BoostedEffect &&
                        renderedWeapon.Range == equippedWeapon.Range && renderedWeapon.Radius == equippedWeapon.Radius && renderedWeapon.Duration == equippedWeapon.Duration &&
                        renderedWeapon.GrenadeVerticalSpeed == equippedWeapon.GrenadeVerticalSpeed,
                        "native projectile family " + mode + " wire route preserves exact accepted original visual and flight parameters");
                    Check(accepted.WeaponBattery.HasValue && accepted.WeaponBattery.Value < evidence.batteryBefore &&
                        frontend.LiveState.Snapshot.WeaponBattery == accepted.WeaponBattery.Value,
                        "native projectile family " + mode + " attempt " + attempt + " applies its actual FIRE battery cost");
                    projectileTransactions.Add(new { mode, attempt, targetId = evidence.targetId, targetHpBefore = evidence.targetHpBefore,
                        equippedItemId = itemId, wireBulletId = accepted.WeaponId, accepted.Slot, accepted.Charged,
                        batteryBefore = evidence.batteryBefore, batteryAfter = accepted.WeaponBattery,
                        accepted.HasStart, accepted.X, accepted.Y, accepted.Z, accepted.ToX, accepted.ToY, accepted.ToZ,
                        particleBulletId = accepted.Charged ? renderedWeapon.BoostedEffect : renderedWeapon.Effect });
                    if (!capturedFlight) { await CaptureProjectileFlight(motor, mode); capturedFlight = true; }
                    uint firePeer = mode == 5 ? ProtocolProjectiles.RocketPeer : ProtocolProjectiles.GrenadePeer;
                    await Wait(() => peerFrames.Any(f => f.PacketType == firePeer && ProtocolProjectiles.TryDecode(f, out var p) && p.Slot == accepted.Slot),
                        "native projectile family " + mode + " observer receives accepted trajectory");
                    await Wait(() => evidence.impactRecorded && frontend.LiveWorld.ProjectileCount == 0,
                        "native projectile family " + mode + " attempt " + attempt + " reaches its real terminal impact and clears motor ownership", 15);
                    FlushFlightAttempts(); evidence.batteryAfter = frontend.LiveState.Snapshot.WeaponBattery;
                    if (evidence.impactTargets.Length == 0)
                    {
                        // RustyFusion consumes an empty-target HIT without a HIT
                        // acknowledgement/broadcast. Retrying that native miss
                        // requires another real FIRE and its original cooldown.
                        evidence.result = "terminal-empty-impact-no-hit-ack-in-source";
                        Debug.Log("WEAPON MISS: mode=" + mode + " attempt=" + attempt + " slot=" + evidence.slot + " target=" + evidence.targetId);
                        activeProjectileAttempt = null;
                        continue;
                    }
                    evidence.result = "awaiting-authoritative-nonempty-impact";
                    await Wait(() => frames.Any(f => MatchingProjectileImpact(f, mode, accepted.Slot, out _)) &&
                        peerFrames.Any(f => MatchingProjectileImpact(f, mode, accepted.Slot, out _)),
                        "native projectile family " + mode + " attempt " + attempt + " receives matching-slot owner and observer HIT", 15);
                    MatchingProjectileImpact(frames.Last(f => MatchingProjectileImpact(f, mode, accepted.Slot, out _)), mode, accepted.Slot, out var ownerHit);
                    MatchingProjectileImpact(peerFrames.Last(f => MatchingProjectileImpact(f, mode, accepted.Slot, out _)), mode, accepted.Slot, out var observerHit);
                    var damage = ownerHit.Hits.FirstOrDefault(h => h.Kind == WorldEntityKind.Npc && h.Id == evidence.targetId && h.Damage > 0 && h.Hp < evidence.targetHpBefore);
                    Check(damage.Id == evidence.targetId && observerHit.Hits.Any(h => h.Kind == damage.Kind && h.Id == damage.Id && h.Damage == damage.Damage && h.Hp == damage.Hp),
                        "native projectile family " + mode + " attempt " + attempt + " authoritative damage and observer HP agree for its actual target");
                    evidence.result = "authoritative-target-damage-and-observer-agree";
                    return evidence.targetHpBefore;
                }
                throw new InvalidOperationException("Native projectile family " + mode + " missed its real target in all three source-valid attempts.");
            }
            finally { activeProjectileAttempt = null; }
        }
        bool MatchingProjectileImpact(ProtocolFrame frame, int mode, int slot, out ProjectileUpdate update)
            => ProtocolProjectiles.TryDecode(frame, out update) && update.Phase == ProjectilePhase.Hit && update.Mode == mode &&
                update.Slot == slot && update.PlayerId == frontend.LiveState.Snapshot.PlayerId;
        object ProjectileTargetSnapshot(int targetId)
        {
            var actor = frontend && frontend.LiveWorld ? frontend.LiveWorld.Replicas.Actors.FirstOrDefault(a => a && a.Entity.Kind == WorldEntityKind.Npc && a.Entity.Id == targetId) : null;
            if (!actor) return new { targetId, missingReplica = true };
            var rendered = WorldCoordinates.ToServer(actor.transform.position); var collider = actor.GetComponent<Collider>();
            return new { targetId, actor.Entity.Hp, actor.Entity.Speed, actor.Entity.StateFlags, actor.Entity.ConditionFlags,
                actor.Entity.X, actor.Entity.Y, actor.Entity.Z, renderedX = rendered.x, renderedY = rendered.y, renderedZ = rendered.z,
                colliderBounds = collider ? collider.bounds.ToString() : null, frame = Time.frameCount, time = Time.realtimeSinceStartup, deltaTime = Time.deltaTime };
        }
        void RecordProjectilePacket(string recipient, ProtocolFrame frame)
        {
            var evidence = activeProjectileAttempt; if (evidence == null) return;
            if (frame.PacketType == ProtocolWorld.NpcMove && ProtocolWorld.TryDecode(frame, out var updates))
            {
                foreach (var update in updates.Where(u => u.Entity.Kind == WorldEntityKind.Npc && u.Entity.Id == evidence.targetId))
                    evidence.targetTrajectory.Add(new { phase = "native-movement", recipient, frame = Time.frameCount, time = Time.realtimeSinceStartup, deltaTime = Time.deltaTime,
                        update.Entity.X, update.Entity.Y, update.Entity.Z, update.Entity.Speed, nativeMoveStyle = Protocol0104.Read16(frame.Payload, 20),
                        payloadBase64 = Convert.ToBase64String(frame.Payload), rendered = ProjectileTargetSnapshot(evidence.targetId) });
            }
            if (!ProtocolProjectiles.TryDecode(frame, out var projectile) || projectile.Phase == ProjectilePhase.Ready || projectile.Mode != evidence.mode ||
                (evidence.slot >= 0 && projectile.Slot != evidence.slot) || (!projectile.Own && projectile.PlayerId != frontend.LiveState.Snapshot.PlayerId)) return;
            evidence.incomingPackets.Add(new { recipient, type = frame.PacketType.ToString("x8"), payloadBase64 = Convert.ToBase64String(frame.Payload),
                frame = Time.frameCount, time = Time.realtimeSinceStartup, deltaTime = Time.deltaTime, phase = projectile.Phase.ToString(), projectile.Slot,
                projectile.HasStart, projectile.X, projectile.Y, projectile.Z, projectile.ToX, projectile.ToY, projectile.ToZ, projectile.WeaponBattery,
                hits = projectile.Hits.Select(h => new { kind = h.Kind.ToString(), h.Id, h.Damage, h.Hp }).ToArray(), target = ProjectileTargetSnapshot(evidence.targetId) });
        }
        async Task CaptureProjectileFlight(NetworkProjectileMotor motor, int mode)
        {
            Check(motor, "native projectile family " + mode + " remains alive at visual capture");
            var view = frontend.LiveWorld.View;
            Check(PositionFlightCamera(motor, mode, "before-native-LateUpdate", motor.transform.position, 3),
                "native projectile family " + mode + " camera reaches a clear authored sight line without disabling colliders");
            // Capture after real native billboard/trail geometry construction
            // in this frame. Yielding to another Update lets a close grenade
            // legitimately impact before the acceptance can inspect its flight.
            var completed = new TaskCompletionSource<bool>();
            Check(captureFlightAfterNativeEffects == null, "native projectile flight inspection owns one LateUpdate capture");
            Action attempt = null;
            attempt = () => {
                try
                {
                    if (CaptureNativeFlightFrame(motor, mode)) completed.SetResult(true);
                    else captureFlightAfterNativeEffects = attempt;
                }
                catch (Exception failure)
                {
                    try { FlushFlightAttempts(); }
                    catch (Exception evidenceError) { failure = new AggregateException(failure, evidenceError); }
                    completed.SetException(failure);
                }
            };
            captureFlightAfterNativeEffects = attempt;
            await completed.Task;
        }
        bool CaptureNativeFlightFrame(NetworkProjectileMotor motor, int mode)
        {
            var view = frontend.LiveWorld.View;
            Check(motor, "native projectile family " + mode + " remains alive until an original authored flight frame can be captured");
            var renderers = motor.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(motor.transform.position, Vector3.one * .01f);
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float distance = Mathf.Max(3, bounds.extents.magnitude / Mathf.Sin(view.fieldOfView * .5f * Mathf.Deg2Rad) * 1.2f);
            // Native flight advanced during warmup. Reframe the current motor
            // and authored renderer bounds immediately before rendering.
            Check(PositionFlightCamera(motor, mode, "after-native-motion-before-capture", bounds.center, distance),
                "native projectile family " + mode + " actual camera-to-motor and geometry paths are unobstructed at capture");
            var viewport = view.WorldToViewportPoint(motor.transform.position);
            Check(viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1,
                "native projectile family " + mode + " is inside the captured camera view");
            Texture2D empty = null, visible = null; bool retained = false;
            try
            {
                foreach (var renderer in renderers) renderer.enabled = false;
                empty = ReadEffectPixels(view);
                foreach (var renderer in renderers) renderer.enabled = true;
                visible = ReadEffectPixels(view);
                var a = empty.GetPixels32(); var b = visible.GetPixels32(); int changed = 0;
                for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 6) changed++;
                string name = "weapon-" + mode + "-flight";
                string attemptName = name + "-attempt-" + projectileFlightAttempts.Count.ToString("000", CultureInfo.InvariantCulture);
                var buffered = new FlightAttemptImages { AttemptName = attemptName, Visible = visible, Hidden = empty };
                pendingFlightAttempts.Add(buffered); retained = true;
                projectileFlightAttempts.Add(new { mode, frame = Time.frameCount, visiblePixels = changed, screenshot = attemptName + ".png",
                    hiddenScreenshot = attemptName + "-motor-hidden.png", motorServerPosition = WorldCoordinates.ToServer(motor.transform.position).ToString(),
                    cameraPosition = view.transform.position.ToString(), rendererBounds = bounds.ToString(),
                    geometry = renderers.Select(r => {
                        var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                        return new { r.name, type = r.GetType().Name, vertices = mesh ? mesh.vertexCount : 0,
                            indices = mesh && mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) : 0, bounds = r.bounds.ToString(),
                            materials = r.sharedMaterials.Select(m => new { name = m ? m.name : null, shader = m && m.shader ? m.shader.name : null,
                                color = m && m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : null }).ToArray() }; }).ToArray() });
                if (changed <= 32) return false;
                Check(renderers.Length > 0, "native projectile family " + mode + " retains its original renderer at capture");
                Check(changed > 32, "native projectile family " + mode + " original in-flight renderer contributes unobstructed camera pixels (" + changed + ")");
                buffered.SelectedName = name;
                RecordWorldCapture(name, visible);
                projectileFlightVisuals.Add(new { mode, visiblePixels = changed, totalPixels = a.Length, screenshot = name + ".png", hiddenScreenshot = name + "-motor-hidden.png",
                    motorServerPosition = WorldCoordinates.ToServer(motor.transform.position).ToString(), cameraPosition = view.transform.position.ToString(), cameraDistance = Vector3.Distance(view.transform.position, motor.transform.position) });
                return true;
            }
            finally
            {
                foreach (var renderer in renderers) if (renderer) renderer.enabled = true;
                if (!retained) { if (empty) Destroy(empty); if (visible) Destroy(visible); }
            }
        }
        void FlushFlightAttempts()
        {
            // Encoding is deferred until native flight has ended, or a terminal
            // inspection failure. It cannot stall the next projectile Update.
            var attempts = pendingFlightAttempts.ToArray(); pendingFlightAttempts.Clear();
            var errors = new List<Exception>();
            foreach (var attempt in attempts)
            {
                try
                {
                    byte[] shown = attempt.Visible.EncodeToPNG(), hidden = attempt.Hidden.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(directory, attempt.AttemptName + ".png"), shown);
                    File.WriteAllBytes(Path.Combine(directory, attempt.AttemptName + "-motor-hidden.png"), hidden);
                    if (attempt.SelectedName != null)
                    {
                        File.WriteAllBytes(Path.Combine(directory, attempt.SelectedName + ".png"), shown);
                        File.WriteAllBytes(Path.Combine(directory, attempt.SelectedName + "-motor-hidden.png"), hidden);
                    }
                }
                catch (Exception error) { errors.Add(error); }
                finally { if (attempt.Visible) Destroy(attempt.Visible); if (attempt.Hidden) Destroy(attempt.Hidden); }
            }
            if (errors.Count > 0) throw new AggregateException("Could not save native flight evidence.", errors);
        }
        bool PositionFlightCamera(NetworkProjectileMotor motor, int mode, string phase, Vector3 center, float distance)
        {
            var view = frontend.LiveWorld.View; var original = view.transform.position;
            var direction = original - center; direction.y = 0;
            direction = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.back;
            for (int step = -1; step < 32; step++)
            {
                float angle = step < 0 ? 0 : (step % 2 == 0 ? 1 : -1) * ((step + 1) / 2) * 22.5f;
                var offset = Quaternion.Euler(0, angle, 0) * direction * distance + Vector3.up * (step < 16 ? .8f : 1.6f);
                var candidate = step < 0 ? original : center + offset;
                var point = motor.transform.position;
                var blockers = FlightBlockers(candidate, point, motor).Concat(FlightBlockers(point, candidate, motor))
                    .Concat(FlightBlockers(candidate, center, motor)).Concat(FlightBlockers(center, candidate, motor)).Distinct().ToArray();
                flightCameraDiagnostics.Add(new { mode, phase, step, motorPosition = point.ToString(), rendererCenter = center.ToString(),
                    cameraPosition = candidate.ToString(), blockers = blockers.Select(c => c.name).ToArray() });
                if (blockers.Length > 0) continue;
                view.transform.position = candidate; view.transform.LookAt(center);
                var motorViewport = view.WorldToViewportPoint(point);
                if (motorViewport.z <= 0 || motorViewport.x <= 0 || motorViewport.x >= 1 || motorViewport.y <= 0 || motorViewport.y >= 1) continue;
                return true;
            }
            return false;
        }
        Collider[] FlightBlockers(Vector3 from, Vector3 to, NetworkProjectileMotor motor)
        {
            var delta = to - from;
            return Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~(1 << 31), QueryTriggerInteraction.Collide)
                .Where(hit => !hit.collider.transform.IsChildOf(frontend.LiveWorld.Player) && !hit.collider.transform.IsChildOf(motor.transform))
                .OrderBy(hit => hit.distance).Select(hit => hit.collider).ToArray();
        }
        async Task PrepareWeaponAim(NetworkEntityView target, int mode)
        {
            if (mode < 5)
            {
                await frontend.LiveMoveTo(target.Entity.X - 100, target.Entity.Y, target.Entity.Z);
                await FollowWeaponCaster("native weapon family " + mode + " moves real observer into mutual attack interest");
                await Wait(() => frontend.LiveWeaponCooldownReady, "native hitscan weapon world and cooldown are ready");
                frontend.LiveWorld.View.transform.LookAt(target.transform.position + Vector3.up * .8f);
                return;
            }
            float distance = mode == 5 ? 6 : 1.5f;
            for (int step = 0; step < 16; step++)
            {
                float angle = Mathf.PI + step * Mathf.PI / 8;
                var candidate = target.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                var aim = target.GetComponent<Collider>().bounds.center;
                if (!WeaponSightClear(candidate + Vector3.up * .8f, aim, target, out var candidateBlock))
                {
                    RecordAimGeometry(target, mode, step, candidateBlock, "candidate-muzzle");
                    continue;
                }
                var position = WorldCoordinates.ToServer(candidate);
                await frontend.LiveMoveTo(position.x, position.y, position.z);
                await FollowWeaponCaster("native weapon family " + mode + " moves real observer into mutual attack interest");
                await Pause(.1f);
                await Wait(() => frontend.LiveWeaponCooldownReady, "native projectile weapon destination and cooldown are ready");
                aim = target.GetComponent<Collider>().bounds.center;
                frontend.LiveWorld.View.transform.LookAt(aim);
                if (!WeaponSightClear(frontend.LiveWorld.Player.position + Vector3.up * .8f, aim, target, out var muzzleBlock))
                { RecordAimGeometry(target, mode, step, muzzleBlock, "actual-muzzle"); continue; }
                if (!WeaponSightClear(frontend.LiveWorld.View.transform.position, aim, target, out var cameraBlock))
                { RecordAimGeometry(target, mode, step, cameraBlock, "actual-camera"); continue; }
                RecordAimGeometry(target, mode, step, null, "clear-camera-and-muzzle");
                Check(true, "native weapon family " + mode + " aims through clear authored scenery from muzzle and real camera");
                // The roaming target can move while the real movement packets
                // and observer interest are acknowledged. Recheck the actual
                // stance after those awaits; return directly to native FIRE.
                var currentOffset = target.transform.position - frontend.LiveWorld.Player.position;
                currentOffset.y = 0;
                if (mode == 6 && currentOffset.magnitude > 1.9f)
                { RecordAimGeometry(target, mode, step, null, "actual-approach-too-far"); continue; }
                return;
            }
            throw new InvalidOperationException("No clear native weapon " + mode + " aim stance around target " + target.Entity.Id);
        }
        bool WeaponSightClear(Vector3 from, Vector3 to, NetworkEntityView target, out Collider blocker)
        {
            var delta = to - from;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~(1 << 31), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(frontend.LiveWorld.Player)) continue;
                if (hit.collider.GetComponentInParent<NetworkEntityView>() == target) continue;
                blocker = hit.collider; return false;
            }
            blocker = null; return true;
        }
        void RecordAimGeometry(NetworkEntityView target, int mode, int step, Collider blocker, string phase)
        {
            var ground = Physics.RaycastAll(target.transform.position + Vector3.up * 20, Vector3.down, 40, ~(1 << 31), QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.GetComponentInParent<NetworkEntityView>() && !h.collider.transform.IsChildOf(frontend.LiveWorld.Player)).OrderBy(h => h.distance).ToArray();
            var player = WorldCoordinates.ToServer(frontend.LiveWorld.Player.position);
            var position = WorldCoordinates.ToServer(target.transform.position);
            var muzzle = WorldCoordinates.ToServer(frontend.LiveWorld.Player.position + Vector3.up * .8f);
            var offset = target.transform.position - frontend.LiveWorld.Player.position; offset.y = 0;
            var collider = target.GetComponent<Collider>();
            string obstruction = blocker ? blocker.name : null;
            aimGeometry.Add(new { mode, step, phase, targetId = target.Entity.Id, playerX = player.x, playerY = player.y, playerZ = player.z,
                targetX = position.x, targetY = position.y, targetZ = position.z, targetHeight = target.Definition.Height, targetRadius = target.Definition.Radius,
                actualPlanarDistance = offset.magnitude, muzzleX = muzzle.x, muzzleY = muzzle.y, muzzleZ = muzzle.z, deltaTime = Time.deltaTime,
                targetEntityX = target.Entity.X, targetEntityY = target.Entity.Y, targetEntityZ = target.Entity.Z,
                actualColliderHeight = collider ? collider.bounds.size.y : 0, actualColliderMinY = collider ? collider.bounds.min.y : 0,
                actualColliderMaxY = collider ? collider.bounds.max.y : 0,
                blocker = obstruction, blockerBounds = blocker ? blocker.bounds.ToString() : null,
                supportingSurface = ground.Length > 0 ? ground[0].collider.name : null,
                targetAboveSurface = ground.Length > 0 ? (float?)(target.transform.position.y - ground[0].point.y) : null });
            Debug.Log("WEAPON AIM: mode=" + mode + " step=" + step + " phase=" + phase + " target=" + position + " player=" + player +
                " obstruction=" + obstruction + " surface=" + (ground.Length > 0 ? ground[0].collider.name + " deltaY=" + (target.transform.position.y - ground[0].point.y) : "none"));
        }
        void RecordProjectileImpact(ProjectileImpact impact)
        {
            var point = WorldCoordinates.ToServer(impact.Position);
            var evidence = activeProjectileAttempt;
            if (evidence != null && evidence.mode == impact.Mode && evidence.slot == impact.Slot)
            {
                evidence.impactRecorded = true; evidence.impactTargets = impact.Targets; evidence.targetAtImpact = ProjectileTargetSnapshot(evidence.targetId);
                // Reconstructed from the production impact event, not an
                // outgoing transport capture. Incoming raw receipts are separate.
                evidence.expectedHitPayloadFromImpact = Convert.ToBase64String(ProtocolProjectiles.Hit(impact.Slot, point.x, point.y, point.z, impact.Targets));
            }
            projectileImpacts.Add(new { impact.Mode, impact.Slot, attempt = evidence?.attempt, x = point.x, y = point.y, z = point.z, targets = impact.Targets,
                frame = Time.frameCount, time = Time.realtimeSinceStartup, deltaTime = Time.deltaTime, source = "productionImpactEvent" });
            Debug.Log("WEAPON IMPACT: mode=" + impact.Mode + " slot=" + impact.Slot + " at=" + point + " targets=" + string.Join(",", impact.Targets));
        }
        async Task Wait(Func<bool> predicate, string message, float seconds = 120)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(message); await Task.Yield(); }
            Check(true, message);
        }
        static async Task Pause(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) await Task.Yield(); }
        static async Task Frames(int count) { for (int i = 0; i < count; i++) await Task.Yield(); }
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks.Add(message); Debug.Log("WEAPON CHECK: " + message); }
        async Task CaptureBatteryHud(string name, int batteryBefore)
        {
            await Frames(8);
            int amount = frontend.LiveState.Snapshot.WeaponBattery;
            string expected = amount.ToString("0000", CultureInfo.InvariantCulture);
            var label = frontend.Root.Q<Label>("weapon-battery-count");
            Check(label != null && label.visible && label.text == expected && label.worldBound.width > 0 && label.worldBound.height > 0,
                name + " actual HUD Label binds authoritative post-cost battery " + expected);
            Rect bounds = label.worldBound;
            Check(bounds.xMin >= 0 && bounds.yMin >= 0 && bounds.xMax <= uiImage.width && bounds.yMax <= uiImage.height,
                name + " actual battery Label lies inside rendered HUD panel");
            var shown = ReadPixels(uiImage); Texture2D hidden = null; var visibility = label.style.visibility;
            try
            {
                label.style.visibility = Visibility.Hidden; await Frames(4); hidden = ReadPixels(uiImage);
                var a = shown.GetPixels32(); var b = hidden.GetPixels32(); int changed = 0;
                int left = Mathf.FloorToInt(bounds.xMin), right = Mathf.CeilToInt(bounds.xMax);
                int bottom = uiImage.height - Mathf.CeilToInt(bounds.yMax), top = uiImage.height - Mathf.FloorToInt(bounds.yMin);
                for (int y = bottom; y < top; y++) for (int x = left; x < right; x++)
                {
                    int i = y * uiImage.width + x;
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + Math.Abs(a[i].a - b[i].a) > 15) changed++;
                }
                Check(changed >= 8, name + " actual battery text contributes HUD pixels (" + changed + ")");
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), shown.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(directory, name + "-label-hidden.png"), hidden.EncodeToPNG());
                hudBatteryVisuals.Add(new { name, batteryBefore, battery = amount, chargedTotal = batteryBefore - amount, expectedText = expected, labelText = label.text, visiblePixels = changed,
                    x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height, screenshot = name + ".png" });
            }
            finally { label.style.visibility = visibility; Destroy(shown); if (hidden) Destroy(hidden); }
            await Frames(4);
        }
        static Texture2D ReadPixels(RenderTexture source)
        {
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false); var old = RenderTexture.active;
            try { RenderTexture.active = source; result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); result.Apply(); return result; }
            finally { RenderTexture.active = old; }
        }
        async Task ValidateAllProjectileClosures()
        {
            // This visual matrix is separate from the actual six-family server
            // transactions above. It proves every original projectile closure
            // contributes pixels in the built player at its authored scale.
            var catalog = WorldWeaponCatalog.Load();
            var bulletIds = catalog.Weapons.Where(w => w.Projectile).SelectMany(w => new[] { w.Effect, w.BoostedEffect }).Distinct().OrderBy(id => id).ToArray();
            Check(bulletIds.SequenceEqual(new[] { 68, 69, 72, 135, 149, 150, 160, 161, 162, 163, 164 }), "visual closure matrix preserves all eleven accepted projectile bullet rows");
            var stage = new GameObject("Original projectile closure inspection");
            var cameraObject = new GameObject("Projectile closure camera");
            var view = cameraObject.AddComponent<Camera>();
            view.enabled = false; view.cullingMask = 1 << 30; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.16f, .16f, .16f, 1); view.fieldOfView = 40; view.aspect = (float)image.width / image.height;
            view.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var lampObject = new GameObject("Projectile closure inspection light");
            var lamp = lampObject.AddComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1; lamp.cullingMask = 1 << 30;
            lamp.transform.rotation = Quaternion.Euler(35, -30, 0);
            try
            {
                foreach (int bulletId in bulletIds) foreach (bool impact in new[] { false, true })
                {
                    var bullet = catalog.Bullet(bulletId);
                    var weapon = catalog.Weapons.Where(w => w.Projectile && (w.Effect == bulletId || w.BoostedEffect == bulletId)).OrderBy(w => w.Id).First();
                    int effectId = impact ? bullet.Impact : bullet.Particle; float actualScale = impact ? bullet.ImpactScale : bullet.Scale;
                    GameObject effect = null;
                    try
                    {
                        // Oblique inspection keeps an authored rotating trail's
                        // cross section distinct from its flight direction.
                        var inspectionRotation = Quaternion.Euler(15, 35, 0);
                        view.transform.SetPositionAndRotation(inspectionRotation * Vector3.back * 5, inspectionRotation);
                        effect = NativeCombatEffect.Spawn(stage.transform, effectId, actualScale, !impact, view, -1);
                        if (!impact) effect.transform.rotation = Quaternion.LookRotation(Vector3.right);
                        foreach (var node in effect.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
                        float started = Time.realtimeSinceStartup, previous = started;
                        while (Time.realtimeSinceStartup - started < .15f)
                        {
                            await Task.Yield(); float now = Time.realtimeSinceStartup;
                            if (!impact) effect.transform.position += Vector3.right * weapon.Range * (now - previous);
                            previous = now;
                        }
                        // Let the regular effect LateUpdate build its geometry at
                        // this accepted flight position before inspecting bounds.
                        await Task.Yield();
                        var renderers = effect.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                        Check(renderers.Length > 0, "bullet " + bulletId + (impact ? " impact" : " flight") + " creates original runtime renderers");
                        var materials = renderers.SelectMany(r => r.sharedMaterials).ToArray();
                        Check(materials.Length > 0 && materials.All(m => m && m.shader && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader"),
                            "bullet " + bulletId + (impact ? " impact" : " flight") + " uses supported original material shaders");
                        var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        float radius = Mathf.Max(.01f, bounds.extents.magnitude);
                        float distance = radius / Mathf.Sin(view.fieldOfView * .5f * Mathf.Deg2Rad) * 1.2f;
                        view.nearClipPlane = Mathf.Max(.001f, distance * .001f); view.farClipPlane = Mathf.Max(10, distance * 4);
                        var cameraOffset = inspectionRotation * Vector3.back;
                        bool planar = TryPlanarMeshFrontNormal(renderers, bounds, out var frontNormal);
                        bool reflectedCamera = planar && Vector3.Dot(cameraOffset, frontNormal) < 0;
                        // ES793's two original planes have disabled billboard
                        // components and authored back-face culling. Inspect their
                        // front without changing either the model or its material.
                        if (reflectedCamera) cameraOffset = Vector3.Reflect(cameraOffset, frontNormal);
                        view.transform.SetPositionAndRotation(bounds.center + cameraOffset * distance, Quaternion.LookRotation(-cameraOffset, Vector3.up));
                        Texture2D empty = null, rendered = null;
                        try
                        {
                            effect.SetActive(false); empty = ReadEffectPixels(view);
                            effect.SetActive(true); rendered = ReadEffectPixels(view);
                            var before = empty.GetPixels32(); var after = rendered.GetPixels32(); int changed = 0;
                            for (int i = 0; i < after.Length; i++)
                                if (Math.Abs(after[i].r - before[i].r) + Math.Abs(after[i].g - before[i].g) + Math.Abs(after[i].b - before[i].b) > 6) changed++;
                            string name = "bullet-" + bulletId + (impact ? "-impact" : "-flight");
                            File.WriteAllBytes(Path.Combine(directory, name + ".png"), rendered.EncodeToPNG());
                            File.WriteAllBytes(Path.Combine(directory, name + "-effect-hidden.png"), empty.EncodeToPNG());
                            projectileClosureVisuals.Add(new { bulletId, effectId, phase = impact ? "impact" : "flight", actualScale, referenceWeaponId = weapon.Id,
                                authoredFlightSpeed = impact ? 0 : weapon.Range, renderers = renderers.Length,
                                shaders = materials.Select(m => m.shader.name).Distinct().ToArray(), changedPixels = changed, totalPixels = after.Length,
                                cameraDistance = distance, cameraPosition = view.transform.position.ToString(), cameraForward = view.transform.forward.ToString(),
                                planarMesh = planar, reflectedCamera, frontNormal = new { x = frontNormal.x, y = frontNormal.y, z = frontNormal.z },
                                cameraDotFrontNormal = planar ? Vector3.Dot(cameraOffset, frontNormal) : 0,
                                geometry = renderers.Select(r => {
                                    var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                                    return new { r.name, type = r.GetType().Name, vertices = mesh ? mesh.vertexCount : 0,
                                        indices = mesh && mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) : 0, bounds = r.bounds.ToString(),
                                        materials = r.sharedMaterials.Select(m => new { name = m.name, shader = m.shader.name,
                                            cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : -1,
                                            color = m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : null }).ToArray() }; }).ToArray(),
                                screenshot = name + ".png", hiddenScreenshot = name + "-effect-hidden.png", kind = "isolated-original-effect-closure" });
                            Check(changed > 32, "bullet " + bulletId + (impact ? " impact" : " flight") + " contributes visible camera pixels at authored scale");
                        }
                        finally { if (empty) Destroy(empty); if (rendered) Destroy(rendered); }
                    }
                    finally { if (effect) { effect.SetActive(false); Destroy(effect); } }
                }
                Check(projectileClosureVisuals.Count == bulletIds.Length * 2, "all eleven original projectile rows have independent flight and impact pixel proof");
            }
            finally { Destroy(stage); Destroy(cameraObject); Destroy(lampObject); }
        }
        static bool TryPlanarMeshFrontNormal(Renderer[] renderers, Bounds bounds, out Vector3 normal)
        {
            normal = Vector3.zero;
            var points = new List<Vector3>(); Vector3 planePoint = Vector3.zero; bool found = false;
            foreach (var renderer in renderers)
            {
                if (!(renderer is MeshRenderer)) return false;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh || !mesh.isReadable || renderer.sharedMaterials.Length == 0 ||
                    renderer.sharedMaterials.Any(m => !m || !m.HasProperty("_Cull") || m.GetFloat("_Cull") != 2)) return false;
                var vertices = mesh.vertices.Select(renderer.transform.TransformPoint).ToArray(); points.AddRange(vertices);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) return false;
                    var indices = mesh.GetTriangles(submesh);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var a = vertices[indices[i]];
                        var cross = Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a);
                        if (cross.sqrMagnitude <= 1e-12f) continue;
                        var face = cross.normalized;
                        if (!found) { normal = face; planePoint = a; found = true; }
                        else if (Vector3.Dot(normal, face) < .9999f) return false;
                    }
                }
            }
            if (!found) return false;
            float tolerance = Mathf.Max(.00001f, bounds.extents.magnitude * .0001f);
            foreach (var point in points) if (Mathf.Abs(Vector3.Dot(point - planePoint, normal)) > tolerance) return false;
            return true;
        }
        Texture2D ReadEffectPixels(Camera view)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = image };
            if (!RenderPipeline.SupportsRenderRequest(view, request)) throw new InvalidOperationException("Projectile closure inspection requires the real player render pipeline.");
            RenderPipeline.SubmitRenderRequest(view, request);
            var result = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false); var old = RenderTexture.active;
            try { RenderTexture.active = image; result.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); result.Apply(); return result; }
            finally { RenderTexture.active = old; }
        }
        void Capture(string name)
        {
            var camera = frontend.LiveWorld.View;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = image };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
            var pixels = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false); var old = RenderTexture.active;
            try {
                RenderTexture.active = image; pixels.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); pixels.Apply();
                RecordWorldCapture(name, pixels);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Destroy(pixels); }
        }
        void RecordWorldCapture(string name, Texture2D pixels)
        {
            var colors = pixels.GetPixels32(); var bins = new HashSet<int>(); int visible = 0; double sum = 0, square = 0;
            for (int i = 0; i < colors.Length; i += 16) { var c = colors[i]; int luma = (c.r * 3 + c.g * 6 + c.b) / 10;
                bins.Add((c.r >> 4) << 8 | (c.g >> 4) << 4 | (c.b >> 4)); if (luma > 8) visible++; sum += luma; square += luma * luma; }
            int samples = (colors.Length + 15) / 16; double variance = square / samples - Math.Pow(sum / samples, 2);
            Check(visible > samples / 100 && bins.Count > 12 && variance > 2, name + " captures nonempty original world rendering");
            screenshots.Add(new { name, width = pixels.width, height = pixels.height, visibleSamples = visible, samples, colorBins = bins.Count, luminanceVariance = variance });
        }
        void Write(bool passed, string error)
        {
            if (frontend?.LiveWorld) failures.AddRange(frontend.LiveWorld.Replicas.ContentFailures);
            var pvpTarget = CurrentPeerActor();
            object pvpCurrentState = pvpTarget && frontend && frontend.LiveWorld ? new { baselineHp = pvpBaselineHp, peerHp, replicaHp = pvpTarget.Entity.Hp, guards = frontend.LiveWeaponIntentDiagnostics(pvpTarget) } : null;
            if (directory != null) File.WriteAllText(Path.Combine(directory, "weapons-result.json"), JsonConvert.SerializeObject(new { passed = passed && failures.Count == 0, checks, screenshots, hudBatteryVisuals, projectileClosureVisuals, projectileTransactions, projectileAttemptResults, projectileImpacts, projectileFlightVisuals, projectileFlightAttempts, flightCameraDiagnostics, pvpIntentDiagnostics, pvpReceipts, pvpCurrentState, peerHealthReceipts, aimGeometry, contentComplete = failures.Count == 0, contentErrors = failures.Distinct().ToArray(), error }, Formatting.Indented));
        }
        void OnDestroy()
        {
            if (frontend) frontend.NetworkEventReceived -= Observe; observer?.Dispose();
            foreach (var attempt in pendingFlightAttempts)
            { if (attempt.Visible) Destroy(attempt.Visible); if (attempt.Hidden) Destroy(attempt.Hidden); }
            pendingFlightAttempts.Clear(); captureFlightAfterNativeEffects = null;
            if (image) { image.Release(); Destroy(image); }
            if (uiImage) { uiImage.Release(); Destroy(uiImage); }
            if (uiPanel) Destroy(uiPanel);
        }
    }
}
#endif
