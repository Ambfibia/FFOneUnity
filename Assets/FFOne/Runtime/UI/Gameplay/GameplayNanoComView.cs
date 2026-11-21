using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal static class GameplayNanoComView
    {
        static readonly (string Role, string Text)[] RussianCaptions =
        {
            ("inventory", "МОИ ВЕЩИ"), ("journal", "ЖУРНАЛ"), ("mail", "ПОЧТА"),
            ("map", "КАРТА"), ("settings", "НАСТРОЙКИ"), ("guide", "РУКОВОДСТВО"), ("quit", "ВЫХОД")
        };

        internal static VisualElement Instantiate() =>
            FFView.InstantiateClass("ui/views/Retrobution/NanoCom", "nanocom-view");

        internal static VisualElement Panel(VisualElement root) => FFView.RequireClass<VisualElement>(root, "nanocom-window");

        internal static Button Action(VisualElement root, string role) => FFView.RequireClass<Button>(root, "nanocom-action-" + role);

        internal static void Localize(VisualElement root, string language)
        {
            if (language != "ru") return;
            foreach (var (role, text) in RussianCaptions)
                FFView.RequireClass<Label>(root, "nanocom-label-" + role).text = text;
        }

        internal static void BindZoom(VisualElement root, Action<int> zoom, bool enabled = true)
        {
            var closer = FFView.RequireClass<Button>(root, "nanocom-zoom-in");
            var farther = FFView.RequireClass<Button>(root, "nanocom-zoom-out");
            closer.SetEnabled(enabled);
            farther.SetEnabled(enabled);
            closer.clicked += () => zoom(-1);
            farther.clicked += () => zoom(1);
        }

        internal static void AttachMinimap(VisualElement root, VisualElement minimap)
        {
            var panel = Panel(root);
            panel.Insert(1, minimap);
            minimap.style.left = minimap.style.top = 0;
            minimap.style.scale = new Scale(Vector3.one);
        }

        internal static void Place(VisualElement root, float width, float height)
        {
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            var panel = Panel(root);
            float scale = RetrobutionLayout.Scale(height);
            panel.style.transformOrigin = new TransformOrigin(0, 0);
            panel.style.scale = new Scale(new Vector3(scale, scale, 1));
            panel.style.left = width - 187 * scale;
            panel.style.top = -4 * scale;
        }
    }
}
