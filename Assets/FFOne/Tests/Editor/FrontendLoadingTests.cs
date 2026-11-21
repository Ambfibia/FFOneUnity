using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class FrontendLoadingTests
    {
        [Test]
        public void LoadingDialogUsesPrimaryCenterAnd681PixelScale()
        {
            Assert.That(RetrobutionLoadingPresentation.Window(1281, 681), Is.EqualTo(new Rect(386, 186, 509, 309)),
                "FullScreen truncates the screen pivot before scaling, retaining the source half-pixel asymmetry.");
            var high = RetrobutionLoadingPresentation.Window(1920, 1080);
            Assert.That(high.x, Is.EqualTo(557.1806f).Within(.001));
            Assert.That(high.y, Is.EqualTo(295.7709f).Within(.001));
            Assert.That(high.width, Is.EqualTo(807.2247f).Within(.001));
            Assert.That(high.height, Is.EqualTo(490.0441f).Within(.001));
        }

        [TestCase(0, 15)]
        [TestCase(.5f, 239)]
        [TestCase(1, 463)]
        [TestCase(-1, 15)]
        [TestCase(2, 463)]
        public void LoadingFillKeepsOriginalEndCapBeforeThe448PixelClip(float progress, float width)
            => Assert.That(RetrobutionLoadingPresentation.FillWidth(progress), Is.EqualTo(width));

        [Test]
        public void InvalidLoadingProgressCannotBreakTheBarLayout()
            => Assert.That(RetrobutionLoadingPresentation.FillWidth(float.NaN), Is.EqualTo(15));

        [Test]
        public void PrimaryLoadingArtHasItsOwnAddressWithoutReplacingAcceptedBackground()
        {
            Assert.That(RetrobutionLoadingPresentation.BackgroundAddress, Is.EqualTo("ui/en/gameplay/loading/retrobution-load"));
            const string primary = "Assets/FFOne/Content/UI/Artwork/en/gameplay/loading/retrobution-load.png";
            using var input = File.OpenRead(primary);
            using var sha = SHA256.Create();
            string hash = System.BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            Assert.That(hash, Is.EqualTo("a08ddd03d9d92815da5aafb628b24e08c27b9e2ce483762b0f225db7ab6bb6b9"),
                "Primary main.unity3d sharedassets0.assets Texture2D369, exact PNG payload.");
            Assert.That(ContentAssets.Load<Texture2D>("ui/en/gameplay/loading/load"), Is.Not.Null);
            Assert.That(ContentAssets.Load<Texture2D>(RetrobutionLoadingPresentation.BackgroundAddress), Is.Not.Null);
        }

        [TestCase("en", "ui/en/gameplay/loading/retrobution-load")]
        [TestCase("ru", "ui/ru/gameplay/loading/load")]
        public async Task LoadingUsesThePublishedBackgroundForTheTextLocale(string locale, string address)
        {
            var expected = ContentAssets.Load<Texture2D>(address);
            Assert.That(expected, Is.Not.Null);
            Assert.That(await RetrobutionLoadingPresentation.LoadBackgroundAsync(locale), Is.SameAs(expected));
        }
    }
}
