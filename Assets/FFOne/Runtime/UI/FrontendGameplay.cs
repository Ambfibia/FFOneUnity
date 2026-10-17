using FFOne.Integration;
using FFOne.Network;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using FFOne.World;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        GameplayUiSession gameplayState;
        GameplayUiController gameplayUi;
        bool gameplayCommandSending;
        GameplayPrompt revivePrompt;
        float reviveRequestedUntil;
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

        void StartGameplayUi(WorldEntry entry, CharacterInfo character)
        {
            StopGameplayUi();
            gameplayState = new GameplayUiSession();
            var operation = worldLifetime;
            var activeSession = Session;
            gameplayState.FollowUpTask += task => _ = StartFollowUpMission(task, activeSession, operation);
            gameplayState.ServerError += ShowError;
            ConfigureMissionProgression();
            gameplayState.SetWorld(entry, character);
            ConfigureCombat(entry);
            StartBuffEffects(entry);
            gameplayUi = new GameplayUiController(Root, gameplayState, language, OpenSettings, () => ToggleWorldMenuCore(true));
            gameplayUi.InputGateChanged += ApplyWorldInputGate;
            var preview = GetComponent<PreviewStage>();
            if (preview) gameplayUi.InventoryAvatarChanged += preview.SetGameplayAvatar;
            if (preview) gameplayUi.InventoryRotationRequested += preview.Rotate;
            gameplayUi.ConfigureMissions(
                (task, npc) => StartNpcMission(task, npc, activeSession, operation),
                (task, npc) => activeSession.CompleteTaskAsync(task, npc, cancellationToken: operation.Token),
                task => activeSession.StopTaskAsync(task, operation.Token),
                mission => activeSession.TrackMissionAsync(mission, operation.Token));
            gameplayUi.ConfigureServices(activeSession, operation.Token);
            gameplayUi.ConfigureTravel(activeSession, operation.Token, entry);
            gameplayUi.ConfigureVehicle(activeSession, operation.Token, () => networkWorld && networkWorld.IsReady && !worldTransition && !networkWorld.Riding && networkWorld.SliderId == 0);
            gameplayUi.ConfigureSocial(activeSession, operation.Token,
                () => networkWorld?.Replicas?.Session.Entities ?? Array.Empty<WorldEntity>(), entry.BootstrapPackets);
            gameplayUi.SetChatSender(text => activeSession.SendFreeChatAsync(text, operation.Token));
            gameplayUi.SetItemMover((from, fromSlot, to, toSlot) => activeSession.SendItemMoveAsync(from, fromSlot, to, toSlot, operation.Token));
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
            GetComponent<PreviewStage>()?.SetGameplayPortrait(gameplayUi.Hud.Portrait);
            RefreshGameplayEquipmentAppearance();
            ApplyWorldInputGate();
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
            gameplayUi.SetLanguage(language);
            gameplayUi.Tick(Time.deltaTime, preferences.Committed, worldMenu != null || settings?.Visible == true || busy);
            if (gameplayState.Snapshot.Hp == 0 && revivePrompt == null && Time.unscaledTime >= reviveRequestedUntil)
            {
                revivePrompt = new GameplayPrompt(Root, language == "ru" ? "ВОЗРОЖДЕНИЕ" : "REVIVE",
                    gameplayState.Snapshot.HasBuff(5) ? (language == "ru" ? "Возродиться силой Phoenix?" : "Revive with Phoenix?") :
                        language == "ru" ? "Вернуться к ближайшему пункту возрождения?" : "Return to the nearest resurrection point?",
                    language == "ru", null, async _ => {
                        if (gameplayState.Snapshot.HasBuff(5)) await Session.RevivePhoenixAsync(false, worldLifetime.Token);
                        else await Session.ReviveAsync(worldLifetime.Token);
                        reviveRequestedUntil = Time.unscaledTime + 10;
                    },
                    () => { reviveRequestedUntil = Time.unscaledTime + 3; return Task.CompletedTask; }, ShowError);
                revivePrompt.Closed += () => { revivePrompt = null; ApplyWorldInputGate(); };
                ApplyWorldInputGate();
            }
            if (!networkWorld || networkWorld.Riding || gameplayCommandSending || gameplayUi.InputBlocked || revivePrompt != null ||
                worldMenu != null || settings?.Visible == true || busy || !Application.isFocused || gameplayState.Snapshot.Hp <= 0) return;
            if (SettingsInputReader.Pressed("VehicleToggle", preferences.Committed)) { _ = gameplayUi.ToggleVehicle(); return; }
            if (gameplayUi.VehicleBusy) return;
            if (gameplayState.Snapshot.VehicleMounted)
            {
                if (SettingsInputReader.Pressed("Fire1", preferences.Committed) || SettingsInputReader.Pressed("Fire2", preferences.Committed) ||
                    SettingsInputReader.Pressed("NanoCharge", preferences.Committed) || Enumerable.Range(1, 3).Any(slot => SettingsInputReader.Pressed("Nano" + slot, preferences.Committed)))
                    _ = gameplayUi.ToggleVehicle();
                return;
            }
            for (int slot = 0; slot < 3; slot++)
                if (SettingsInputReader.Pressed("Nano" + (slot + 1), preferences.Committed))
                { _ = SendGameplayCommand(() => Session.ActivateNanoAsync(gameplayState.Snapshot.ActiveNanoSlot == slot ? -1 : slot, worldLifetime.Token)); return; }
            if (SettingsInputReader.Pressed("NanoCharge", preferences.Committed))
                _ = SendGameplayCommand(() => Session.ChargeNanoAsync(worldLifetime.Token));
            else if (SettingsInputReader.Pressed("Fire2", preferences.Committed)) UseActiveNanoSkill();
            else if (SettingsInputReader.Held("Fire1", preferences.Committed))
            {
                var target = networkWorld.Target();
                if (TryFireWeapon(target)) return;
                if (!SettingsInputReader.Pressed("Fire1", preferences.Committed)) return;
                if (!target && nearbyBank) { gameplayUi.OpenNpcService(nearbyBank.Entity, nearbyBank.Definition); return; }
                if (target && target.Entity.Hp > 0)
                {
                    if (target.Entity.Kind == WorldEntityKind.Npc && Vector3.Distance(networkWorld.Player.position, target.transform.position) <= 8)
                    {
                        if (gameplayUi.OpenNpcTravel(target.Entity, target.Definition)) { }
                        else if (target.Definition?.IsBank == true || target.Definition?.IsVendor == true) gameplayUi.OpenNpcService(target.Entity, target.Definition);
                        else gameplayUi.OpenNpcMissions(target.Entity);
                    }
                }
            }
        }

        void UseActiveNanoSkill()
        {
            if (CombatIncapacitated) return;
            var state = gameplayState.Snapshot; int slot = state.ActiveNanoSlot;
            if (slot < 0 || slot >= state.NanoSlots.Count) return;
            var nano = state.Nanos.FirstOrDefault(n => n.Id == state.NanoSlots[slot]);
            var skill = WorldSkillCatalog.Load().Find(nano.SkillId);
            if (skill == null || skill.Passive || nano.Stamina <= 0 || nanoCooldowns.TryGetValue(skill.Id, out float ready) && Time.unscaledTime < ready) return;
            int[] targets; int focus;
            if (skill.Type == 27 || skill.Type == 28) { focus = 0; targets = Array.Empty<int>(); }
            else if (skill.TargetType == 1)
            {
                var actor = networkWorld.Target(Mathf.Max(1, skill.Range) + 3);
                if (!actor || !actor.Hostile || actor.Entity.Hp <= 0 ||
                    Vector3.Distance(networkWorld.Player.position, actor.transform.position) > skill.Range + (actor.Definition?.Radius ?? 0)) return;
                focus = actor.Entity.Id; targets = new[] { focus };
            }
            else if (skill.TargetType == 3) { focus = 0; targets = Array.Empty<int>(); }
            else { focus = state.PlayerId; targets = new[] { focus }; }
            _ = SendGameplayCommand(() => Session.UseNanoSkillAsync(targets, focus, worldLifetime.Token));
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
            revivePrompt?.Dispose(); revivePrompt = null; gameplayCommandSending = false; reviveRequestedUntil = 0; nanoCooldowns.Clear(); pendingMissionFollowUps.Clear();
            NetworkEventReceived -= HandleGameplayEvent;
            GetComponent<PreviewStage>()?.SetGameplayPortrait(null);
            GetComponent<PreviewStage>()?.SetGameplayAvatar(null);
            if (gameplayUi != null)
            {
                gameplayUi.InputGateChanged -= ApplyWorldInputGate;
                gameplayUi.Dispose(); gameplayUi = null;
            }
            gameplayState?.Clear(); gameplayState = null;
        }

        void HandleGameplayEvent(NetworkEvent value)
        {
            if (gameplayUi == null || !value.IsShard || value.Kind != NetworkEventKind.Packet) return;
            try
            {
                BindCombatWorld();
                networkWorld?.Replicas?.Receive(value.Frame);
                if (value.Frame.PacketType != ProtocolInventory.MoveSuccess && value.Frame.PacketType != ProtocolInventory.EquipmentChanged)
                    gameplayState.ApplyFrame(value.Frame);
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
                gameplayUi.ReceiveServices(value.Frame);
                gameplayUi.ReceiveSocial(value.Frame);
                gameplayUi.ReceiveTravel(value.Frame);
                gameplayUi.ReceiveVehicle(value.Frame);
                HandleTravelFrame(value.Frame);
                gameplayUi.ReceiveChat(value.Frame); gameplayUi.ReceiveInventory(value.Frame); gameplayUi.ReceiveTrade(value.Frame);
                if (value.Frame.PacketType == ProtocolGameplay.RegenSuccess)
                {
                    var state = gameplayState.Snapshot;
                    var p = value.Frame.Payload;
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
                networkWorld.RelocateMap(map, x, y, z);
            }
            networkWorld.SetServerPosition(x, y, z);
            lastMovementPosition = movementPosition = networkWorld.Player.position;
            lastMovementAt = Time.unscaledTime; movementPending = lastMovementWasMoving = false;
            gameplayState?.SetLocalPosition(lastMovementPosition);
        }
    }
}
