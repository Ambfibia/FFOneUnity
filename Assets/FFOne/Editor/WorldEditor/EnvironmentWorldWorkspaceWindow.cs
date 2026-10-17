using ContentPaths = FFOne.Content.ContentPaths;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class EnvironmentWorldWorkspaceWindow : EditorWindow
    {
        string[] tiles;
        int selected;
        Vector2 scroll;
        public static void Open() => GetWindow<EnvironmentWorldWorkspaceWindow>("Участки мира");
        void OnEnable() { tiles = ContentPaths.GetDirectories("Assets/Resources/World/Tiles").Select(Path.GetFileName).OrderBy(x => x).ToArray(); string active=Path.GetFileNameWithoutExtension(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path); selected=System.Array.IndexOf(tiles,active);if(selected<0)selected=System.Math.Max(0,System.Array.IndexOf(tiles,"map_12_01")); minSize=new Vector2(620,500); }
        void OnInspectorUpdate() { if (TerrainVegetationImport.IsRunning) Repaint(); }
        void OnGUI()
        {
            if (tiles == null || tiles.Length == 0) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Редактируемые сцены мира", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Сохранённая сцена задаёт расстановку. Повторное открытие и импорт не пересоздают её. Зелёные ячейки подключены к игровому стримингу через Addressables. NPC и серверные ID остаются в сетевой системе.", MessageType.Info);
            var catalog=AssetDatabase.LoadAssetAtPath<FFOne.World.WorldCatalog>(EnvironmentWorldWorkspace.WorldPath);
            if(catalog)
            for(int z=15;z>=0;z--)
            using(new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(z.ToString("00"),GUILayout.Width(24));
                for(int x=0;x<16;x++)
                {
                    var cell=catalog.Tiles.FirstOrDefault(t=>t.X==x && t.Z==z);
                    Color previous=GUI.backgroundColor;
                    if(cell!=null && !string.IsNullOrEmpty(cell.SceneAddress))GUI.backgroundColor=Color.green;
                    using(new EditorGUI.DisabledScope(cell==null))
                        if(GUILayout.Button(cell==null ? "–" : x.ToString("00"),GUILayout.Width(30),GUILayout.Height(20)))selected=System.Array.IndexOf(tiles,cell.Id);
                    GUI.backgroundColor=previous;
                }
            }
            selected = EditorGUILayout.Popup("Ячейка", selected, tiles);
            string tile = tiles[selected];
            var definition = JObject.Parse(File.ReadAllText(ContentPaths.FilePath("Assets/Resources/map/tiles/" + tile + "/terrain/terrain.json")));
            string reason = TerrainVegetationImport.Incompatibility(definition);
            if (reason != null) EditorGUILayout.HelpBox(reason + " Сохраняется исходный меш рельефа и растительности.", MessageType.Warning);
            else EditorGUILayout.HelpBox("Terrain Details использует исходные клетки плотности. Отдельные травинки внутри клеток распределяет Unity.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EnvironmentLibraryBuilder.IsRunning || TerrainVegetationImport.IsRunning))
            {
                if (GUILayout.Button("Открыть ячейку для редактирования")) EnvironmentWorldWorkspace.Open(tile);
                if (GUILayout.Button("Открыть ячейку и 8 соседей")) EnvironmentWorldWorkspace.OpenNeighborhood(tile);
                if (GUILayout.Button("Подключить сохранённую сцену к игре")) { EnvironmentWorldWorkspace.Publish(tile); ContentLayoutSetup.Rebuild(); }
                if (GUILayout.Button("Отчёт о стыках рельефа")) WorldSeamReviewWindow.Open();
                if (GUILayout.Button("Открыть весь мир")) { if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(WholeWorldMigration.DemoPath); }
                if (GUILayout.Button("Открыть демонстрацию района")) WorldDistrictAcceptance.OpenDemo();
                using (new EditorGUI.DisabledScope(reason != null))
                    if (GUILayout.Button("Подготовить Terrain Details этой ячейки")) TerrainVegetationImport.UpgradeTile(tile);
                if (GUILayout.Button("Подготовить все совместимые Terrain Details")) TerrainVegetationImport.BeginAll();
            }
            EditorGUILayout.LabelField(TerrainVegetationImport.Status, EditorStyles.wordWrappedLabel);
            foreach (string id in tiles)
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(id, GUILayout.Width(110))) selected = System.Array.IndexOf(tiles, id);
                    GUILayout.Label(File.Exists(ContentPaths.FilePath(TerrainVegetationImport.DataPath(id))) ? "Terrain Details" : "Исходная растительность");
                    if (File.Exists(ContentPaths.FilePath(EnvironmentWorldWorkspace.ScenePath(id)))) GUILayout.Label("Сцена создана", GUILayout.Width(105));
                }
            EditorGUILayout.EndScrollView();
        }
    }
}
