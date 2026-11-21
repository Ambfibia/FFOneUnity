using FFOne.Integration;

using FFOne.Content;

using FFOne.Network;

using System;

using System.Threading.Tasks;

using System.Collections.Generic;

using System.Linq;

using FFOne.World;

using UnityEngine;

using UnityEngine.InputSystem;

using CharacterInfo = FFOne.Network.CharacterInfo;



namespace FFOne.UI

{

    public sealed partial class FrontendController

    {

        static readonly GameplayAction[] NanoActions = { GameplayAction.Nano1, GameplayAction.Nano2, GameplayAction.Nano3 };

        GameplayUiSession gameplayState;

        GameplayUiController gameplayUi;

        bool gameplayCommandSending;

        GameplayResurrection revivePrompt;

        readonly Dictionary<int, float> nanoCooldowns = new();

        readonly List<NetworkEvent> deferredGameplayEvents = new();

        readonly HashSet<int> pendingMissionFollowUps = new();



        bool DeferGameplayEvent(NetworkEvent value)

        {

            if (!networkWorld || gameplayUi != null || worldLifetime == null || !value.IsShard || value.Kind != NetworkEventKind.Packet) return false;

            // LOADING_COMPLETE is acknowledged before the buddy list is sent. Asset loading

            // may still yield on the main thread before StartGameplayUi subscribes.

            if (deferredGameplayEvents.Count >= 4096)

            {

                DisposeWorldFlow(); Session?.Dispose(); Session = new FusionClient();

                SetWorldPresentation(false); ShowLogin(); SetBusy(false);

                ShowError("Excessive gameplay startup event backlog."); return true;

            }

            deferredGameplayEvents.Add(value); return true;

        }



        Task SendWorldChat(FusionClient client, string text, System.Threading.CancellationToken token)

        {

            var words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            string command = words.Length == 0 ? "" : words[0].ToLowerInvariant();

            if (command is not ("/taro" or "/taros" or "/fm" or "/fusionmatter" or "/hp" or "/health" or "/warp" or "/item" or "/nano"))

                return client.SendFreeChatAsync(text, token);

            if (client.World.UserLevel > 50)

                throw new InvalidOperationException(LocalizationCatalog.Lookup("ui.chat.command.gm.access", language).Replace("{level}", "50"));

            int[] values = new int[words.Length - 1];

            for (int i = 0; i < values.Length; i++)

                if (!int.TryParse(words[i + 1], out values[i]))

                    throw new ArgumentException(command);

            switch (command)

            {

                case "/warp" when values.Length == 2:

                    return client.OperatorWarpAsync(values[0], values[1], token);

                case "/nano" when values.Length == 1 && GameplayUiCatalog.Load().NanoIds.Contains(values[0]):

                    return client.GiveOwnNanoAsync(values[0], token);

                case "/item" when values.Length is 3 or 4 && values[0] is >= 0 and <= 10 &&

                    values[1] is > 0 and <= short.MaxValue && values[2] > 0 && (values.Length == 3 || values[3] >= 0):

                    var slots = gameplayState.Snapshot.Inventory;

                    for (int i = 0; i < slots.Count; i++)

                        if (slots[i].IsEmpty) return client.GiveOwnItemAsync(i, values[0], values[1], values[2],

                            values.Length == 4 ? values[3] : 0, token);

                    throw new InvalidOperationException(LocalizationCatalog.Lookup("ui.chat.command.gm.inventory_full", language));

                case "/taro" or "/taros" or "/fm" or "/fusionmatter" or "/hp" or "/health" when values.Length == 1 && values[0] >= 0:

                    int kind = command is "/taro" or "/taros" ? 5 : command is "/fm" or "/fusionmatter" ? 4 : 1;

                    return client.SetOwnOperatorValueAsync(kind, values[0], token);

                default:

                    throw new ArgumentException(command);

            }

        }



        void StartGameplayUi(WorldEntry entry, CharacterInfo character)

