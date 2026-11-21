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
        internal WorldNanoCompanion LocalNano { get; private set; }
        long characterUid;
        int weaponEquipType, equippedWeaponId;
        string weaponChangeClip;
        float lastPlayerCombatResult = float.NegativeInfinity;
        float playerAttackUntil;
        string error;
        float yaw, pitch = 12, vertical, jumpCooldown, jumpAge;
        readonly LegacyGameplayCameraInput cameraInput = new();
        bool freeCamera;
        float cameraDistance = 6;
        bool autoRun, playerDead;
        internal bool InventoryOpen { get; set; }
        internal bool MenuAutoRun { get; set; }
        float MovementYaw => freeCamera && Player ? Player.eulerAngles.y : yaw;
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
                weaponEquipType = WorldWeaponCatalog.AnimationEquipType(character);
                equippedWeaponId = character.Equipment[0].ItemId;
                weaponChangeClip = null;
                if (weaponEquipType != 0)
                    avatar.PlayerAnimation?.Play(WorldWeaponCatalog.AnimationPrefix(weaponEquipType) + "stand1", true);
                lastPlayerCombatResult = float.NegativeInfinity;
                playerAttackUntil = 0;
                motor = playerObject.AddComponent<CharacterController>();
                playerObject.AddComponent<SliderGroundContact>().Owner = this;
                playerObject.AddComponent<FFOne.Characters.PlayerContactShadow>();
                NativePlayerMotor.Configure(motor);
                UpdateCamera();
                MapNumber = entry.MapNumber;
                Stream = ownedRoot.AddComponent<WorldStreamer>(); Stream.View = View; Stream.Anchor = spawn; Stream.MapNumber = MapNumber;
                MovementEnabled = false;
            }
            catch (Exception failure) { error = failure.Message; Stop(); throw; }
        }

        void Update()
        {
            UpdateComputer();
            UpdatePoisonContact();
            bool readMouse = cameraInput.Capture(Stream && Player && MovementEnabled && IsReady && !InputBlocked && string.IsNullOrEmpty(LastError));
            if (Riding && Player && Stream)
            {
                Player.position = Vector3.Lerp(flightFrom, flightTo, Mathf.Clamp01(Time.unscaledTime - flightAt));
                Stream.Anchor = Player.position; UpdateCamera(); return;
            }
            if (!Stream || !Player || !MovementEnabled || !string.IsNullOrEmpty(LastError)) return;
            var keys = Keyboard.current;
            if (TickLauncher()) return;
            if (keys?.escapeKey.wasPressedThisFrame == true || GameplayInput.Pressed(GameplayAction.Escape)) MenuRequested?.Invoke();
            if (keys?.f10Key.wasPressedThisFrame == true || GameplayInput.Pressed(GameplayAction.Option)) SettingsRequested?.Invoke();
            if (TickZipline()) return;
            // Never step into a tile before its authored colliders have arrived.
            CarrySlider();
            if (!IsReady) return;
            float delta = Mathf.Min(Time.deltaTime, .1f);
            bool canMove = !playerDead && !BuffMovementBlocked && Application.isFocused;
            bool canInput = canMove && !InputBlocked;
            if (canInput)
            {
                int zoom = (GameplayInput.Held(GameplayAction.ZoomOut) ? 1 : 0) -
                    (GameplayInput.Held(GameplayAction.ZoomIn) ? 1 : 0);
                cameraDistance = Mathf.Clamp(cameraDistance + zoom * 3 * delta, 4, 12);
            }
            freeCamera = canInput && GameplayInput.Held(GameplayAction.FreeCamera) &&
                !GameplayInput.Held(GameplayAction.RightTurn) && !GameplayInput.Held(GameplayAction.LeftTurn);
            float forward = canInput ? (GameplayInput.Held(GameplayAction.Up) ? 1 : 0) - (GameplayInput.Held(GameplayAction.Down) ? 1 : 0) : 0;
            float side = canInput ? (GameplayInput.Held(GameplayAction.Right) ? 1 : 0) - (GameplayInput.Held(GameplayAction.Left) ? 1 : 0) : 0;
            if (!canMove || InputBlocked && !MenuAutoRun || forward != 0 || side != 0) autoRun = false;
            if (autoRun) forward = 1;
            if (canInput && GameplayInput.Pressed(GameplayAction.AutoRun)) autoRun = !autoRun;
            var mouse = Mouse.current;
            if (canInput && readMouse && mouse != null)
            {
                var look = LegacyGameplayCameraInput.Look(mouse.delta.ReadValue(), MouseSensitivity, InvertMouseY);
                yaw += look.x; pitch = Mathf.Clamp(pitch + look.y, -30, 70);
            }
            if (canInput && Gamepad.current != null)
            {
                var pad = GameplayInput.PadLook(delta, (float?)Preferences?["input"]?["pad_sensitivity"] ?? 5,
                    (bool?)Preferences?["input"]?["pad_invert_y"] ?? false);
                yaw += pad.x;
                pitch = Mathf.Clamp(pitch + pad.y, -30, 70);
            }
            if (canInput && keys != null)
                yaw += ((GameplayInput.Held(GameplayAction.RightTurn) ? 1 : 0) - (GameplayInput.Held(GameplayAction.LeftTurn) ? 1 : 0)) * 100 * delta;
            jumpCooldown = Mathf.Max(0, jumpCooldown - delta);
            bool grounded = slider || NativePlayerMotor.IsGrounded(motor, vertical);
            bool launched = canInput && GameplayInput.Pressed(GameplayAction.Jump) && grounded && jumpCooldown <= 0;
            if (launched) { nanoJump = false; LaunchJump(); }
            launched |= nanoJumpFresh;
            nanoJumpFresh = false;
            Vector3 horizontal = playerDead ? Vector3.zero : HorizontalMovement(side, forward, delta);
            var before = Player.position;
            bool dashing = nanoDashTime > 0 && !playerDead && !BuffMovementBlocked;
            if (dashing)
            {
                Player.rotation = Quaternion.Euler(0, yaw, 0);
                horizontal = Player.forward * nanoDashSpeed;
                nanoDashTime -= delta;
                nanoDashSpeed = Mathf.Max(0, nanoDashSpeed - 60 * delta);
            }
            else nanoDashTime = 0;
            bool restingOnSlider = slider && !launched && side == 0 && forward == 0;
            var flags = restingOnSlider ? CollisionFlags.Below : NativePlayerMotor.Step(motor, horizontal, ref vertical, ref jumpAge, delta,
                canInput && GameplayInput.Held(GameplayAction.Jump), launched, !PersonalVehicleMounted && !nanoJump);
            if (slider && !restingOnSlider) UpdatePassengerFooting();
            Velocity = delta > 0 ? (Player.position - before) / delta : Vector3.zero;
            MoveDirection = WorldCoordinates.Direction(side, forward);
            if (MoveDirection != 0) Player.rotation = Quaternion.Euler(0, MovementYaw, 0);
            if ((flags & CollisionFlags.Below) != 0 && vertical <= 0)
            {
                vertical = 0; Jumping = false; nanoJump = false;
                if (PersonalVehicleMounted)
                    avatar.PlayLocomotion(InventoryOpen ? "inven" : horizontal.sqrMagnitude < .01f ? "stand1" : forward < 0 ? "runback" : "run");
            }
            if ((flags & CollisionFlags.Above) != 0 && vertical > 0) vertical = 0;
            if (!dashing && !PersonalVehicleMounted && !Buffs.Has(10) && !Buffs.Has(11))
            {
                if (MoveDirection != 0 || launched) avatar.CancelEmote();
                if (InventoryOpen && !Jumping && !playerDead)
                    avatar.PlayLocomotion("inven");
                else if (!avatar.HasActiveEmote)
                {
                    if (weaponChangeClip != null)
                    {
                        var pose = avatar.PlayerAnimation.Animator.GetCurrentAnimatorStateInfo(0);
                        if (MoveDirection != 0 || Jumping || !pose.IsName(NativePlayerAnimator.BaseLayer + "." + weaponChangeClip) || pose.normalizedTime >= 1)
                            weaponChangeClip = null;
                    }
                    bool ready = weaponChangeClip != null || Time.time < playerAttackUntil || GameplayHudStatus.CombatActive(lastPlayerCombatResult, Time.realtimeSinceStartup);
                    avatar.PlayerAnimation?.UpdateLocomotion(side, forward, !launched && NativePlayerMotor.IsGrounded(motor, vertical), launched,
                        WorldWeaponCatalog.AnimationPrefix(weaponEquipType), ready, delta, vertical);
                }
            }
            if (MoveDirection != 0 || Jumping || (Player.position - before).sqrMagnitude > .000001f)
                Moved?.Invoke(Player.position, Player.eulerAngles.y);
            Stream.Anchor = Player.position;
            UpdateCamera();
        }

        float nanoDashTime, nanoDashSpeed;
        bool nanoJump, nanoJumpFresh;
        internal bool LaunchNanoMovement(WorldSkillDefinition skill, int slot)
        {
            if (!IsReady || !motor || playerDead || BuffMovementBlocked || PersonalVehicleMounted || Riding || slot < 0 || slot > 2) return false;
            int tier = Buffs.Value(21 + slot);
            if (skill.MovementPower == null || tier < 0 || tier >= skill.MovementPower.Length) return false;
            bool airborne = !NativePlayerMotor.IsGrounded(motor, vertical);
            LaunchJump();
            nanoJump = nanoJumpFresh = true;
            vertical = JumpLaunchVelocity = 5.68f + skill.MovementPower[tier] * .01f;
            if (skill.Type != 40) return true;
            nanoDashTime = .5555555f;
            nanoDashSpeed = 35;
            string profile = WorldWeaponCatalog.AnimationPrefix(weaponEquipType);
            string clip = airborne ? "rocketsomersault" : profile == "rifle" ? "rifledash" : profile == "pistol" ? "rifletumbling" : "stickdash";
            avatar.PlayerAnimation?.Play(clip, true);
            if (airborne && profile != "bomb")
                avatar.PlayerAnimation?.Play(profile == "rifle" ? "rifledodgeupper" : "stickdodgeupper", true);
            return true;
        }

        void LaunchJump()
        {
            vertical = JumpLaunchVelocity = BuffJumpVelocity * (PersonalVehicleMounted ? 1 : NativePlayerMotor.JumpMultiplier);
            Jumping = true; jumpCooldown = .5f; jumpAge = 0;
            LeaveSlider(); JumpKey = JumpKey == uint.MaxValue ? 1073741823 : JumpKey + 1;
            if (PersonalVehicleMounted) avatar.PlayLocomotion("jumpstart", true);
        }

        public void SetPlayerDead(bool dead)
        {
            if (playerDead == dead) return;
            playerDead = dead;
            if (!dead) return;
            StopZipline(detached: true);
            autoRun = false;
            MoveDirection = 0;
            Velocity = Vector3.up * Velocity.y;
            vehicleMotion.Reset();
        }

        internal bool RewardCamera { get; set; }
        void UpdateCamera()
        {
            if (RewardCamera) return;
            var focus = Player.position + Vector3.up * 1.6f;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var offset = rotation * new Vector3(0, .2f, -cameraDistance);
            offset = NativePlayerCamera.ResolveOffset(View, Player, focus, offset);
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
            bool ready = avatar.IsReady && string.IsNullOrEmpty(failure);
            if (ready)
            {
                weaponEquipType = WorldWeaponCatalog.AnimationEquipType(character);
                int hand = character.Equipment[0].ItemId;
                if (hand != equippedWeaponId)
                {
                    equippedWeaponId = hand;
                    weaponChangeClip = null;
                    var animation = avatar.PlayerAnimation;
                    animation?.CancelUpperMotion();
                    playerAttackUntil = 0;
                    string clip = WorldWeaponCatalog.AnimationPrefix(weaponEquipType) + "ready";
                    if (animation && !playerDead && !Riding && !Ziplining && !PersonalVehicleMounted && !avatar.HasActiveEmote &&
                        !BuffMovementBlocked && MoveDirection == 0 && NativePlayerMotor.IsGrounded(motor, vertical) && animation.Play(clip, true))
                        weaponChangeClip = clip;
                }
            }
            return ready;
        }

        public void SetServerPosition(int x, int y, int z)
        {
            StopLauncher();
            if (!Player || !motor) return;
            StopZipline();
            autoRun = false;
            LeaveSlider();
            var destination = WorldCoordinates.FromServer(x, y, z);
            if (!HasCoverage(Stream.Catalog, MapNumber, destination))
            { error = $"Server destination has no imported world at {x}, {y}, {z}."; MovementEnabled = false; return; }
            NativePlayerMotor.Teleport(motor, destination);
            vertical = 0; Jumping = false; Velocity = Vector3.zero; MoveDirection = 0;
            nanoDashTime = 0; nanoJump = nanoJumpFresh = false;
            avatar?.PlayerAnimation?.ResetLocomotion();
            Stream.Anchor = Player.position; UpdateCamera();
        }

        public void RelocateMap(int map, int x, int y, int z)
        { LeaveSlider(); SetPersonalVehicle(0); MapNumber = map; Stream.MapNumber = map; SetServerPosition(x, y, z); }
        public void SetRiding(bool active)
        {
            if (active == Riding) return;
            if (active)
            {
                StopZipline();
                autoRun = false;
                LeaveSlider();
                SetPersonalVehicle(0);
                LocalNano?.SetNano(0);
            }
            Riding = active; motor.enabled = !active; vertical = 0; Velocity = Vector3.zero;
            avatar?.PlayerAnimation?.ResetLocomotion();
            avatar?.SetSkywayPresentation(active);
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
            LocalNano = Player.gameObject.AddComponent<WorldNanoCompanion>();
            LocalNano.Initialize(Replicas, View, false);
            projectilePlayerId = entry.PlayerId;
            Replicas.Begin(entry.PlayerId, entry.BootstrapPackets);
        }

        internal Func<int, bool> CanInteractWithMissionMarker;

        public NetworkEntityView Target(float range = 50)
        {
            if (!View || !Player || !Replicas) return null;
            var ray = View.ViewportPointToRay(new Vector3(.5f, .5f));
            foreach (var hit in Physics.RaycastAll(ray, range + Vector3.Distance(View.transform.position, Player.position), ~(1 << 31), QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (hit.collider.transform.IsChildOf(Player)) continue;
                var actor = hit.collider.GetComponentInParent<NetworkEntityView>();
                if (actor && actor.Entity.Kind != WorldEntityKind.Transportation && !actor.Entity.Hidden && (actor.Hostile || actor.Entity.Interactable) &&
                    (actor.Definition?.HiddenMarker != true || actor.Definition.IsBank || CanInteractWithMissionMarker?.Invoke(actor.Entity.NpcType) == true)) return actor;
                if (!hit.collider.isTrigger) return null;
            }
            return null;
        }

        public void PlayAttack(int equipType = -1)
        {
            avatar?.CancelEmote();
            string clip = WorldWeaponCatalog.AttackClip(equipType < 0 ? weaponEquipType : equipType);
            var animation = avatar?.PlayerAnimation;
            if (animation && animation.Play(clip, true)) playerAttackUntil = Time.time + animation.Duration(clip);
        }

        internal void RecordPlayerCombatResult(CombatUpdate combat, int playerId)
        {
            if (combat.WeaponBattery.HasValue || combat.AttackerKind == WorldEntityKind.Player && combat.AttackerId == playerId ||
                combat.Hits.Any(hit => hit.Kind == WorldEntityKind.Player && hit.Id == playerId))
                lastPlayerCombatResult = Time.realtimeSinceStartup;
        }
        internal void PlayPlayerDamageReaction(bool dead, bool critical)
        {
            if (!avatar) return;
            if (!dead && !Ziplining && !Riding && !PersonalVehicleMounted) avatar.PlayerAnimation?.Play("woundupper", true);
            avatar.PlayDamageVoice(dead, critical, (string)Preferences["voice_locale"], FrontendController.AudioGain(Preferences, "voice"));
        }

        float danceMusicGain = .5f;
        internal void SetDanceMusicGain(float gain)
        {
            danceMusicGain = gain;
            if (avatar) avatar.SetDanceMusicGain(gain);
            if (Replicas)
                foreach (var replica in Replicas.Actors)
                    if (replica.Avatar) replica.Avatar.SetDanceMusicGain(gain);
        }

        public void PlayAvatarEmote(int id, int localId, int code, Action<int> continuation)
        {
            bool local = id == localId;
            var actor = local ? avatar : Replicas?.Actors.FirstOrDefault(view => view.Entity.Kind == WorldEntityKind.Player && view.Entity.Id == id)?.Avatar;
            if (!actor) return;
            actor.SetDanceMusicGain(danceMusicGain);
            actor.PlayEmote(code, (string)Preferences["voice_locale"], FrontendController.AudioGain(Preferences, "voice"), local ? continuation : null);
        }

        public void Stop()
        {
            ClearComputer();
            cameraInput.Release();
            StopZipline();
            ClearProjectiles();
            ClearEruptions();
            LeaveSlider();
            SetPersonalVehicle(0);
            MovementEnabled = false;
            characterUid = 0;
            if (ownedRoot) { ownedRoot.SetActive(false); Destroy(ownedRoot); ownedRoot = null; }
            foreach (var camera in previousCameras) if (camera) camera.enabled = true;
            foreach (var listener in previousListeners) if (listener) listener.enabled = true;
            previousCameras.Clear(); previousListeners.Clear(); Player = null; Stream = null; View = null; Replicas = null; LocalNano = null;
            if (captured)
            {
                RenderSettings.fog = oldFog; RenderSettings.fogMode = oldFogMode;
                RenderSettings.fogColor = oldFogColor; RenderSettings.fogDensity = oldFogDensity; captured = false;
            }
        }
        void OnDestroy() => Stop();
    }
}
