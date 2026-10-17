using System.Collections.Generic;
using UnityEngine;

namespace FFOne.Runtime.World
{
    // DongLoader.GetDongColors uses the global 16x16 registration. Retain the
    // records after a tile unload so stream residency cannot change ambience.
    public sealed class WorldTerrainEnvironment : MonoBehaviour
    {
        public Vector2Int Grid;
        public float FogDepth;
        public Color FogColor, SkyColor, LightColor = Color.white;
        public static Transform SamplingTarget;
        struct Entry { public float FogDepth; public Color Fog, Sky, Light; }
        static readonly Dictionary<Vector2Int, Entry> Entries = new();
        static int appliedFrame = -1;
        static readonly int LightId = Shader.PropertyToID("_FFWorldAmbienceLight");
        static readonly int FogId = Shader.PropertyToID("_FFWorldFog");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry() { Entries.Clear(); appliedFrame = -1; SamplingTarget = null; }

        public static void Register(Vector2Int grid, float fogDepth, Color fog, Color sky, Color light)
            => Entries[grid] = new Entry { FogDepth = fogDepth, Fog = fog, Sky = sky, Light = light };

        void Start() => Register(Grid, FogDepth, FogColor, SkyColor, LightColor);
        void LateUpdate()
        {
            if (appliedFrame == Time.frameCount) return;
            var camera = Camera.main;
            if (!camera && !SamplingTarget) return;
            appliedFrame = Time.frameCount;
            Vector3 p = SamplingTarget ? SamplingTarget.position : camera.transform.position;
            float x = p.x / 512f - .5f, y = p.z / 512f - .5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float tx = Mathf.Clamp01((x - ix - .25f) * 2), ty = Mathf.Clamp01((y - iy - .25f) * 2);
            float total = 0, depth = 0; Color fog = Color.clear, sky = Color.clear, light = Color.clear;
            for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
            {
                if (!Entries.TryGetValue(new Vector2Int(ix + dx, iy + dy), out var entry)) continue;
                float weight = (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty);
                total += weight; depth += entry.FogDepth * weight; fog += entry.Fog * weight;
                sky += entry.Sky * weight; light += entry.Light * weight;
            }
            if (total > 0) { depth /= total; fog /= total; sky /= total; light /= total; }
            else { fog = Color.clear; sky = light = Color.white; depth = 0; }
            light = light * .6f + Color.white * .4f;
            float far = camera ? camera.farClipPlane : 340;
            float density = Mathf.Max(Mathf.Max(depth, 0) * .005f, Mathf.Sqrt(-Mathf.Log(.02f)) / far);
            Shader.SetGlobalVector(LightId, light);
            Shader.SetGlobalVector(FogId, new Vector4(fog.r, fog.g, fog.b, density));
            RenderSettings.fog = depth > 0; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = density;
            if (camera) camera.backgroundColor = sky;
        }
    }
}
