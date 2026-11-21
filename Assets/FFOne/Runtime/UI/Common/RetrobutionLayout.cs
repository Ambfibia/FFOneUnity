using UnityEngine;

namespace FFOne.UI
{
    public static class RetrobutionLayout
    {
        // Primary 20260821 FFGUIUtility.GetUiScale; the source default uiScaleFactor is 1.
        internal static bool AutomaticScale { get; set; } = true;
        public static float Scale(float height) => Scale(height, AutomaticScale ? 1 : 0);
        public static float Scale(float height, float factor) => Mathf.Lerp(1, height / 681f, factor);

        // cnQuit.ScrollWindow truncates before integer division; GUI.matrix then scales around
        // the integer screen-center pivot. Rounding after scaling shifts odd-sized viewports.
        public static Rect QuitDialog(int width, int height)
        {
            float scale = Scale(height);
            var pivot = new Vector2(width / 2, height / 2);
            var position = new Vector2((width - 206) / 2, (height - 188) / 2);
            position = pivot + (position - pivot) * scale;
            return new Rect(position.x, position.y, 206, 188);
        }

        public static Rect QuitBackdrop(int width, int height)
        {
            float scale = Scale(height);
            return new Rect((width / 2) * (1 - scale), (height / 2) * (1 - scale), width, height);
        }
    }
}
