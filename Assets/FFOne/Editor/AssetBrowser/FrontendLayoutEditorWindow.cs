using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using System.Text;
using FFOne.Content;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class FrontendLayoutEditorWindow : EditorWindow
    {
        const string AssetPath = "Assets/Resources/ui/layout-overrides.json";
        static readonly string[] Names = {
            "login-panel", "username", "password", "login-submit", "login-language", "registration-help",
            "selection-group", "avatar-group", "character-slot-1", "character-slot-2", "character-slot-3", "character-slot-4",
            "selected-avatar-preview", "selection-title", "create-character", "delete-character", "enter-world", "music-toggle", "quit", "fullscreen"
        };
        LayoutOverrides document;
        LayoutOverride current = new();
        int selected;
        string original, status = "";
        [MenuItem("FFOne/Frontend Layout")]
        static void Open() => GetWindow<FrontendLayoutEditorWindow>("FFOne Layout");
        void OnEnable() { Load(); Select(0); }
        void Load()
        {
            original = File.Exists(ContentPaths.FilePath(AssetPath)) ? File.ReadAllText(ContentPaths.FilePath(AssetPath)) : null;
            document = original != null ? JsonUtility.FromJson<LayoutOverrides>(original) : new LayoutOverrides();
        }
        void Select(int index)
        {
            selected = index;
            var row = document.elements.FirstOrDefault(e => e.name == Names[index]);
            current = row == null ? new LayoutOverride { name = Names[index] } : JsonUtility.FromJson<LayoutOverride>(JsonUtility.ToJson(row));
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Login / character selection", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Offsets are in UI Toolkit pixels relative to recovered original layout. Width/height -1 preserves the original size. Saved overrides are read by the running screen.", MessageType.Info);
            int index = EditorGUILayout.Popup("Element", selected, Names);
            if (index != selected) Select(index);
            current.x = EditorGUILayout.FloatField("Horizontal offset", current.x);
            current.y = EditorGUILayout.FloatField("Vertical offset", current.y);
            current.width = EditorGUILayout.FloatField("Width (-1 original)", current.width);
            current.height = EditorGUILayout.FloatField("Height (-1 original)", current.height);
            if (GUILayout.Button("Save and preview")) Save(false);
            if (GUILayout.Button("Reset this element to original")) Save(true);
            if (GUILayout.Button("Select editable stylesheet")) Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ContentPaths.Resolve("Assets/Resources/ui/styles/Frontend.uss"));
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        }
        void Save(bool remove)
        {
            try
            {
                var now = File.Exists(ContentPaths.FilePath(AssetPath)) ? File.ReadAllText(ContentPaths.FilePath(AssetPath)) : null;
                if (now != original) throw new IOException("Layout changed outside this window; reopen it before saving.");
                if (!float.IsFinite(current.x) || !float.IsFinite(current.y) || !float.IsFinite(current.width) || !float.IsFinite(current.height)) throw new InvalidDataException("Geometry must contain finite numbers.");
                var rows = document.elements.Where(e => e.name != current.name).ToList();
                if (!remove) rows.Add(current);
                document.elements = rows.ToArray();
                original = JsonUtility.ToJson(document, true) + "\n";
                Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(AssetPath)!));
                File.WriteAllText(ContentPaths.FilePath(AssetPath), original, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ContentPaths.Resolve(AssetPath)); FrontendLayoutOverrides.Reload();
                var client = UnityEngine.Object.FindAnyObjectByType<FFOne.UI.FrontendController>();
                if (client != null && Application.isPlaying) client.RefreshLayout();
                Select(selected); status = "Saved. The next layout pass applies this override.";
            }
            catch (Exception exception) { status = exception.Message; }
        }
    }
}
