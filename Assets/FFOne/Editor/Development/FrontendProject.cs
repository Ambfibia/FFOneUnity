using ContentPaths = FFOne.Content.ContentPaths;
using System.IO;
using FFOne.Creation;
using FFOne.Integration;
using FFOne.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace FFOne.Editor
{
    public static class FrontendProject
    {
        public const string ScenePath = "Assets/FFOne/Scenes/Bootstrap/Main.unity";
        const string PanelPath = "Assets/UI/MainPanel.asset";

        [MenuItem("FFOne/Open Client")]
        public static void OpenClient()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ContentPaths.FilePath(ScenePath))) EditorSceneManager.OpenScene(ContentPaths.Resolve(ScenePath));
            else CreateScene();
        }

        // Called explicitly after content import; never runs automatically over an edited scene.
        public static void CreateScene()
        {
            if (File.Exists(ContentPaths.FilePath(ScenePath))) throw new IOException("Client scene already exists; use FFOne/Open Client.");
            Directory.CreateDirectory(ContentPaths.FilePath("Assets/Scenes"));
            Directory.CreateDirectory(ContentPaths.FilePath("Assets/UI"));
            AssetDatabase.Refresh();
            FFOne.Editor.Integration.ClientPresentationSetup.Ensure();
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(ContentPaths.Resolve(PanelPath));
            if (!panel)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.name = "MainPanel";
                panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                panel.referenceResolution = new Vector2Int(1920, 1080);
                panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match = .5f;
                AssetDatabase.CreateAsset(panel, ContentPaths.Resolve(PanelPath));
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = Color.black;
            camera.GetComponent<Camera>().cullingMask = ~(1 << 31);
            var light = new GameObject("Directional Light", typeof(Light));
            light.transform.rotation = Quaternion.LookRotation(new Vector3(.89370085f, -.30901706f, -.32528046f));
            light.GetComponent<Light>().type = LightType.Directional;
            light.GetComponent<Light>().intensity = 1;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            var client = new GameObject("FFOne Client");
            var document = client.AddComponent<UIDocument>();
            client.AddComponent<FrontendController>();
            client.AddComponent<PreviewStage>();
            client.AddComponent<CharacterCreationController>();
            document.panelSettings = panel;
            EditorUtility.SetDirty(document);
            EditorSceneManager.SaveScene(scene, ContentPaths.Resolve(ScenePath));
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "FFOne";
            PlayerSettings.companyName = "FFOne";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
        }
    }
}
