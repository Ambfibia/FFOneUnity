using System;
using System.Linq;
using FFOne.Content;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    /// <summary>Lazy, GUID-based browser of published prefabs. Source meshes are not cards.</summary>
    public sealed class ContentLibraryWindow : EditorWindow
    {
        ContentPaths.Entry[] entries=Array.Empty<ContentPaths.Entry>(), filtered=Array.Empty<ContentPaths.Entry>();
        string query="";
        int category, page;
        bool technical;
        Vector2 scroll;
        static readonly string[] Categories={"All","Environment","Characters","Vehicles","Interactables","VFX"};
        const int PageSize=48;
        [MenuItem("FFOne/Content/Asset Browser",priority=0)]
        public static void Open() => GetWindow<ContentLibraryWindow>("Asset Browser");
        void OnEnable()
        {
            var imported=ContentPaths.Entries.Where(e=>!string.IsNullOrEmpty(e.guid)).ToDictionary(e=>e.guid);
            entries=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/FFOne/Content"})
                .Select(guid=>new ContentPaths.Entry { guid=guid,path=AssetDatabase.GUIDToAssetPath(guid),old=imported.TryGetValue(guid,out var entry) ? entry.old : "" })
                .OrderBy(e=>e.path,StringComparer.Ordinal).ToArray();
            Filter();
        }
        void Filter()
        {
            filtered=entries.Where(e=>(category==0 || e.path.StartsWith("Assets/FFOne/Content/"+Categories[category]+"/",StringComparison.Ordinal)) &&
                (technical || !(e.path.Contains("/Technical/") || e.old.Contains("/collision/"))) &&
                (string.IsNullOrWhiteSpace(query) || e.path.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 || e.old.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            page=0;
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Published game objects",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag a prefab into the scene. Imported objects still require gameplay and animation acceptance. Technical parts are hidden by default. The Environment Library contains assembled environment cards and manual classification.",MessageType.Info);
            EditorGUI.BeginChangeCheck();
            using(new EditorGUILayout.HorizontalScope())
            {
                query=EditorGUILayout.TextField(query,EditorStyles.toolbarSearchField);
                category=EditorGUILayout.Popup(category,Categories,GUILayout.Width(125));
                technical=GUILayout.Toggle(technical,"Technical parts",GUILayout.Width(115));
            }
            if(EditorGUI.EndChangeCheck())Filter();
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(filtered.Length+" prefabs");
                if(GUILayout.Button("Environment Library"))EnvironmentLibraryWindow.Open();
                if(GUILayout.Button("World Editor"))EnvironmentWorldWorkspaceWindow.Open();
            }
            int pages=Math.Max(1,(filtered.Length+PageSize-1)/PageSize);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(page==0))if(GUILayout.Button("Previous"))page--;
                GUILayout.Label((page+1)+" / "+pages,GUILayout.Width(90));
                using(new EditorGUI.DisabledScope(page+1>=pages))if(GUILayout.Button("Next"))page++;
            }
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var entry in filtered.Skip(page*PageSize).Take(PageSize))
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
                using(new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    var preview=prefab ? AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab) : null;
                    Rect image=GUILayoutUtility.GetRect(64,64,GUILayout.Width(64));
                    if(preview)GUI.DrawTexture(image,preview,ScaleMode.ScaleToFit);
                    string owner=entry.path.Replace("Assets/FFOne/Content/","").Replace("/Prefabs/"," / ");
                    using(new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(owner,EditorStyles.wordWrappedLabel);
                        GUILayout.Label(entry.path.Contains("NeedsClassification") ? "Needs classification" : string.IsNullOrEmpty(entry.old) ? "Authored prefab" : "Imported — acceptance pending",EditorStyles.miniLabel);
                        if(GUILayout.Button("Inspect",GUILayout.Width(80))){ Selection.activeObject=prefab; EditorGUIUtility.PingObject(prefab); }
                    }
                    if(prefab && Event.current.type==EventType.MouseDrag && image.Contains(Event.current.mousePosition))
                    { DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences=new UnityEngine.Object[]{prefab}; DragAndDrop.StartDrag(prefab.name); Event.current.Use(); }
                }
            }
            EditorGUILayout.EndScrollView();
            if(AssetPreview.IsLoadingAssetPreviews())Repaint();
        }
    }
}
