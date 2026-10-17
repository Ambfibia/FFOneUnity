using System;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.Tutorial;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    public sealed partial class FrontendController
    {
        TutorialController tutorial;
        NetworkWorldController networkWorld;
        CancellationTokenSource worldLifetime;
        bool movementPending, movementSending, worldTransition, lastMovementWasMoving;
        Vector3 movementPosition, lastMovementPosition;
        float movementHeading, lastMovementAt, nextMovementAt, lastMovementEventAt;
        Task movementTask = Task.CompletedTask;

        async Task EnterSelectedWorldAsync()
        {
            var character = SelectedCharacter;
            DisposeWorldFlow();
            worldLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var operation = worldLifetime;
            var session = Session;
            try
            {
                var entry = await session.EnterWorldAsync(character.Uid, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                WorldEntered?.Invoke(entry);
                if (character.Style.TutorialComplete)
                {
                    await PrepareOverworldAsync(character, entry, operation, session);
                    return;
                }
                tutorial = gameObject.AddComponent<TutorialController>();
                tutorial.enabled = false;
                tutorial.Moved += QueueTutorialMovement;
                tutorial.Begin(character, language == "ru", FinishTutorial, CancelTutorial, (string)preferences.Committed["voice_locale"] == "ru");
                ApplyOptions();
                float started = Time.realtimeSinceStartup;
                while (!tutorial.IsReady)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(tutorial.LastError)) throw new InvalidOperationException(tutorial.LastError);
                    if (Time.realtimeSinceStartup - started > 120)
                        throw new TimeoutException(language == "ru" ? "Не удалось подготовить ресурсы обучения." : "Tutorial resources did not become ready.");
                    await Task.Yield();
                    if (!CurrentWorldOperation(operation)) return;
                }
                await session.CompleteLoadingAsync(operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                lastMovementPosition = tutorial.PlayerUnityPosition;
                lastMovementAt = nextMovementAt = Time.unscaledTime;
                SetWorldPresentation(true);
                SetStatus("");
                tutorial.enabled = true;
            }
            catch
            {
                if (CurrentWorldOperation(operation))
                {
                    try { if (session.IsInWorld) await session.LeaveWorldAsync(operation.Token); }
                    catch (Exception) { }
                    if (CurrentWorldOperation(operation)) { DisposeWorldFlow(); SetWorldPresentation(false); ShowSelection(); }
                }
                throw;
            }
        }

        bool CurrentWorldOperation(CancellationTokenSource operation) =>
            this != null && operation != null && operation == worldLifetime && !operation.IsCancellationRequested;

        async Task PrepareOverworldAsync(CharacterInfo character, WorldEntry entry, CancellationTokenSource operation, FusionClient session)
        {
            networkWorld = gameObject.AddComponent<NetworkWorldController>();
            networkWorld.Moved += QueueTutorialMovement;
            networkWorld.MenuRequested += ToggleWorldMenu;
            networkWorld.SettingsRequested += OpenSettings;
            networkWorld.Begin(character, entry);
            ApplyOptions();
            SetWorldPresentation(true);
            float started = Time.realtimeSinceStartup;
            while (!networkWorld.IsReady)
            {
                operation.Token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(networkWorld.LastError)) throw new InvalidOperationException(networkWorld.LastError);
                if (Time.realtimeSinceStartup - started > 120) throw new TimeoutException("World resources did not become ready.");
                await Task.Yield();
                if (!CurrentWorldOperation(operation)) return;
            }
            await session.CompleteLoadingAsync(operation.Token);
            if (!CurrentWorldOperation(operation)) return;
            networkWorld.SetServerPosition(entry.X, entry.Y, entry.Z);
            started = Time.realtimeSinceStartup;
            while (!networkWorld.IsReady)
            {
                operation.Token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(networkWorld.LastError)) throw new InvalidOperationException(networkWorld.LastError);
                if (Time.realtimeSinceStartup - started > 120) throw new TimeoutException("Server destination resources did not become ready.");
                await Task.Yield();
                if (!CurrentWorldOperation(operation)) return;
            }
            lastMovementPosition = networkWorld.Player.position;
            movementPosition = lastMovementPosition;
            movementPending = movementSending = lastMovementWasMoving = false;
            movementHeading = networkWorld.Heading;
            lastMovementAt = nextMovementAt = Time.unscaledTime;
            networkWorld.StartReplication(entry);
            StartGameplayUi(entry, character);
            SetStatus(""); networkWorld.MovementEnabled = true;
        }

        void SetWorldPresentation(bool active)
        {
            loginRoot.style.display = DisplayStyle.None;
            selectionRoot.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            Root.style.backgroundColor = active ? Color.clear : Color.black;
            if (active) music?.Pause();
            else if (playMusic) music?.UnPause();
        }

        void QueueTutorialMovement(Vector3 position, float heading)
        {
            if (worldTransition || worldLifetime == null) return;
            if (gameplayState?.Snapshot.IsActive == true) gameplayState.SetLocalPosition(position);
            gameplayUi?.SetMapHeading(heading);
            movementPosition = position; movementHeading = heading; movementPending = true;
            lastMovementEventAt = Time.unscaledTime;
        }

        void TickWorldFlow()
        {
            if (networkWorld && !string.IsNullOrEmpty(networkWorld.LastError) && !worldTransition)
            { ShowError(networkWorld.LastError); CancelTutorial(); return; }
            // The controller emits movement while keys/gravity move the player; send one STOP
            // after its final event so the server does not retain the last running state.
            if (!movementPending && lastMovementWasMoving && Time.unscaledTime - lastMovementEventAt >= .15f)
            { movementPosition = lastMovementPosition; movementPending = true; }
            if (worldTransition || networkWorld && networkWorld.Riding || movementSending || !movementPending || worldLifetime == null ||
                !Session.IsWorldLoaded || Time.unscaledTime < nextMovementAt) return;
            movementTask = SendTutorialMovementAsync(worldLifetime, Session);
        }

        async Task SendTutorialMovementAsync(CancellationTokenSource operation, FusionClient session)
        {
            movementSending = true; movementPending = false;
            Vector3 position = movementPosition;
            float now = Time.unscaledTime;
            Vector3 velocity = (position - lastMovementPosition) / Mathf.Max(.001f, now - lastMovementAt);
            bool stopped = velocity.sqrMagnitude < .0001f;
            lastMovementWasMoving = !stopped;
            nextMovementAt = now + .1f;
            lastMovementPosition = position; lastMovementAt = now;
            try
            {
                var point = WorldCoordinates.ToServer(position);
                var localVelocity = Quaternion.Euler(0, -movementHeading, 0) * velocity;
                byte direction = stopped ? (byte)0 : WorldCoordinates.Direction(localVelocity.x, localVelocity.z);
                if (networkWorld && networkWorld.SliderId != 0)
                {
                    var local = WorldCoordinates.ToServer(networkWorld.SliderLocalPosition);
                    await session.SendSliderMovementAsync(networkWorld.SliderId, local.x, local.y, local.z, point.x, point.y, point.z,
                        WorldCoordinates.ToServerHeading(movementHeading), velocity.x, velocity.z, velocity.y,
                        (int)(new Vector2(velocity.x, velocity.z).magnitude * 100), direction, operation.Token);
                }
                else if (networkWorld && networkWorld.Jumping)
                {
                    var jumpVelocity = WorldCoordinates.ToServer(networkWorld.Velocity);
                    await session.SendJumpAsync(point.x, point.y, point.z, WorldCoordinates.ToServerHeading(movementHeading),
                        jumpVelocity.x, jumpVelocity.y, jumpVelocity.z, Mathf.RoundToInt(networkWorld.JumpLaunchVelocity * 100), networkWorld.MoveDirection,
                        networkWorld.JumpLaunchVelocity, networkWorld.JumpKey, operation.Token);
                }
                else await session.SendMovementAsync(point.x, point.y, point.z, WorldCoordinates.ToServerHeading(movementHeading),
                    velocity.x, velocity.z, velocity.y, (int)(new Vector2(velocity.x, velocity.z).magnitude * 100),
                    direction, stopped, operation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (CurrentWorldOperation(operation)) { ShowError(error.Message); CancelTutorial(); }
            }
            finally { if (operation == worldLifetime) movementSending = false; }
        }

        async void FinishTutorial()
        {
            if (worldTransition || worldLifetime == null || tutorial == null) return;
            worldTransition = true;
            var operation = worldLifetime;
            var session = Session;
            long uid = SelectedCharacter.Uid;
            SetBusy(true);
            SetStatus(language == "ru" ? "СОХРАНЕНИЕ ОБУЧЕНИЯ..." : "SAVING TUTORIAL...");
            try
            {
                await movementTask;
                var entry = await session.CompleteTutorialAsync(uid, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                tutorial.Moved -= QueueTutorialMovement; tutorial.Stop(); Destroy(tutorial); tutorial = null;
                // Tutorial teardown uses deferred Destroy. Let its cameras retire before capturing world presentation.
                await Task.Yield();
                if (!CurrentWorldOperation(operation)) return;
                WorldEntered?.Invoke(entry);
                await PrepareOverworldAsync(SelectedCharacter, entry, operation, session);
                if (CurrentWorldOperation(operation)) worldTransition = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (CurrentWorldOperation(operation))
                {
                    try { if (session.IsInWorld) await session.LeaveWorldAsync(operation.Token); }
                    catch (Exception) { }
                    if (CurrentWorldOperation(operation))
                    {
                        DisposeWorldFlow(); SetWorldPresentation(false); ShowSelection(); ShowError(error.Message);
                    }
                }
            }
            finally { if (this != null && (operation == worldLifetime || worldLifetime == null)) SetBusy(false); }
        }

        async void CancelTutorial()
        {
            if (worldTransition || worldLifetime == null) return;
            worldTransition = true;
            var operation = worldLifetime;
            var session = Session;
            SetBusy(true);
            string failure = null;
            try
            {
                await movementTask;
                if (session.IsInWorld) await session.LeaveWorldAsync(operation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { failure = error.Message; }
            finally
            {
                if (CurrentWorldOperation(operation))
                {
                    DisposeWorldFlow(); SetWorldPresentation(false); ShowSelection(); SetBusy(false);
                    if (failure != null) ShowError(failure);
                }
            }
        }

        void HandleWorldDisconnect(NetworkEvent value)
        {
            if (value.Kind != NetworkEventKind.Disconnected || worldLifetime == null) return;
            DisposeWorldFlow(); SetWorldPresentation(false); SetBusy(false);
        }

        void DisposeWorldFlow()
        {
            StopGameplayUi();
            deferredGameplayEvents.Clear();
            CloseWorldMenu();
            settings?.Close();
            worldLifetime?.Cancel(); worldLifetime?.Dispose(); worldLifetime = null;
            if (tutorial != null) { tutorial.Moved -= QueueTutorialMovement; tutorial.Stop(); Destroy(tutorial); tutorial = null; }
            if (networkWorld != null) { networkWorld.Moved -= QueueTutorialMovement; networkWorld.Stop(); Destroy(networkWorld); networkWorld = null; }
            movementPending = movementSending = worldTransition = lastMovementWasMoving = false;
            movementTask = Task.CompletedTask;
        }
    }
}
