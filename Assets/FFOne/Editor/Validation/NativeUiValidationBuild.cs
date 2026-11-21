using System;
using System.IO;
using FFOne.Editor.Integration;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Code-only validation after a successful full content build, preserving the project build policy.</summary>
    public static class NativeUiValidationBuild
    {
        public static void PrepareTravelContent()
        {
            PlayerAnimatorSetup.Rebuild();
            ContentLayoutSetup.Rebuild();
        }
        public static void BuildWithUpdatedContent()
        {
            BuildContent();
            BuildWithCurrentContent();
        }
        public static void BuildNativeVfxPublication()
        {
            BuildContent();
            BuildWithCurrentContentAt(Path.GetFullPath("Builds/NativeVfxPublication/Windows/FFOne.exe"));
        }
        public static void BuildContent()
        {
            ContentLayoutSetup.Rebuild();
            ContentLayoutSetup.Validate();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect();
            AddressableAssetSettings.BuildPlayerContent(out var content);
            if (!string.IsNullOrEmpty(content.Error)) throw new InvalidOperationException("Addressables build failed: " + content.Error);
            Debug.Log("NATIVE GAMEPLAY CONTENT BUILD SUCCEEDED");
        }
        public static void BuildWithCurrentContent()
            => BuildWithCurrentContentAt(Path.Combine(Path.GetFullPath(
                Environment.GetEnvironmentVariable("FFONE_VALIDATION_BUILD_DIR") ?? DevelopmentSettings.BuildDirectory), "FFOne.exe"));

        public static void BuildNativeTutorialTimeline()
            => BuildWithCurrentContentAt(Path.GetFullPath("Builds/NativeTutorialTimeline/Windows/FFOne.exe"));

        public static void BuildNativeGameplayInput()
            => BuildWithCurrentContentAt(Path.GetFullPath("Builds/NativeGameplayInput/Windows/FFOne.exe"));

        public static void BuildNativeTutorialOverlay()
            => BuildWithCurrentContentAt(Path.GetFullPath("Builds/NativeTutorialOverlay/Windows/FFOne.exe"));

        public static void BuildMusicTransitions()
            => BuildWithCurrentContentAt(Path.GetFullPath("Builds/MusicTransitions/Windows/FFOne.exe"));

        static void BuildWithCurrentContentAt(string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            const string content = "Library/com.unity.addressables/aa/Windows/settings.json";
            if (!File.Exists(content)) throw new InvalidOperationException("Build the Windows Addressables content before code-only validation.");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings) throw new InvalidOperationException("Addressables settings are missing.");
            var previous = settings.BuildAddressablesWithPlayerBuild;
            try
            {
                settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { FrontendProject.ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.AllowDebugging
                });
                if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("UI validation player build failed: " + report.summary.result);
                Debug.Log("NATIVE UI VALIDATION BUILD SUCCEEDED: " + output);
            }
            finally
            {
                settings.BuildAddressablesWithPlayerBuild = previous;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            }
        }
    }
}
