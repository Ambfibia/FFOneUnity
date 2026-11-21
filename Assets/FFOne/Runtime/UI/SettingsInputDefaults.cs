using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.InputSystem;

namespace FFOne.UI
{
    public static class SettingsInputDefaults
    {
        // Aliases from the original options file. Only profile loading and applying
        // options need them; live input uses the resolved InputAction bindings.
        static readonly Dictionary<string, string> LegacyControls = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Return"] = "<Keyboard>/enter",
            ["LeftControl"] = "<Keyboard>/leftCtrl", ["RightControl"] = "<Keyboard>/rightCtrl",
            ["LeftCommand"] = "<Keyboard>/leftMeta", ["RightCommand"] = "<Keyboard>/rightMeta",
            ["LeftWindows"] = "<Keyboard>/leftMeta", ["RightWindows"] = "<Keyboard>/rightMeta",
            ["LeftApple"] = "<Keyboard>/leftMeta", ["RightApple"] = "<Keyboard>/rightMeta",
            ["Print"] = "<Keyboard>/printScreen",
            ["Mouse0"] = "<Mouse>/leftButton", ["Mouse1"] = "<Mouse>/rightButton",
            ["Mouse2"] = "<Mouse>/middleButton", ["Mouse3"] = "<Mouse>/backButton",
            ["Mouse4"] = "<Mouse>/forwardButton",
            ["Wheel+"] = "<Mouse>/scroll/up", ["Wheel-"] = "<Mouse>/scroll/down",
            ["MouseX+"] = "<Mouse>/delta/right", ["MouseX-"] = "<Mouse>/delta/left",
            ["MouseY+"] = "<Mouse>/delta/up", ["MouseY-"] = "<Mouse>/delta/down"
        };

        public static JObject Create()
        {
            var rows = new JObject();
            foreach (var action in GameplayInput.Map.actions)
            {
                var row = new JObject();
                foreach (var binding in action.bindings)
                    row[binding.name] = binding.path ?? "";
                rows[action.name] = row;
            }
            return rows;
        }

        internal static string ControlPath(string value, string slot)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value[0] == '<') return value;
            if (slot == "pad") return "<Gamepad>/" + value;
            if (LegacyControls.TryGetValue(value, out var path)) return path;

            string keyName = value;
            if (value.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase)) keyName = "Digit" + value.Substring(5);
            else if (value.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase)) keyName = "Numpad" + value.Substring(6);
            if (!Enum.TryParse(keyName, true, out Key key) || key == Key.None || !Enum.IsDefined(typeof(Key), key)) return "";

            keyName = key switch
            {
                Key.LeftMeta => "LeftMeta",
                Key.RightMeta => "RightMeta",
                _ => key.ToString()
            };
            return keyName.StartsWith("Digit", StringComparison.Ordinal)
                ? "<Keyboard>/" + keyName.Substring(5)
                : "<Keyboard>/" + char.ToLowerInvariant(keyName[0]) + keyName.Substring(1);
        }
    }
}
