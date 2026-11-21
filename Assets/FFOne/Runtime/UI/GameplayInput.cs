using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace FFOne.UI
{
    internal enum GameplayAction
    {
        Up, Down, RightTurn, LeftTurn, Right, Left, Jump,
        NanoBook, Inventory, Journal, Email, WorldMap, Menu, Escape, Option,
        FreeCamera, Fire1, Fire2, NanoCharge, Nano1, Nano2, Nano3, VehicleToggle, WeaponChange, AutoRun, CameraUp, CameraDown, CameraLeft, CameraRight, ZoomIn, ZoomOut
    }

    internal static class GameplayInput
    {
        static InputActionAsset cachedAsset;
        static InputAction[] actions;
        static readonly HashSet<Gamepad> waitingForNeutral = new();
        static readonly HashSet<Gamepad> reportedState = new();

        internal static void TrackGamepads()
        {
            waitingForNeutral.Clear();
            reportedState.Clear();
            InputSystem.onDeviceChange -= GamepadChanged;
            InputSystem.onDeviceChange += GamepadChanged;
            InputSystem.onEvent -= GamepadStateReported;
            InputSystem.onEvent += GamepadStateReported;
            InputSystem.onAfterUpdate -= ArmNeutralGamepads;
            InputSystem.onAfterUpdate += ArmNeutralGamepads;
        }

        static void GamepadChanged(InputDevice device, InputDeviceChange change)
        {
            if (device is not Gamepad pad) return;
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Enabled:
                    reportedState.Remove(pad);
                    waitingForNeutral.Add(pad);
                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Disabled:
                    reportedState.Remove(pad);
                    waitingForNeutral.Remove(pad);
                    break;
            }
        }

        static void GamepadStateReported(InputEventPtr input, InputDevice device)
        {
            if (device is Gamepad pad && waitingForNeutral.Contains(pad) && (input.IsA<StateEvent>() || input.IsA<DeltaStateEvent>()))
                reportedState.Add(pad);
        }

        static void ArmNeutralGamepads()
        {
            if (InputSystem.actions == null) return;
            // The default state immediately after AddDevice is not a hardware report.
            reportedState.RemoveWhere(pad =>
            {
                for (var id = GameplayAction.Up; id <= GameplayAction.Left; id++)
                    foreach (var control in Action(id).controls)
                        if (control.device == pad && control is ButtonControl button && button.isPressed) return false;
                foreach (var control in Action(GameplayAction.AutoRun).controls)
                    if (control.device == pad && control is ButtonControl button && button.isPressed) return false;
                waitingForNeutral.Remove(pad);
                return true;
            });
        }

        internal static InputActionMap Map => InputSystem.actions.FindActionMap("FFOneGameplay", true);

        static InputAction Action(GameplayAction action)
        {
            var asset = InputSystem.actions;
            if (actions == null || asset != cachedAsset)
            {
                var map = Map;
                actions = Enum.GetValues(typeof(GameplayAction)).Cast<GameplayAction>()
                    .Select(id => map.FindAction(id.ToString(), true)).ToArray();
                cachedAsset = asset;
            }
            return actions[(int)action];
        }

        internal static UnityEngine.Vector2 PadLook(float delta, float sensitivity, bool invert)
        {
            float Axis(GameplayAction action)
            {
                float value = 0;
                foreach (var control in Action(action).controls)
                    if (control is ButtonControl button && control.device is Gamepad && IsAvailable(control))
                        value = UnityEngine.Mathf.Max(value, button.ReadValue());
                return value;
            }
            float scale = UnityEngine.Mathf.Clamp(sensitivity, 1, 10) * .2f * delta;
            return new UnityEngine.Vector2((Axis(GameplayAction.CameraRight) - Axis(GameplayAction.CameraLeft)) * 90 * scale,
                (Axis(GameplayAction.CameraUp) - Axis(GameplayAction.CameraDown)) * (invert ? 60 : -60) * scale);
        }

        internal static bool Held(GameplayAction action)
        {
            var input = Action(action);
            if (action > GameplayAction.Left || waitingForNeutral.Count == 0) return input.IsPressed();
            return input.controls.Any(control => IsAvailable(control) && control is ButtonControl button && button.isPressed);
        }

        internal static bool Pressed(GameplayAction action)
        {
            var input = Action(action);
            if (action != GameplayAction.AutoRun || waitingForNeutral.Count == 0) return input.WasPressedThisFrame();
            return input.controls.Any(control => IsAvailable(control) && control is ButtonControl button && button.wasPressedThisFrame);
        }

        static bool IsAvailable(InputControl control) => control.device is not Gamepad pad || !waitingForNeutral.Contains(pad);
        internal static bool Released(GameplayAction action) => Action(action).WasReleasedThisFrame();

        internal static void Apply(JObject committed)
        {
            var rows = committed["input"]["bindings"];
            foreach (var action in Map.actions)
            {
                var row = rows[action.name];
                for (int index = 0; index < action.bindings.Count; index++)
                {
                    var binding = action.bindings[index];
                    var saved = (string)row?[binding.name];
                    string path = saved == null ? binding.path : SettingsInputDefaults.ControlPath(saved, binding.name);
                    if (path == binding.effectivePath) continue;
                    if (path == binding.path) action.RemoveBindingOverride(index);
                    else action.ApplyBindingOverride(index, path);
                }
            }
        }
    }
}
