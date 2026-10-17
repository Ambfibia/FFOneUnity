using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FFOne.Editor
{
    public static class ContentLayoutAcceptance
    {
        public static void Prepare()
        {
            ContentLayoutSetup.ValidateSources();
            ContentLayoutSetup.Rebuild();
            ContentLayoutSetup.Validate();
        }
        public static void BuildWindows()
        {
            ContentLayoutSetup.Rebuild();
            ContentLayoutSetup.Validate();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect();
            UnityEditor.AddressableAssets.Settings.ProjectConfigData.GenerateBuildLayout=true;
            UnityEditor.AddressableAssets.Settings.ProjectConfigData.BuildLayoutReportFileFormat=UnityEditor.AddressableAssets.Settings.ProjectConfigData.ReportFileFormat.JSON;
            AddressableAssetSettings.BuildPlayerContent(out var content);
            if(!string.IsNullOrEmpty(content.Error))throw new InvalidOperationException("Addressables build failed: "+content.Error);
            EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect();
            BuildPlayerOnly();
        }
        public static void BuildPlayerOnly()
        {
            if(!Directory.Exists(UnityEngine.AddressableAssets.Addressables.BuildPath))throw new InvalidOperationException("Build local Addressables content first.");
            var settings=AddressableAssetSettingsDefaultObject.Settings;
            var previous=settings.BuildAddressablesWithPlayerBuild;
            try
            {
                settings.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                Directory.CreateDirectory("Builds/ContentLayout/Windows");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=new[]{"Assets/FFOne/Scenes/Bootstrap/Main.unity"},
                    target=BuildTarget.StandaloneWindows64,
                    locationPathName="Builds/ContentLayout/Windows/FFOne.exe", options=BuildOptions.Development });
                File.WriteAllText("Logs/ContentLayoutBuildResult.json",JsonUtility.ToJson(new Result {
                    result=report.summary.result.ToString(), bytes=report.summary.totalSize,
                    errors=report.summary.totalErrors, warnings=report.summary.totalWarnings },true));
                if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Windows build failed: "+report.summary.result);
                Debug.Log("CONTENT LAYOUT WINDOWS BUILD SUCCEEDED: "+report.summary.totalSize+" bytes.");
            }
            finally { settings.BuildAddressablesWithPlayerBuild=previous;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets(); }
        }
        public static void OpenWorkspace()
        {
            FrontendProject.OpenClient();
            ContentLibraryWindow.Open();
        }
        [Serializable] sealed class Result { public string result;public ulong bytes;public int errors,warnings; }
    }
}
