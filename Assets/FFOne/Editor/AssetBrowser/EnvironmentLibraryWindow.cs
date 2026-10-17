using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class EnvironmentLibraryWindow : EditorWindow
    {
        EnvironmentLibrary library;
        string search = "", category = "Все категории";
        bool technical, showParts;
        int page;
        Vector2 scroll;
        EnvironmentLibrary.Entry selected;
        const int PageSize = 36;
        [MenuItem("FFOne/Environment/Environment Library")]
        public static void Open() { var window = GetWindow<EnvironmentLibraryWindow>("Окружение"); window.minSize = new Vector2(740, 500); }
        void OnEnable() { library = EnvironmentLibraryBuilder.Load(); }
        void OnInspectorUpdate() { if (EnvironmentLibraryBuilder.IsRunning) Repaint(); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Библиотека окружения", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Растительность, архитектура, мебель, транспорт и декор. Персонажи размещаются через систему NPC/XDT.", EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                search = GUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
                var categories = new[] { "Все категории" }.Concat(EnvironmentAssetRules.Categories).ToArray();
                int index = Math.Max(0, Array.IndexOf(categories, category));
                category = categories[EditorGUILayout.Popup(index, categories, GUILayout.Width(235))];
                technical = GUILayout.Toggle(technical, "Технические", EditorStyles.toolbarButton, GUILayout.Width(95));
                showParts = GUILayout.Toggle(showParts, "Детали моделей", EditorStyles.toolbarButton, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck()) page = 0;
                using (new EditorGUI.DisabledScope(EnvironmentLibraryBuilder.IsRunning))
                    if (GUILayout.Button("Обновить", EditorStyles.toolbarButton, GUILayout.Width(80))) { EnvironmentLibraryBuilder.Build(); library = EnvironmentLibraryBuilder.Load(); }
            }
            if (!library) { EditorGUILayout.HelpBox("Нажмите «Обновить», чтобы построить каталог опубликованного окружения.", MessageType.Info); return; }
            var partIdentities = library.CompoundEntries.SelectMany(e => e.PartIdentities).ToHashSet();
            var filtered = library.CompoundEntries.Concat(library.Entries.Where(e => showParts || !partIdentities.Contains(e.Identity))).Where(e => (category == "Все категории" || e.Category == category) &&
                (technical || category != "Все категории" || !e.Category.StartsWith("Technical/")) &&
                (string.IsNullOrWhiteSpace(search) || (e.Name + " " + e.Family + " " + e.Identity).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            var cards = filtered.GroupBy(e => string.IsNullOrEmpty(e.CardKey) ? e.Identity : e.CardKey).ToArray();
            int pages = Math.Max(1, (cards.Length + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{cards.Length} карточек · {filtered.Length} исходных записей · страница {page + 1}/{pages}");
                using (new EditorGUI.DisabledScope(page == 0)) if (GUILayout.Button("←", GUILayout.Width(35))) page--;
                using (new EditorGUI.DisabledScope(page == pages - 1)) if (GUILayout.Button("→", GUILayout.Width(35))) page++;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                var visible = cards.Skip(page * PageSize).Take(PageSize).ToArray();
                int columns = Math.Max(2, (int)((position.width - 280) / 155));
                for (int i = 0; i < visible.Length; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    for (int j = i; j < Math.Min(i + columns, visible.Length); j++)
                    {
                        var variants = visible[j].ToArray();
                        var entry = variants.FirstOrDefault(e => e == selected) ?? variants[0];
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(string.IsNullOrEmpty(entry.AssemblyGuid) ? entry.PrefabGuid : entry.AssemblyGuid))));
                        using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(148)))
                        {
                            var texture = prefab ? AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab) : null;
                            if (GUILayout.Button(new GUIContent(texture, entry.Name), GUILayout.Width(138), GUILayout.Height(100))) selected = entry;
                            var rect = GUILayoutUtility.GetLastRect();
                            if (prefab && Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition))
                            { DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new UnityEngine.Object[] { prefab }; DragAndDrop.StartDrag(entry.Name); Event.current.Use(); }
                            GUILayout.Label(entry.Name, EditorStyles.wordWrappedMiniLabel, GUILayout.Width(138), GUILayout.Height(40));
                            GUILayout.Label(variants.Select(e => string.IsNullOrEmpty(e.RenderKey) ? e.Identity : e.RenderKey).Distinct().Count() + " вариантов", EditorStyles.miniLabel);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
                using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(250))) Details();
            }
            if (EnvironmentLibraryBuilder.Status != "") EditorGUILayout.LabelField(EnvironmentLibraryBuilder.Status, EditorStyles.miniLabel);
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }
        void Details()
        {
            if (selected == null) { GUILayout.Label("Выберите объект для просмотра и перетащите его в сцену.", EditorStyles.wordWrappedLabel); return; }
            var members = library.Entries.Concat(library.CompoundEntries).Where(e => (string.IsNullOrEmpty(e.CardKey) ? e.Identity : e.CardKey) == (string.IsNullOrEmpty(selected.CardKey) ? selected.Identity : selected.CardKey)).ToArray();
            var variants = members.GroupBy(e => string.IsNullOrEmpty(e.RenderKey) ? e.Identity : e.RenderKey).Select(g => g.Contains(selected) ? selected : g.First()).ToArray();
            int current = Math.Max(0, Array.IndexOf(variants, selected));
            int chosen = EditorGUILayout.Popup("Вариант", current, variants.Select((e, i) => (i + 1) + ". " + e.Identity.Split('/').Reverse().Skip(1).First()).ToArray());
            if (chosen != current) selected = variants[chosen];
            GUILayout.Label(selected.Name, EditorStyles.wordWrappedLabel);
            GUILayout.Label(members.Length + " исходных записей · " + variants.Length + " вариантов", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Label("Семейство: " + selected.Family);
            GUILayout.Label("Исходных размещений: " + selected.SourcePlacements);
            GUILayout.Label(selected.ManuallyClassified ? "Категория задана вручную" : "Категория предложена по исходному имени", EditorStyles.wordWrappedMiniLabel);
            int index = Math.Max(0, Array.IndexOf(EnvironmentAssetRules.Categories, selected.Category));
            int next = EditorGUILayout.Popup(index, EnvironmentAssetRules.Categories);
            if (next != index)
            {
                Undo.RecordObject(library, "Classify environment asset"); selected.Category = EnvironmentAssetRules.Categories[next]; selected.ManuallyClassified = true;
                EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
            }
            string path = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(string.IsNullOrEmpty(selected.AssemblyGuid) ? selected.PrefabGuid : selected.AssemblyGuid));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
            if (GUILayout.Button("Открыть prefab")) AssetDatabase.OpenAsset(prefab);
            if (GUILayout.Button("Показать в Project")) { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); }
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(selected.AssemblyStatus) ? "Визуальная модель. Сборка для расстановки ещё не создана." : selected.AssemblyStatus, MessageType.Info);
            EditorGUILayout.HelpBox("Меши и материалы общие. Изменение общего ресурса применяется ко всем использующим его вариантам.", MessageType.Info);
            if (!selected.Identity.StartsWith("assemblies/") && GUILayout.Button("Собрать prefab для расстановки")) { EnvironmentAssemblyBuilder.Build(selected); EnvironmentLibraryGrouping.Rebuild(library); EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); }
            var environment = prefab ? prefab.GetComponent<FFOne.World.EnvironmentObject>() : null;
            if (environment && environment.OriginalStates.Length > 0) GUILayout.Label("Исходные состояния: " + string.Join(", ", environment.OriginalStates), EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(EnvironmentLibraryBuilder.IsRunning))
                if (GUILayout.Button("Открыть общую библиотеку")) ContentLibraryWindow.Open();
        }
    }
}
