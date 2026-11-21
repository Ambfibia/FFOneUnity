using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    internal enum UiLayer { Hud, Window, Notification, Guide, Dialog, Settings, Cinematic, Loading, Status }

    // UI Toolkit paints siblings in hierarchy order. Reserve the order before any async
    // content can attach, including late server messages and resize/reopen paths.
    internal sealed class UiLayers
    {
        static readonly ConditionalWeakTable<VisualElement, UiLayers> Layouts = new();
        readonly VisualElement[] hosts = new VisualElement[(int)UiLayer.Status + 1];

        UiLayers(VisualElement viewport)
        {
            for (int i = 0; i < hosts.Length; i++)
            {
                var layer = (UiLayer)i;
                var host = new VisualElement { name = "ui-layer-" + layer.ToString().ToLowerInvariant(), pickingMode = PickingMode.Ignore };
                host.StretchToParentSize();
                viewport.Add(host);
                hosts[i] = host;
            }
        }

        internal static UiLayers For(VisualElement viewport) => Layouts.GetValue(viewport, root => new UiLayers(root));
        internal VisualElement Host(UiLayer layer) => hosts[(int)layer];
        internal void Add(VisualElement view, UiLayer layer) => Host(layer).Add(view);
    }
}