        {

            StopGameplayUi();

            gameplayState = new GameplayUiSession();

            var operation = worldLifetime;

            var activeSession = Session;

            gameplayState.FollowUpTask += task => _ = StartFollowUpMission(task, activeSession, operation);

            gameplayState.ServerError += ShowError;

            ConfigureMissionProgression();

            gameplayState.Changed += SyncWorldNano;

            gameplayState.SetWorld(entry, character);

            ConfigureCombat(entry);

            StartBuffEffects(entry);

            gameplayUi = new GameplayUiController(Root, gameplayState, language, OpenSettings, () => ToggleWorldMenuCore(true), PlayUiSound);

            gameplayUi.ServiceVoice = (npc,cue) => {

                if (!audio || npc == null) return;

                var speaker = networkWorld.Replicas.Actors.FirstOrDefault(actor =>

                    actor.Entity.Kind == WorldEntityKind.Npc && actor.Entity.Id == npc.Id);

                _ = audio.PlayServiceVoice(WorldNpcCatalog.Load().Find(npc.NpcType)?.VoiceOwner, cue,

                    VoiceLanguage, AudioGain(preferences.Committed, "voice"), operation.Token, speaker ? speaker.transform : null);

            };

            gameplayUi.NanoRewardWorld = networkWorld;

            gameplayUi.MissionVoiceSpeaker = type => networkWorld && networkWorld.Replicas && networkWorld.Player

                ? networkWorld.Replicas.Actors.Where(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == type && !a.Entity.Hidden && a.Entity.Hp > 0)

                    .OrderBy(a => (a.transform.position-networkWorld.Player.position).sqrMagnitude).Select(a => a.Entity).FirstOrDefault() : null;

            gameplayUi.GuideComVoice = npcType => {

                if (audio) _ = audio.PlayServiceVoice(WorldNpcCatalog.Load().Find(npcType)?.VoiceOwner, "CommOut",

                    VoiceLanguage, AudioGain(preferences.Committed, "voice"), operation.Token);

            };

            gameplayUi.ApplyDisplayPreferences(preferences.Committed);

            gameplayUi.ConfigureFirstUse(entry, id => activeSession.SetFirstUseAsync(id, operation.Token));

            gameplayUi.FirstUseVoice = cue => {

                const string prefix = "Computress_";

                if (audio && cue.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

                    _ = audio.PlayServiceVoice("computress", cue.Substring(prefix.Length), VoiceLanguage,

                        AudioGain(preferences.Committed, "voice"), operation.Token);

            };

            if (entry.Level <= 1 && entry.MapNumber == 0) gameplayUi.CheckFirstUse(46);

            gameplayUi.InputGateChanged += ApplyWorldInputGate;

            var preview = rosterPreview;

            networkWorld.CanInteractWithMissionMarker = type => gameplayState != null && gameplayState.Snapshot.RunningQuests.Any(

                q => WorldMissionCatalog.Load().Find(q.TaskId) is { Type: 3 } task && task.EndNpc == type);

            gameplayUi.ConfigureBarber(character, preview, () => {

                preview?.RefreshGameplayAppearance(character);

                if (networkWorld && !networkWorld.RefreshAppearance(character, out var failure)) ShowError(failure);

            });

            if (preview) gameplayUi.InventoryAvatarChanged += preview.SetGameplayAvatar;

            if (preview) gameplayUi.InventoryRotationRequested += preview.Rotate;

            gameplayUi.ConfigureMissions(

                (task, npc) => StartNpcMission(task, npc, activeSession, operation),

                (task, npc) => activeSession.CompleteTaskAsync(task, npc, cancellationToken: operation.Token),

                task => activeSession.StopTaskAsync(task, operation.Token),

                mission => activeSession.TrackMissionAsync(mission, operation.Token));

            gameplayUi.ConfigureServices(activeSession, operation.Token);

            gameplayUi.ResolveServiceNpc = id => networkWorld && networkWorld.Replicas ?

                networkWorld.Replicas.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.Id == id) : null;

            gameplayUi.ConfigureTravel(activeSession, operation.Token, entry);

            gameplayUi.SyncWarpPosition = SyncNpcWarpPositionAsync;

            gameplayUi.ConfigureRankProvider(profiles.Current?.Endpoint.Host);

            gameplayUi.ConfigureVehicle(activeSession, operation.Token, () => networkWorld && networkWorld.IsReady && !worldTransition && !networkWorld.Riding && networkWorld.SliderId == 0);

            gameplayUi.ConfigureSocial(activeSession, operation.Token,

                () => networkWorld?.Replicas?.Session.Entities ?? Array.Empty<WorldEntity>(), entry.BootstrapPackets);

            gameplayUi.SetChatSender(text => SendWorldChat(activeSession, text, operation.Token));

            gameplayUi.ConfigureLocationDebug(entry.UserLevel, networkWorld);

            gameplayUi.RequestMenuChat = (text, emote) => activeSession.SendMenuChatAsync(text, emote, operation.Token);

            gameplayUi.RequestEmote = emote => activeSession.SendAvatarEmoteAsync(entry.PlayerId, emote, operation.Token);

            gameplayUi.SetItemMover((from, fromSlot, to, toSlot) => activeSession.SendItemMoveAsync(from, fromSlot, to, toSlot, operation.Token));

            gameplayUi.DeleteItem = slot => activeSession.DeleteInventoryItemAsync(slot, operation.Token);

            gameplayUi.ConfigureTrade(new GameplayTradeSession(entry.PlayerId), new GameplayTradeActions {

                Offer = peer => activeSession.SendTradeOfferAsync(peer, operation.Token),

                Accept = (from, to) => activeSession.SendTradeAcceptAsync(from, to, operation.Token),

                Refuse = (from, to) => activeSession.SendTradeRefuseAsync(from, to, operation.Token),

                CancelOffer = (from, to) => activeSession.SendTradeCancelOfferAsync(from, to, operation.Token),

                Confirm = (from, to) => activeSession.SendTradeConfirmAsync(from, to, operation.Token),

                Cancel = (from, to) => activeSession.SendTradeCancelAsync(from, to, operation.Token),

                Cash = (from, to, amount) => activeSession.SendTradeRegisterCashAsync(from, to, amount, operation.Token),

                Chat = (from, to, text) => activeSession.SendTradeChatAsync(from, to, text, operation.Token),

                AddItem = (from, to, item) => activeSession.SendTradeRegisterItemAsync(from, to, item, operation.Token),

                RemoveItem = (from, to, item) => activeSession.SendTradeUnregisterItemAsync(from, to, item, operation.Token)

            });

            gameplayUi.Error += ShowError;

            NetworkEventReceived += HandleGameplayEvent;

            var deferred = deferredGameplayEvents.ToArray(); deferredGameplayEvents.Clear();

            foreach (var value in deferred)

            {

                NetworkEventReceived?.Invoke(value);

                if (value.PlayerPosition != null)

                    ApplyServerRelocation(gameplayState.Snapshot.MapNumber, value.PlayerPosition.X, value.PlayerPosition.Y, value.PlayerPosition.Z);

            }

            rosterPreview?.SetGameplayPortrait(gameplayUi.Hud.Portrait);

            RefreshGameplayEquipmentAppearance();

            ApplyWorldInputGate();

        }



