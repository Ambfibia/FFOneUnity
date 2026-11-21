using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public enum GameplayWindow { NanoCom, Inventory, Journal, Map, Chat, Trade, Nanos, Contacts, Mail, Help, Service, Travel, Mentor, CashMall, Race, NpcServices, NpcMissions, Barber }

    /// <summary>Owns the lifetime and focus of the active gameplay window. Views own their layout.</summary>
    public sealed class GameplayWindowHost : IDisposable
    {
        readonly VisualElement parent;
        readonly Func<GameplayWindow, bool> available;
        readonly Dictionary<GameplayWindow, Func<VisualElement>> factories = new();
        VisualElement previousFocus;
        bool disposed;
        public GameplayWindow? Current { get; private set; }
        public VisualElement View { get; private set; }
        public bool IsOpen => View != null;
        public event Action Changed;

        public GameplayWindowHost(VisualElement parent, Func<GameplayWindow, bool> available = null)
        {
            this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
            this.available = available;
        }

        public void Register(GameplayWindow window, Func<VisualElement> factory)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameplayWindowHost));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            factories.Add(window, factory);
        }

        public bool Supports(GameplayWindow window) => !disposed && factories.ContainsKey(window) && (available?.Invoke(window) ?? true);

        public bool Open(GameplayWindow window)
        {
            if (!Supports(window)) return false;
            if (Current == window) return true;
            return Show(window);
        }

        public void Refresh()
        {
            if (Current.HasValue) Show(Current.Value);
        }

        bool Show(GameplayWindow window)
        {
            // Prepare first: a missing asset/binding must not destroy the window already open.
            var next = factories[window]();
            if (next == null) throw new InvalidOperationException("Gameplay view factory returned no view: " + window);
            if (next.parent != null) throw new InvalidOperationException("Gameplay views must be detached before opening.");
            if (!IsOpen) previousFocus = parent.panel?.focusController.focusedElement as VisualElement;
            View?.RemoveFromHierarchy();
            Current = window; View = next;
            parent.Add(next);
            // Opening a window must not select the first tab's focused artwork.
            // Put keyboard focus on the window; navigation selects a control explicitly.
            next.focusable = true;
            next.Focus();
            Changed?.Invoke();
            return true;
        }

        public bool Toggle(GameplayWindow window)
        {
            if (Current == window) { Close(); return true; }
            return Open(window);
        }

        public bool Close()
        {
            if (!IsOpen) return false;
            View.RemoveFromHierarchy(); View = null; Current = null;
            if (previousFocus?.panel == parent.panel && previousFocus?.enabledInHierarchy == true) previousFocus.Focus();
            previousFocus = null;
            Changed?.Invoke();
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            Close(); factories.Clear(); Changed = null; disposed = true;
        }
    }
}
