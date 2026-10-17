using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Content
{
    [Serializable] public sealed class LayoutOverride
    {
        public string name;
        public float x, y, width = -1, height = -1;
    }
    [Serializable] public sealed class LayoutOverrides
    {
        public string schema = "ffone.unity.frontend-layout.v1";
        public LayoutOverride[] elements = Array.Empty<LayoutOverride>();
    }
    public static class FrontendLayoutOverrides
    {
        static LayoutOverrides cached;
        sealed class OriginalSize { public StyleLength Width, Height; }
        static readonly ConditionalWeakTable<VisualElement, OriginalSize> OriginalSizes = new();
        static readonly HashSet<string> PreviouslyTranslated = new();
        public static void Reload() => cached = null;
        public static void Apply(VisualElement root)
        {
            if (cached == null)
            {
                var source = FFOne.Content.ContentAssets.Load<TextAsset>("ui/layout-overrides");
                cached = source ? JsonUtility.FromJson<LayoutOverrides>(source.text) : new LayoutOverrides();
            }
            if (cached.schema != "ffone.unity.frontend-layout.v1") return;
            foreach (var name in PreviouslyTranslated)
            {
                var previous = root.Q(name);
                if (previous != null)
                {
                    previous.style.translate = new Translate(0, 0);
                    if (OriginalSizes.TryGetValue(previous, out var original))
                    { previous.style.width = original.Width; previous.style.height = original.Height; }
                }
            }
            foreach (var entry in cached.elements)
            {
                var element = root.Q(entry.name);
                if (element == null) continue;
                OriginalSizes.GetValue(element, e => new OriginalSize { Width = e.style.width, Height = e.style.height });
                element.style.translate = new Translate(entry.x, entry.y);
                PreviouslyTranslated.Add(entry.name);
                if (entry.width >= 0) element.style.width = entry.width;
                if (entry.height >= 0) element.style.height = entry.height;
            }
        }
    }
}
