using System;
using System.Collections.Generic;
using FFOne.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FFOne.World
{
    public sealed partial class NetworkWorldController
    {
        WorldLauncher launcher;
        Vector2 launcherAim;
        float launcherPower, nextLaunchUpdate;
        bool chargingLauncher, launcherPowerRising, launcherFlight;
        Vector3 launcherVelocity;
        readonly List<Renderer> hiddenForLauncher = new();
        internal bool AimingLauncher => launcher != null;
        internal bool UsingLauncher => AimingLauncher || launcherFlight;
        internal float LauncherGauge => launcher == null ? 0 : Mathf.InverseLerp(launcher.MinPower, launcher.MaxPower, launcherPower);
        internal event Action<Vector3, Vector3, float> LauncherMoved;
        internal bool HasLauncherTarget => Player && IsReady && MovementEnabled && !InputBlocked &&
            !playerDead && !BuffMovementBlocked && !Riding && !Ziplining && !PersonalVehicleMounted && !UsingLauncher &&
            WorldLauncher.Find(View, Player.position) != null;

        internal bool TryStartLauncher()
        {
            if (!HasLauncherTarget) return false;
            launcher = WorldLauncher.Find(View, Player.position);
            launcherPower = launcher.MinPower;
            launcherAim = Vector2.zero;
            launcherPowerRising = true;
            chargingLauncher = false;
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>())
                if (!renderer.forceRenderingOff) { renderer.forceRenderingOff = true; hiddenForLauncher.Add(renderer); }
            return true;
        }

        void StopLauncher()
        {
            foreach (var renderer in hiddenForLauncher) if (renderer) renderer.forceRenderingOff = false;
            hiddenForLauncher.Clear();
            launcher = null;
            launcherFlight = chargingLauncher = false;
        }

        bool TickLauncher()
        {
            if (!UsingLauncher) return false;
            if (playerDead || !MovementEnabled || Riding || AimingLauncher && !launcher.Owner)
            { StopLauncher(); return false; }
            if (launcherFlight)
            {
                if (!IsReady) return true;
                float dt = Mathf.Min(.1f, Time.deltaTime);
                launcherVelocity.y -= NativePlayerMotor.Gravity * dt;
                var flags = motor.Move(launcherVelocity * dt);
                bool landed = (flags & CollisionFlags.Sides) != 0 || launcherVelocity.y <= 0 && (flags & CollisionFlags.Below) != 0;
                Velocity = launcherVelocity;
                Stream.Anchor = Player.position;
                UpdateCamera();
                if (landed || Time.unscaledTime >= nextLaunchUpdate)
                {
                    LauncherMoved?.Invoke(Player.position, landed ? Vector3.zero : launcherVelocity, yaw);
                    nextLaunchUpdate = Time.unscaledTime + .1f;
                }
                if (landed) { StopLauncher(); vertical = 0; Jumping = false; }
                return true;
            }
            if (GameplayInput.Pressed(GameplayAction.Escape) || Gamepad.current?.buttonEast.wasPressedThisFrame == true)
            { StopLauncher(); UpdateCamera(); return true; }
            float horizontal = (GameplayInput.Held(GameplayAction.Right) || GameplayInput.Held(GameplayAction.RightTurn) ? 1 : 0) -
                (GameplayInput.Held(GameplayAction.Left) || GameplayInput.Held(GameplayAction.LeftTurn) ? 1 : 0);
            float up = (GameplayInput.Held(GameplayAction.Up) ? 1 : 0) - (GameplayInput.Held(GameplayAction.Down) ? 1 : 0);
            var stick = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;
            float step = 80 * Mathf.Min(.1f, Time.deltaTime);
            launcherAim.x = Mathf.Clamp(launcherAim.x - Mathf.Clamp(up + stick.y, -1, 1) * step, -launcher.MaximumRotation.x, 0);
            launcherAim.y = Mathf.Clamp(launcherAim.y + Mathf.Clamp(horizontal + stick.x, -1, 1) * step, -launcher.MaximumRotation.y, launcher.MaximumRotation.y);
            yaw = launcher.InitialRotation.y + launcherAim.y + 180;
            var direction = Quaternion.Euler(launcherAim.x, yaw, 0);
            View.transform.SetPositionAndRotation(launcher.Center + Vector3.up * 1.6f, direction);
            bool fire = GameplayInput.Held(GameplayAction.Fire1) || Gamepad.current?.buttonSouth.isPressed == true;
            if (fire)
            {
                chargingLauncher = true;
                float change = (launcher.MaxPower - launcher.MinPower) * Time.deltaTime;
                launcherPower = Mathf.Clamp(launcherPower + (launcherPowerRising ? change : -change), launcher.MinPower, launcher.MaxPower);
                if (launcherPower >= launcher.MaxPower) launcherPowerRising = false;
                if (launcherPower <= launcher.MinPower) launcherPowerRising = true;
            }
            else if (chargingLauncher)
            {
                var forward = direction * Vector3.forward;
                var start = View.transform.position + forward * 5;
                launcherVelocity = forward * launcherPower;
                StopLauncher();
                launcherFlight = true;
                NativePlayerMotor.Teleport(motor, start);
                Player.rotation = Quaternion.Euler(0, yaw, 0);
                LauncherMoved?.Invoke(start, launcherVelocity, yaw);
                nextLaunchUpdate = Time.unscaledTime + .1f;
                Jumping = true;
                Stream.Anchor = start;
                UpdateCamera();
            }
            return true;
        }
    }
}
