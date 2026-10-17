using System;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class GameplayWindowHostTests
    {
        [Test]
        public void UnsupportedOrFailedOpenPreservesTheCurrentWindow()
        {
            var root = new VisualElement();
            using var host = new GameplayWindowHost(root);
            host.Register(GameplayWindow.NanoCom, () => new VisualElement { name = "nanocom" });
            host.Register(GameplayWindow.Inventory, () => throw new InvalidOperationException("Missing binding"));
            Assert.That(host.Open(GameplayWindow.NanoCom), Is.True);
            var original = host.View;
            Assert.That(host.Open(GameplayWindow.Trade), Is.False);
            Assert.Throws<InvalidOperationException>(() => host.Open(GameplayWindow.Inventory));
            Assert.That(host.View, Is.SameAs(original));
            Assert.That(original.parent, Is.SameAs(root));
            Assert.That(host.Current, Is.EqualTo(GameplayWindow.NanoCom));
        }

        [Test]
        public void WindowTransitionsHaveOneOwnerAndReleaseTheirInputGate()
        {
            var root = new VisualElement();
            using var host = new GameplayWindowHost(root);
            int changes = 0;
            host.Changed += () => changes++;
            foreach (var window in new[] { GameplayWindow.NanoCom, GameplayWindow.Journal })
                host.Register(window, () => new VisualElement());
            host.Open(GameplayWindow.NanoCom);
            var first = host.View;
            host.Open(GameplayWindow.NanoCom);
            Assert.That(changes, Is.EqualTo(1));
            host.Open(GameplayWindow.Journal);
            Assert.That(first.parent, Is.Null);
            Assert.That(root.childCount, Is.EqualTo(1));
            host.Toggle(GameplayWindow.Journal);
            Assert.That(host.IsOpen, Is.False);
            Assert.That(root.childCount, Is.Zero);
            Assert.That(changes, Is.EqualTo(3));
            host.Dispose();
            Assert.That(host.Open(GameplayWindow.NanoCom), Is.False);
        }
    }
}
