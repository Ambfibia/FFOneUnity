using System;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.Integration;
using FFOne.Tutorial;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
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
        Vector3Int? rosterReturnPosition;

        async Task EnterSelectedWorldAsync()
        {
            var character = SelectedCharacter;
            DisposeWorldFlow();
            worldLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var operation = worldLifetime;
            var session = Session;
            try
            {
                await ShowLoadingAsync(character.Style.TutorialComplete ? WorldResourceStage(character) : FrontendLoadingStage.Tutorial);
                audio?.SetMode(RetrobutionAudioController.Mode.Loading);
                if (!character.Style.TutorialComplete)
                {
                    var cutscene = GetComponent<DexterShipCutscene>() ?? gameObject.AddComponent<DexterShipCutscene>();
                    cutscene.VoiceLanguage = VoiceLanguage;
                    await Task.WhenAll(cutscene.PreloadBeforeTutorialAsync(language, operation.Token),
                        PrepareWorldAssetsAsync(character, operation.Token));
                    if (!CurrentWorldOperation(operation)) return;
                    SetLoadingProgress(1, 1);
                    SetLoadingResourceProgressVisible(false);
                    if (!CurrentWorldOperation(operation)) return;
                    HideLoading();
                    SetCreationPresentationActive(true);
                    // World/HUD uploads can stall the main thread. Finish their
                    // preparation under the loading screen before starting voices.
                    await cutscene.PlayBeforeTutorialAsync(Root, language, operation.Token);
                    if (CurrentWorldOperation(operation)) await ShowLoadingAsync(FrontendLoadingStage.Tutorial, false);
                    SetLoadingProgress(1, 1); SetLoadingResourceProgressVisible(false);
                }
                else await PrepareWorldAssetsAsync(character, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
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
                await tutorial.BeginAsync(character, language == "ru", FinishTutorial, CancelTutorial,
                    (string)preferences.Committed["voice_locale"] == "ru", operation.Token);
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
                tutorial.AttachHud(Root, entry, character, rosterPreview);
                await WaitForWorldPresentationAsync(tutorial.ViewCamera, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                tutorial.enabled = true;
                audio?.SetMode(RetrobutionAudioController.Mode.Tutorial);
                HideLoading();
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
            finally { if (this && !selectionPreparing && (operation == worldLifetime || worldLifetime == null)) HideLoading(); }
        }

        bool CurrentWorldOperation(CancellationTokenSource operation) =>
            this != null && operation != null && operation == worldLifetime && !operation.IsCancellationRequested;

        async Task PrepareOverworldAsync(CharacterInfo character, WorldEntry entry, CancellationTokenSource operation, FusionClient session)
        {
            if (audio) await audio.PreloadWorldAsync(WorldCoordinates.FromServer(entry.X, entry.Y, entry.Z), entry.MapNumber != 0, operation.Token, entry.MapNumber);
            if (!CurrentWorldOperation(operation)) return;
            SetLoadingProgress(1, 1);
            await ShowLoadingAsync(IsFuture(entry.X, entry.Y, character) ? FrontendLoadingStage.FutureArea : FrontendLoadingStage.PastArea, false);
            audio?.SetMode(RetrobutionAudioController.Mode.Loading);
            SetLoadingResourceProgressVisible(false);
            if (!CurrentWorldOperation(operation)) return;
            networkWorld = gameObject.AddComponent<NetworkWorldController>();
            networkWorld.Moved += QueueTutorialMovement;
            networkWorld.ReportPoisonContact = flag => Session.SetPoisonGroundAsync(flag, lifetime.Token);
            networkWorld.ZiplineMoved += QueueZiplineMovement;
            networkWorld.LauncherMoved += QueueLauncherMovement;
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
            await WaitForWorldPresentationAsync(networkWorld.View, operation.Token);
            if (!CurrentWorldOperation(operation)) return;
            SetStatus(""); networkWorld.MovementEnabled = true;
            audio?.SetWorldPosition(networkWorld.Player.position, networkWorld.MapNumber != 0, mapNumber: networkWorld.MapNumber);
            audio?.SetMode(RetrobutionAudioController.Mode.World);
            HideLoading();
        }

        static FrontendLoadingStage WorldResourceStage(CharacterInfo character) => IsFuture(character.X, character.Y, character)
            ? FrontendLoadingStage.FutureAssets : FrontendLoadingStage.PastAssets;
        static bool IsFuture(int x, int y, CharacterInfo character)
        {
            var location = CharacterLocations.Resolve(x, y);
            // Lair coordinates can be outside the named overworld rectangles.
            return location != null ? location.Zone == "The Future" : character?.Style.Payzone != true;
        }

        async Task WaitForWorldPresentationAsync(Camera camera, CancellationToken cancellation)
        {
            bool rendered = false;
            void Presented(ScriptableRenderContext context, Camera view) { if (view == camera) rendered = true; }
            RenderPipelineManager.endCameraRendering += Presented;
            try
            {
                var previews = rosterPreview;
                float deadline = Time.realtimeSinceStartup + 120;
                while (!rendered || previews && !previews.GameplayPortraitReady)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!camera) throw new InvalidOperationException("World camera was removed before its first frame.");
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("World presentation did not render its first frame.");
                    await Task.Yield();
                }
            }
            finally { RenderPipelineManager.endCameraRendering -= Presented; }
        }

        void SetWorldPresentation(bool active)
        {
            if (active) showingSelection = false;
            loginRoot.style.display = DisplayStyle.None;
            selectionRoot.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            Root.style.backgroundColor = active ? Color.clear : Color.black;
            if (!active) audio?.SetMode(RetrobutionAudioController.Mode.Lobby);
        }

        void QueueTutorialMovement(Vector3 position, float heading)
        {
            if (worldTransition || worldLifetime == null) return;
            if (gameplayState?.Snapshot.IsActive == true) gameplayState.SetLocalPosition(position);
            gameplayUi?.SetMapHeading(heading);
            movementPosition = position; movementHeading = heading; movementPending = true;
            lastMovementEventAt = Time.unscaledTime;
        }

        void QueueLauncherMovement(Vector3 position, Vector3 velocity, float heading)
        {
            if (worldTransition || worldLifetime == null) return;
            var point = WorldCoordinates.ToServer(position);
            var payload = ProtocolLauncher.Movement((point.x, point.y, point.z), velocity.x, velocity.z, velocity.y, WorldCoordinates.ToServerHeading(heading));
            gameplayState.SetLocalPosition(position);
            movementPending = lastMovementWasMoving = false;
            lastMovementPosition = movementPosition = position;
            lastMovementAt = Time.unscaledTime;
            movementTask = SendLauncherMovementAsync(movementTask, Session, worldLifetime, payload);
        }

        async Task SendLauncherMovementAsync(Task previous, FusionClient client, CancellationTokenSource operation, byte[] payload)
        {
            try
            {
                await previous;
                if (CurrentWorldOperation(operation)) await client.SendLauncherAsync(payload, operation.Token);
            }
            catch (OperationCanceledException) when (!CurrentWorldOperation(operation)) { }
            catch (Exception error) { if (CurrentWorldOperation(operation)) ShowError(error.Message); }
        }

        void QueueZiplineMovement(ZiplineMovement step)
        {
            if (worldTransition || worldLifetime == null) return;
            var start = WorldCoordinates.ToServer(step.Start);
            var position = WorldCoordinates.ToServer(step.Position);
            byte[] payload = ProtocolZipline.Movement((start.x, start.y, start.z), (position.x, position.y, position.z),
                (step.Direction.x, step.Direction.z, step.Direction.y), step.Moved, step.Length,
                WorldCoordinates.ToServerHeading(step.Heading), (int)(step.Speed * 100), step.Detached);
            bool recall = step.Started && gameplayState.Snapshot.ActiveNanoSlot >= 0;
            gameplayState.SetLocalPosition(step.Position);
            gameplayUi?.SetMapHeading(step.Heading);
            movementPending = lastMovementWasMoving = false;
            lastMovementPosition = movementPosition = step.Position;
            lastMovementAt = Time.unscaledTime;
            movementTask = SendZiplineMovementAsync(movementTask, Session, worldLifetime, payload, recall);
        }

        async Task SendZiplineMovementAsync(Task previous, FusionClient client, CancellationTokenSource operation, byte[] payload, bool recall)
        {
            try
            {
                await previous;
                if (!CurrentWorldOperation(operation)) return;
                await client.SendZiplineAsync(payload, operation.Token);
                if (recall && CurrentWorldOperation(operation)) await client.ActivateNanoAsync(-1, operation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (CurrentWorldOperation(operation)) { ShowError(error.Message); CancelTutorial(); }
            }
        }

        void TickWorldFlow()
        {
            if (networkWorld && networkWorld.Player && !worldTransition)
                audio?.SetWorldPosition(networkWorld.Player.position, networkWorld.MapNumber != 0, mapNumber: networkWorld.MapNumber);
            if (tutorial && !string.IsNullOrEmpty(tutorial.LastError) && !worldTransition)
            { string error = tutorial.LastError; CancelTutorial(); ShowError(error); return; }
            if (networkWorld && !string.IsNullOrEmpty(networkWorld.LastError) && !worldTransition)
            { ShowError(networkWorld.LastError); CancelTutorial(); return; }
            // The controller emits movement while keys/gravity move the player; send one STOP
            // after its final event so the server does not retain the last running state.
            if (!movementPending && lastMovementWasMoving && Time.unscaledTime - lastMovementEventAt >= .15f)
            { movementPosition = lastMovementPosition; movementPending = true; }
            if (worldTransition || networkWorld && (networkWorld.Riding || networkWorld.Ziplining) ||
                movementSending || !movementTask.IsCompleted || !movementPending || worldLifetime == null ||
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
                await ShowLoadingAsync(IsFuture(entry.X, entry.Y, SelectedCharacter) ? FrontendLoadingStage.FutureAssets : FrontendLoadingStage.PastAssets);
                audio?.SetMode(RetrobutionAudioController.Mode.Loading);
                tutorial.Moved -= QueueTutorialMovement; tutorial.Stop(); Destroy(tutorial); tutorial = null;
                // Tutorial teardown uses deferred Destroy. Let its cameras retire before capturing world presentation.
                await Task.Yield();
                if (!CurrentWorldOperation(operation)) return;
                WorldEntered?.Invoke(entry);
                await PrepareWorldAssetsAsync(SelectedCharacter, operation.Token);
                if (!CurrentWorldOperation(operation)) return;
                await PrepareOverworldAsync(SelectedCharacter, entry, operation, session);
                if (CurrentWorldOperation(operation))
                {
                    worldTransition = false;
                    SyncWorldNano(gameplayState.Snapshot);
                }
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
                if (session.IsInWorld)
                {
                    var character = SelectedCharacter;
                    Vector3Int? position = networkWorld && gameplayState != null && networkWorld.MapNumber == 0
                        ? WorldCoordinates.ToServer(gameplayState.Snapshot.WorldPosition)
                        : rosterReturnPosition;
                    await session.LeaveWorldAsync(operation.Token);
                    if (CurrentWorldOperation(operation) && character != null && position is Vector3Int saved)
                    {
                        character.X = saved.x;
                        character.Y = saved.y;
                        character.Z = saved.z;
                    }
                }
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
            HideLoading();
            StopGameplayUi();
            deferredGameplayEvents.Clear();
            CloseWorldMenu();
            settings?.Close();
            worldLifetime?.Cancel(); worldLifetime?.Dispose(); worldLifetime = null;
            if (tutorial != null) { tutorial.Moved -= QueueTutorialMovement; tutorial.Stop(); Destroy(tutorial); tutorial = null; }
            if (networkWorld != null) { networkWorld.Moved -= QueueTutorialMovement; networkWorld.Stop(); Destroy(networkWorld); networkWorld = null; }
            movementPending = movementSending = worldTransition = lastMovementWasMoving = false;
            movementTask = Task.CompletedTask;
            rosterReturnPosition = null;
        }
    }
}
