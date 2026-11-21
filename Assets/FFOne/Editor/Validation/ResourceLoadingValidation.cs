using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    public static class ResourceLoadingValidation
    {
        const string Status = "Logs/ResourceLoading/build.txt";

        [MenuItem("FFOne/Validation/Build Optimized Content And Client")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before building the client.");
            var previousScene = SceneManager.GetActiveScene();
            if (previousScene.isDirty)
                throw new InvalidOperationException("Save the active scene before building the client.");
            string previousPath = previousScene.path;
            Directory.CreateDirectory(Path.GetDirectoryName(Status));
            try
            {
                File.WriteAllText(Status, "Building Addressables");
                ProjectConfigData.GenerateBuildLayout = true;
                ProjectConfigData.BuildLayoutReportFileFormat = ProjectConfigData.ReportFileFormat.JSON;
                NativeUiValidationBuild.BuildContent();
                File.WriteAllText(Status, "Building Windows player");
                NativeUiValidationBuild.BuildWithCurrentContent();
                File.WriteAllText(Status, "PASSED");
                Debug.Log("RESOURCE LOADING BUILD PASSED");
            }
            catch (Exception error)
            {
                File.WriteAllText(Status, "FAILED\n" + error);
                Debug.LogException(error);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath)) EditorSceneManager.OpenScene(previousPath);
            }
        }
    }
}
