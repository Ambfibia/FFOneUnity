using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        async Task<bool> SyncNpcWarpPositionAsync(WorldEntity npc, CancellationToken cancellation)
        {
            await movementTask;
            cancellation.ThrowIfCancellationRequested();
            if (!networkWorld || !networkWorld.Player || networkWorld.Riding || networkWorld.Ziplining || !Session.IsWorldLoaded) return false;
            var position = networkWorld.Player.position;
            var point = WorldCoordinates.ToServer(position);
            var delta = new Vector3(point.x - npc.X, point.y - npc.Y, point.z - npc.Z);
            // Keep the same 800 server-unit reach as RustyFusion.
            if (delta.sqrMagnitude > 800 * 800) return false;
            movementPending = lastMovementWasMoving = false;
            lastMovementPosition = movementPosition = position;
            lastMovementAt = Time.unscaledTime;
            await Session.SendMovementAsync(point.x, point.y, point.z,
                WorldCoordinates.ToServerHeading(networkWorld.Player.eulerAngles.y), 0, 0, 0, 0, 0, true, cancellation);
            return true;
        }

        void HandleTravelFrame(ProtocolFrame frame)
        {
            if (!networkWorld || !ProtocolTravel.TryDecode(frame, out var u) || u.Failed) return;
            if (u.Type == ProtocolTravel.WarpOk) { _ = FinishWarpAsync(u); return; }
            if (u.Type == ProtocolTravel.TripOk && u.Transport == 1)
            { ApplyServerRelocation(networkWorld.MapNumber, u.X, u.Y, u.Z); return; }
            if (u.PlayerId != gameplayState.Snapshot.PlayerId) return;
            if (u.Type == ProtocolTravel.Riding || u.Type == ProtocolTravel.PeerRiding)
            {
                networkWorld.SetRiding(u.Ride != 0); movementPending = lastMovementWasMoving = false;
                if (u.Ride == 0)
                {
                    lastMovementPosition = movementPosition = networkWorld.Player.position; lastMovementAt = Time.unscaledTime;
                    var p = WorldCoordinates.ToServer(movementPosition); gameplayState.Relocate(networkWorld.MapNumber, p.x, p.y, p.z);
                }
            }
            if (u.Type == ProtocolTravel.FlightMove) networkWorld.FlyTo(u.X, u.Y, u.Z);
        }
        async Task FinishWarpAsync(TravelUpdate destination)
        {
            if (worldTransition || worldLifetime == null) return;
            worldTransition = true; var operation = worldLifetime; var active = Session;
            networkWorld.LocalNano?.Clear();
            networkWorld.MovementEnabled = false; movementPending = lastMovementWasMoving = false;
            gameplayUi.Windows.Close(); networkWorld.Replicas?.Session.Clear();
            SetBusy(true); SetStatus("");
            try
            {
                var layers = UiLayers.For(Root);
                var hud = layers.Host(UiLayer.Hud);
                var notices = layers.Host(UiLayer.Notification);
                var previousHud = hud.style.display;
                var previousNotices = notices.style.display;
                var bars = FFView.InstantiateClass("ui/views/Retrobution/GameplayHud", "world-warp-bars");
                layers.Add(bars, UiLayer.Cinematic);
                try
                {
                    hud.style.display = notices.style.display = DisplayStyle.None;
                    NativeCombatEffect.Spawn(networkWorld.Player, 394, 1, false, networkWorld.View, 1.5f);
                    PlayUiSound("dexbot_warp");
                    float end = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }
                finally
                {
                    bars.RemoveFromHierarchy();
                    hud.style.display = previousHud;
                    notices.style.display = previousNotices;
                }
                await ShowLoadingAsync(IsFuture(destination.X, destination.Y, SelectedCharacter) ? FrontendLoadingStage.FutureAssets : FrontendLoadingStage.PastAssets);
                await movementTask;
                bool instance = WorldTravelCatalog.Load().Warps.Any(w => w.Map != 0 && w.X == destination.X && w.Y == destination.Y && w.Z == destination.Z);
                var entry = await active.CompleteWarpAsync(destination, instance, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                // RustyFusion persists the last exterior position when exiting from a private instance.
                if (networkWorld.MapNumber == 0 && entry.MapNumber != 0)
                    rosterReturnPosition = WorldCoordinates.ToServer(gameplayState.Snapshot.WorldPosition);
                networkWorld.RelocateMap(entry.MapNumber, entry.X, entry.Y, entry.Z);
                gameplayState.Relocate(entry.MapNumber, entry.X, entry.Y, entry.Z);
                foreach (var f in entry.BootstrapPackets) HandleGameplayEvent(new NetworkEvent(true, f));
                if (audio) await audio.PreloadWorldAsync(WorldCoordinates.FromServer(entry.X, entry.Y, entry.Z), entry.MapNumber != 0, operation.Token, entry.MapNumber);
                if (!CurrentWorldOperation(operation)) return;
                SetLoadingProgress(1, 1);
                await ShowLoadingAsync(IsFuture(entry.X, entry.Y, SelectedCharacter) ? FrontendLoadingStage.FutureArea : FrontendLoadingStage.PastArea, false);
                // Let the streamer retire the preceding map before its readiness is observed.
                await Task.Yield(); float started = Time.realtimeSinceStartup;
                while (!networkWorld.IsReady)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(networkWorld.LastError)) throw new InvalidOperationException(networkWorld.LastError);
                    if (Time.realtimeSinceStartup - started > 120) throw new TimeoutException("Destination scene did not load.");
                    await Task.Yield(); if (!CurrentWorldOperation(operation)) return;
                }
                lastMovementPosition = movementPosition = networkWorld.Player.position; lastMovementAt = nextMovementAt = Time.unscaledTime;
                await WaitForWorldPresentationAsync(networkWorld.View, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                audio?.SetWorldPosition(networkWorld.Player.position, networkWorld.MapNumber != 0, relocated: true, mapNumber: networkWorld.MapNumber);
                audio?.SetMode(RetrobutionAudioController.Mode.World);
                movementPending = lastMovementWasMoving = false; networkWorld.MovementEnabled = true;
                HideLoading();
                SetStatus(""); worldTransition = false;
                SyncWorldNano(gameplayState.Snapshot);
            }
            catch (OperationCanceledException) when (!CurrentWorldOperation(operation)) { }
            catch (Exception e)
            {
                if (CurrentWorldOperation(operation))
                { DisposeWorldFlow(); active.Dispose(); Session = new FusionClient(); SetWorldPresentation(false); ShowLogin(); ShowError(e.Message); }
            }
            finally { if (this && (operation == worldLifetime || worldLifetime == null)) { HideLoading(); SetBusy(false); } }
        }
    }
}
