using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FFOne.Editor.Integration
{
    [InitializeOnLoad]
    public static class DevelopmentServer
    {
        const string StateKey = "FFOne.Development.ServerState";
        [Serializable] sealed class State
        {
            public long job;
            public int processId, loginPort, shardPort;
            public string log, profiles, endpoint, error, pendingPlayer;
            public bool ready, pendingPlay, stopping;
            public double stopDeadline;
        }
        static State state;
        static bool checking, launchingPlay;
        static double nextPoll;
        public static bool IsRunning => state.job != 0 && DevelopmentProcess.IsRunning(state.job);
        public static bool IsReady => state.ready && IsRunning && !state.stopping;
        public static bool IsStopping => state.stopping;
        public static string Endpoint => state.endpoint ?? DevelopmentSettings.Endpoint;
        public static string ProfilesDirectory => state.profiles ?? DevelopmentSettings.ProfilesDirectory;
        public static string LogPath => state.log ?? Path.Combine(DevelopmentSettings.CacheDirectory, "rustyfusion.log");
        public static string Status => !string.IsNullOrEmpty(state.error) ? state.error : (state.stopping ? "Saving players and stopping RustyFusion..." :
            IsReady ? "Ready — login " + state.endpoint + ", shard 127.0.0.1:" + state.shardPort :
            IsRunning ? "Building / starting RustyFusion..." : "Stopped");

        static DevelopmentServer()
        {
            var saved = SessionState.GetString(StateKey, "");
            state = string.IsNullOrEmpty(saved) ? new State() : JsonUtility.FromJson<State>(saved) ?? new State();
            EditorApplication.update += Update;
            EditorApplication.quitting += Quit;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        static void Save() => SessionState.SetString(StateKey, JsonUtility.ToJson(state));

        [MenuItem("FFOne/Development/Start RustyFusion", false, 20)]
        public static void Start()
        {
            if (IsRunning || state.stopping) return;
            try
            {
                var arguments = DevelopmentSettings.Arguments(DevelopmentSettings.IsolatedDatabase,
                    DevelopmentSettings.LoginPort, DevelopmentSettings.ShardPort);
                var directory = Path.GetFullPath(DevelopmentSettings.ServerDirectory);
                if (!File.Exists(ContentPaths.FilePath(Path.Combine(directory, "Cargo.toml"))) || !File.Exists(ContentPaths.FilePath(Path.Combine(directory, "config.toml"))))
                    throw new DirectoryNotFoundException("Select the RustyFusion directory containing Cargo.toml and config.toml.");
                CheckFreePort(DevelopmentSettings.LoginPort);
                CheckFreePort(DevelopmentSettings.ShardPort);
                Directory.CreateDirectory(ContentPaths.FilePath(DevelopmentSettings.CacheDirectory));
                if (DevelopmentSettings.IsolatedDatabase) Directory.CreateDirectory(ContentPaths.FilePath(DevelopmentSettings.DataDirectory));
                if (state.job != 0) DevelopmentProcess.Close(state.job);
                state = new State {
                    loginPort = DevelopmentSettings.LoginPort, shardPort = DevelopmentSettings.ShardPort,
                    endpoint = DevelopmentSettings.Endpoint, profiles = DevelopmentSettings.ProfilesDirectory,
                    log = Path.Combine(DevelopmentSettings.CacheDirectory, "rustyfusion.log")
                };
                state.job = DevelopmentProcess.Start(DevelopmentSettings.CargoExecutable, arguments, directory,
                    state.log, out state.processId);
                Save();
            }
            catch (Exception error) { state.error = error.Message; state.pendingPlay = false; Save(); Debug.LogError("RustyFusion: " + error.Message); }
        }

        [MenuItem("FFOne/Development/Stop RustyFusion", false, 21)]
        public static void Stop()
        {
            state.pendingPlay = false; state.pendingPlayer = null;
            if (!IsRunning) { FinishStop(); return; }
            if (state.stopping) return;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            state.stopping = true; state.ready = false;
            state.stopDeadline = EditorApplication.timeSinceStartup + 15;
            try { DevelopmentProcess.Interrupt(state.job, state.processId, Path.Combine(DevelopmentSettings.CacheDirectory, "stop-server.ps1")); }
            catch (Exception error) { Debug.LogWarning("Could not send Ctrl+C to RustyFusion: " + error.Message); }
            Save();
        }

        [MenuItem("FFOne/Development/Play with RustyFusion", false, 1)]
        public static void PlayWithServer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || state.stopping) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ContentPaths.FilePath(FrontendProject.ScenePath))) FrontendProject.CreateScene();
            else if (SceneManager.GetActiveScene().path != FrontendProject.ScenePath)
                EditorSceneManager.OpenScene(ContentPaths.Resolve(FrontendProject.ScenePath));
            Start();
            if (!string.IsNullOrEmpty(state.error) || !IsRunning) return;
            state.pendingPlay = true; Save();
        }

        public static void RunClientWhenReady(string executable)
        {
            if (!File.Exists(ContentPaths.FilePath(executable))) throw new FileNotFoundException("Build the client first.", executable);
            Start();
            if (!string.IsNullOrEmpty(state.error) || !IsRunning) return;
            state.pendingPlayer = executable; Save();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && DevelopmentSettings.StartForPlay && !launchingPlay)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += PlayWithServer;
            }
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                launchingPlay = false;
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, null);
                Environment.SetEnvironmentVariable(DevelopmentConnection.ProfilesVariable, null);
            }
        }

        static async void Update()
        {
            if (state.job == 0 || checking || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            if (!IsRunning)
            {
                bool expected = state.stopping;
                FinishStop();
                if (!expected) { state.error = "RustyFusion exited. See the server log for the build or startup error."; Save(); }
                return;
            }
            if (state.stopping)
            {
                if (EditorApplication.timeSinceStartup >= state.stopDeadline)
                {
                    Debug.LogWarning("RustyFusion did not exit after Ctrl+C; closing only this launcher's process job.");
                    FinishStop();
                }
                return;
            }
            if (!state.ready)
            {
                checking = true;
                var pending = state;
                try
                {
                    // Both sockets alone can open before shard registration has completed.
                    var log = ReadLogTail(64 * 1024);
                    if (log.Contains("Connected to login server") &&
                        await CheckListening(pending.loginPort) && await CheckListening(pending.shardPort) &&
                        ReferenceEquals(state, pending) && IsRunning && !state.stopping)
                    { state.ready = true; Save(); }
                }
                catch (Exception error) { Debug.LogWarning("RustyFusion readiness: " + error.Message); }
                finally { checking = false; }
            }
            if (state.pendingPlay && IsReady && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                state.pendingPlay = false; Save();
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, state.endpoint);
                Environment.SetEnvironmentVariable(DevelopmentConnection.ProfilesVariable, state.profiles);
                launchingPlay = true;
                EditorApplication.EnterPlaymode();
            }
            if (!string.IsNullOrEmpty(state.pendingPlayer) && IsReady)
            {
                string executable = state.pendingPlayer;
                state.pendingPlayer = null; Save();
                try
                {
                    var start = new ProcessStartInfo {
                        FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable), UseShellExecute = false,
                        Arguments = "-logFile " + DevelopmentProcess.Quote(Path.Combine(DevelopmentSettings.CacheDirectory, "player.log"))
                    };
                    start.EnvironmentVariables[DevelopmentConnection.ServerVariable] = state.endpoint;
                    start.EnvironmentVariables[DevelopmentConnection.ProfilesVariable] = state.profiles;
                    Process.Start(start)?.Dispose();
                }
                catch (Exception error) { state.error = "Client launch failed: " + error.Message; Save(); Debug.LogError(state.error); }
            }
        }

        public static string ReadLogTail(int maximumBytes = 16000)
        {
            if (!File.Exists(ContentPaths.FilePath(LogPath))) return "";
            try
            {
                using var file = new FileStream(ContentPaths.FilePath(LogPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                file.Position = Math.Max(0, file.Length - maximumBytes);
                using var reader = new StreamReader(file, Encoding.UTF8);
                return Regex.Replace(reader.ReadToEnd(), "\u001b\\[[0-9;]*m", "");
            }
            catch (IOException) { return "Server log is being opened..."; }
        }

        static void CheckFreePort(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.ExclusiveAddressUse = true;
            try { listener.Start(); }
            catch (SocketException) { throw new InvalidOperationException("Port " + port + " is already in use. Select different development ports or stop the existing server."); }
            finally { listener.Stop(); }
        }
        static async Task<bool> CheckListening(int port)
        {
            using var socket = new TcpClient();
            try
            {
                var connect = socket.ConnectAsync(IPAddress.Loopback, port);
                if (await Task.WhenAny(connect, Task.Delay(300)) != connect) return false;
                await connect;
                return socket.Connected;
            }
            catch (SocketException) { return false; }
        }
        static void FinishStop()
        {
            DevelopmentProcess.Close(state.job);
            state.job = 0; state.ready = state.stopping = state.pendingPlay = false;
            state.error = state.pendingPlayer = null; Save();
        }
        static void Quit()
        {
            if (!IsRunning) { FinishStop(); return; }
            Stop();
            var wait = Stopwatch.StartNew();
            while (IsRunning && wait.ElapsedMilliseconds < 3000) Thread.Sleep(40);
            FinishStop();
        }
    }
}
