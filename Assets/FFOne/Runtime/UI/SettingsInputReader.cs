using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FFOne.UI
{
    /// <summary>Reads committed Option mappings using physical inputs, independent of text locale.</summary>
    public static class SettingsInputReader
    {
        static readonly Dictionary<string, Key> Keys = new(StringComparer.Ordinal);
        static readonly Dictionary<string, Key> Aliases = new(StringComparer.Ordinal)
        {
            ["Return"] = Key.Enter, ["KeypadEnter"] = Key.NumpadEnter,
            ["UpArrow"] = Key.UpArrow, ["DownArrow"] = Key.DownArrow,
            ["LeftArrow"] = Key.LeftArrow, ["RightArrow"] = Key.RightArrow,
            ["LeftControl"] = Key.LeftCtrl, ["RightControl"] = Key.RightCtrl,
            ["LeftCommand"] = Key.LeftMeta, ["RightCommand"] = Key.RightMeta,
            ["LeftWindows"] = Key.LeftMeta, ["RightWindows"] = Key.RightMeta,
            ["BackQuote"] = Key.Backquote, ["Quote"] = Key.Quote,
            ["KeypadPeriod"] = Key.NumpadPeriod, ["KeypadDivide"] = Key.NumpadDivide,
            ["KeypadMultiply"] = Key.NumpadMultiply, ["KeypadMinus"] = Key.NumpadMinus,
            ["KeypadPlus"] = Key.NumpadPlus, ["KeypadEquals"] = Key.NumpadEquals,
            ["Numlock"] = Key.NumLock, ["Print"] = Key.PrintScreen,
        };
        public static bool Held(string action, JObject committed) => Read(action, committed, false);
        public static bool Pressed(string action, JObject committed) => Read(action, committed, true);
        static bool Read(string action, JObject committed, bool pressed)
        {
            if (string.IsNullOrEmpty(action) || !(committed?["input"]?["bindings"]?[action] is JObject row)) return false;
            return ReadKeyboardOrMouse((string)row["primary"], pressed) ||
                ReadKeyboardOrMouse((string)row["alternate"], pressed) || ReadPad((string)row["pad"], pressed);
        }
        static bool ReadKeyboardOrMouse(string binding, bool pressed)
        {
            if (string.IsNullOrEmpty(binding)) return false;
            if (binding.StartsWith("Mouse", StringComparison.Ordinal) || binding.StartsWith("Wheel", StringComparison.Ordinal))
            {
                var mouse = Mouse.current; if (mouse == null) return false;
                if (binding == "Wheel+") return mouse.scroll.ReadValue().y > 0;
                if (binding == "Wheel-") return mouse.scroll.ReadValue().y < 0;
                if (binding == "MouseX+") return mouse.delta.ReadValue().x > 0;
                if (binding == "MouseX-") return mouse.delta.ReadValue().x < 0;
                if (binding == "MouseY+") return mouse.delta.ReadValue().y > 0;
                if (binding == "MouseY-") return mouse.delta.ReadValue().y < 0;
                return Button(binding switch {
                    "Mouse0" => mouse.leftButton, "Mouse1" => mouse.rightButton,
                    "Mouse2" => mouse.middleButton, "Mouse3" => mouse.backButton,
                    "Mouse4" => mouse.forwardButton, _ => null
                }, pressed);
            }
            var keyboard = Keyboard.current; if (keyboard == null) return false;
            if (!Keys.TryGetValue(binding, out var key))
            {
                string name = binding;
                if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
                else if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "Numpad" + name.Substring(6);
                if (!Aliases.TryGetValue(binding, out key) && !Enum.TryParse(name, true, out key)) key = Key.None;
                // Invalid persisted values must never index an unavailable key control.
                if (!Enum.IsDefined(typeof(Key), key)) key = Key.None;
                Keys[binding] = key;
            }
            return key != Key.None && Button(keyboard[key], pressed);
        }
        static bool ReadPad(string binding, bool pressed)
        {
            if (string.IsNullOrEmpty(binding) || Gamepad.current == null) return false;
            return Button(Gamepad.current.TryGetChildControl<ButtonControl>(binding), pressed);
        }
        static bool Button(ButtonControl control, bool pressed) => control != null && (pressed ? control.wasPressedThisFrame : control.isPressed);
    }
}
