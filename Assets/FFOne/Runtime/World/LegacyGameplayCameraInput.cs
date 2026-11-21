using UnityEngine;

namespace FFOne.World
{
    // cnMainGame.Update locks the gameplay cursor; cnPlayerCamera.Update reads
    // Mouse X/Y without a mouse-button gate. mainData's axis sensitivity is .03,
    // camera sensitivity 5 and fMouseSensivity 6: .36/.18 degrees per pixel.
    public sealed class LegacyGameplayCameraInput
    {
        bool captured;
        public bool Capture(bool gameplay)
        {
            bool next = gameplay && Application.isFocused;
            bool continuous = next && captured && Cursor.lockState == CursorLockMode.Locked;
            if (next || captured)
            {
                Cursor.lockState = next ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !next;
            }
            captured = next;
            // Locking/focus recovery can contain the cursor-warp delta.
            return continuous;
        }
        public void Release() => Capture(false);
        public static Vector2 Look(Vector2 pixels, float sensitivity = 1, bool invertY = false)
            => new(pixels.x * .36f * sensitivity, pixels.y * .18f * sensitivity * (invertY ? 1 : -1));
    }
}
