using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    // One skin/state implementation for login, roster, creation and future game windows.
    public static class FFStyle
    {
        sealed class ButtonState { public string Normal, Hover, Pressed; public bool Inside, Focused, Down; }
        static readonly ConditionalWeakTable<Button, ButtonState> States = new();

        public static void Background(VisualElement element, string path, Vector4? overrideBorder = null)
        {
            var border = overrideBorder ?? FFSpriteBorders.For(path);
            var sprite = string.IsNullOrEmpty(path) ? null : FFOne.Content.ContentAssets.Load<Sprite>(path);
            // The UI Toolkit renderer falls back to the Sprite's border when all style
            // slices are zero. Use the texture for a mismatched border, including a
            // requested full stretch, to apply the selected GUIStyle's exact slices.
            if (sprite != null && sprite.border != border) sprite = null;
            element.style.backgroundImage = sprite != null ? new StyleBackground(sprite) :
                new StyleBackground(string.IsNullOrEmpty(path) ? null : FFOne.Content.ContentAssets.Load<Texture2D>(path));
            element.style.unitySliceLeft = (int)border.x;
            element.style.unitySliceBottom = (int)border.y;
            element.style.unitySliceRight = (int)border.z;
            element.style.unitySliceTop = (int)border.w;
            element.style.unitySliceScale = 1;
        }

        public static void Button(Button button, string normal, string hover = null, string pressed = null)
        {
            button.AddToClassList("ff-button");
            if (!States.TryGetValue(button, out var state))
            {
                state = new ButtonState(); States.Add(button, state);
                button.RegisterCallback<PointerEnterEvent>(_ => { state.Inside = true; Refresh(button, state); });
                button.RegisterCallback<PointerLeaveEvent>(_ => { state.Inside = state.Down = false; Refresh(button, state); });
                button.RegisterCallback<FocusInEvent>(_ => { state.Focused = true; Refresh(button, state); });
                button.RegisterCallback<FocusOutEvent>(_ => { state.Focused = state.Down = false; Refresh(button, state); });
                button.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { state.Down = true; Refresh(button, state); } }, TrickleDown.TrickleDown);
                button.RegisterCallback<PointerUpEvent>(_ => { state.Down = false; Refresh(button, state); }, TrickleDown.TrickleDown);
                button.RegisterCallback<PointerCaptureOutEvent>(_ => { state.Down = false; Refresh(button, state); });
                button.RegisterCallback<DetachFromPanelEvent>(_ => state.Inside = state.Focused = state.Down = false);
            }
            state.Normal = normal; state.Hover = hover ?? normal; state.Pressed = pressed ?? state.Hover;
            Refresh(button, state);
        }
        static void Refresh(Button button, ButtonState state) => Background(button,
            button.enabledInHierarchy && state.Down ? state.Pressed :
            button.enabledInHierarchy && (state.Inside || state.Focused) ? state.Hover : state.Normal);

        public static string Caption(string text) => (text ?? "").ToUpperInvariant();
    }
}
