using System;
using System.IO;
using System.Linq;
using FFOne.Editor;
using FFOne.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FFOne.Tests
{
    public sealed class WorldCellAuthoringTests
    {
        [Test]
        public void ReimportAndReopenPreserveAuthoredTransformsNamesDeletionsAndAdditions()
        {
            const string id="map_98_98";
            string folder=EnvironmentWorldWorkspace.Folder(id),path=EnvironmentWorldWorkspace.ScenePath(id);
            Assert.That(Directory.Exists(folder),Is.False,"Fixture directory must not contain user work");
            var previous=SceneManager.GetActiveScene();Scene scene=default;
            try
            {
                EnvironmentWorldWorkspace.EnsureFolder(folder);
                scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var root=new GameObject("Authored Cell");SceneManager.MoveGameObjectToScene(root,scene);
                var original=new GameObject("Original Imported Object");original.transform.SetParent(root.transform);
                UnityEngine.Object.DestroyImmediate(original);
                var added=new GameObject("Hand placed marker");added.transform.SetParent(root.transform);
                added.transform.localPosition=new Vector3(12.25f,3,-15.75f);added.transform.localRotation=Quaternion.Euler(5,37,0);added.SetActive(false);
                Assert.That(EditorSceneManager.SaveScene(scene,path),Is.True);
                EditorSceneManager.CloseScene(scene,true);
                string before=EnvironmentWorldWorkspace.Hash(path),guid=AssetDatabase.AssetPathToGUID(path);
                Assert.That(EnvironmentWorldWorkspace.CreateMissing(id),Is.False);
                EnvironmentWorldWorkspace.Open(id);scene=SceneManager.GetSceneByPath(path);
                Assert.That(scene.isDirty,Is.False);
                var children=scene.GetRootGameObjects().Single().GetComponentsInChildren<Transform>(true);
                Assert.That(children.Any(t=>t.name=="Original Imported Object"),Is.False);
                var marker=children.Single(t=>t.name=="Hand placed marker");
                Assert.That(marker.localPosition,Is.EqualTo(new Vector3(12.25f,3,-15.75f)));
                Assert.That(Quaternion.Angle(marker.localRotation,Quaternion.Euler(5,37,0)),Is.LessThan(.001f));
                Assert.That(marker.gameObject.activeSelf,Is.False);
                Assert.That(EnvironmentWorldWorkspace.Hash(path),Is.EqualTo(before));
                Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo(guid));
            }
            finally
            {
                if(scene.IsValid() && scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
                AssetDatabase.DeleteAsset(folder);
            }
        }
        [Test]
        public void FullReimportIsRejectedBeforeTouchingTheAcceptedCatalog()
        {
            string before=EnvironmentWorldWorkspace.Hash(EnvironmentWorldWorkspace.WorldPath);
            var exception=Assert.Throws<InvalidOperationException>(()=>WorldContentImport.Import("does-not-exist"));
            Assert.That(exception.Message,Does.Contain("protected"));
            Assert.That(EnvironmentWorldWorkspace.Hash(EnvironmentWorldWorkspace.WorldPath),Is.EqualTo(before));
        }
        [Test]
        public void WholeWorldHas170DistinctPublishedAuthoringScenes()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorldCatalog>(EnvironmentWorldWorkspace.WorldPath);
            Assert.That(catalog.Tiles.Length,Is.EqualTo(170));
            Assert.That(catalog.Tiles.Select(t=>t.SceneAddress).Distinct().Count(),Is.EqualTo(170));
            foreach(var tile in catalog.Tiles)
            {
                var definition=AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(EnvironmentWorldWorkspace.DefinitionPath(tile.Id));
                Assert.That(definition,Is.Not.Null,tile.Id);
                Assert.That(tile.SceneAddress,Is.EqualTo("ffone/"+AssetDatabase.AssetPathToGUID(EnvironmentWorldWorkspace.ScenePath(tile.Id))),tile.Id);
                Assert.That(definition.SceneAddress,Is.EqualTo(tile.SceneAddress));
                Assert.That(definition.ImportedContentGuid,Is.Not.Empty);
            }
        }
        [Test]
        public void EntryDistrictHasNineExplicitSceneAddressesAndLocalBounds()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorldCatalog>(EnvironmentWorldWorkspace.WorldPath);
            var ids=EnvironmentWorldWorkspace.Neighbors("map_12_01");Assert.That(ids.Length,Is.EqualTo(9));
            foreach(string id in ids)
            {
                var cell=AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(EnvironmentWorldWorkspace.DefinitionPath(id));
                Assert.That(cell,Is.Not.Null,id);
                Assert.That(cell.SceneAddress,Is.EqualTo("ffone/"+AssetDatabase.AssetPathToGUID(EnvironmentWorldWorkspace.ScenePath(id))));
                Assert.That(catalog.Tiles.Single(t=>t.Id==id).SceneAddress,Is.EqualTo(cell.SceneAddress));
                Assert.That(cell.Maximum.x-cell.Minimum.x,Is.EqualTo(512));
                Assert.That(cell.Maximum.z-cell.Minimum.z,Is.EqualTo(512));
            }
        }
    }
}
