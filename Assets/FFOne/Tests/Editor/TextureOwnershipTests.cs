using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class TextureOwnershipTests
    {
        static JObject Plan => JObject.Parse(File.ReadAllText("Tools/ContentMigration/unassigned-texture-plan.json"));
        [Test]
        public void AllTextureIdentitiesLoadAfterMovingAndSourceMipsAreNotAddressable()
        {
            var settings=AddressableAssetSettingsDefaultObject.Settings;
            var keys=JObject.Parse(File.ReadAllText("Assets/FFOne/Settings/Resources/FFOneContentIndex.json"))["keys"].Values<string>().ToHashSet();
            int textures=0,mips=0;
            foreach(var move in Plan["moves"])
            {
                string path=(string)move["to"],old=(string)move["old"],guid=(string)move["guid"];
                Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo(guid),path);
                Assert.That(ContentPaths.Resolve(old),Is.EqualTo(path));
                Assert.That(ContentPaths.Resolve((string)move["from"]),Is.EqualTo(path));
                Assert.That(ContentPaths.Legacy(path),Is.EqualTo(old));
                Assert.That(ContentAssets.Load<Texture2D>(ContentPaths.Resource(old)),Is.Not.Null,old);
                var entry=settings.FindAssetEntry(guid);
                if((bool?)move["editorOnlySource"]==true)
                {
                    mips++;Assert.That(entry,Is.Null,path);Assert.That(keys.Contains(ContentPaths.Resource(old)),Is.False,path);
                    Assert.That(path,Does.StartWith("Assets/FFOne/Editor/ImportMetadata/TextureMipSources/"));
                }
                else
                {
                    textures++;Assert.That(entry,Is.Not.Null,path);
                    Assert.That(entry.address,Is.EqualTo(ContentPaths.Resource(old)));Assert.That(keys.Contains(entry.address),Is.True);
                }
            }
            Assert.That(textures,Is.EqualTo(182));Assert.That(mips,Is.EqualTo(113));
        }
        [Test]
        public void ExactWorldMipSourcesStillProduceTheAcceptedTextureResources()
        {
            int before=WorldModelTextureAssets.CreatedResources;
            int checkedModels=0;
            foreach(var entry in ContentPaths.Entries.Where(e=>e.old.StartsWith("Assets/Resources/map/shared/effects/models/") && e.old.EndsWith(".glb")))
            {
                var doc=NativeGlbDocument.Read(ContentPaths.CurrentPath(entry));
                if(!doc.Json.ToString().Contains("effects/shared/textures/"))continue;
                WorldModelTextureAssets.Prepare(ContentPaths.CurrentPath(entry),doc.Json);
                checkedModels++;
            }
            Assert.That(checkedModels,Is.GreaterThanOrEqualTo(6));
            Assert.That(WorldModelTextureAssets.CreatedResources,Is.EqualTo(before),"A folder move must not change the exact texture content key");
        }
    }
}
