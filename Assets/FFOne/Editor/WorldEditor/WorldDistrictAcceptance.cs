using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    public static class WorldDistrictAcceptance
    {
        public const string RegionFolder="Assets/FFOne/Worlds/Overworld/Regions/EntryDistrict";
        public const string DemoPath="Assets/FFOne/Scenes/Development/WorldDistrict.unity";
        public static readonly Vector3 Entry=new Vector3(6374.3188f,-56.4746f,668.2039f);
        public static void Prepare()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Run preparation in batch mode; use World Editor for interactive work.");
            EditorSceneManager.OpenScene("Assets/FFOne/Scenes/Bootstrap/Main.unity",OpenSceneMode.Single);
            var ids=EnvironmentWorldWorkspace.Neighbors("map_12_01");
            if(ids.Length!=9)throw new InvalidDataException("District must contain nine cells");
            foreach(string id in ids)
            {
                bool created=EnvironmentWorldWorkspace.CreateMissing(id);
                EnvironmentWorldWorkspace.Publish(id);
                Debug.Log("WORLD DISTRICT CELL: "+id+" created="+created);
            }
            EnvironmentWorldWorkspace.EnsureFolder(RegionFolder);
            string regionPath=RegionFolder+"/RegionDefinition.asset";
            var region=AssetDatabase.LoadAssetAtPath<WorldRegionDefinition>(regionPath);
            if(!region)
            {
                region=ScriptableObject.CreateInstance<WorldRegionDefinition>();region.RegionId="EntryDistrict";region.EntryPoint=Entry;
                region.Cells=ids.Select(id=>AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(EnvironmentWorldWorkspace.DefinitionPath(id))).ToArray();
                AssetDatabase.CreateAsset(region,regionPath);
            }
            var main=AssetDatabase.LoadAssetAtPath<WorldCatalog>(EnvironmentWorldWorkspace.WorldPath);
            string districtPath=RegionFolder+"/DistrictWorld.asset";
            var district=AssetDatabase.LoadAssetAtPath<WorldCatalog>(districtPath);
            if(!district){district=ScriptableObject.CreateInstance<WorldCatalog>();AssetDatabase.CreateAsset(district,districtPath);}
            district.Tiles=main.Tiles.Where(t=>ids.Contains(t.Id)).ToArray();district.Atmospheres=main.Atmospheres;
            district.NeighborWater=main.NeighborWater.Where(t=>ids.Contains(t.Id)).ToArray();EditorUtility.SetDirty(district);
            if(!File.Exists(DemoPath))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var cameraObject=new GameObject("World Camera",typeof(Camera),typeof(AudioListener));cameraObject.tag="MainCamera";
                var camera=cameraObject.GetComponent<Camera>();camera.fieldOfView=45;camera.farClipPlane=800;camera.nearClipPlane=.1f;
                var sun=new GameObject("World Sun",typeof(Light));sun.transform.rotation=Quaternion.Euler(60,130,0);sun.GetComponent<Light>().type=LightType.Directional;
                var world=new GameObject("District Services",typeof(WorldStreamer),typeof(WorldViewer));
                var stream=world.GetComponent<WorldStreamer>();stream.View=camera;stream.Anchor=Entry;stream.CatalogAddress="ffone/"+AssetDatabase.AssetPathToGUID(districtPath);
                world.AddComponent<WorldDistrictSmoke>();
                EditorSceneManager.SaveScene(scene,DemoPath);
            }
            AssetDatabase.SaveAssets();ContentLayoutSetup.Rebuild();Validate();
        }
        public static void Validate()
        {
            var ids=EnvironmentWorldWorkspace.Neighbors("map_12_01");
            var rows=new System.Collections.Generic.List<object>();
            foreach(string id in ids)
            {
                string path=EnvironmentWorldWorkspace.ScenePath(id), before=EnvironmentWorldWorkspace.Hash(path);
                if(EnvironmentWorldWorkspace.CreateMissing(id))throw new InvalidDataException("Existing scene was recreated");
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    var roots=scene.GetRootGameObjects();
                    if(roots.Length!=1)throw new InvalidDataException(id+": expected one scene-owned cell root");
                    var cell=roots[0].GetComponent<EnvironmentCellBehaviour>();
                    if(!cell || !cell.Definition || cell.TileId!=id || cell.InitializeOnStart)throw new InvalidDataException("Invalid cell root "+id);
                    var source=AssetDatabase.LoadAssetAtPath<WorldTileContent>(AssetDatabase.GUIDToAssetPath(cell.Definition.ImportedContentGuid));
                    if(cell.Bindings.Length!=source.Visuals.Length+source.Colliders.Length || cell.Bindings.Any(b=>!b.Target))throw new InvalidDataException("Missing source binding "+id);
                    if(roots[0].GetComponentsInChildren<Camera>(true).Length>0)throw new InvalidDataException("Cell contains a camera "+id);
                    foreach(var renderer in roots[0].GetComponentsInChildren<Renderer>(true))
                        if(renderer.sharedMaterials.Any(m=>!m))throw new InvalidDataException("Missing material "+id+"/"+renderer.name);
                    string guid=AssetDatabase.AssetPathToGUID(path);
                    var address=AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(guid);
                    if(address==null || address.address!=cell.Definition.SceneAddress)throw new InvalidDataException("Missing scene address "+id);
                    rows.Add(new { id,scene=path,sceneAddress=address.address,visuals=source.Visuals.Length,colliders=source.Colliders.Length,sha256=before });
                }
                finally { EditorSceneManager.CloseScene(scene,true); }
                if(EnvironmentWorldWorkspace.Hash(path)!=before)throw new InvalidDataException("Reimport modified authored scene "+id);
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            Directory.CreateDirectory("Tools/ContentMigration");
            File.WriteAllText("Tools/ContentMigration/world-district-scenes.json",Newtonsoft.Json.JsonConvert.SerializeObject(new { cells=rows,reimportPreservesSceneBytes=true },Newtonsoft.Json.Formatting.Indented));
            Debug.Log("WORLD DISTRICT VALID: nine authored scenes and stable Addressables entries; repeated creation leaves scenes unchanged.");
        }
        public static void Build()
        {
            ContentLayoutSetup.Rebuild();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            UnityEditor.AddressableAssets.Settings.ProjectConfigData.GenerateBuildLayout=true;
            UnityEditor.AddressableAssets.Settings.ProjectConfigData.BuildLayoutReportFileFormat=UnityEditor.AddressableAssets.Settings.ProjectConfigData.ReportFileFormat.JSON;
            AddressableAssetSettings.BuildPlayerContent(out var content);
            if(!string.IsNullOrEmpty(content.Error))throw new InvalidOperationException(content.Error);
            BuildPlayerOnly();
        }
        public static void BuildPlayerOnly()
        {
            var settings=AddressableAssetSettingsDefaultObject.Settings;var previous=settings.BuildAddressablesWithPlayerBuild;
            try
            {
                settings.BuildAddressablesWithPlayerBuild=AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
                Directory.CreateDirectory("Builds/WorldDistrict/Windows");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/FFOne/Scenes/Bootstrap/Main.unity",DemoPath},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/WorldDistrict/Windows/FFOne.exe",options=BuildOptions.Development });
                File.WriteAllText("Tools/ContentMigration/world-district-build.json",Newtonsoft.Json.JsonConvert.SerializeObject(new { result=report.summary.result.ToString(),bytes=report.summary.totalSize,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings },Newtonsoft.Json.Formatting.Indented));
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("District player build failed");
                File.WriteAllText("Builds/WorldDistrict/Windows/OpenDistrict.cmd","@echo off\r\n\"%~dp0FFOne.exe\" --world-district-demo %*\r\n");
            }
            finally{settings.BuildAddressablesWithPlayerBuild=previous;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();}
        }
        [MenuItem("FFOne/World/Open Entry District Demo")]
        public static void OpenDemo()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(DemoPath);
        }
        public static void OpenWorkspace()
        {
            OpenDemo();EnvironmentWorldWorkspace.OpenNeighborhood("map_12_01");EnvironmentWorldWorkspaceWindow.Open();
        }
    }
}
