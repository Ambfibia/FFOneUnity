using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Tutorial;
using UnityEditor;

namespace FFOne.Editor
{
    /// <summary>Add new imported cues and fill missing fields without replacing authored content.</summary>
    public static class TutorialAudioRefresh
    {
        public readonly struct Result
        {
            public readonly int Added, English, Russian, Categories;
            public bool Changed => Added + English + Russian + Categories > 0;
            public Result(int added, int english, int russian, int categories)
            { Added = added; English = english; Russian = russian; Categories = categories; }
            public override string ToString() => $"{Added} added cues; filled {English} English, {Russian} Russian references and {Categories} categories. Authored fields preserved.";
        }

        public static Result Apply(TutorialContent content, IEnumerable<TutorialAudioDefinition> required)
        {
            if (!content) throw new ArgumentNullException(nameof(content));
            if (required == null) throw new ArgumentNullException(nameof(required));
            var expected = required.ToArray();
            var existing = content.Audio ?? Array.Empty<TutorialAudioDefinition>();
            Validate(existing, false);
            Validate(expected, true); // Validate the whole incoming set before mutating content.
            var output = existing.ToList();
            var lookup = existing.Select((cue, index) => (cue.Cue, index)).ToDictionary(p => p.Cue, p => p.index, StringComparer.OrdinalIgnoreCase);
            int added = 0, english = 0, russian = 0, categories = 0;
            foreach (var cue in expected)
            {
                if (!lookup.TryGetValue(cue.Cue, out int index))
                {
                    output.Add(Copy(cue)); added++;
                    continue;
                }
                var old = existing[index];
                bool fillEnglish = !old.English;
                bool fillRussian = !old.Russian && cue.Russian;
                bool fillCategory = string.IsNullOrWhiteSpace(old.Category);
                if (!fillEnglish && !fillRussian && !fillCategory) continue;
                var updated = Copy(old);
                if (fillEnglish) { updated.English = cue.English; english++; }
                if (fillRussian) { updated.Russian = cue.Russian; russian++; }
                if (fillCategory) { updated.Category = cue.Category; categories++; }
                output[index] = updated;
            }
            var result = new Result(added, english, russian, categories);
            if (result.Changed) { content.Audio = output.ToArray(); EditorUtility.SetDirty(content); }
            return result;
        }

        static TutorialAudioDefinition Copy(TutorialAudioDefinition cue) => new TutorialAudioDefinition {
            Cue = cue.Cue, Category = cue.Category, English = cue.English, Russian = cue.Russian
        };

        static void Validate(TutorialAudioDefinition[] cues, bool imported)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cue in cues)
            {
                if (cue == null || string.IsNullOrWhiteSpace(cue.Cue) || !seen.Add(cue.Cue))
                    throw new InvalidDataException("Tutorial audio contains an empty or duplicate cue identity; no definitions changed.");
                if (imported && (!cue.English || string.IsNullOrWhiteSpace(cue.Category)))
                    throw new InvalidDataException("Imported tutorial cue is missing English audio or category; no definitions changed: " + cue.Cue);
            }
        }
    }
}
