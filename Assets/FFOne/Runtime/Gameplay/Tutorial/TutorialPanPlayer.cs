using System;
using UnityEngine;

namespace FFOne.Tutorial
{
    /// <summary>The eight-image Planet Fusion pan in cntutorialscript's BasicCombatC scene.</summary>
    public sealed class TutorialPanPlayer : IDisposable
    {
        public const int FrameCount = 8;
        public const float PanSeconds = 23;
        readonly Texture2D[] textures = new Texture2D[FrameCount];
        public bool Loaded { get; private set; }
        public bool Active { get; private set; }
        public float ElapsedSeconds { get; private set; }

        public void Preload()
        {
            // Keep the accepted native versions of panels 06/07 and the remaining primary panels.
            // Retain handles between replays; logical Release must not evict a shared resource.
            for (int i = 0; i < FrameCount; i++)
            {
                if (!textures[i]) textures[i] = FFOne.Content.ContentAssets.Load<Texture2D>("ui/en/gameplay/intro/tutorialpan_0" + (i + 1));
                if (!textures[i]) throw new InvalidOperationException("Missing tutorial pan image " + (i + 1));
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
        public void Stop() => Active = false;
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
        public void Render(Rect viewport)
        {
            if (!Active || !Loaded || viewport.width <= 0 || viewport.height <= 0) return;
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.BeginGroup(viewport);
            try
            {
                Rect local = new Rect(0, 0, viewport.width, viewport.height);
                for (int i = 0; i < FrameCount; i++)
                    GUI.DrawTexture(FrameRect(local, i, ElapsedSeconds), textures[i], ScaleMode.StretchToFill, true);
            }
            finally { GUI.EndGroup(); GUI.color = previous; }
        }

        public void Dispose()
        {
            Release();
            Array.Clear(textures, 0, textures.Length);
        }
    }
}
