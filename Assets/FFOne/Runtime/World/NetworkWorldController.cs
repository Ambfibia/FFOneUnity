using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.World
{
    // Owns the loaded overworld and local avatar; interest-set replicas have a separate lifetime owner.
    public sealed partial class NetworkWorldController : MonoBehaviour
    {
        public WorldStreamer Stream { get; private set; }
        public Transform Player { get; private set; }
        public Camera View { get; private set; }
        public NetworkWorldEntities Replicas { get; private set; }
        public bool IsReady => Stream && Stream.IsReady && string.IsNullOrEmpty(LastError);
        public string LastError => error ?? (Stream ? Stream.LastError : null);
        public bool InputBlocked { get; set; }
        public bool MovementEnabled { get; set; }
        public int MapNumber { get; private set; }
        public bool Riding { get; private set; }
        Vector3 flightFrom, flightTo;
        float flightAt;
        public float MouseSensitivity { get; set; } = 1;
        public bool InvertMouseY { get; set; }
        public JObject Preferences { get; set; } = SettingsPreferences.Defaults();
        public byte MoveDirection { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Heading => yaw;
        public bool Jumping { get; private set; }
        public float JumpLaunchVelocity { get; private set; }
        public uint JumpKey { get; private set; } = 1073741823;
        public event Action<Vector3, float> Moved;
        public event Action MenuRequested;
        public event Action SettingsRequested;
        readonly List<Camera> previousCameras = new();
        readonly List<AudioListener> previousListeners = new();
        GameObject ownedRoot;
        CharacterController motor;
        NativeAvatarPreview avatar;
        long characterUid;
        string error;
        float yaw, pitch = 12, vertical, jumpCooldown;
        Color oldFogColor;
        bool oldFog, captured;
        float oldFogDensity;
        FogMode oldFogMode;

        public static bool HasCoverage(WorldCatalog catalog, int mapNumber, Vector3 position) =>
            catalog && (mapNumber == 0 || WorldTravelCatalog.Load().Maps.ContainsKey(mapNumber)) && catalog.Tiles.Any(tile =>
                (mapNumber == 0 || WorldTravelCatalog.Load().Maps.TryGetValue(mapNumber, out var cell) && tile.X == cell.X && tile.Z == cell.Z) && position.x >= tile.Minimum.x &&
                position.x < tile.Maximum.x && position.z >= tile.Minimum.z && position.z < tile.Maximum.z);

        public void Begin(CharacterInfo character, WorldEntry entry)
        {
            if (ownedRoot) throw new InvalidOperationException("World is already running.");
            Vector3 spawn = WorldCoordinates.FromServer(entry.X, entry.Y, entry.Z);
            var catalog = FFOne.Content.ContentAssets.Load<WorldCatalog>("World/Catalog");
            if (!HasCoverage(catalog, entry.MapNumber, spawn))
                throw new InvalidOperationException($"No imported world for map {entry.MapNumber} at {entry.X}, {entry.Y}, {entry.Z}.");
            try
            {
                oldFog = RenderSettings.fog; oldFogColor = RenderSettings.fogColor;
                oldFogDensity = RenderSettings.fogDensity; oldFogMode = RenderSettings.fogMode; captured = true;
                ownedRoot = new GameObject("Network overworld");
                ownedRoot.transform.SetParent(transform, false);
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (camera.enabled) { previousCameras.Add(camera); camera.enabled = false; }
                foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    if (listener.enabled) { previousListeners.Add(listener); listener.enabled = false; }
                var cameraObject = new GameObject("World Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.transform.SetParent(ownedRoot.transform, false); cameraObject.tag = "MainCamera";
                View = cameraObject.GetComponent<Camera>(); View.cullingMask = ~(1 << 31);
                View.fieldOfView = 45; View.nearClipPlane = .1f; View.farClipPlane = 340;
                var playerObject = new GameObject("Local player " + entry.PlayerId);
                playerObject.transform.SetParent(ownedRoot.transform, false); Player = playerObject.transform;
                yaw = WorldCoordinates.FromServerHeading(entry.Angle);
                Player.SetPositionAndRotation(spawn, Quaternion.Euler(0, yaw, 0));
                avatar = playerObject.AddComponent<NativeAvatarPreview>(); avatar.Apply(character);
                if (!avatar.IsReady) throw new InvalidOperationException(avatar.LastError);
                characterUid = character.Uid;
                motor = playerObject.AddComponent<CharacterController>();
                playerObject.AddComponent<SliderGroundContact>().Owner = this;
                motor.height = 1.8f; motor.radius = .45f; motor.center = Vector3.up * .9f;
                motor.stepOffset = .4f; motor.slopeLimit = 50; motor.minMoveDistance = 0;
                UpdateCamera();
                MapNumber = entry.MapNumber;
                Stream = ownedRoot.AddComponent<WorldStreamer>(); Stream.View = View; Stream.Anchor = spawn; Stream.MapNumber = MapNumber;
                MovementEnabled = false;
            }
            catch (Exception failure) { error = failure.Message; Stop(); throw; }
        }

        void Update()
        {
            if (Riding && Player && Stream)
            {
                Player.position = Vector3.Lerp(flightFrom, flightTo, Mathf.Clamp01(Time.unscaledTime - flightAt));
                Stream.Anchor = Player.position; UpdateCamera(); return;
            }
            if (!Stream || !Player || !MovementEnabled || !string.IsNullOrEmpty(LastError)) return;
            var keys = Keyboard.current;
            if (keys?.escapeKey.wasPressedThisFrame == true || SettingsInputReader.Pressed("Escape", Preferences)) MenuRequested?.Invoke();
            if (keys?.f10Key.wasPressedThisFrame == true || SettingsInputReader.Pressed("Option", Preferences)) SettingsRequested?.Invoke();
            // Never step into a tile before its authored colliders have arrived.
            CarrySlider();
            if (!IsReady) return;
            float delta = Mathf.Min(Time.deltaTime, .1f);
            bool canInput = !InputBlocked && !BuffMovementBlocked && Application.isFocused;
            float forward = canInput ? (SettingsInputReader.Held("Up", Preferences) ? 1 : 0) - (SettingsInputReader.Held("Down", Preferences) ? 1 : 0) : 0;
            float side = canInput ? (SettingsInputReader.Held("Right", Preferences) ? 1 : 0) - (SettingsInputReader.Held("Left", Preferences) ? 1 : 0) : 0;
            var mouse = Mouse.current;
            if (canInput && mouse?.rightButton.isPressed == true)
            { yaw += mouse.delta.ReadValue().x * .13f * MouseSensitivity; pitch = Mathf.Clamp(pitch + mouse.delta.ReadValue().y * .1f * MouseSensitivity * (InvertMouseY ? 1 : -1), -65, 70); }
            if (canInput && keys != null)
                yaw += ((SettingsInputReader.Held("RightTurn", Preferences) ? 1 : 0) - (SettingsInputReader.Held("LeftTurn", Preferences) ? 1 : 0)) * 100 * delta;
            jumpCooldown = Mathf.Max(0, jumpCooldown - delta);
            if (canInput && SettingsInputReader.Pressed("Jump", Preferences) && motor.isGrounded && jumpCooldown <= 0)
                LaunchJump();
            if (!motor.isGrounded) vertical = Mathf.Max(-10, vertical - 10 * delta);
            else if (vertical < 0) vertical = 0;
            Vector3 horizontal = HorizontalMovement(side, forward, delta);
            var before = Player.position;
            var flags = motor.Move((horizontal + Vector3.up * vertical) * delta - Vector3.one * .000001f);
            Velocity = delta > 0 ? (Player.position - before) / delta : Vector3.zero;
            MoveDirection = WorldCoordinates.Direction(side, forward);
            if (MoveDirection != 0) Player.rotation = Quaternion.Euler(0, yaw, 0);
            if ((flags & CollisionFlags.Below) != 0 && vertical <= 0)
            { vertical = 0; Jumping = false; avatar.PlayLocomotion(horizontal.sqrMagnitude < .01f ? "stand1" : forward < 0 ? "runback" : "run"); }
            if ((flags & CollisionFlags.Above) != 0 && vertical > 0) vertical = 0;
            if (MoveDirection != 0 || Jumping || (Player.position - before).sqrMagnitude > .000001f)
                Moved?.Invoke(Player.position, yaw);
            Stream.Anchor = Player.position;
            UpdateCamera();
        }

        void LaunchJump()
        {
            vertical = JumpLaunchVelocity = BuffJumpVelocity; Jumping = true; jumpCooldown = .5f;
            LeaveSlider(); JumpKey = JumpKey == uint.MaxValue ? 1073741823 : JumpKey + 1;
            avatar.PlayLocomotion("jumpstart", true);
        }

        void UpdateCamera()
        {
            var focus = Player.position + Vector3.up * 1.6f;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var offset = rotation * new Vector3(0, .2f, -6);
            foreach (var hit in Physics.RaycastAll(focus, offset.normalized, offset.magnitude, ~(1 << 31), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(Player)) continue;
                offset = offset.normalized * Mathf.Max(.6f, hit.distance - .15f); break;
            }
            View.transform.SetPositionAndRotation(focus + offset, rotation);
        }

        public bool RefreshAppearance(CharacterInfo character, out string failure)
        {
            failure = null;
            if (!avatar || !Player || character == null || character.Uid != characterUid)
            { failure = "Equipment appearance does not belong to the active world avatar."; return false; }
            // Same-gender Apply replaces visual parts on the existing rig, retaining its Animator,
            // playback, motor, movement state, camera, and any socket attachments.
            avatar.Apply(character);
            failure = avatar.LastError;
            return avatar.IsReady && string.IsNullOrEmpty(failure);
        }

        public void SetServerPosition(int x, int y, int z)
        {
            if (!Player || !motor) return;
            LeaveSlider();
            var destination = WorldCoordinates.FromServer(x, y, z);
            if (!HasCoverage(Stream.Catalog, MapNumber, destination))
            { error = $"Server destination has no imported world at {x}, {y}, {z}."; MovementEnabled = false; return; }
            motor.enabled = false; Player.position = destination; motor.enabled = true;
            vertical = 0; Jumping = false; Velocity = Vector3.zero; MoveDirection = 0;
            Stream.Anchor = Player.position; UpdateCamera();
        }

        public void RelocateMap(int map, int x, int y, int z)
        { LeaveSlider(); SetPersonalVehicle(0); MapNumber = map; Stream.MapNumber = map; SetServerPosition(x, y, z); }
        public void SetRiding(bool active)
        {
            if (active == Riding) return;
            if (active) { LeaveSlider(); SetPersonalVehicle(0); }
            Riding = active; motor.enabled = !active; vertical = 0; Velocity = Vector3.zero;
            if (active) { flightFrom = flightTo = Player.position; flightAt = Time.unscaledTime; }
            else { Player.position = flightTo; Stream.Anchor = Player.position; UpdateCamera(); }
        }
        public void FlyTo(int x, int y, int z)
        {
            if (!Riding) SetRiding(true);
            flightFrom = Player.position; flightTo = WorldCoordinates.FromServer(x, y, z); flightAt = Time.unscaledTime;
            // Admit upcoming scenery before the next path segment arrives.
            Stream.Anchor = flightTo;
        }

        public void StartReplication(WorldEntry entry)
        {
            if (!ownedRoot || Replicas) throw new InvalidOperationException("Replication requires one active world.");
            var owner = new GameObject("Server entities"); owner.transform.SetParent(ownedRoot.transform, false);
            Replicas = owner.AddComponent<NetworkWorldEntities>();
            Replicas.ContentError += message => Debug.LogWarning("FFOne replicated content: " + message);
            projectilePlayerId = entry.PlayerId;
            Replicas.Begin(entry.PlayerId, entry.BootstrapPackets);
        }

        public NetworkEntityView Target(float range = 50)
        {
            if (!View || !Player || !Replicas) return null;
            var ray = View.ViewportPointToRay(new Vector3(.5f, .5f));
            foreach (var hit in Physics.RaycastAll(ray, range + Vector3.Distance(View.transform.position, Player.position), ~(1 << 31), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(Player)) continue;
                var actor = hit.collider.GetComponentInParent<NetworkEntityView>();
                if (actor && actor.Entity.Kind != WorldEntityKind.Transportation && !actor.Entity.Hidden && (actor.Hostile || actor.Entity.Interactable) &&
                    (actor.Definition?.HiddenMarker != true || actor.Definition.IsBank)) return actor;
                if (!hit.collider.isTrigger) return null;
            }
            return null;
        }

        public void PlayAttack(int weaponMode = 3) => avatar?.PlayerAnimation?.Play(WorldWeaponCatalog.AttackClip(weaponMode), true);

        public void Stop()
        {
            ClearProjectiles();
            LeaveSlider();
            SetPersonalVehicle(0);
            MovementEnabled = false;
            characterUid = 0;
            if (ownedRoot) { ownedRoot.SetActive(false); Destroy(ownedRoot); ownedRoot = null; }
            foreach (var camera in previousCameras) if (camera) camera.enabled = true;
            foreach (var listener in previousListeners) if (listener) listener.enabled = true;
            previousCameras.Clear(); previousListeners.Clear(); Player = null; Stream = null; View = null; Replicas = null;
            if (captured)
            {
                RenderSettings.fog = oldFog; RenderSettings.fogMode = oldFogMode;
                RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; captured = false;
            }
        }
        void OnDestroy() => Stop();
    }
}
