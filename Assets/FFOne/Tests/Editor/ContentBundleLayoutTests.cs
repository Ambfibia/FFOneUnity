using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace FFOne.Tests
{
    public sealed class ContentBundleLayoutTests
    {
        [Test]
        public void LoadingArtworkDoesNotShareAGroupWithLoginOrCreation()
        {
            string Group(string suffix) => ContentBundleLayout.SpecializedGroup("Assets/FFOne/Content/UI/Artwork/" + suffix);
            Assert.That(Group("en/gameplay/loading/load.png"), Is.EqualTo(Group("en/gameplay/loading/loadingbar.png")));
            Assert.That(Group("en/gameplay/loading/load.png"), Is.Not.EqualTo(Group("en/launcher/login/login_screen_bg_16x10.png")));
            Assert.That(Group("en/gameplay/loading/load.png"), Is.Not.EqualTo(Group("en/character/creation/camera/CCRotateLeftNormal.png")));
            Assert.That(Group("en/gameplay/loading/load.png"), Is.Not.EqualTo(Group("ru/gameplay/loading/load.png")));
        }

        [Test]
        public void ItemMeshesMaterialsAndTexturesRemainTogether()
        {
            const string root = "Assets/FFOne/Content/Characters/Humanoids/Equipment/shirt/shirt_vanrookvest/";
            string group = ContentBundleLayout.SpecializedGroup(root + "Prefabs/models/f_shirt_vanrookvest/model.prefab");
            Assert.That(ContentBundleLayout.SpecializedGroup(root + "Materials/model/body.mat"), Is.EqualTo(group));
            Assert.That(ContentBundleLayout.SpecializedGroup(root + "Textures/textures/f_shirt_vanrookvest.png"), Is.EqualTo(group));
            Assert.That(group, Does.StartWith("Characters-Humanoids-Equipment-shirt-"));
            Assert.That(ContentBundleLayout.SpecializedGroup(root.Replace("/shirt/", "/pants/") + "Prefabs/model.prefab"), Is.Not.EqualTo(group));
        }

        [Test]
        public void VoiceRoutingKeepsLanguagesSeparateAndSpeakerLinesTogether()
        {
            const string root = "Assets/FFOne/Content/Audio/Voice/";
            string english = ContentBundleLayout.SpecializedGroup(root + "en/computress/tut01.ogg");
            Assert.That(ContentBundleLayout.SpecializedGroup(root + "en/computress/tut02.ogg"), Is.EqualTo(english));
            Assert.That(ContentBundleLayout.SpecializedGroup(root + "ru/computress/tut01.ogg"), Is.Not.EqualTo(english));
        }

        [Test]
        public void PublishedGroupsRetainSemanticAddressesAndUseDemandLoading()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            foreach (string address in new[] { "ui/en/gameplay/loading/retrobution-load", "audio/music/00_main_theme", "NativeEditable/Index" })
            {
                var entry = settings.groups.Where(g => g).SelectMany(g => g.entries).Single(e => e.address == address);
                var schema = entry.parentGroup.GetSchema<BundledAssetGroupSchema>();
                Assert.That(schema.IncludeAddressInCatalog, Is.True, address);
                Assert.That(schema.AssetLoadMode.ToString(), Is.EqualTo("RequestedAssetAndDependencies"), address);
                Assert.That(schema.Compression, Is.EqualTo(BundledAssetGroupSchema.BundleCompressionMode.LZ4), address);
            }
            Assert.That(settings.FindGroup("Audio-Music").GetSchema<BundledAssetGroupSchema>().BundleMode,
                Is.EqualTo(BundledAssetGroupSchema.BundlePackingMode.PackSeparately));
            Assert.That(settings.FindGroup("UI-Artwork-en-gameplay-loading"), Is.Not.Null);
        }

        [Test]
        public async Task ConcurrentNativeDocumentReadsSurviveACanceledConsumer()
        {
            const string identity = "data/character_creation/customization";
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var first = NativeData.ObjectAsync(identity);
            var second = NativeData.ObjectAsync(identity);
            var abandoned = NativeData.ObjectAsync(identity, canceled.Token);
            try { await abandoned; Assert.Fail("A pre-canceled consumer must not publish data."); }
            catch (OperationCanceledException) { }
            Assert.That(JToken.DeepEquals(await first, await second), Is.True);
            Assert.That(JToken.DeepEquals(await NativeData.ObjectAsync(identity), await first), Is.True);
        }
    }
}
