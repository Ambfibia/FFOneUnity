using System.Linq;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    // Unity restores the authoring scene setup after leaving Play Mode. Starting
    // from the services scene prevents a second copy of the open cells loading.
    [InitializeOnLoad]
    public static class WorldCellPlayMode
    {
        const string Key="FFOne.WorldCellPlayMode.";
        static WorldCellPlayMode() => EditorApplication.playModeStateChanged += Changed;
        static void Changed(PlayModeStateChange state)
        {
            if(Application.isBatchMode)return;
            if(state==PlayModeStateChange.ExitingEditMode &&
               Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s=>s.isLoaded)
                   .Any(s=>s.GetRootGameObjects().Any(r=>r.GetComponentsInChildren<EnvironmentCellBehaviour>(true).Any(c=>c.Definition))))
            {
                SessionState.SetString(Key+"Previous",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                SessionState.SetBool(Key+"Restore",true);
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(System.IO.File.Exists(WholeWorldMigration.DemoPath) ? WholeWorldMigration.DemoPath : WorldDistrictAcceptance.DemoPath);
                var cells=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s=>s.isLoaded).SelectMany(s=>s.GetRootGameObjects()).SelectMany(r=>r.GetComponentsInChildren<EnvironmentCellBehaviour>(true)).Where(c=>c.Definition).ToArray();
                var cell=cells.FirstOrDefault(c=>c.gameObject.scene==SceneManager.GetActiveScene()) ?? cells.First();
                var point=(cell.Definition.Minimum+cell.Definition.Maximum)*.5f;
                foreach(var terrain in cell.GetComponentsInChildren<FFOne.Runtime.World.WorldTerrainParameters>(true))
                {var collider=terrain.GetComponent<MeshCollider>();RaycastHit hit;if(collider && collider.Raycast(new Ray(new Vector3(point.x,10000,point.z),Vector3.down),out hit,20000)){point.y=hit.point.y+2;break;}}
                SessionState.SetFloat(Key+"X",point.x);SessionState.SetFloat(Key+"Y",point.y);SessionState.SetFloat(Key+"Z",point.z);
            }
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key+"Restore",false))EditorApplication.update+=PlacePreview;
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key+"Restore",false))
            {
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+"Previous",""));
                EditorApplication.update-=PlacePreview;
                SessionState.EraseFloat(Key+"X");SessionState.EraseFloat(Key+"Y");SessionState.EraseFloat(Key+"Z");
                SessionState.EraseBool(Key+"Restore");SessionState.EraseString(Key+"Previous");
            }
        }
        static void PlacePreview()
        {
            if(!EditorApplication.isPlaying){EditorApplication.update-=PlacePreview;return;}
            var stream=UnityEngine.Object.FindFirstObjectByType<WorldStreamer>();
            if(!stream || !stream.Catalog)return;
            stream.Anchor=new Vector3(SessionState.GetFloat(Key+"X",0),SessionState.GetFloat(Key+"Y",0),SessionState.GetFloat(Key+"Z",0));
            EditorApplication.update-=PlacePreview;
        }
    }
}
