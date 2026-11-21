using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using FFOne.Runtime.World;
using FFOne.World;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Tutorial
{
    public sealed partial class TutorialController
    {
        public const string WorldTileId = "map_01_01";
        public WorldStreamer WorldStream => worldStream;
        WorldStreamer worldStream;
        WorldCatalog tutorialCatalog;
        CancellationTokenSource loadingCancellation;
        Material tutorialSkybox;
        bool starting, environmentCaptured;
        public Vector3 ConstrainCinematicCamera(Vector3 eye, Vector3 target, float radius)
        {
            if (radius <= 0 || !worldStream || !worldStream.IsReady) return eye;
            var offset = eye - target;
            float distance = offset.magnitude;
            if (distance <= radius) return eye;
            var direction = offset / distance;
            float clear = distance;
            foreach (var hit in Physics.SphereCastAll(target, radius, direction, distance, ~0, QueryTriggerInteraction.Ignore))
                if (worldStream.Owns(hit.collider)) clear = Mathf.Min(clear, Mathf.Max(0, hit.distance - .02f));
            return target + direction * clear;
        }
        bool previousFog, previousTutorialScope;
        float previousFogDensity;
        FogMode previousFogMode;
        Color previousFogColor;
        Vector4 previousWorldFog, previousWorldLight;
        Camera previousEnvironmentCamera;
        Transform previousEnvironmentTarget;

        public static async Task PreloadResourcesAsync(CharacterInfo character, CancellationToken cancellation = default)
        {
            var contentRequest = ContentAssets.GetAsync<TutorialContent>("Tutorial/Content", cancellation);
            var minimap = new[] { "fusionmeter/regular", "fusionmeter/regular_right", "fusionmatter_us",
                "map_icon_02", "camera", "map_icon_00", "line_minimap" }
                .Select(image => ContentAssets.GetAsync<Texture2D>(TutorialMinimapArt + image, cancellation));
            await Task.WhenAll(contentRequest,
                Task.WhenAll(minimap),
                UI.GameplayMinimap.PreloadTilesAsync(StartPosition, cancellation),
                ContentAssets.GetAsync<Shader>("Tutorial/Particle", cancellation),
                TutorialEffectAssets.PreloadAsync(767, cancellation),
                TutorialEffectAssets.PreloadAsync(750, cancellation),
                TutorialCinematicEffects.PreloadAllAsync(cancellation),
                ContentAssets.GetAsync<WorldCatalog>("World/Catalog", cancellation),
                ContentAssets.GetAsync<AudioClip>("audio/ambient/tutorialmain_wambient_loop", cancellation),
                NativeAvatarPreview.PreloadAsync(character, cancellation),
                ContentAssets.GetAsync<NativeModelCatalog>(NativeModelAssets.EditableRoot + "/Index", cancellation),
                NativeModelAssets.PreloadAsync("characters/nanos/nano_buttercup/nano_buttercup", cancellation),
                NativeModelAssets.PreloadAsync("characters/player/items/weapon/rifle_electricgun/models/rifle_electricgun/model", cancellation),
                ContentAssets.GetAsync<Texture2D>("characters/player/items/weapon/rifle_electricgun/textures/rifle_electricgun", cancellation),
                ContentAssets.GetAsync<NativeDataDocument>(NativeData.Resource("data/tutorial/cinematics"), cancellation));
            if (!contentRequest.Result) throw new InvalidOperationException("Import tutorial content with FFOne → Import Tutorial Content.");
            await Task.WhenAll(PreloadTutorialHudAsync(contentRequest.Result, cancellation),
                Task.WhenAll(Enumerable.Range(1, TutorialPanPlayer.FrameCount).Select(i =>
                    ContentAssets.GetAsync<Texture2D>("ui/en/gameplay/intro/tutorialpan_0" + i, cancellation))),
                ContentAssets.GetAsync<UnityEngine.TextCore.Text.FontAsset>("fonts/jeffe-sdf", cancellation),
                ContentAssets.GetAsync<UnityEngine.TextCore.Text.FontAsset>("fonts/chaletbook-regular-sdf", cancellation),
                LocalizationCatalog.PreloadAsync("en", cancellation), LocalizationCatalog.PreloadAsync("ru", cancellation));
            // Asset handles may complete before background audio decoding.
            foreach (var clip in contentRequest.Result.Audio.SelectMany(a => new[] { a.English, a.Russian }).Where(c => c).Distinct())
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                float deadline = Time.realtimeSinceStartup + 30;
                while (clip.loadState == AudioDataLoadState.Loading)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Tutorial audio is not ready: " + clip.name);
                    await Task.Yield();
                }
                if (clip.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("Tutorial audio failed: " + clip.name);
            }
#if UNITY_EDITOR
            // The editor may have authored model overrides in addition to the shipped assets.
            foreach (var actor in contentRequest.Result.Actors)
            {
                string path = ContentPaths.Legacy(UnityEditor.AssetDatabase.GetAssetPath(actor.Model));
                const string prefix = "Assets/Resources/";
                if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    await NativeModelAssets.PreloadAsync(path.Substring(prefix.Length), cancellation);
            }
#endif
        }

        public async Task BeginAsync(CharacterInfo character, bool russian, Action onCompleted, Action onCancelled,
            bool? voiceRussian = null, CancellationToken cancellation = default)
        {
            if (IsReady || worldRoot || starting) throw new InvalidOperationException("Tutorial is already running.");
            starting = true; ending = false; LastError = null;
            loadingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var token = loadingCancellation.Token;
            Russian = russian; VoiceRussian = voiceRussian ?? russian;
            completed = onCompleted; cancelled = onCancelled;
            try
            {
                await PreloadResourcesAsync(character, token);
                content = await ContentAssets.GetAsync<TutorialContent>("Tutorial/Content", token);
                if (!content) throw new InvalidOperationException("Import tutorial content with FFOne → Import Tutorial Content.");
                var catalog = await ContentAssets.GetAsync<WorldCatalog>("World/Catalog", token);
                tutorialCatalog = CreateWorldCatalog(catalog);
                var ambientClip = await ContentAssets.GetAsync<AudioClip>("audio/ambient/tutorialmain_wambient_loop", token);
                token.ThrowIfCancellationRequested();

                oldAmbient = RenderSettings.ambientLight; ambientCaptured = true;
                CaptureEnvironment();
                worldRoot = new GameObject("Tutorial world");
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (camera.enabled) { previousCameras.Add(camera); camera.enabled = false; }
                foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (listener.enabled) { previousListeners.Add(listener); listener.enabled = false; }
                cameraRoot = new GameObject("Tutorial camera"); cameraRoot.tag = "MainCamera";
                ViewCamera = cameraRoot.AddComponent<Camera>(); cameraRoot.AddComponent<AudioListener>();
                // Keep the accepted native horizon; the fog density floor also
                // depends on this distance. An 1800 m clip exposes the tile edge.
                ViewCamera.nearClipPlane = .15f; ViewCamera.farClipPlane = 340; ViewCamera.fieldOfView = 45;
                ViewCamera.backgroundColor = new Color(.34f, .42f, .42f);
                if (content.Skybox)
                {
                    tutorialSkybox = new Material(content.Skybox);
                    cameraRoot.AddComponent<Skybox>().material = tutorialSkybox;
                    ViewCamera.clearFlags = CameraClearFlags.Skybox;
                }
                else ViewCamera.clearFlags = CameraClearFlags.SolidColor;

                playerRoot = new GameObject("Tutorial player"); Player = playerRoot.transform;
                yaw = WorldCoordinates.FromServerHeading(270); pitch = 12;
                Player.SetPositionAndRotation(StartPosition, Quaternion.Euler(0, yaw, 0));
                WorldTerrainEnvironment.SamplingTarget = Player;
                WorldTerrainEnvironment.ViewOverride = ViewCamera;
                WorldTerrainEnvironment.TutorialScope = true;
                worldStream = worldRoot.AddComponent<WorldStreamer>();
                worldStream.View = ViewCamera; worldStream.Anchor = StartPosition; worldStream.CatalogOverride = tutorialCatalog;
                // Keep all tutorial collision geometry resident through camera cuts and warps.
                float worldDeadline = Time.realtimeSinceStartup + 120;
                while (!worldStream.IsReady)
                {
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(worldStream.LastError)) throw new InvalidOperationException(worldStream.LastError);
                    if (Time.realtimeSinceStartup > worldDeadline) throw new TimeoutException("Timed out loading the authored tutorial world.");
                    await Task.Yield();
                    if (!worldStream) throw new OperationCanceledException(token);
                }
                if (worldStream.ColliderCount == 0) throw new InvalidOperationException("Tutorial scenery has no collision geometry.");
                // The source tutorial camera clips rasterized geometry at its
                // horizon. The overworld's infinite projection exposes distant
                // poison-water triangles as a noisy strip beneath these shots.
                var projection = ViewCamera.GetComponent<WorldCameraProjection>();
                if (projection) { projection.enabled = false; Destroy(projection); }
                token.ThrowIfCancellationRequested(); Physics.SyncTransforms();
                motor = playerRoot.AddComponent<CharacterController>();
                playerRoot.AddComponent<FFOne.Characters.PlayerContactShadow>();
                NativePlayerMotor.Configure(motor);
                avatar = playerRoot.AddComponent<NativeAvatarPreview>(); avatar.Apply(character);
                if (!avatar.IsReady) throw new InvalidOperationException(avatar.LastError);
                if (content.ButtercupNano)
                {
                    nano = Instantiate(NativeModelAssets.Resolve(content.ButtercupNano), worldRoot.transform);
                    FFOne.World.NativeNanoFaceTrack.Attach(nano, FFOne.World.NanoPresentationCatalog.Find("nano_buttercup"));
                    nano.name = "Buttercup Nano"; nano.SetActive(false);
                }
                voice = cameraRoot.AddComponent<AudioSource>(); sound = cameraRoot.AddComponent<AudioSource>();
                ambient = cameraRoot.AddComponent<AudioSource>(); music = cameraRoot.AddComponent<AudioSource>();
                ambient.clip = ambientClip; ambient.loop = true; ambient.volume = .35f; ambient.Play();
                RenderSettings.ambientLight = new Color(.65f, .65f, .7f);
                PanPlayer = new TutorialPanPlayer(); cinematics = new TutorialCinematics(this);
                effects = worldRoot.AddComponent<TutorialCinematicEffects>();
                effects.Initialize(id => Actor(id)?.transform, expression => cinematics.ResolvePosition(expression), ViewCamera, VoiceRussian);
                await effects.PrepareAllAsync(token);
                // InitStartChapter's persistent set piece precedes every
                // finite scene. Its bounds and height must survive grounding.
                SpawnActor(2, 2968, new Vector3(973, -76, 702), 268);
                UpdateCamera(); IsReady = true; SetStage(0, 0);
            }
            catch (OperationCanceledException) { Cleanup(); throw; }
            catch (Exception error) { LastError = error.Message; Debug.LogException(error, this); Cleanup(); throw; }
            finally { starting = false; }
        }

        public static WorldCatalog CreateWorldCatalog(WorldCatalog source)
        {
            if (!source) throw new InvalidOperationException("World catalog is missing.");
            var tile = source.Tiles.SingleOrDefault(t => t.Id == WorldTileId);
            if (tile == null || string.IsNullOrEmpty(tile.SceneAddress))
                throw new InvalidOperationException("The authored tutorial cell is missing from the world catalog: " + WorldTileId);
            var result = ScriptableObject.CreateInstance<WorldCatalog>();
            result.name = "Tutorial authored world"; result.Tiles = new[] { tile };
            result.Atmospheres = source.Atmospheres;
            // The authored cell owns its own surface. Admit the surrounding
            // tutorial poison surfaces through the ordinary asynchronous loader.
            result.NeighborWater = source.NeighborWater.Where(t => t.Id != tile.Id).ToArray();
            return result;
        }

        void CaptureEnvironment()
        {
            previousFog = RenderSettings.fog; previousFogMode = RenderSettings.fogMode;
            previousFogDensity = RenderSettings.fogDensity; previousFogColor = RenderSettings.fogColor;
            previousWorldFog = Shader.GetGlobalVector("_FFWorldFog");
            previousWorldLight = Shader.GetGlobalVector("_FFWorldAmbienceLight");
            previousEnvironmentCamera = WorldTerrainEnvironment.ViewOverride;
            previousEnvironmentTarget = WorldTerrainEnvironment.SamplingTarget;
            previousTutorialScope = WorldTerrainEnvironment.TutorialScope;
            environmentCaptured = true;
        }

        void StopWorld()
        {
            loadingCancellation?.Cancel(); loadingCancellation?.Dispose(); loadingCancellation = null;
            if (worldRoot) worldRoot.SetActive(false);
            if (worldStream) Destroy(worldStream); worldStream = null;
            if (tutorialCatalog) Destroy(tutorialCatalog); tutorialCatalog = null;
            if (tutorialSkybox) Destroy(tutorialSkybox); tutorialSkybox = null;
            if (!environmentCaptured) return;
            RenderSettings.fog = previousFog; RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogDensity = previousFogDensity; RenderSettings.fogColor = previousFogColor;
            Shader.SetGlobalVector("_FFWorldFog", previousWorldFog);
            Shader.SetGlobalVector("_FFWorldAmbienceLight", previousWorldLight);
            WorldTerrainEnvironment.ViewOverride = previousEnvironmentCamera;
            WorldTerrainEnvironment.SamplingTarget = previousEnvironmentTarget;
            WorldTerrainEnvironment.TutorialScope = previousTutorialScope;
            environmentCaptured = false;
        }
    }
}
