using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class ContentOwnershipTests
    {
        [Test]
        public void MovedAssetsKeepGuidsAndBothHistoricalPathFormsResolve()
        {
            var plan=JObject.Parse(File.ReadAllText("Tools/ContentMigration/ownership-plan.json"));
            foreach(var move in plan["moves"])
            {
                string path=AssetDatabase.GUIDToAssetPath((string)move["guid"]),old=(string)move["old"],previous=(string)move["from"];
                Assert.That(ContentPaths.Resolve((string)move["to"]),Is.EqualTo(path),"Subsequent move alias");
                Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo((string)move["guid"]),path);
                Assert.That(ContentPaths.Resolve(old),Is.EqualTo(path),old);
                Assert.That(ContentPaths.Resolve(previous),Is.EqualTo(path),previous);
                Assert.That(ContentPaths.Legacy(path),Is.EqualTo(old),path);
            }
        }
        [Test]
        public void AllNanoModelsRemainLoadableUnderTheirStableIdentities()
        {
            var models=ContentPaths.Entries.Where(e=>e.old.EndsWith(".glb") &&
                (e.old.StartsWith("Assets/Resources/characters/nanos/") || e.old.StartsWith("Assets/Resources/characters/nano/"))).ToArray();
            Assert.That(models.Length,Is.EqualTo(69));
            foreach(var entry in models)
            {
                string path=ContentPaths.CurrentPath(entry);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path),Is.Not.Null,path);
                Assert.That(NativeModelAssets.Load(ContentPaths.Resource(entry.old)),Is.Not.Null,entry.old);
            }
            Assert.That(Directory.GetDirectories("Assets/FFOne/Content/Characters/Nanos").Length,Is.EqualTo(68));
            Assert.That(Directory.Exists("Assets/FFOne/Content/Characters/Nanos/Appearances"),Is.False);
        }
        [Test]
        public void NewGeneratedImportsHaveAnUnambiguousStagingIdentity()
        {
            foreach(string original in new[]{"Assets/Generated/WorldTerrainShared/Mesh_new.asset","Assets/Generated/WorldModelTextures/new.asset",
                "Assets/Resources/Environment/Shared/Meshes/new.asset","Assets/Resources/Environment/Shared/Materials/new.mat"})
            {
                string physical=ContentPaths.Resolve(original);
                Assert.That(physical,Does.StartWith("Assets/FFOne/Content/Incoming/"));
                Assert.That(ContentPaths.Legacy(physical),Is.EqualTo(original));
            }
        }
        [Test]
        public void NoGenericSharedFoldersRemainInThePlayableContentLibrary()
        {
            var shared=Directory.GetDirectories("Assets/FFOne/Content","*",SearchOption.AllDirectories)
                .Where(p=>string.Equals(Path.GetFileName(p),"shared",System.StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.That(shared,Is.Empty);
        }
    }
}
