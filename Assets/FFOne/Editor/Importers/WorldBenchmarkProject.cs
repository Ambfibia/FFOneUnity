using ContentPaths = FFOne.Content.ContentPaths;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FFOne.Editor
{
    public static class WorldBenchmarkProject
    {
        public const string Scene = "Assets/FFOne/Scenes/Development/NativeWorld.unity";
        [MenuItem("FFOne/World/Prepare Comparable Renderer")]
        public static void PrepareRenderer()
        {
            const string folder = "Assets/Resources/World";
            var catalog = AssetDatabase.LoadAssetAtPath<WorldCatalog>(ContentPaths.Resolve(folder + "/Catalog.asset"));
            if (!catalog) throw new IOException("The native world catalog is missing.");
            var waterTiles = new List<WorldWaterTile>();
            foreach (var tile in catalog.Tiles)
            {
                var scenePath = NativeContentImport.ResolveSafe(NativeContentImport.DefaultSource, "map/tiles/" + tile.Id + "/scene.json");
                var scene = JObject.Parse(File.ReadAllText(ContentPaths.FilePath(scenePath)));
                tile.SourceVisuals = ((JArray)scene["visuals"]).Count;
                if (tile.SourceVisuals < tile.Visuals) throw new IOException("World source placement count is smaller than imported count: " + tile.Id);
                var models = scene["models"].ToDictionary(m => (string)m["id"]);
                var waters = new List<WorldPlacement>();
                foreach (var visual in scene["visuals"])
                {
                    var model = models[(string)visual["model"]];
                    string path = (string)model["path"], root = (string)model["rootName"];
                    if (root != "ffWater" && root != "ffPoison") continue;
                    string stem = root.ToLowerInvariant();
                    if (!Regex.IsMatch(path, "^objects/unclassified/ffpoison/models/" + stem + "(?:_variant_[0-9]{4})?/visual\\.glb$")) continue;
                    var pose = visual["transform"]; var p = pose["translation"]; var q = pose["rotation"]; var s = pose["scale"];
                    waters.Add(new WorldPlacement { Name = (string)visual["name"], ModelResource = path.Substring(0, path.Length - 4),
                        Position = new Vector3(-(float)p[0], (float)p[1], (float)p[2]),
                        Rotation = new Quaternion((float)q[0], -(float)q[1], -(float)q[2], (float)q[3]),
                        Scale = new Vector3((float)s[0], (float)s[1], (float)s[2]) });
                }
                if (waters.Count > 0) waterTiles.Add(new WorldWaterTile { Id = tile.Id, X = tile.X, Z = tile.Z, Visuals = waters.ToArray() });
            }
            catalog.NeighborWater = waterTiles.ToArray();
            EditorUtility.SetDirty(catalog);
            var sourcePipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (!sourcePipeline) throw new IOException("An active URP asset is required.");
            var sourceSettings = new SerializedObject(sourcePipeline);
            var rendererList = sourceSettings.FindProperty("m_RendererDataList");
            var sourceRenderer = rendererList.GetArrayElementAtIndex(sourceSettings.FindProperty("m_DefaultRendererIndex").intValue).objectReferenceValue as UniversalRendererData;
            if (!sourceRenderer) throw new IOException("The URP renderer is missing.");
            string rendererPath = folder + "/Renderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ContentPaths.Resolve(rendererPath));
            if (!renderer) { renderer = Object.Instantiate(sourceRenderer); renderer.name = "Native World Forward Renderer"; AssetDatabase.CreateAsset(renderer, ContentPaths.Resolve(rendererPath)); }
            renderer.rendererFeatures.RemoveAll(feature => !feature || !(feature is RenderObjects));
            var settings = new SerializedObject(renderer);
            settings.FindProperty("m_RenderingMode").intValue = 0;
            settings.FindProperty("m_UseNativeRenderPass").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(renderer);
            string pipelinePath = folder + "/RenderPipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ContentPaths.Resolve(pipelinePath));
            if (!pipeline) { pipeline = Object.Instantiate(sourcePipeline); pipeline.name = "Native World Comparable Pipeline"; AssetDatabase.CreateAsset(pipeline, ContentPaths.Resolve(pipelinePath)); }
            settings = new SerializedObject(pipeline);
            rendererList = settings.FindProperty("m_RendererDataList"); rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            settings.FindProperty("m_DefaultRendererIndex").intValue = 0;
            settings.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.msaaSampleCount = 1; pipeline.renderScale = 1; pipeline.supportsHDR = false;
            pipeline.supportsCameraDepthTexture = false; pipeline.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
        }
        [MenuItem("FFOne/World/Open Native World")]
        public static void Open()
        {
            if (File.Exists(ContentPaths.FilePath(Scene))) { EditorSceneManager.OpenScene(ContentPaths.Resolve(Scene)); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("World Camera", typeof(Camera), typeof(AudioListener)); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView = 45; camera.nearClipPlane = .1f; camera.farClipPlane = 340;
            camera.allowMSAA = false; camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var lightObject = new GameObject("World Sun", typeof(Light)); lightObject.transform.rotation = Quaternion.Euler(90, 0, 0);
            var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1; light.shadows = LightShadows.None;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .2f;
            var world = new GameObject("Native World", typeof(WorldStreamer), typeof(WorldViewer), typeof(WorldBenchmark));
            world.GetComponent<WorldStreamer>().View = camera;
            world.GetComponent<WorldStreamer>().Anchor = new Vector3(6374.3188f, -56.4746f, 668.2039f);
            Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(Scene)!)); EditorSceneManager.SaveScene(scene, ContentPaths.Resolve(Scene));
        }
        [MenuItem("FFOne/World/Build World Comparison Player")]
        public static void Build()
        {
            PrepareRenderer();
            if (!File.Exists(ContentPaths.FilePath(Scene))) Open();
            string path = "Builds/WorldBenchmark/Player/FFOneWorld.exe";
            Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(path)!));
            // Include Vulkan so both engines can use the same stable backend;
            // the bundled D3D12 runtime can crash during player shutdown.
            var previousApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            bool previousDefaultApis = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
            UnityEditor.Build.Reporting.BuildReport result;
            try
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
                result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, locationPathName = path,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.CompressWithLz4 | BuildOptions.StrictMode });
            }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, previousApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, previousDefaultApis);
            }
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new IOException("World player build failed: " + result.summary.result);
            Debug.Log("Native world standalone ready: " + Path.GetFullPath(path));
        }
    }
}
