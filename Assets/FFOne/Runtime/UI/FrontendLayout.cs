using UnityEngine;

namespace FFOne.UI
{
    /// <summary>Retrobution character screens and the FFOne login, in logical UI pixels.</summary>
    public static class FrontendLayout
    {
        public static readonly float[] SlotY = { 43, 141, 237, 334 };
        public static readonly float[] NameY = { 52, 148, 244, 341 };
        public static readonly float[] LevelY = { 70, 166, 262, 359 };
        public static readonly float[] LocationY = { 87, 184, 280, 377 };
        public static readonly float[] EmptyY = { 71, 169, 265, 362 };
        public static readonly float[] PortraitY = { 31, 128, 225, 321 };
        public static readonly float[] DiskBackY = { 93, 190, 287, 383 };
        public static readonly float[] DiskFrontY = { 106, 203, 300, 396 };

        public static Rect Selection(float width, float height) => new Rect(width / 2 + 30, height / 2 - 288, 478, 600);
        public static Rect Avatar(float width, float height) => new Rect(width / 2 - 510, height / 2 - 315, 600, 600);
        public static Rect Chrome(float width, float height) => new Rect((width - 1920) / 2, (height - 1440) / 2, 1920, 1440);
        public static Rect Login(float width, float height) => new Rect((width - 370) / 2, (height - 299) / 2, 370, 299);
        public static float UiScale(float height) => RetrobutionLayout.AutomaticScale ? Mathf.Max(1, height / 768 * 1.05f) : 1;
        // Retrobution FFGUIUtility.GetUiScale, with the default UI Scale Factor of 1.
        public static float CharacterScale(float height) => RetrobutionLayout.Scale(height);
        // Keep the source composition at 16:9 and 16:10; fit narrower windows too.
        public static float CharacterScale(float width, float height) => Mathf.Min(RetrobutionLayout.Scale(height), width / 1089.6f);

        public static Rect Centered(float width, float height, Vector2 size, float scale) =>
            new Rect((width - size.x * scale) / 2, (height - size.y * scale) / 2, size.x, size.y);

        public static Vector2Int DefaultResolution(int displayWidth, int displayHeight) =>
            new Vector2Int(1920, displayWidth > 0 && displayHeight > 0 &&
                Mathf.Abs((float)displayWidth / displayHeight - 1.6f) < .04f ? 1200 : 1080);

        public static float BackgroundX(int frame, float seconds)
        {
            float x = frame * 542 - Mathf.Repeat(Mathf.Max(0, seconds) * 100, 5420);
            return x + 542 <= 0 ? x + 5420 : x;
        }
    }
}