        void SyncWorldNano(GameplayUiState state)

        {

            if (!networkWorld || !state.IsActive) return;

            networkWorld.SetPlayerDead(state.Hp <= 0);

            if (!networkWorld.LocalNano || worldTransition) return;

            int slot = state.ActiveNanoSlot;

            networkWorld.LocalNano.SetWeapon(state.Equipment[0].ItemId);

            networkWorld.LocalNano.SetNano(!networkWorld.Riding && !networkWorld.Ziplining && slot >= 0 && slot < state.NanoSlots.Count ? state.NanoSlots[slot] : 0);

        }



        void TickGameplayUi()

        {

            if (gameplayUi == null) return;

            TickBuffEffects();

            TickMissionProgression();

            var nearbyBank = networkWorld?.Replicas?.Actors

                .Where(v => v.Definition?.IsBank == true && v.Definition.HiddenMarker && !v.Entity.Hidden && v.Entity.Interactable &&

                    Vector3.Distance(v.transform.position, networkWorld.Player.position) <= 8)

                .OrderBy(v => Vector3.Distance(v.transform.position, networkWorld.Player.position)).FirstOrDefault();

            gameplayUi.SetNearbyService(nearbyBank?.Entity, nearbyBank?.Definition);

            if (networkWorld && networkWorld.View) gameplayUi.SetCameraHeading(networkWorld.View.transform.eulerAngles.y);

            gameplayUi.SetLanguage(language);

            gameplayUi.Tick(Time.deltaTime, preferences.Committed, worldMenu != null || settings?.Visible == true || busy || gameplayState.Snapshot.Hp == 0);

            if (networkWorld && !worldTransition && !busy && !gameplayCommandSending && Application.isFocused &&

                !gameplayUi.InputBlocked && !gameplayUi.VehicleBusy &&

                (GameplayInput.Pressed(GameplayAction.Fire1) ||

                    GameplayInput.Pressed(GameplayAction.Jump) && Gamepad.current?.buttonSouth.wasPressedThisFrame == true) &&

                !networkWorld.UsingLauncher && (networkWorld.TryStartZipline() || networkWorld.TryStartLauncher()))

                CancelProjectileInput();

            gameplayUi.ShowLauncher(networkWorld && networkWorld.AimingLauncher, networkWorld ? networkWorld.LauncherGauge : 0);

            bool targetBlocked = !networkWorld || !networkWorld.IsReady || networkWorld.Riding || networkWorld.Ziplining || networkWorld.UsingLauncher || worldTransition ||

                worldMenu != null || settings?.Visible == true || busy || !Application.isFocused || gameplayUi.InputBlocked;

            var aimTarget = targetBlocked ? null : networkWorld.Target();

            gameplayUi.UpdateTarget(aimTarget, networkWorld?.Player, networkWorld?.View, targetBlocked,

                !targetBlocked && (networkWorld.HasZiplineTarget || networkWorld.HasLauncherTarget));

            gameplayUi.UpdateWorldAnnotations(networkWorld, targetBlocked);

            if (!targetBlocked) gameplayUi.TickAmbientBarks(networkWorld);

            if (gameplayState.Snapshot.Hp == 0)

            {

                if (revivePrompt == null)

                {

                    gameplayUi.CloseWindow();

                    revivePrompt = new GameplayResurrection(UiLayers.For(Root).Host(UiLayer.Dialog), language, (type, slot) => type switch {

                        3 => Session.RevivePhoenixAsync(false, worldLifetime.Token),

                        6 => Session.ReviveWithItemAsync(slot, worldLifetime.Token),

                        _ => Session.ReviveAsync(worldLifetime.Token)

                    }, ShowError, PlayUiSound);

                    ApplyWorldInputGate();

                }

                revivePrompt.Tick(gameplayState.Snapshot, networkWorld && networkWorld.IsReady, worldMenu != null || settings?.Visible == true || busy, Time.unscaledDeltaTime);

            }

            else if (revivePrompt != null) { revivePrompt.Dispose(); revivePrompt = null; ApplyWorldInputGate(); }

            if (!networkWorld || networkWorld.Riding || networkWorld.Ziplining || networkWorld.UsingLauncher || gameplayUi.InputBlocked || revivePrompt != null ||

                worldMenu != null || settings?.Visible == true || busy || !Application.isFocused || gameplayState.Snapshot.Hp <= 0)

            {

                CancelProjectileInput();

                return;

            }

            bool projectileBlocked = targetBlocked || gameplayUi.WeaponChangePending || networkWorld.SliderId != 0;
            if (!projectileBlocked && Gamepad.current?.buttonSouth.wasPressedThisFrame == true &&
                GameplayInput.Pressed(GameplayAction.Jump) && TryTalkToNpc(aimTarget))
            {
                padWorldInteractFrame = Time.frameCount;
                CancelProjectileInput();
                return;
            }

            if (projectileBlocked) CancelProjectileInput();

            if (GameplayInput.Pressed(GameplayAction.VehicleToggle) && !gameplayCommandSending)

            {

                CancelProjectileInput();

                _ = gameplayUi.ToggleVehicle();

                return;

            }

            if (gameplayUi.VehicleBusy) { CancelProjectileInput(); return; }

            if (gameplayState.Snapshot.VehicleMounted)

            {

                CancelProjectileInput();

                if (!gameplayCommandSending && (GameplayInput.Pressed(GameplayAction.Fire1) || GameplayInput.Pressed(GameplayAction.Fire2) ||

                    GameplayInput.Pressed(GameplayAction.NanoCharge) || NanoActions.Any(GameplayInput.Pressed)))

                    _ = gameplayUi.ToggleVehicle();

                return;

            }

            if (gameplayCommandSending)

            {

                if (!projectileBlocked) TickProjectileInput();

                return;

            }

            for (int slot = 0; slot < 3; slot++)

                if (GameplayInput.Pressed(NanoActions[slot]))

                {

                    CancelProjectileInput();

                    var snapshot = gameplayState.Snapshot;

                    int requested = snapshot.ActiveNanoSlot == slot ? -1 : slot;

                    if (requested >= 0)

                    {

                        if (slot >= snapshot.NanoSlots.Count) return;

                        var nano = snapshot.Nanos.FirstOrDefault(candidate => candidate.Id == snapshot.NanoSlots[slot]);

                        int maximumStamina = GameplayUiCatalog.Load().Nano(nano.Id)?.NanoMaxStamina ?? 0;

                        if (!nano.IsOwned || nano.SkillId <= 0 || maximumStamina <= 0 || nano.Stamina * 5 <= maximumStamina) return;

                    }

                    _ = SendGameplayCommand(() => Session.ActivateNanoAsync(requested, worldLifetime.Token));

                    return;

                }

            if (GameplayInput.Pressed(GameplayAction.WeaponChange) && networkWorld.IsReady && !worldTransition)

            {

                CancelProjectileInput();

                ChangeEquippedWeapon();

            }

            else if (GameplayInput.Pressed(GameplayAction.NanoCharge))

            {

                CancelProjectileInput();

                _ = SendGameplayCommand(() => Session.ChargeNanoAsync(worldLifetime.Token));

            }

            else if (GameplayInput.Pressed(GameplayAction.Fire2))

            {

                CancelProjectileInput();

                UseActiveNanoSkill();

            }

            else if (!projectileBlocked && !characterAttackPermission && GameplayInput.Pressed(GameplayAction.Fire1) &&

                aimTarget && aimTarget.Entity.Kind == WorldEntityKind.Player && aimTarget.Entity.Hp > 0 &&

                Vector3.Distance(networkWorld.Player.position, aimTarget.transform.position) <= 8)

            {

                CancelProjectileInput();

                gameplayUi.OpenPlayer(aimTarget.Entity);

            }

            else if (!projectileBlocked && TickProjectileInput()) return;

            else if (GameplayInput.Held(GameplayAction.Fire1) && !gameplayUi.WeaponChangePending)

            {

                var target = aimTarget ?? networkWorld.Target();

                if (TryFireWeapon(target)) return;

                if (!GameplayInput.Pressed(GameplayAction.Fire1)) return;

                if (!target && nearbyBank) { gameplayUi.OpenNpcService(nearbyBank.Entity, nearbyBank.Definition); return; }

                TryTalkToNpc(target);

            }

        }



