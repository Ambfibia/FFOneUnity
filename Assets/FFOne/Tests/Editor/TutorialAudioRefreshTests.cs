using ContentPaths = FFOne.Content.ContentPaths;
using System.IO;
using FFOne.Editor;
using FFOne.Tutorial;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class TutorialAudioRefreshTests
    {
        [Test]
        public void RefreshAddsMissingCuesFillsOnlyEmptyFieldsAndPreservesAuthoredWorldActorsAndAudio()
        {
            var content = ScriptableObject.CreateInstance<TutorialContent>();
            var world = new GameObject("Authored world");
            var authored = AudioClip.Create("authored", 10, 1, 8000, false);
            var imported = AudioClip.Create("imported", 10, 1, 8000, false);
            var actors = new[] { new TutorialActorDefinition { NpcType = 123, Model = world } };
            try
            {
                content.World = world; content.ButtercupNano = world; content.Actors = actors;
                content.Audio = new[] {
                    new TutorialAudioDefinition { Cue = "Existing", Category = "authored-category", English = authored },
                    new TutorialAudioDefinition { Cue = "Empty", Category = " ", Russian = authored },
                    new TutorialAudioDefinition { Cue = "Custom", English = authored, Category = "custom" }
                };
                var incoming = new[] {
                    new TutorialAudioDefinition { Cue = "existing", Category = "voice", English = imported, Russian = imported },
                    new TutorialAudioDefinition { Cue = "Empty", Category = "voice", English = imported, Russian = imported },
                    new TutorialAudioDefinition { Cue = "New", Category = "voice", English = imported }
                };
                var result = TutorialAudioRefresh.Apply(content, incoming);
                Assert.That(result.Added, Is.EqualTo(1)); Assert.That(result.English, Is.EqualTo(1));
                Assert.That(result.Russian, Is.EqualTo(1)); Assert.That(result.Categories, Is.EqualTo(1));
                Assert.That(content.Audio[0].Cue, Is.EqualTo("Existing"));
                Assert.That(content.Audio[0].English, Is.SameAs(authored));
                Assert.That(content.Audio[0].Category, Is.EqualTo("authored-category"));
                Assert.That(content.Audio[0].Russian, Is.SameAs(imported));
                Assert.That(content.Audio[1].Russian, Is.SameAs(authored));
                Assert.That(content.Audio[1].English, Is.SameAs(imported));
                Assert.That(content.Audio[1].Category, Is.EqualTo("voice"));
                Assert.That(content.Audio[2].Cue, Is.EqualTo("Custom"));
                Assert.That(content.World, Is.SameAs(world)); Assert.That(content.ButtercupNano, Is.SameAs(world)); Assert.That(content.Actors, Is.SameAs(actors));
                var applied = content.Audio;
                Assert.That(TutorialAudioRefresh.Apply(content, incoming).Changed, Is.False);
                Assert.That(content.Audio, Is.SameAs(applied));
            }
            finally { Object.DestroyImmediate(content); Object.DestroyImmediate(world); Object.DestroyImmediate(authored); Object.DestroyImmediate(imported); }
        }

        [Test]
        public void InvalidLastIncomingCueDoesNotPartiallyFillEarlierDefinitions()
        {
            var content = ScriptableObject.CreateInstance<TutorialContent>();
            var clip = AudioClip.Create("imported", 10, 1, 8000, false);
            try
            {
                var existing = new[] { new TutorialAudioDefinition { Cue = "First" } };
                content.Audio = existing;
                Assert.Throws<InvalidDataException>(() => TutorialAudioRefresh.Apply(content, new[] {
                    new TutorialAudioDefinition { Cue = "First", Category = "voice", English = clip },
                    new TutorialAudioDefinition { Cue = "Last", Category = "voice" }
                }));
                Assert.That(content.Audio, Is.SameAs(existing));
                Assert.That(existing[0].English, Is.Null); Assert.That(existing[0].Category, Is.Null);
            }
            finally { Object.DestroyImmediate(content); Object.DestroyImmediate(clip); }
        }
    }
}
