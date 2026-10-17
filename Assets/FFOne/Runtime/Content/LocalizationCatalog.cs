using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public static class LocalizationCatalog
    {
        static readonly Dictionary<string, JObject> Languages = new();
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