        internal bool TryTalkToNpc(NetworkEntityView target)

        {

            if (!target || target.Entity.Kind != WorldEntityKind.Npc || target.Definition?.Team != 1 ||

                target.Entity.Hp <= 0 || target.Entity.Hidden || !target.Entity.Interactable) return false;

            var player = WorldCoordinates.ToServer(networkWorld.Player.position);

            var npc = target.Entity;

            // RANGE_INTERACT uses the truncated centimetre distance between roots on the shard.

            double x = (double)player.x - npc.X, y = (double)player.y - npc.Y, z = (double)player.z - npc.Z;

            if (x*x + y*y + z*z >= 801d*801d) return false;

            var facing = networkWorld.Player.position - target.transform.position;

            facing.y = 0;

            if (facing.sqrMagnitude > .0001f) target.transform.rotation = Quaternion.LookRotation(facing);

            if (gameplayUi.OpenNpcTravel(npc, target.Definition)) return true;

            if (target.Definition.IsBank || target.Definition.IsVendor) gameplayUi.OpenNpcService(npc, target.Definition);

            else gameplayUi.OpenNpcConversation(npc);

            return true;

        }



        void UseActiveNanoSkill()

        {

            if (CombatUnavailable) return;

            var state = gameplayState.Snapshot; int slot = state.ActiveNanoSlot;

            if (slot < 0 || slot >= state.NanoSlots.Count) return;

            var nano = state.Nanos.FirstOrDefault(n => n.Id == state.NanoSlots[slot]);

            var skill = WorldSkillCatalog.Load().Find(nano.SkillId);

            if (skill == null || skill.Passive || nano.Stamina <= 0 || nanoCooldowns.TryGetValue(skill.Id, out float ready) && Time.unscaledTime < ready) return;

            int[] targets; int focus;

            if (skill.Type == 27 || skill.Type == 28) { focus = 0; targets = Array.Empty<int>(); }

            else if (skill.TargetType == 1)

            {

                if (!TrySelectNanoTargets(skill, out focus, out targets)) return;

            }

            else if (skill.TargetType == 3) { focus = 0; targets = Array.Empty<int>(); }

            else { focus = state.PlayerId; targets = new[] { focus }; }

            var session = Session;

            var operation = worldLifetime;

            _ = SendGameplayCommand(async () => {

                await session.UseNanoSkillAsync(targets, focus, operation.Token);

                if (CurrentWorldOperation(operation) && skill.MovementPower != null && networkWorld.LaunchNanoMovement(skill, slot))

                    nanoCooldowns[skill.Id] = Time.unscaledTime + skill.Cooldown;

            });

        }



