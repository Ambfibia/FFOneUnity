using System;
using FFOne.Content;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Loads an authored screen without losing styles attached to its template container.</summary>
    public static class FFView
    {
        public static VisualElement Instantiate(string address, string rootName)
        {
            var asset = ContentAssets.Load<VisualTreeAsset>(address);
            if (!asset) throw new InvalidOperationException("Missing UI view: " + address);
            var tree = asset.Instantiate();
            var root = Require<VisualElement>(tree, rootName);
            for (int i = 0; i < tree.styleSheets.count; i++)
                if (!root.styleSheets.Contains(tree.styleSheets[i])) root.styleSheets.Add(tree.styleSheets[i]);
            root.RemoveFromHierarchy();
            return root;
        }

        public static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"UI view '{root.name}' requires {typeof(T).Name} '{name}'.");
            return element;
        }
    }
}
