using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public sealed class WorldSeamReviewWindow : EditorWindow
    {
        const string Report="Tools/ContentMigration/whole-world-seam-audit.json";
        const string Review="Tools/ContentMigration/world-boundary-acceptance.json";
        Vector2 scroll;
        JObject audit,review;
        [MenuItem("FFOne/World/Review Terrain Boundaries")]
        public static void Open() => GetWindow<WorldSeamReviewWindow>("Стыки рельефа");
        void OnEnable(){minSize=new Vector2(640,400);Reload();}
        void Reload()
        {
            audit=File.Exists(Report)?JObject.Parse(File.ReadAllText(Report)):null;
            review=File.Exists(Review)?JObject.Parse(File.ReadAllText(Review)):null;
        }
        static string Label(string kind)
        {
            switch(kind)
            {
                case "instance-separation":return "Граница отдельных зон";
                case "authored-blocker":return "Исходное препятствие / периметр";
                case "covered-by-poison-surface":return "Перекрыто поверхностью яда";
                case "repaired-source-crack":return "Локальный разрыв исправлен";
                case "mixed-repaired-and-exterior-relief":return "Локальный ремонт; остальной рельеф сохранён";
                case "background-boundary":return "Фон / вода — назначение не полностью установлено";
                default:return "Исходный рельеф — назначение требует проверки";
            }
        }
        static void Frame(JToken point)
        {
            var view=SceneView.lastActiveSceneView;
            if(!view || point==null)return;
            float y=point["aY"]!=null?((float)point["aY"]+(float)point["bY"])*.5f:0;
            view.LookAt(new Vector3((float)point["x"],y,(float)point["z"]),Quaternion.Euler(55,35,0),35);
            view.Focus();
        }
        void DrawReview()
        {
            EditorGUILayout.HelpBox("Сохранённый отчёт после ремонта. Проходимость проверена по указанным маршрутам; она не подтверждает все возможные пути. Неудачный проход сам по себе не доказывает замысел автора. Открытие границы не сохраняет и не пересоздаёт сцены.",MessageType.Info);
            GUILayout.Label("Пар: "+review["pairsReviewed"]+" · Исправлено координат: "+((JArray)review["geometryRepairs"]).Count+" · Сцен: "+review["cellsChanged"]);
            var walking=review["walking"];
            GUILayout.Label("Пешие пробы: "+walking["probes"]+" · Проходов до / после: "+walking["beforeCrossed"]+" / "+walking["afterCrossed"]+" · Регрессий: "+((JArray)walking["regressions"]).Count);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var edge in review["edges"].OrderByDescending(e=>(float)e["remainingMaximumGap"]))
            {
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(edge["a"]+" / "+edge["b"],EditorStyles.boldLabel);
                        if(GUILayout.Button("Открыть",GUILayout.Width(80)))
                        {
                            // This is a review window: never create a missing authoring scene.
                            var ids=new[]{(string)edge["a"],(string)edge["b"]};
                            if(ids.Any(id=>!File.Exists(EnvironmentWorldWorkspace.ScenePath(id))))
                                ShowNotification(new GUIContent("Сцена отсутствует. Восстановите её из резервной копии."));
                            else
                            {
                                foreach(string id in ids)EnvironmentWorldWorkspace.Open(id);
                                var repaired=((JArray)edge["fixedCoordinates"]).FirstOrDefault();
                                Frame(repaired??edge["samples"].OrderByDescending(p=>(float)p["gap"]).FirstOrDefault());
                            }
                        }
                    }
                    GUILayout.Label(new GUIContent(Label((string)edge["kind"]),(string)edge["reason"]),EditorStyles.wordWrappedLabel);
                    GUILayout.Label("Максимальный перепад до / после: "+((float)edge["maximumGap"]).ToString("F3")+" / "+((float)edge["remainingMaximumGap"]).ToString("F3")+" м");
                    var probes=edge["walking"];
                    GUILayout.Label("Маршрутов проверено: "+probes["tested"]+" · Пройдено: "+probes["crossed"]+" · Исправлено координат: "+((JArray)edge["fixedCoordinates"]).Count);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        void OnGUI()
        {
            if(GUILayout.Button("Прочитать последний отчёт"))Reload();
            if(review!=null){DrawReview();return;}
            if(audit==null){EditorGUILayout.HelpBox("Сначала выполните WholeWorldSeams.Inspect в batchmode.",MessageType.Info);return;}
            EditorGUILayout.HelpBox("Крупные перепады исходного рельефа сохранены. Список показывает результаты проверки геометрии, а не разрешённые маршруты движения. Открытие границы не изменяет сцены.",MessageType.Info);
            GUILayout.Label("Ячеек: "+audit["cells"]+" · Общих координат: "+audit["sharedEdgeCoordinates"]+" · Максимальный перепад: "+audit["maximumGap"]);
            var groups=((JArray)audit["discontinuities"]).GroupBy(g=>string.Join(" / ",g["ids"].Select(id=>(string)id))).OrderByDescending(g=>g.Max(p=>(float)p["gap"]));
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var group in groups)
            {
                var largest=group.OrderByDescending(g=>(float)g["gap"]).First();
                using(new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(group.Key+" · "+((float)largest["gap"]).ToString("F3")+" м · "+group.Count()+" координат");
                    if(GUILayout.Button("Открыть",GUILayout.Width(80)))
                    {
                        foreach(string id in largest["ids"].Select(id=>(string)id))EnvironmentWorldWorkspace.Open(id);
                        var view=SceneView.lastActiveSceneView;
                        if(view){view.LookAt(new Vector3((float)largest["x"],0,(float)largest["z"]),Quaternion.Euler(55,35,0),250);view.Focus();}
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
