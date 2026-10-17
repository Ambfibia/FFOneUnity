using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Editor
{
    public static class WholeWorldMigration
    {
        public const string Receipt="Tools/ContentMigration/whole-world-scenes.json";
        public const string DemoPath="Assets/FFOne/Scenes/Development/WorldOverview.unity";
        public static WorldCatalog Catalog => AssetDatabase.LoadAssetAtPath<WorldCatalog>(EnvironmentWorldWorkspace.WorldPath);
        public static void Write(string path,object value)
        {
            string temporary=path+".tmp";
            File.WriteAllText(temporary,JsonConvert.SerializeObject(value,Formatting.Indented));
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
        }
        public static void Prepare()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Use batch mode for whole-world migration.");
            EditorSceneManager.OpenScene(WorldDistrictAcceptance.DemoPath);
            var rows=File.Exists(Receipt) ? JArray.Parse(File.ReadAllText(Receipt)) : new JArray();
            var ids=Catalog.Tiles.OrderBy(t=>t.Z).ThenBy(t=>t.X).Select(t=>t.Id).ToArray();
            if(ids.Length!=170)throw new InvalidDataException("Expected 170 accepted tiles");
            foreach(string id in ids)
            {
                string path=EnvironmentWorldWorkspace.ScenePath(id);
                var row=rows.FirstOrDefault(r=>(string)r["id"]==id);
                if(row==null)
                {
                    bool existed=File.Exists(path);
                    string before=existed ? EnvironmentWorldWorkspace.Hash(path) : null;
                    if(!existed)EnvironmentWorldWorkspace.CreateMissing(id);
                    bool upgraded=false;
                    if(!File.Exists(EnvironmentWorldWorkspace.DefinitionPath(id))) { UpgradeLegacy(id);upgraded=true; }
                    row=JObject.FromObject(new {id,kind=upgraded ? "upgraded-existing" : existed ? "preserved-existing" : "created",beforeSha256=before,acceptedSha256=EnvironmentWorldWorkspace.Hash(path),guid=AssetDatabase.AssetPathToGUID(path)});
                    rows.Add(row);Write(Receipt,rows);
                }
                EnvironmentWorldWorkspace.Publish(id);
                ValidateCell(id);
                Debug.Log("WHOLE WORLD PREPARED "+id+" "+row["kind"]);
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
            }
            if(!File.Exists(DemoPath))
            {
                var scene=EditorSceneManager.OpenScene(WorldDistrictAcceptance.DemoPath);
                var stream=UnityEngine.Object.FindFirstObjectByType<WorldStreamer>();
                stream.CatalogOverride=null;stream.CatalogAddress="World/Catalog";
                EditorSceneManager.SaveScene(scene,DemoPath,true);
            }
            AssetDatabase.SaveAssets();ContentLayoutSetup.Rebuild();
            Debug.Log("WHOLE WORLD PREPARATION PASSED: "+ids.Length);
        }
        // Preserve serialized object identities, world poses, prefab overrides and existing bindings.
        // Preview cameras/lights stay in the scene as disabled EditorOnly objects.
        static void UpgradeLegacy(string id)
        {
            string path=EnvironmentWorldWorkspace.ScenePath(id);
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var roots=scene.GetRootGameObjects();
                var cell=roots.SelectMany(r=>r.GetComponentsInChildren<EnvironmentCellBehaviour>(true)).Single();
                var reference=Catalog.Tiles.Single(t=>t.Id==id);
                string sourcePath=ContentPaths.ResourcePath(reference.Resource);
                var content=AssetDatabase.LoadAssetAtPath<WorldTileContent>(sourcePath);
                var transforms=roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
                var poses=transforms.Select(t=>t.localToWorldMatrix).ToArray();
                foreach(var root in roots)
                    if(root!=cell.gameObject)root.transform.SetParent(cell.transform,true);
                foreach(var binding in cell.Bindings)
                {
                    var original=content.Visuals.FirstOrDefault(p=>p.SourceNode==binding.SourceNode && p.LegacyId==binding.LegacyId);
                    if(original!=null){binding.Visual=true;binding.PresentationEnd=original.CpuPresentationEnd;}
                }
                foreach(var camera in cell.GetComponentsInChildren<Camera>(true)){camera.enabled=false;camera.gameObject.tag="EditorOnly";}
                foreach(var listener in cell.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
                foreach(var light in cell.GetComponentsInChildren<Light>(true).Where(l=>l.type==LightType.Directional)){light.enabled=false;light.gameObject.tag="EditorOnly";}
                for(int i=0;i<transforms.Length;i++)
                    for(int j=0;j<16;j++)
                        if(Mathf.Abs(poses[i][j]-transforms[i].localToWorldMatrix[j])>.0001f)throw new InvalidDataException("Upgrade changed authored pose "+id+"/"+transforms[i].name);
                var definition=ScriptableObject.CreateInstance<WorldCellDefinition>();
                definition.TileId=id;definition.Grid=new Vector2Int(reference.X,reference.Z);definition.Minimum=reference.Minimum;definition.Maximum=reference.Maximum;
                definition.SceneAddress="ffone/"+AssetDatabase.AssetPathToGUID(path);
                definition.ImportedContentGuid=AssetDatabase.AssetPathToGUID(sourcePath);definition.ImportedContentSha256=EnvironmentWorldWorkspace.Hash(sourcePath);
                definition.AuthoringNotes="Upgraded existing authoring scene in place. Object identities, poses and bindings retained. Source reimport must be staged.";
                AssetDatabase.CreateAsset(definition,EnvironmentWorldWorkspace.DefinitionPath(id));
                cell.Definition=definition;cell.View=null;cell.InitializeOnStart=false;
                EnvironmentWorldWorkspace.EnsureFolder(EnvironmentWorldWorkspace.Folder(id)+"/Terrain");
                EnvironmentWorldWorkspace.EnsureFolder(EnvironmentWorldWorkspace.Folder(id)+"/Navigation");
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
        }
        public static void ValidateCell(string id)
        {
            string path=EnvironmentWorldWorkspace.ScenePath(id),hash=EnvironmentWorldWorkspace.Hash(path);
            if(EnvironmentWorldWorkspace.CreateMissing(id))throw new InvalidDataException("Scene recreated: "+id);
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var roots=scene.GetRootGameObjects();
                if(roots.Length!=1)throw new InvalidDataException("Expected one cell root: "+id);
                var cell=roots[0].GetComponent<EnvironmentCellBehaviour>();
                if(!cell || !cell.Definition || cell.TileId!=id || cell.InitializeOnStart)throw new InvalidDataException("Invalid scene ownership: "+id);
                if(cell.Bindings.Any(b=>!b.Target))throw new InvalidDataException("Unresolved authoring binding: "+id);
                if(roots[0].GetComponentsInChildren<Camera>(true).Any(c=>c.enabled))throw new InvalidDataException("Active cell camera: "+id);
                foreach(var renderer in roots[0].GetComponentsInChildren<Renderer>(true))
                    if(renderer.sharedMaterials.Any(m=>!m))throw new InvalidDataException("Missing material: "+id+"/"+renderer.name);
                if(AssetDatabase.AssetPathToGUID(path)!=cell.Definition.SceneAddress.Substring("ffone/".Length))throw new InvalidDataException("Scene GUID mismatch: "+id);
            }
            finally{EditorSceneManager.CloseScene(scene,true);}
            if(EnvironmentWorldWorkspace.Hash(path)!=hash)throw new InvalidDataException("Read-only reopening changed scene: "+id);
        }
        [MenuItem("FFOne/World/Open Whole World Workspace")]
        public static void OpenWorkspace()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            EditorSceneManager.OpenScene(DemoPath);
            EnvironmentWorldWorkspace.OpenNeighborhood("map_03_05");
            EnvironmentWorldWorkspaceWindow.Open();
        }
        public static void Build()
        {
            ContentLayoutSetup.Rebuild();
            foreach(var tile in Catalog.Tiles)
            {
                var entry=AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(EnvironmentWorldWorkspace.ScenePath(tile.Id)));
                if(string.IsNullOrEmpty(tile.SceneAddress) || entry==null || entry.address!=tile.SceneAddress)throw new InvalidDataException("Unpublished cell "+tile.Id);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();
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
                EditorUtility.UnloadUnusedAssetsImmediate();GC.Collect();Directory.CreateDirectory("Builds/WholeWorld/Windows");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/FFOne/Scenes/Bootstrap/Main.unity",WorldDistrictAcceptance.DemoPath,DemoPath},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/WholeWorld/Windows/FFOne.exe",options=BuildOptions.Development});
                Write("Tools/ContentMigration/whole-world-build.json",new{result=report.summary.result.ToString(),bytes=report.summary.totalSize,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings});
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("World build failed");
                File.WriteAllText("Builds/WholeWorld/Windows/OpenWorld.cmd","@echo off\r\n\"%~dp0FFOne.exe\" --whole-world-demo %*\r\n");
            }
            finally{settings.BuildAddressablesWithPlayerBuild=previous;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();}
        }
    }
}