        bool TrySelectNanoTargets(WorldSkillDefinition skill, out int focus, out int[] targets)

        {

            const int Cone = 3, SelfArea = 5, TargetArea = 6;

            bool centeredOnCaster = skill.TargetingType == SelfArea;

            var origin = networkWorld.Player.position;

            var primary = centeredOnCaster ? null : networkWorld.Target(Mathf.Max(1, skill.Range) + 3);

            focus = 0;

            targets = Array.Empty<int>();

            if (!centeredOnCaster && (!ValidEnemy(primary) || !InRange(primary, origin, skill.Range))) return false;

            if (primary) focus = primary.Entity.Id;

            if (skill.TargetingType != Cone && skill.TargetingType != SelfArea && skill.TargetingType != TargetArea)

            {

                if (primary) targets = new[] { focus };

                return targets.Length != 0;

            }



            var center = skill.TargetingType == TargetArea ? primary.transform.position : origin;

            float range = skill.TargetingType == Cone ? skill.Range : skill.Area;

            var enemies = networkWorld.Replicas.Actors.Where(actor => ValidEnemy(actor) && actor != primary && InRange(actor, center, range));

            if (skill.TargetingType == Cone)

            {

                var direction = Vector3.ProjectOnPlane(primary.transform.position - origin, Vector3.up);

                enemies = enemies.Where(actor => Vector3.Angle(direction,

                    Vector3.ProjectOnPlane(actor.transform.position - origin, Vector3.up)) <= skill.Angle * .5f);

            }

            enemies = enemies.OrderBy(actor => (actor.transform.position - center).sqrMagnitude);

            if (primary) enemies = enemies.Prepend(primary);

            targets = enemies.Take(Mathf.Min(3, skill.TargetCount)).Select(actor => actor.Entity.Id).ToArray();

            return targets.Length != 0;



            static bool ValidEnemy(NetworkEntityView actor) => actor && actor.Hostile && !actor.Entity.Hidden && actor.Entity.Hp > 0;

            static bool InRange(NetworkEntityView actor, Vector3 source, float range) =>

                Vector3.Distance(source, actor.transform.position) < range + Mathf.Max(actor.Definition?.Radius ?? 0, (actor.Definition?.Height ?? 0) * .5f);

        }



