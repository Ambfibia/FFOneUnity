using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class CharacterCreationViewTests
    {
        const string Path = "Assets/FFOne/Content/UI/Views/CharacterCreation.uxml";
        VisualElement Tree()
        {
            var view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Path);
            Assert.That(view, Is.Not.Null);
            return view.CloneTree();
        }
        [TestCase("generated-name-panel", 672, 365)]
        [TestCase("custom-name-panel", 672, 365)]
        [TestCase("appearance-panel", 1020, 638)]
        public void AuthoredPanelsPreserveLogicalCanvasAndUniqueBindingNames(string panel, float width, float height)
        {
            var element = Tree().Q(panel);
            Assert.That(element, Is.Not.Null);
            // Geometry is validated on an attached runtime panel by NativeUiViewTests.
            // UXML inline declarations are stylesheet rules, not element.style setters.
            var names = element.Query<VisualElement>().ToList().Where(e => !string.IsNullOrEmpty(e.name)).Select(e => e.name).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "Named controller bindings must remain unambiguous within each panel.");
            Assert.That(element.Q<Label>("creation-error"), Is.Not.Null);
        }
        [Test]
        public void NameWheelKeepsSeparateClippedRowsAndCustomNameWireLimit()
        {
            var tree = Tree(); var generated = tree.Q("generated-name-panel");
            for (int part = 0; part < 3; part++)
            {
                var wheel = generated.Q("name-wheel-" + part);
                Assert.That(wheel.ClassListContains("creation-name-wheel"), Is.True);
                for (int row = 0; row < 7; row++)
                {
                    var label = wheel.Q<Label>("name-" + part + "-" + row);
                    Assert.That(label, Is.Not.Null);
                }
                Assert.That(generated.Q<Button>("name-up-" + part), Is.Not.Null);
                Assert.That(generated.Q<Button>("name-down-" + part), Is.Not.Null);
            }
            Assert.That(tree.Q("custom-name-panel").Q<TextField>("custom-character-name").maxLength, Is.EqualTo(25));
        }
        [Test]
        public void AppearanceHasAllPaletteSlotsAndNonInteractiveSelectedClothes()
        {
            var panel = Tree().Q("appearance-panel");
            foreach (var pair in new[] { ("skin", 12), ("hair", 18), ("eye", 5) })
                for (int i = 0; i < pair.Item2; i++)
                {
                    Assert.That(panel.Q<Button>(pair.Item1 + "-color-slot-" + i), Is.Not.Null);
                    Assert.That(panel.Q(pair.Item1 + "-outline-slot-" + i), Is.Not.Null);
                    Assert.That(panel.Q(pair.Item1 + "-selected-slot-" + i), Is.Not.Null);
                }
            foreach (var category in new[] { "shirt", "pants", "shoes" })
                for (int i = 0; i < 5; i++)
                {
                    var item = panel.Q(category + "-item-" + i);
                    Assert.That(item, Is.Not.Null);
                    Assert.That(item is Button, Is.EqualTo(i != 2));
                }
            Assert.That(panel.Q<Image>("creation-avatar"), Is.Not.Null, "The character preview remains a dynamic render target.");
            Assert.That(panel.Q<Button>("save-character"), Is.Not.Null);
        }
    }
}
