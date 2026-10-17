using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public static class RetrobutionImage
    {
        // Unity 6000.4's Image importer does not bind the obsolete image UXML attribute.
        // Inline USS asset URLs retain GUID dependencies. Promote that reference to Image
        // before applying source UV cropping, removing the otherwise duplicated background.
        public static Texture Bind(Image image)
        {
            if (image.image) return image.image;
            var texture = image.style.backgroundImage.value.texture;
            if (!texture) texture = image.resolvedStyle.backgroundImage.texture;
            if (!texture) return null;
            image.image = texture;
            image.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            return texture;
        }
        public static void BindAll(VisualElement root) => root.Query<Image>().ForEach(image => Bind(image));
    }
}
