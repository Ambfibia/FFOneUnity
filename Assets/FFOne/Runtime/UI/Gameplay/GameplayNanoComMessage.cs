using System;
using FFOne.Content;
using UnityEngine;
using UnityEngine.UIElements;
using static FFOne.UI.FFWidgets;

namespace FFOne.UI
{
    [UxmlElement]
    public sealed partial class NanoComMessageCard : VisualElement
    {
        internal Label Body { get; private set; }

        internal void Bind(string title, string body, string frame, string icon, Action click)
        {
            var heading = this.Q<Label>(className: "nanocom-message-heading");
            heading.text = FFStyle.Caption(title);
            heading.enableRichText = false;
            AutoFit(heading);
            Body = this.Q<Label>(className: "nanocom-message-body");
            Body.text = body;
            Body.enableRichText = false;
            this.Q<Image>(className: "nanocom-message-icon").image = Texture(icon);
            if (frame == GameplayNanoComMessage.InvitationFrame)
            {
                this.Q(className: "nanocom-message-frame-npc").style.display = DisplayStyle.None;
                this.Q(className: "nanocom-message-frame-invitation").style.display = DisplayStyle.Flex;
            }
            var open = this.Q<Button>(className: "nanocom-message-open");
            if (click != null)
            {
                open.style.display = DisplayStyle.Flex;
                open.clicked += click;
            }
        }
    }

    // cnGUINanocom compact message frame as retained by FFOneClient's NanoCom presentation.
    internal static class GameplayNanoComMessage
    {
        const string Art = "ui/en/gameplay/nanocom/";
        internal const string NpcFrame = Art + "nanocom_message_npc";
        internal const string InvitationFrame = Art + "nanocom_message_etc";
        internal const float NpcLifetime = 10;

        internal static NanoComMessageCard Create(VisualElement parent, string title, string body, string frame, string icon, Action click = null)
        {
            var asset = ContentAssets.Load<VisualTreeAsset>("ui/views/Retrobution/NanoCom");
            if (!asset) throw new InvalidOperationException("Missing NanoCom UXML.");
            var tree = asset.Instantiate();
            var card = tree.Q<NanoComMessageCard>() ?? throw new InvalidOperationException("Missing NanoCom message card.");
            for (int i = 0; i < tree.styleSheets.count; i++)
                card.styleSheets.Add(tree.styleSheets[i]);
            card.Bind(title, body, frame, icon, click);
            card.RemoveFromHierarchy();
            parent.Add(card);
            return card;
        }

        internal static void Place(VisualElement card, VisualElement viewport, float age)
        {
            float width = viewport.resolvedStyle.width, height = viewport.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float scale = FrontendLayout.UiScale(height);
            float remaining = Mathf.Clamp01(1 - age * 2);
            float reveal = Mathf.Pow(Mathf.Sin(remaining * remaining * Mathf.PI / 2), 2);
            ScaledRect(card, new Rect(width - 494 + 372 * reveal, 0, 372 * (1 - reveal), 122), scale, new Vector2(width, 0));
        }
    }
}
