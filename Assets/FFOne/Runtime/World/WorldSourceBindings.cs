using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FFOne.World
{
    public static class WorldSourceBindings
    {
        // Compare with immutable accepted import metadata, never the edited scene.
        // Deleting an authored target cannot turn it into a source omission.
        public static ISet<string> FindUnpublishedModels(string document, WorldTileContent accepted)
        {
            if (!accepted) throw new ArgumentNullException(nameof(accepted));
            var published = new HashSet<string>(accepted.Visuals.Select(p => p.LegacyId)
                .Concat(accepted.Colliders.Select(p => p.LegacyId)).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            return new HashSet<string>(JObject.Parse(document).Descendants().OfType<JProperty>()
                .Where(p => p.Name == "models" && p.Value is JArray)
                .SelectMany(p => (JArray)p.Value).Select(v => (string)v).Where(id => !string.IsNullOrEmpty(id) && !published.Contains(id)), StringComparer.Ordinal);
        }
    }
}
