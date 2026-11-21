using System;
using FFOne.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tutorial
{
    /// <summary>The eight-image Planet Fusion pan in cntutorialscript's BasicCombatC scene.</summary>
    public sealed class TutorialPanPlayer : IDisposable
    {
        public const int FrameCount = 8;
        public const float PanSeconds = 23;
        readonly Texture2D[] textures = new Texture2D[FrameCount];
        readonly Image[] panels = new Image[FrameCount];
        internal VisualElement View { get; }
        public bool Loaded { get; private set; }
        public bool Active { get; private set; }
        public float ElapsedSeconds { get; private set; }

        public TutorialPanPlayer()
        {
            View = new VisualElement { name = "tutorial-pan", pickingMode = PickingMode.Ignore };
            View.style.overflow = Overflow.Hidden;
            View.style.display = DisplayStyle.None;
            for (int i = 0; i < panels.Length; i++)
                panels[i] = FFWidgets.Image(View, "tutorial-pan-panel-" + i, null, 0, 0, 0, 0);
        }

        public void Preload()
        {
            // Keep the accepted native versions of panels 06/07 and the remaining primary panels.
            // Retain handles between replays; logical Release must not evict a shared resource.
            for (int i = 0; i < FrameCount; i++)
            {
                if (!textures[i]) textures[i] = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/intro/tutorialpan_0" + (i + 1));
                if (!textures[i]) throw new InvalidOperationException("Missing tutorial pan image " + (i + 1));
                panels[i].image = textures[i];
            }
            Loaded = true;
        }

        public void Start()
        {
            if (!Loaded) Preload();
            ElapsedSeconds = 0;
            Active = true;
        }

        public void Begin() => Start();
        public void Stop()
        {
            Active = false;
            View.style.display = DisplayStyle.None;
        }
        public void Release() { Stop(); Loaded = false; }

        public void Tick(float deltaSeconds)
        {
            if (Active && float.IsFinite(deltaSeconds)) ElapsedSeconds += Mathf.Max(0, deltaSeconds);
        }

        public static Rect FrameRect(Rect viewport, int index, float elapsedSeconds)
        {
            if (index < 0 || index >= FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
            float offset = (viewport.height * FrameCount - viewport.width) * (elapsedSeconds / PanSeconds);
            return new Rect(viewport.x + index * viewport.height - offset, viewport.y, viewport.height, viewport.height);
        }

        // Source draws the strip after cinematic bars, then draws fade and subtitles over it.
        internal void Present(float width, float height)
        {
            bool visible = Active && Loaded && width > 0 && height > 0;
            View.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            var viewport = new Rect(0, 0, width, height);
            FFWidgets.Rect(View, viewport);
            for (int i = 0; i < panels.Length; i++)
                FFWidgets.Rect(panels[i], FrameRect(viewport, i, ElapsedSeconds));
        }

        public void Dispose()
        {
            Release();
            View.RemoveFromHierarchy();
            foreach (var panel in panels) panel.image = null;
            Array.Clear(textures, 0, textures.Length);
        }
    }
}
