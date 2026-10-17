using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    [CustomEditor(typeof(NativeDataDocument))]
    public sealed class NativeDataDocumentEditor : UnityEditor.Editor
    {
        int selected, page;
        int editing = -1;
        string editValue;
        string search = "", error;
        readonly Stack<int> parents = new();
        void OnEnable() => Undo.undoRedoPerformed += RefreshAfterUndo;
        void OnDisable() => Undo.undoRedoPerformed -= RefreshAfterUndo;
        void RefreshAfterUndo() { editing = -1; Repaint(); }
        public override void OnInspectorGUI()
        {
            var document = (NativeDataDocument)target;
            EditorGUILayout.LabelField(document.Identity, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField($"{document.Nodes.Length:N0} typed values • ordered keys/rows • precise numeric IDs");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export JSON…"))
                {
                    string path = EditorUtility.SaveFilePanel("Export exact native data", "", Path.GetFileName(document.Identity), "json");
                    if (!string.IsNullOrEmpty(path))
                        try { File.WriteAllText(ContentPaths.FilePath(path), document.ToJson()); error = null; }
                        catch (Exception failure) { error = failure.Message; }
                }
                if (GUILayout.Button("Import JSON…"))
                {
                    string path = EditorUtility.OpenFilePanel("Import data into this asset (GUID retained)", "", "json");
                    if (!string.IsNullOrEmpty(path))
                        try
                        {
                            Undo.RecordObject(document, "Import native data"); document.Import(File.ReadAllText(ContentPaths.FilePath(path)));
                            EditorUtility.SetDirty(document); AssetDatabase.SaveAssetIfDirty(document);
                            selected = page = 0; editing = -1; parents.Clear(); search = ""; error = null; LocalizationCatalog.Reload();
                        }
                        catch (Exception failure) { error = failure.Message; }
                }
            }
            if (document.Nodes.Length == 0) return;
            selected = Mathf.Clamp(selected, 0, document.Nodes.Length - 1);
            string query = EditorGUILayout.TextField("Find key / value", search);
            if (query != search) { search = query; page = 0; }
            if (parents.Count > 0 && GUILayout.Button("← Parent")) { selected = parents.Pop(); page = 0; }
            var current = document.Nodes[selected];
            EditorGUILayout.LabelField(selected == 0 ? "Root" : string.IsNullOrEmpty(current.Key) ? "Array item / empty key" : current.Key, current.Kind.ToString());
            if (current.Kind != NativeDataDocument.ValueKind.Object && current.Kind != NativeDataDocument.ValueKind.Array)
            {
                if (editing != selected) { editing = selected; editValue = current.Value ?? "null"; }
                editValue = EditorGUILayout.TextArea(editValue, GUILayout.MinHeight(70));
                string value = editValue;
                if (value != (current.Value ?? "null") && GUILayout.Button("Apply value (Undo supported)"))
                {
                    try
                    {
                        var check = CreateInstance<NativeDataDocument>();
                        try { check.Import(current.Kind == NativeDataDocument.ValueKind.String ? Newtonsoft.Json.JsonConvert.ToString(value) : value);
                            if (check.Nodes.Length != 1 || check.Nodes[0].Kind != current.Kind) throw new InvalidOperationException("Keep the existing field type."); }
                        finally { DestroyImmediate(check); }
                        Undo.RecordObject(document, "Edit native data"); current.Value = value; document.Nodes[selected] = current;
                        EditorUtility.SetDirty(document); AssetDatabase.SaveAssetIfDirty(document); LocalizationCatalog.Reload(); error = null;
                    }
                    catch (Exception failure) { error = failure.Message; }
                }
                var catalog = FFOne.Content.ContentAssets.Load<NativeContentCatalog>("NativeContent");
                var link = current.Kind == NativeDataDocument.ValueKind.String && catalog ? catalog.Resolve(document.Identity, current.Value) : null;
                if (link != null && GUILayout.Button("Select linked Unity asset: " + link.Identity))
                    Selection.activeObject = catalog.Load(link.Identity);
            }
            else
            {
                IEnumerable<int> rows = string.IsNullOrEmpty(search) ? document.Children(selected) : Enumerable.Range(0, document.Nodes.Length)
                    .Where(i => (document.Nodes[i].Key ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) || (document.Nodes[i].Value ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
                var matches = rows.Skip(page * 100).Take(101).ToArray();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("Previous 100")) page--;
                    EditorGUILayout.LabelField("Page " + (page + 1));
                    using (new EditorGUI.DisabledScope(matches.Length <= 100)) if (GUILayout.Button("Next 100")) page++;
                }
                int ordinal = page * 100;
                foreach (int index in matches.Take(100))
                {
                    var row = document.Nodes[index];
                    string summary = row.Kind == NativeDataDocument.ValueKind.Object || row.Kind == NativeDataDocument.ValueKind.Array ? row.Kind.ToString() : row.Kind == NativeDataDocument.ValueKind.Null ? "null" : row.Value ?? "";
                    summary = summary.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                    if (summary.Length > 100) summary = summary.Substring(0, 100) + "…";
                    string label = string.IsNullOrEmpty(search) && current.Kind == NativeDataDocument.ValueKind.Array ? "[" + ordinal + "]" : string.IsNullOrEmpty(row.Key) ? "\"\" / node " + index : row.Key;
                    ordinal++;
                    if (GUILayout.Button(label + " : " + summary, EditorStyles.miniButton))
                    { parents.Push(selected); selected = index; page = 0; search = ""; GUI.FocusControl(null); }
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }

    public sealed class NativeContentBrowser : EditorWindow
    {
        string search = "";
        int page, kind;
        static readonly string[] Kinds = { "All", "Model", "Data", "Texture", "Audio", "Font", "Binary", "Shader source" };
        Vector2 scroll;
        [MenuItem("FFOne/Content/All Native Assets and Data")]
        static void Open() => GetWindow<NativeContentBrowser>("Native content");
        void OnGUI()
        {
            var catalog = FFOne.Content.ContentAssets.Load<NativeContentCatalog>("NativeContent");
            if (!catalog) { EditorGUILayout.LabelField("Publish the complete content set first."); return; }
            EditorGUILayout.LabelField($"{catalog.Entries.Length:N0} source identities — assets load only when selected");
            string query = EditorGUILayout.TextField("Path / name", search);
            int filter = EditorGUILayout.Popup("Type", kind, Kinds);
            if (query != search || filter != kind) { search = query; kind = filter; page = 0; scroll = Vector2.zero; }
            var matches = catalog.Entries.Concat(catalog.Supplements).Where(e => (kind == 0 || (int)e.Kind == kind - 1) && e.Identity.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
            int pages = Mathf.Max(1, (matches.Length + 199) / 200); page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("Previous 200")) { page--; scroll = Vector2.zero; }
                EditorGUILayout.LabelField($"{matches.Length:N0} matches • {page + 1}/{pages}");
                using (new EditorGUI.DisabledScope(page + 1 >= pages)) if (GUILayout.Button("Next 200")) { page++; scroll = Vector2.zero; }
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var entry in matches.Skip(page * 200).Take(200))
                if (GUILayout.Button(entry.Kind + "  " + entry.Identity, EditorStyles.label)) Selection.activeObject = catalog.Load(entry.Identity);
            EditorGUILayout.EndScrollView();
        }
    }
}
