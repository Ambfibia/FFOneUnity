using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FFOne.Editor.Integration
{
    public sealed class DevelopmentWindow : EditorWindow
    {
        Vector2 scroll;
        string log = "";
        double nextRefresh;

        [MenuItem("FFOne/Development/Open Controls", false, 0)]
        public static void Open() => GetWindow<DevelopmentWindow>("FFOne Development");

        void OnEnable() { minSize = new Vector2(540, 520); EditorApplication.update += Refresh; }
        void OnDisable() => EditorApplication.update -= Refresh;
        void Refresh()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + 1;
            log = DevelopmentServer.ReadLogTail(); Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("RustyFusion + FFOne", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(DevelopmentServer.Status, DevelopmentServer.IsReady ? MessageType.Info : MessageType.None);
            using (new EditorGUI.DisabledScope(DevelopmentServer.IsRunning || DevelopmentServer.IsStopping))
            {
                DevelopmentSettings.ServerDirectory = EditorGUILayout.TextField("RustyFusion directory", DevelopmentSettings.ServerDirectory);
                DevelopmentSettings.CargoExecutable = EditorGUILayout.TextField("Cargo executable", DevelopmentSettings.CargoExecutable);
                DevelopmentSettings.IsolatedDatabase = EditorGUILayout.Toggle("Isolated development database", DevelopmentSettings.IsolatedDatabase);
                DevelopmentSettings.LoginPort = EditorGUILayout.IntField("Local login port", DevelopmentSettings.LoginPort);
                DevelopmentSettings.ShardPort = EditorGUILayout.IntField("Local shard port", DevelopmentSettings.ShardPort);
            }
            EditorGUILayout.HelpBox(DevelopmentSettings.IsolatedDatabase
                ? "Development accounts and characters are saved in RustyFusion/target/ffone-unity/database.db. The local server accepts password login and creates accounts on first login."
                : "Uses the database configured in RustyFusion/config.toml. Playing changes those accounts and characters. Local password login is enabled for this process.",
                DevelopmentSettings.IsolatedDatabase ? MessageType.Info : MessageType.Warning);
            DevelopmentSettings.StartForPlay = EditorGUILayout.Toggle("Start server on ordinary Play", DevelopmentSettings.StartForPlay);
            EditorGUILayout.LabelField("Stopping Play leaves the server running. Stop Server saves players and exits.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(DevelopmentServer.IsRunning || DevelopmentServer.IsStopping))
                    if (GUILayout.Button("Start Server")) DevelopmentServer.Start();
                using (new EditorGUI.DisabledScope(!DevelopmentServer.IsRunning || DevelopmentServer.IsStopping))
                    if (GUILayout.Button("Stop Server")) DevelopmentServer.Stop();
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || DevelopmentServer.IsStopping))
                    if (GUILayout.Button("Play with Server")) DevelopmentServer.PlayWithServer();
            }
            EditorGUILayout.Space();
            DevelopmentSettings.BuildDirectory = EditorGUILayout.TextField("Windows build directory", DevelopmentSettings.BuildDirectory);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build Client")) BuildClient();
                if (GUILayout.Button("Build && Run with Server")) BuildAndRun();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Server log", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!File.Exists(ContentPaths.FilePath(DevelopmentServer.LogPath))))
                    if (GUILayout.Button("Open Log", GUILayout.Width(90))) EditorUtility.OpenWithDefaultApp(DevelopmentServer.LogPath);
                if (GUILayout.Button("Copy Address", GUILayout.Width(110))) EditorGUIUtility.systemCopyBuffer = DevelopmentServer.Endpoint;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        [MenuItem("FFOne/Development/Build Windows Client", false, 40)]
        public static void BuildClient() => Build(false);

        [MenuItem("FFOne/Development/Build and Run with RustyFusion", false, 41)]
        public static void BuildAndRun() => Build(true);

        static void Build(bool run)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
                    throw new InvalidOperationException("Stop Play mode and wait for compilation before building.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                    throw new InvalidOperationException("Install Windows Build Support for this Unity Editor in Unity Hub.");
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!File.Exists(ContentPaths.FilePath(FrontendProject.ScenePath))) FrontendProject.CreateScene();
                string directory = Path.GetFullPath(DevelopmentSettings.BuildDirectory);
                string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
                if ((directory + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.OrdinalIgnoreCase) ||
                    directory.Equals(DevelopmentSettings.ProjectDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose a build folder outside Assets and distinct from the project root.");
                Directory.CreateDirectory(ContentPaths.FilePath(directory));
                string executable = Path.Combine(directory, "FFOne.exe");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { FrontendProject.ScenePath }, locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.AllowDebugging
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Client build " + report.summary.result + ". See the Unity Console.");
                Debug.Log("FFOne development client built: " + executable);
                if (run) DevelopmentServer.RunClientWhenReady(executable);
            }
            catch (Exception error) { Debug.LogError("FFOne development build: " + error.Message); }
        }
    }
}