        async Task SendGameplayCommand(Func<Task> action)

        {

            var operation = worldLifetime; gameplayCommandSending = true;

            try { await action(); }

            catch (OperationCanceledException) { }

            catch (Exception failure) { if (CurrentWorldOperation(operation)) ShowError(failure.Message); }

            finally { if (CurrentWorldOperation(operation)) gameplayCommandSending = false; }

        }



        async Task StartFollowUpMission(int task, FusionClient activeSession, System.Threading.CancellationTokenSource operation)

        {

            if (!pendingMissionFollowUps.Add(task)) return;

            try

            {

                var definition = WorldMissionCatalog.Load().Find(task);

                if (definition == null) throw new ProtocolException("Unknown outgoing mission task: " + task);

                float deadline = Time.realtimeSinceStartup + 120;

                int escort = 0, giver = 0;

                while (CurrentWorldOperation(operation))

                {

                    operation.Token.ThrowIfCancellationRequested();

                    bool ready = activeSession.IsWorldLoaded && !worldTransition && networkWorld && networkWorld.IsReady &&

                        (definition.MapNumber == 0 || definition.MapNumber == networkWorld.MapNumber);

                    if (ready && definition.GiverNpc > 0)

                    {

                        var actor = networkWorld.Replicas?.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc &&

                            a.Entity.NpcType == definition.GiverNpc && a.Entity.Hp > 0 && !a.Entity.Hidden && a.Entity.Interactable &&

                            Vector3.Distance(a.transform.position, networkWorld.Player.position) <= 8);

                        giver = actor?.Entity.Id ?? 0; ready = giver > 0;

                    }

                    if (ready && definition.EscortNpc > 0)

                    {

                        var actor = networkWorld.Replicas?.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Npc &&

                            a.Entity.NpcType == definition.EscortNpc && a.Entity.Hp > 0 && !a.Entity.Hidden &&

                            Vector3.Distance(a.transform.position, networkWorld.Player.position) <= 8);

                        escort = actor?.Entity.Id ?? 0; ready = escort > 0;

                    }

                    if (ready)

                    {

                        await activeSession.StartTaskAsync(task, giver, escort, operation.Token);

                        return;

                    }

                    if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("Outgoing mission task is waiting for its map, giver or escort: " + task);

                    await Task.Yield();

                }

            }

