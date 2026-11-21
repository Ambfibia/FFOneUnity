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
            return Instantiate(address, tree => Require<VisualElement>(tree, rootName));
        }

        internal static VisualElement InstantiateClass(string address, string role) =>
            Instantiate(address, tree => RequireClass<VisualElement>(tree, role));

        internal static VisualElement OptionalClass(string address, string role) =>
            Instantiate(address, tree => tree.Q<VisualElement>(className: role));

        static VisualElement Instantiate(string address, Func<TemplateContainer, VisualElement> select)
        {
            var asset = ContentAssets.Load<VisualTreeAsset>(address);
            if (!asset) throw new InvalidOperationException("Missing UI view: " + address);
            var tree = asset.Instantiate();
            var root = select(tree);
            if (root == null) return null;
            for (int i = 0; i < tree.styleSheets.count; i++)
                if (!root.styleSheets.Contains(tree.styleSheets[i])) root.styleSheets.Add(tree.styleSheets[i]);
            root.RemoveFromHierarchy();
            return root;
        }

        internal static T RequireClass<T>(VisualElement root, string role) where T : VisualElement
        {
            return root.Q<T>(className: role) ??
                throw new InvalidOperationException($"UI view '{root.name}' requires {typeof(T).Name} with class '{role}'.");
        }

        public static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            var element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"UI view '{root.name}' requires {typeof(T).Name} '{name}'.");
            return element;
        }
    }
}
