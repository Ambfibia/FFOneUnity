using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFOne.Content;
using FFOne.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class EnvironmentWorldWorkspace
    {
        public const string WorldPath="Assets/FFOne/Worlds/Overworld/WorldDefinition.asset";
        public static string Folder(string tile)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(tile,"^map_[0-9]{2}_[0-9]{2}$")) throw new ArgumentException("Invalid cell ID");
            return "Assets/FFOne/Worlds/Overworld/Cells/"+tile;
        }
        public static string ScenePath(string tile) => Folder(tile)+"/"+tile+".unity";
        public static string DefinitionPath(string tile) => Folder(tile)+"/CellDefinition.asset";
        [MenuItem("FFOne/World/World Editor")]
        public static void OpenDefault() => EnvironmentWorldWorkspaceWindow.Open();
        public static void EnsureFolder(string path)
        {
            path=path.Replace('\\','/');
            if(AssetDatabase.IsValidFolder(path))return;
            EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
        }
        public static string Hash(string path)
        {
            using var sha=SHA256.Create(); using var stream=File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        // Existing scenes are authoritative, including deletions, names, overrides and bindings.
        public static bool CreateMissing(string tile)
        {
            string path=ScenePath(tile);
            if(File.Exists(path))return false;
            if(File.Exists(DefinitionPath(tile)))throw new IOException("Definition exists but scene is missing; restore the authored scene: "+tile);
            var catalog=AssetDatabase.LoadAssetAtPath<WorldCatalog>(WorldPath);
            var reference=catalog.Tiles.Single(t=>t.Id==tile);
            string sourcePath=ContentPaths.ResourcePath(reference.Resource);
            var content=AssetDatabase.LoadAssetAtPath<WorldTileContent>(sourcePath);
            if(!content || !content.Terrain)throw new InvalidDataException("Missing accepted terrain: "+tile);
            EnsureFolder(Folder(tile)+"/Terrain"); EnsureFolder(Folder(tile)+"/Navigation");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            bool saved=false;
            try
            {
                var root=new GameObject(tile); SceneManager.MoveGameObjectToScene(root,scene);
                var cell=root.AddComponent<EnvironmentCellBehaviour>(); cell.TileId=tile; cell.InitializeOnStart=false;
                var terrain=(GameObject)PrefabUtility.InstantiatePrefab(content.Terrain,scene);
                terrain.name="Terrain"; terrain.transform.SetParent(root.transform,false);
                foreach(var foliage in terrain.GetComponentsInChildren<Terrain>(true))
                {
                    var data=Object.Instantiate(foliage.terrainData); data.name=tile+" Authored Vegetation";
                    AssetDatabase.CreateAsset(data,AssetDatabase.GenerateUniqueAssetPath(Folder(tile)+"/Terrain/Vegetation.asset"));
                    foliage.terrainData=data;
                    var collider=foliage.GetComponent<TerrainCollider>();
                    if(collider){collider.terrainData=data;PrefabUtility.RecordPrefabInstancePropertyModifications(collider);}
                    PrefabUtility.RecordPrefabInstancePropertyModifications(foliage);
                }
                var visuals=new GameObject("Environment"); visuals.transform.SetParent(root.transform,false);
                var collision=new GameObject("Collision"); collision.transform.SetParent(root.transform,false);
                var markers=new GameObject("Gameplay Markers"); markers.transform.SetParent(root.transform,false);
                var bindings=new List<EnvironmentCellBehaviour.Binding>();
                foreach(var placement in content.Visuals)
                {
                    var model=NativeModelAssets.Resolve(placement.Model);
                    if(!model)throw new InvalidDataException("Missing model: "+placement.Name);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(model,scene);
                    instance.name=placement.Name;instance.transform.SetParent(visuals.transform,false);
                    instance.transform.SetLocalPositionAndRotation(placement.Position,placement.Rotation);instance.transform.localScale=placement.Scale;
                    WorldModelBasis.ApplySource(instance);
                    foreach(var child in instance.GetComponentsInChildren<Transform>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(child);
                    var basis=instance.GetComponent<WorldModelBasis>();if(basis)PrefabUtility.RecordPrefabInstancePropertyModifications(basis);
                    bindings.Add(new EnvironmentCellBehaviour.Binding { LegacyId=placement.LegacyId,SourceNode=placement.SourceNode,Target=instance.transform,Visual=true,PresentationEnd=placement.CpuPresentationEnd });
                }
                foreach(var placement in content.Colliders)
                {
                    var instance=new GameObject(placement.Name);instance.transform.SetParent(collision.transform,false);
                    instance.transform.SetLocalPositionAndRotation(placement.Position,placement.Rotation);instance.transform.localScale=placement.Scale;
                    var collider=instance.AddComponent<MeshCollider>();collider.sharedMesh=placement.Mesh;collider.enabled=!placement.Trigger;
                    bindings.Add(new EnvironmentCellBehaviour.Binding { LegacyId=placement.LegacyId,SourceNode=placement.SourceNode,Target=instance.transform });
                }
                cell.Bindings=bindings.ToArray();
                var definition=ScriptableObject.CreateInstance<WorldCellDefinition>();
                definition.TileId=tile;definition.Grid=new Vector2Int(reference.X,reference.Z);definition.Minimum=reference.Minimum;definition.Maximum=reference.Maximum;
                definition.ImportedContentGuid=AssetDatabase.AssetPathToGUID(sourcePath);definition.ImportedContentSha256=Hash(sourcePath);
                definition.AuthoringNotes="Saved scene owns placements and prefab overrides. Reimport never replaces it. Review changed source in a staging project.";
                AssetDatabase.CreateAsset(definition,DefinitionPath(tile));cell.Definition=definition;
                if(!EditorSceneManager.SaveScene(scene,path))throw new IOException("Failed saving "+path);
                definition.SceneAddress="ffone/"+AssetDatabase.AssetPathToGUID(path);EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
                saved=true;return true;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);
                if(!saved)Debug.LogError("Cell preparation incomplete: "+tile+". Existing authored files were not overwritten.");
            }
        }
        public static void Open(string tile)
        {
            CreateMissing(tile);
            var scene=SceneManager.GetSceneByPath(ScenePath(tile));
            if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath(tile),OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
        }
        public static string[] Neighbors(string tile)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorldCatalog>(WorldPath);var center=catalog.Tiles.Single(t=>t.Id==tile);
            return catalog.Tiles.Where(t=>Math.Abs(t.X-center.X)<=1 && Math.Abs(t.Z-center.Z)<=1).OrderBy(t=>t.Z).ThenBy(t=>t.X).Select(t=>t.Id).ToArray();
        }
        public static void OpenNeighborhood(string tile) { foreach(string id in Neighbors(tile))Open(id);Open(tile); }
        public static void Publish(string tile)
        {
            var definition=AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(DefinitionPath(tile));
            if(!definition || !File.Exists(ScenePath(tile)))throw new InvalidOperationException("Create and validate a runtime cell first: "+tile);
            var open=SceneManager.GetSceneByPath(ScenePath(tile));
            if(open.IsValid() && open.isDirty)throw new InvalidOperationException("Save cell scene before publishing: "+tile);
            var catalog=AssetDatabase.LoadAssetAtPath<WorldCatalog>(WorldPath);
            catalog.Tiles.Single(t=>t.Id==tile).SceneAddress=definition.SceneAddress;
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
        }
    }
}
