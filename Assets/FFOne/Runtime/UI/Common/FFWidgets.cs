using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal static class FFWidgets
    {
        sealed class TextFit { public float BaseSize, Width; }
        static readonly ConditionalWeakTable<TextElement, TextFit> TextFits = new ConditionalWeakTable<TextElement, TextFit>();
        internal const string Controls = "ui/en/character/selection/controls/";
        internal static Texture2D Texture(string path) => string.IsNullOrEmpty(path) ? null : FFOne.Content.ContentAssets.Load<Texture2D>(path);

        internal static void Rect(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = x;
            element.style.top = y;
            element.style.width = width;
            element.style.height = height;
        }

        internal static void Rect(VisualElement element, UnityEngine.Rect rect) => Rect(element, rect.x, rect.y, rect.width, rect.height);

        internal static void Font(VisualElement element, bool chalet = false, int size = 14)
        {
            string path = "fonts/" + (chalet ? "chaletbook-regular" : "jeffe");
            var fontAsset = FFOne.Content.ContentAssets.Load<UnityEngine.TextCore.Text.FontAsset>(path + "-sdf");
            if (fontAsset != null) element.style.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);
            else
            {
                var font = FFOne.Content.ContentAssets.Load<Font>(path);
                if (font != null) element.style.unityFontDefinition = FontDefinition.FromFont(font);
            }
            element.style.unityFontStyleAndWeight = FontStyle.Normal;
            element.style.fontSize = size;
            if (element is TextElement text && TextFits.TryGetValue(text, out var fit)) fit.BaseSize = size;
        }

        internal static VisualElement Group(VisualElement parent, string name)
        {
            var result = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            parent.Add(result);
            return result;
        }

        internal static Image Image(VisualElement parent, string name, string path, float x, float y, float w, float h)
        {
            var image = new Image { name = name, image = string.IsNullOrEmpty(path) ? null : Texture(path),
                scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Rect(image, x, y, w, h);
            parent.Add(image);
            return image;
        }

        internal static Label Label(VisualElement parent, string name, string text, float x, float y, float w, float h,
            bool chalet = false, int size = 14, bool centered = true)
        {
            var label = new Label(chalet ? text : FFStyle.Caption(text)) { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList("ff-label");
            Font(label, chalet, size);
            label.style.unityTextAlign = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            Rect(label, x, y, w, h);
            parent.Add(label);
            AutoFit(label);
            return label;
        }

        internal static TextField Field(VisualElement parent, string name, float x, float y, float w, float h, bool password = false)
        {
            var field = new TextField { name = name, isPasswordField = password, maxLength = 32, maskChar = '*' };
            field.AddToClassList("ff-input");
            Font(field, true);
            FFStyle.Background(field, "ui/en/launcher/login/ff-textfield-normal");
            Rect(field, x, y, w, h);
            parent.Add(field);
            return field;
        }

        internal static Button Button(VisualElement parent, string name, string text, float x, float y, float w, float h,
            string normal, string hover, Action clicked, int fontSize = 12)
        {
            var button = new Button(clicked) { name = name, text = FFStyle.Caption(text) };
            button.AddToClassList("ff-button");
            Font(button, false, fontSize);
            Rect(button, x, y, w, h);
            Skin(button, normal, hover);
            parent.Add(button);
            AutoFit(button);
            return button;
        }

        // Mirror Rust UiTextAutoFit: fixed controls keep their geometry; short
        // source rectangles permit vertical overflow and fit the shaped width.
        internal static void AutoFit(TextElement element, float width = 0)
        {
            if (TextFits.TryGetValue(element, out var existing)) { existing.Width = width; return; }
            TextFits.Add(element, new TextFit { Width = width });
            element.RegisterCallback<GeometryChangedEvent>(_ => FitText(element));
            element.RegisterCallback<AttachToPanelEvent>(_ => element.schedule.Execute(() => FitText(element)));
        }
        internal static void FitTree(VisualElement root)
        {
            root.schedule.Execute(() => root.Query<TextElement>().ForEach(FitText));
        }
        static void FitText(TextElement element)
        {
            if (!TextFits.TryGetValue(element, out var fit) || element.panel == null) return;
            var style = element.resolvedStyle;
            if (style.fontSize <= 0 || float.IsNaN(element.contentRect.width) || element.contentRect.width <= 0) return;
            if (fit.BaseSize == 0) fit.BaseSize = style.fontSize;
            float width = fit.Width > 0 ? fit.Width : element.contentRect.width;
            var size = element.MeasureTextSize(element.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            float desired = Mathf.Max(6, Mathf.Min(fit.BaseSize, size.x > 0 ? style.fontSize * width / size.x : fit.BaseSize));
            if (Mathf.Abs(style.fontSize - desired) > .05f) element.style.fontSize = desired;
        }
        internal static void Padding(VisualElement element, float left = 10, float right = 6, float top = 4, float bottom = 6)
        {
            element.style.paddingLeft = left; element.style.paddingRight = right;
            element.style.paddingTop = top; element.style.paddingBottom = bottom;
        }
        internal static void ScaledRect(VisualElement element, UnityEngine.Rect rect, float scale, Vector2 pivot)
        {
            Rect(element, pivot.x + (rect.x - pivot.x) * scale, pivot.y + (rect.y - pivot.y) * scale, rect.width, rect.height);
            element.style.transformOrigin = new TransformOrigin(0, 0);
            element.style.scale = new Scale(new Vector3(scale, scale, 1));
        }

        internal static void Skin(Button button, string normal, string hover)
        {
            FFStyle.Button(button, normal, hover);
        }

        // The legacy camera controls act while held, measured in seconds rather than frames.
        internal static void Hold(Button button, Action<float> advance)
        {
            bool held = false;
            float previous = 0;
            button.schedule.Execute(() => {
                if (!held) return;
                float now = Time.unscaledTime;
                advance(Mathf.Min(now - previous, .1f)); previous = now;
            }).Every(16);
            button.RegisterCallback<PointerDownEvent>(evt => {
                if (evt.button != 0 || !button.enabledInHierarchy) return;
                held = true; previous = Time.unscaledTime; button.CapturePointer(evt.pointerId);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(evt => { held = false; button.ReleasePointer(evt.pointerId); }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerCaptureOutEvent>(_ => held = false);
            button.RegisterCallback<DetachFromPanelEvent>(_ => held = false);
            button.RegisterCallback<NavigationSubmitEvent>(_ => advance(.1f), TrickleDown.TrickleDown);
        }
    }
}
