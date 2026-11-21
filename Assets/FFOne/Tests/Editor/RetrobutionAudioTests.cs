using System.Collections;
using System.Linq;
using System.Threading;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FFOne.Tests
{
    public sealed class RetrobutionAudioTests
    {
        RetrobutionAudioCatalog Catalog() => new(NativeData.Object("data/audio/retrobution-world"));

        [Test]
        public void PublishedZonesKeepLobbyAndTutorialSilenceSeparateFromWorld()
        {
            var catalog = Catalog();
            Assert.That(catalog.Music.Length, Is.EqualTo(136));
            Assert.That(catalog.Ambient.Length, Is.EqualTo(136));
            Assert.That(catalog.Find(catalog.Music, Vector2.one, false).Address, Is.EqualTo(RetrobutionAudioController.MainTheme));
            Assert.That(catalog.Find(catalog.Music, new Vector2(768, 768), false).Address, Is.Null);
            Assert.That(catalog.Find(catalog.Music, new Vector2(-768, 768), false), Is.Null,
                "Music polygons already use Unity X/Z; reflecting X again loses every world region.");
        }

        [Test]
        public void InstanceSelectionAndRetroDowntownRoutesMatchTheirPublishedZones()
        {
            var catalog = Catalog();
            var lair = catalog.Music.First(z => z.Address == "audio/music/buttercuplair1");
            var center = lair.Points.Aggregate(Vector2.zero, (a, p) => a + p) / lair.Points.Length;
            Assert.That(lair.Instance, Is.True);
            Assert.That(catalog.Find(catalog.Music, center, true).Address, Is.EqualTo(lair.Address));
            Assert.That(catalog.Find(catalog.Music, center, false)?.Address, Is.Not.EqualTo(lair.Address));
            foreach (int index in new[] { 60, 61, 62 })
            {
                Assert.That(catalog.Music[index].Address, Is.EqualTo("audio/music/midtown_v1"));
                Assert.That(catalog.Music[index].Delay, Is.EqualTo(45));
            }
            Assert.That(catalog.Music[63].Address, Is.EqualTo("audio/music/05_midtown"));
            Assert.That(catalog.Music[64].Address, Is.Null);
        }

        [Test]
        public void EveryResolvedZoneUsesAnExistingSemanticAudioAddress()
        {
            var catalog = Catalog();
            foreach (string address in catalog.Music.Concat(catalog.Ambient).Select(z => z.Address).Where(a => a != null).Distinct())
                Assert.That(ContentAssets.Load<AudioClip>(address), Is.Not.Null, address);
        }

        [UnityTest]
        public IEnumerator WorldTrackWaits180SecondsAfterEndingButDifferentTrackStartsImmediately()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            yield return ExerciseWorldMusic();
        }

        static IEnumerator ExerciseWorldMusic()
        {
            var owner = new GameObject("World music repeat validation");
            using var lifetime = new CancellationTokenSource();
            try
            {
                owner.AddComponent<AudioListener>();
                var channel = new RetrobutionAudioChannel(owner.AddComponent<AudioSource>(), false, lifetime.Token);
                const string track = "audio/music/01_future", destination = "audio/music/05_midtown";
                Assert.That(channel.SelectWorldMusic(track, 0), Is.True);
                yield return WaitForTrack(channel, track, 0);
                channel.Source.timeSamples = channel.Source.clip.samples - channel.Source.clip.frequency / 10;
                float deadline = Time.realtimeSinceStartup + 10;
                while (channel.Source.isPlaying && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(channel.Source.isPlaying, Is.False, "The first clip must finish naturally.");

                channel.Tick(0, .5f, 100);
                Assert.That(channel.SelectWorldMusic(track, 279.999f), Is.False, "The same address, including a neighbouring zone, must wait.");
                Assert.That(channel.Starts, Is.EqualTo(1));
                Assert.That(channel.SelectWorldMusic(track, 280), Is.True, "Replay becomes eligible at exactly 180 seconds.");
                yield return WaitForTrack(channel, track, 280);
                Assert.That(channel.Starts, Is.EqualTo(2));

                channel.Source.Stop(); // Exercise another end without waiting for a second complete clip.
                channel.Tick(0, .5f, 300);
                Assert.That(channel.SelectWorldMusic(track, 301), Is.False);
                Assert.That(channel.SelectWorldMusic(destination, 301), Is.True);
                yield return WaitForTrack(channel, destination, 301);
                Assert.That(channel.Starts, Is.EqualTo(3));
                Assert.That(channel.SelectWorldMusic(track, 302), Is.True, "The former track is new again when arriving from a different track.");
                yield return WaitForTrack(channel, track, 302);
            }
            finally
            {
                lifetime.Cancel();
                Object.DestroyImmediate(owner);
            }
            yield return new ExitPlayMode();
        }

        static IEnumerator WaitForTrack(RetrobutionAudioChannel channel, string address, float now)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while ((channel.CurrentAddress != address || !channel.Source.isPlaying) && Time.realtimeSinceStartup < deadline)
            {
                channel.Tick(.1f, .5f, now);
                yield return null;
            }
            Assert.That(channel.CurrentAddress, Is.EqualTo(address));
            Assert.That(channel.Source.isPlaying, Is.True);
        }
    }
}
