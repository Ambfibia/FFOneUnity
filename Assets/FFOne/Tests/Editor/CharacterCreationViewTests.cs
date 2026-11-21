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
        [TestCase("generated-name-panel")]
        [TestCase("custom-name-panel")]
        [TestCase("appearance-panel")]
        public void AuthoredPanelsUseUniqueBindingRoles(string panel)
        {
            var element = Tree().Q(className: "creator-bind-" + panel);
            Assert.That(element, Is.Not.Null);
            // Geometry is validated on an attached runtime panel by NativeUiViewTests.
            // UXML inline declarations are stylesheet rules, not element.style setters.
            var roles = element.Query<VisualElement>().ToList()
                .SelectMany(child => child.GetClasses().Where(role => role.StartsWith("creator-bind-", System.StringComparison.Ordinal)))
                .ToArray();
            Assert.That(roles.Distinct().Count(), Is.EqualTo(roles.Length), "Creator bindings must be unique within each panel.");
            Assert.That(element.Q<Label>(className: "creator-bind-creation-error"), Is.Not.Null);
        }
        [Test]
        public void CreatorShellPaintsTheFullscreenButtonAboveTheCurrentPanel()
        {
            var overlay = Tree().Q(className: "creator-bind-character-creation");
            var backdrop = overlay.Q(className: "creator-bind-creation-background");
            var host = overlay.Q(className: "creator-bind-creation-panel-host");
            var fullscreen = overlay.Q<Button>(className: "creator-bind-creator-fullscreen");
            Assert.That(overlay.IndexOf(backdrop), Is.LessThan(overlay.IndexOf(host)));
            Assert.That(overlay.IndexOf(host), Is.LessThan(overlay.IndexOf(fullscreen)));
            Assert.That(backdrop.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(host.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }
        [Test]
        public void NameWheelKeepsSeparateClippedRowsAndCustomNameWireLimit()
        {
            var tree = Tree(); var generated = tree.Q("generated-name-panel");
            for (int part = 0; part < 3; part++)
            {
                var column = generated.Q<TemplateContainer>("name-wheel-" + part);
                var wheel = column.Q("name-rows");
                Assert.That(wheel.ClassListContains("creation-name-wheel"), Is.True);
                for (int row = 0; row < 7; row++)
                {
                    var label = wheel.Q<Label>("name-row-" + row);
                    Assert.That(label, Is.Not.Null);
                }
                Assert.That(column.Q<Button>("name-up"), Is.Not.Null);
                Assert.That(column.Q<Button>("name-down"), Is.Not.Null);
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
                    var swatch = panel.Q<TemplateContainer>(pair.Item1 + "-swatch-" + i);
                    Assert.That(swatch, Is.Not.Null);
                    Assert.That(swatch.Q<Button>("swatch-color"), Is.Not.Null);
                    Assert.That(swatch.Q("swatch-outline"), Is.Not.Null);
                    Assert.That(swatch.Q("swatch-selected"), Is.Not.Null);
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
