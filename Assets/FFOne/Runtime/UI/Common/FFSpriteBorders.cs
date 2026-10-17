using System;
using UnityEngine;

namespace FFOne.UI
{
    // Unity order: left, bottom, right, top. Shared by import and runtime styling.
    public static class FFSpriteBorders
    {
        public static Vector4 For(string path)
        {
            if (string.IsNullOrEmpty(path)) return Vector4.zero;
            path = path.Replace('\\', '/');
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (path.Contains("/quit-menu/quitbut")) return new Vector4(6, 4, 6, 6);
            // FusionFallInvenSkin.button and its email pagination styles.
            if (path.Contains("/gameplay/chat/blue_button")) return new Vector4(6, 4, 6, 6);
            if (path.Contains("/email/previous")) return new Vector4(10, 10, 4, 4);
            if (path.Contains("/email/next")) return new Vector4(4, 10, 10, 4);
            if (path.Contains("/character/creation/background/")) return Vector4.zero;
            if (name == "login_screen_button_bg") return new Vector4(8, 8, 8, 18);
            if (name.StartsWith("ff-button-", StringComparison.Ordinal)) return All(5);
            if (name.StartsWith("ff-textfield-", StringComparison.Ordinal)) return All(2);
            if (name == "scroll-track" || name == "scroll-thumb") return new Vector4(2, 4, 2, 4);
            if (name == "CCBG") return new Vector4(10, 0, 10, 0);
            if (name == "CCLeftIn12BG") return new Vector4(5, 40, 40, 5);
            if (name == "CCNameTab1" || name == "CCNameTab2") return new Vector4(5, 10, 5, 21);
            if (name.StartsWith("CCNameTabButton", StringComparison.Ordinal)) return new Vector4(35, 0, 35, 20);
            if (name == "CCNameBG") return new Vector4(5, 0, 5, 0);
            if (name.StartsWith("CCCheckbox", StringComparison.Ordinal) || name == "CCSelectedColor") return Vector4.zero;
            if (path.Contains("/character/creation/") && !name.StartsWith("CCFull", StringComparison.Ordinal)
                && !name.StartsWith("CCWindow", StringComparison.Ordinal) && !name.Contains("BG_")) return All(5);
            if (name == "DiskBack" || name == "DiskFront") return All(5);
            if (name.StartsWith("CSCharButton", StringComparison.Ordinal) || name.StartsWith("blue_button", StringComparison.Ordinal)
                || name.StartsWith("red_button", StringComparison.Ordinal) || name == "CancelNormal") return All(5);
            return Vector4.zero;
        }
        static Vector4 All(float value) => new Vector4(value, value, value, value);
    }
}
