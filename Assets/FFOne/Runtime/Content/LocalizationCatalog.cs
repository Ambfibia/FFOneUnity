using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public static class LocalizationCatalog
    {
        static readonly Dictionary<string, JObject> Languages = new();
        public static async Task PreloadAsync(string language, CancellationToken cancellation = default)
        {
            language = language == "ru" ? "ru" : "en";
            if (Languages.ContainsKey(language)) return;
            // NativeData snapshots Unity fields before parsing on a worker; the cache is
            // published back on the Unity context used by Lookup and frontend builders.
            var document = await NativeData.ObjectAsync("localization/" + language, cancellation);
            cancellation.ThrowIfCancellationRequested();
            Languages[language] = document["entries"] as JObject ?? new JObject();
        }
        public static string Lookup(string key, string language = "en")
        {
            language = language == "ru" ? "ru" : "en";
            if (!Languages.TryGetValue(language, out var entries))
            {
                entries = NativeData.Object("localization/" + language)["entries"] as JObject;
                Languages[language] = entries ?? new JObject();
            }
            return (string)Languages[language][key] ?? (language == "ru" ? Lookup(key, "en") : key);
        }
        public static void Reload() => Languages.Clear();
    }
}
