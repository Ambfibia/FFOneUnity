using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed class ContentLayoutTests
    {
        [Test]
        public void AcceptedAssetLookupFollowsGuidAfterAnEditorMove()
        {
            string folder="Assets/__ContentLayoutMoveTest_"+System.Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            try
            {
                string original=folder+"/Original.asset", moved=folder+"/Moved.asset";
                var catalog=ScriptableObject.CreateInstance<NativeContentCatalog>();
                AssetDatabase.CreateAsset(catalog,original);
                var entry=new ContentPaths.Entry { path=original, guid=AssetDatabase.AssetPathToGUID(original) };
                Assert.That(AssetDatabase.MoveAsset(original,moved),Is.Empty);
                Assert.That(ContentPaths.CurrentPath(entry),Is.EqualTo(moved));
                Assert.That(AssetDatabase.LoadAssetAtPath<NativeContentCatalog>(ContentPaths.CurrentPath(entry)),Is.SameAs(catalog));
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
        [Test]
        public void StableAddressesResolvePublishedModelsDataAndFrontend()
        {
            Assert.That(ContentAssets.Load<NativeContentCatalog>("NativeContent"),Is.Not.Null);
            Assert.That(ContentAssets.Load<NativeModelCatalog>("NativeEditable/Index"),Is.Not.Null);
            Assert.That(ContentAssets.Load<VisualTreeAsset>("ui/views/Login"),Is.Not.Null);
            Assert.That(ContentAssets.Load<AudioClip>("audio/music/00_main_theme"),Is.Not.Null);
            Assert.That(NativeModelAssets.Load("characters/mobs/mob_almonster/mob_almonster"),Is.Not.Null);
            Assert.That(NativeData.Load("data/character_creation/appearance"),Is.Not.Null);
        }
        [Test]
        public void CatalogAddressesDoNotDependOnPhysicalResourcesFolders()
        {
            foreach(var old in new[]{"Assets/Resources/NativeContent.asset","Assets/Resources/NativeEditable/Index.asset","Assets/Scenes/Main.unity"})
            {
                var entry=ContentPaths.Entries.Single(e=>e.old==old);
                Assert.That(File.Exists(entry.path),Is.True,entry.path);
                Assert.That(AssetDatabase.AssetPathToGUID(entry.path),Is.EqualTo(entry.guid),entry.path);
                Assert.That(ContentPaths.Resolve(old),Is.EqualTo(entry.path));
                Assert.That(ContentPaths.Legacy(entry.path),Is.EqualTo(old));
            }
        }
        [Test]
        public void OriginalGlbTextureUrisRemainResolvableAfterLayoutChange()
        {
            string old="Assets/Resources/characters/mobs/mob_almonster/mob_almonster.glb";
            var document=NativeGlbDocument.Read(ContentPaths.Resolve(old));
            foreach(var image in document.Json["images"])
            {
                string uri=(string)image["uri"];
                if(string.IsNullOrEmpty(uri) || uri.StartsWith("data:"))continue;
                string logical=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(old),uri));
                Assert.That(File.Exists(ContentPaths.Resolve(logical)),Is.True,uri);
            }
        }
    }
}
