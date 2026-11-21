using FFOne.Editor.Integration;
using FFOne.UI;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    [InitializeOnLoad]
    public static class FrontendRecoveryValidation
    {
        const string Pending = "FFOne.FrontendRecovery.Pending";
        static FrontendRecoveryValidation() => EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Frontend recovery validation").AddComponent<FrontendRecoverySmoke>().Port = DevelopmentSettings.LoginPort;
        };
        [MenuItem("FFOne/Validation/Run Frontend Recovery")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play before starting frontend recovery validation.");
            if (!DevelopmentSettings.IsolatedDatabase) throw new System.InvalidOperationException("Frontend recovery validation requires the isolated development database.");
            SessionState.SetBool(Pending, true);
            DevelopmentServer.PlayWithServer();
        }
        [MenuItem("FFOne/Validation/Build Recovered Client")]
        public static void Build() => NativeUiValidationBuild.BuildWithUpdatedContent();
    }
}