            catch (OperationCanceledException) { }

            catch (Exception failure) { if (CurrentWorldOperation(operation)) ShowError(failure.Message); }

            finally { pendingMissionFollowUps.Remove(task); }

        }



        void StopGameplayUi()

        {

            StopCombat();

            StopBuffEffects();

            revivePrompt?.Dispose(); revivePrompt = null; gameplayCommandSending = false; nanoCooldowns.Clear(); pendingMissionFollowUps.Clear();

            NetworkEventReceived -= HandleGameplayEvent;

            rosterPreview?.SetGameplayPortrait(null);

            rosterPreview?.SetGameplayAvatar(null);

            if (gameplayUi != null)

            {

                gameplayUi.InputGateChanged -= ApplyWorldInputGate;

                gameplayUi.Dispose(); gameplayUi = null;

            }

            if (gameplayState != null) { gameplayState.Changed -= SyncWorldNano; gameplayState.Clear(); gameplayState = null; }

        }



        void HandleGameplayEvent(NetworkEvent value)

        {

            if (gameplayUi == null || !value.IsShard || value.Kind != NetworkEventKind.Packet) return;

            try

            {

                BindCombatWorld();

                networkWorld?.Replicas?.Receive(value.Frame);

                var before = gameplayState.Snapshot;

                if (value.Frame.PacketType != ProtocolInventory.MoveSuccess && value.Frame.PacketType != ProtocolInventory.EquipmentChanged && value.Frame.PacketType != ProtocolInventory.DeleteSuccess)

                    gameplayState.ApplyFrame(value.Frame);

                var after = gameplayState.Snapshot;

                if (value.Frame.PacketType == ProtocolGameplay.NanoCreate)

                {

                    int nano = Protocol0104.Read16(value.Frame.Payload,20);

                    if (!before.Nanos.Any(n => n.Id == nano && n.IsOwned)) gameplayUi.ShowNanoAcquisition(nano);

                }

                if (after.Level > before.Level && before.IsActive) gameplayUi.QueueGuideMessage(after.Guide, true);

                if (networkWorld && before.PlayerId == after.PlayerId && after.Hp < before.Hp && before.Hp > 0)

                {

                    bool critical = ProtocolGameplay.TryCombat(value.Frame, out var attack) &&

                        attack.Hits.Any(hit => hit.Kind == WorldEntityKind.Player && hit.Id == after.PlayerId && hit.Critical);

                    networkWorld.PlayPlayerDamageReaction(after.Hp == 0, critical);

                }

                if (value.Frame.PacketType == ProtocolGameplay.NanoActiveSuccess && Protocol0104.Read32(value.Frame.Payload, 4) != 0)

                {

                    var state = gameplayState.Snapshot;

                    if (state.ActiveNanoSlot >= 0 && state.ActiveNanoSlot < state.NanoSlots.Count)

                    {

                        int id = state.NanoSlots[state.ActiveNanoSlot];

                        var nano = state.Nanos.FirstOrDefault(n => n.Id == id);

                        networkWorld?.LocalNano?.ShowSkill(id, nano.SkillId);

                    }

                }

                gameplayUi.ReceiveNpcSpeech(value.Frame);

                gameplayUi.ReceiveCombat(value.Frame);

                ApplyBuffPacket(value.Frame);

                if (ProtocolNanoSkill.TryDecode(value.Frame, out var nanoSkill))

                {

                    if (nanoSkill.CasterId == gameplayState.Snapshot.PlayerId && value.Frame.PacketType == ProtocolNanoSkill.Success)

                        nanoCooldowns[nanoSkill.SkillId] = Time.unscaledTime + (WorldSkillCatalog.Load().Find(nanoSkill.SkillId)?.Cooldown ?? 0);

                    foreach (var effect in nanoSkill.Effects)

                        if (effect.Kind == WorldEntityKind.Player && effect.Id == gameplayState.Snapshot.PlayerId && effect.Map.HasValue)

                        {

                            ApplyServerRelocation(effect.Map.Value, effect.X, effect.Y, effect.Z);

                        }

                }

                if (gameplayUi == null) return; // Relocation can synchronously retire the previous world UI.

                gameplayUi.ReceiveServices(value.Frame);

                gameplayUi.ReceiveSocial(value.Frame);

                if (settings?.Visible == true && value.Frame.PacketType is ProtocolSocial.List or ProtocolSocial.Removed or ProtocolSocial.Blocked)

                    RefreshBlockedPlayers();

                gameplayUi.ReceiveBarber(value.Frame);

                gameplayUi.ReceiveTravel(value.Frame);

                gameplayUi.ReceiveRace(value.Frame);

                gameplayUi.ReceiveNanoStation(value.Frame);

                gameplayUi.ReceiveVehicle(value.Frame);

                HandleTravelFrame(value.Frame);

                if (gameplayUi == null) return;

                gameplayUi.ReceiveChat(value.Frame); gameplayUi.ReceiveInventory(value.Frame); gameplayUi.ReceiveTrade(value.Frame);

                if (value.Frame.PacketType == ProtocolChat.EmoteReply || value.Frame.PacketType == ProtocolChat.MenuSuccess || value.Frame.PacketType == ProtocolChat.Success)

                {

                    bool standalone = value.Frame.PacketType == ProtocolChat.EmoteReply;

                    Protocol0104.RequireSize(value.Frame.Payload, standalone ? 8 : 264);

                    int code = Protocol0104.Read32(value.Frame.Payload, standalone ? 4 : 260);

                    int local = gameplayState.Snapshot.PlayerId;

                    if (code > 0 && networkWorld) networkWorld.PlayAvatarEmote(Protocol0104.Read32(value.Frame.Payload, 0), local, code,

                        next => _ = SendGameplayCommand(() => Session.SendAvatarEmoteAsync(local, next, worldLifetime.Token)));

                }

                if (value.Frame.PacketType == ProtocolGameplay.RegenSuccess)

                {

                    var state = gameplayState.Snapshot;

                    var p = value.Frame.Payload;

                    PlayUiSound("resurrectm_warp");

                    ApplyServerRelocation(state.MapNumber, ProtocolGameplay.I(p, 8), ProtocolGameplay.I(p, 12), ProtocolGameplay.I(p, 16));

                    revivePrompt?.Dispose(); revivePrompt = null;

                    ApplyWorldInputGate();

                }

                RefreshGameplayEquipmentAppearance();

                RefreshPersonalVehicle();

            }

            catch (ProtocolException error) { ShowError(error.Message); }

        }



        void ApplyServerRelocation(int map, int x, int y, int z)

        {

            if (!networkWorld) return;

            if (map != networkWorld.MapNumber)

            {

                networkWorld.LocalNano?.Clear();

                networkWorld.Replicas?.Session.Clear();

                networkWorld.RelocateMap(map, x, y, z);

                if (gameplayState != null) SyncWorldNano(gameplayState.Snapshot);

            }

            networkWorld.SetServerPosition(x, y, z);

            lastMovementPosition = movementPosition = networkWorld.Player.position;

            lastMovementAt = Time.unscaledTime; movementPending = lastMovementWasMoving = false;

            gameplayState?.SetLocalPosition(lastMovementPosition);

        }

    }

}

