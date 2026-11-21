using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        public static Camera ViewOverride;
        public static bool TutorialScope;
        struct Entry { public float FogDepth; public Color Fog, Sky, Light; }
        static readonly Dictionary<Vector2Int, Entry> Entries = new();
        static readonly Dictionary<int, Vector4> CameraFogRestore = new();
        static int appliedFrame = -1;
        static int enabledEnvironments;
        static Camera fogCamera;
        static Vector4 frameFog;
        static readonly int LightId = Shader.PropertyToID("_FFWorldAmbienceLight");
        static readonly int FogId = Shader.PropertyToID("_FFWorldFog");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegistry()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Entries.Clear(); CameraFogRestore.Clear(); enabledEnvironments = 0;
            appliedFrame = -1; fogCamera = null; frameFog = Vector4.zero;
            SamplingTarget = null; ViewOverride = null; TutorialScope = false;
        }

        public static void Register(Vector2Int grid, float fogDepth, Color fog, Color sky, Color light)
            => Entries[grid] = new Entry { FogDepth = fogDepth, Fog = fog, Sky = sky, Light = light };

        void OnEnable()
        {
            if (enabledEnvironments++ != 0) return;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }
        void OnDisable()
        {
            if (--enabledEnvironments > 0) return;
            enabledEnvironments = 0;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
        }
        static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!fogCamera) return;
            // Portraits and other render-texture cameras share the character
            // shaders, but do not belong to cnPlayerCamera's world fog volume.
            CameraFogRestore[camera.GetInstanceID()] = Shader.GetGlobalVector(FogId);
            var value = frameFog;
            if (camera != fogCamera) value.w = 0;
            Shader.SetGlobalVector(FogId, value);
        }
        static void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!CameraFogRestore.Remove(camera.GetInstanceID(), out var previous)) return;
            Shader.SetGlobalVector(FogId, previous);
        }

        void Start() => Register(Grid, FogDepth, FogColor, SkyColor, LightColor);
        void LateUpdate()
        {
            if (appliedFrame == Time.frameCount) return;
            var camera = ViewOverride ? ViewOverride : Camera.main;
            if (!camera && !SamplingTarget) return;
            appliedFrame = Time.frameCount;
            // cnPlayerCamera.LateUpdate passes its own transform to
            // DongLoader.GetPositionColor, including while a pan leaves the avatar.
            // The streaming anchor is only a fallback for a camera-less consumer.
            Vector3 p = camera ? camera.transform.position : SamplingTarget.position;
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
            // DefaultAmbience sets RenderSettings fog before the tutorial-only
            // blend; only the returned color used for the skybox receives it.
            Color skyTint = TutorialScope ? fog * .5f + Color.white * .5f : fog;
            skyTint.a = .85f;
            float far = camera ? camera.farClipPlane : 340;
            // Keep the fixed-function fog enable flag in the shader uniform too.
            // An unfogged dong must not receive the far-plane density floor.
            float density = depth > 0 ? Mathf.Max(depth * .005f, Mathf.Sqrt(-Mathf.Log(.02f)) / far) : 0;
            fogCamera = camera; frameFog = new Vector4(fog.r, fog.g, fog.b, density);
            Shader.SetGlobalVector(LightId, light);
            Shader.SetGlobalVector(FogId, frameFog);
            RenderSettings.fog = depth > 0; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = density;
            if (camera) camera.backgroundColor = sky;
            var skybox = camera ? camera.GetComponent<Skybox>() : null;
            if (skybox && skybox.material) skybox.material.SetColor("_Tint", skyTint);
        }
    }
}
