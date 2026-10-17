using Newtonsoft.Json.Linq;

namespace FFOne.UI
{
    public static class SettingsInputDefaults
    {
        public static JObject Create()
        {
            var rows = new JObject();
            void Add(string action, string primary = "", string alternate = "", string pad = "") =>
                rows[action] = new JObject { ["primary"] = primary, ["alternate"] = alternate, ["pad"] = pad };
            Add("Up", "W", "UpArrow", "leftStick/up"); Add("Down", "S", "DownArrow", "leftStick/down");
            Add("RightTurn", "E", "RightArrow"); Add("LeftTurn", "Q", "LeftArrow");
            Add("Right", "D", "", "leftStick/right"); Add("Left", "A", "", "leftStick/left");
            Add("Jump", "Space", "", "buttonSouth"); Add("AutoRun", "Home", "", "rightStickPress");
            Add("NanoBook", "N"); Add("Inventory", "I", "", "select"); Add("Journal", "J", "", "dpad/left"); Add("Email", "P"); Add("WorldMap", "M");
            Add("CameraUp", "MouseY+", "", "rightStick/up"); Add("CameraDown", "MouseY-", "", "rightStick/down");
            Add("CameraLeft", "MouseX-", "", "rightStick/left"); Add("CameraRight", "MouseX+", "", "rightStick/right");
            Add("ZoomIn", "Wheel+", "", "dpad/up"); Add("ZoomOut", "Wheel-", "", "dpad/down");
            Add("Fire1", "Z", "Mouse0", "rightTrigger"); Add("WeaponChange", "Tab", "", "dpad/right");
            Add("Fire2", "X", "Mouse1", "leftTrigger"); Add("NanoCharge", "C", "", "rightShoulder");
            Add("Nano1", "Alpha1", "Keypad1", "buttonWest"); Add("Nano2", "Alpha2", "Keypad2", "buttonNorth");
            Add("Nano3", "Alpha3", "Keypad3", "buttonEast"); Add("VehicleToggle", "V", "", "leftStickPress");
            Add("Escape", "BackQuote"); Add("Help"); Add("Menu", "Return", "KeypadEnter", "start");
            Add("Option", "Quote"); Add("Contacts"); Add("SendChat"); Add("FreeCamera", "LeftControl", "RightControl", "leftShoulder");
            Add("WeaponCharge", "R"); Add("Skill1", "R"); Add("Skill2", "F"); Add("Skill3", "Z", "Mouse0"); Add("None");
            return rows;
        }
    }
}
