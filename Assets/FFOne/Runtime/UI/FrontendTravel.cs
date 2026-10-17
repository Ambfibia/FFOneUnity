using System;
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
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
            networkWorld.MovementEnabled = false; movementPending = lastMovementWasMoving = false;
            gameplayUi.Windows.Close(); networkWorld.Replicas?.Session.Clear();
            SetBusy(true); SetStatus(language == "ru" ? "ПЕРЕХОД…" : "TRAVELLING…");
            try
            {
                await movementTask;
                bool instance = WorldTravelCatalog.Load().Warps.Any(w => w.Map != 0 && w.X == destination.X && w.Y == destination.Y && w.Z == destination.Z);
                var entry = await active.CompleteWarpAsync(destination, instance, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                networkWorld.RelocateMap(entry.MapNumber, entry.X, entry.Y, entry.Z);
                gameplayState.Relocate(entry.MapNumber, entry.X, entry.Y, entry.Z);
                foreach (var f in entry.BootstrapPackets) HandleGameplayEvent(new NetworkEvent(true, f));
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
                movementPending = lastMovementWasMoving = false; networkWorld.MovementEnabled = true;
                SetStatus(""); worldTransition = false;
            }
            catch (OperationCanceledException) when (!CurrentWorldOperation(operation)) { }
            catch (Exception e)
            {
                if (CurrentWorldOperation(operation))
                { DisposeWorldFlow(); active.Dispose(); Session = new FusionClient(); SetWorldPresentation(false); ShowLogin(); ShowError(e.Message); }
            }
            finally { if (this && (operation == worldLifetime || worldLifetime == null)) SetBusy(false); }
        }
    }
}
