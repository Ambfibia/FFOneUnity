using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FFOne.Content
{
    public sealed class NativeContentCatalog : ScriptableObject
    {
        public enum AssetKind { Model, Data, Texture, Audio, Font, Binary, ShaderSource }
        [Serializable] public sealed class Entry
        {
            public string Identity, Resource;
            public AssetKind Kind;
        }
        public Entry[] Entries = Array.Empty<Entry>();
        public Entry[] Supplements = Array.Empty<Entry>();
        Dictionary<string, Entry> lookup;
        public Entry Find(string identity)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var entry in Entries) lookup.Add(entry.Identity, entry);
                foreach (var entry in Supplements) lookup.Add(entry.Identity, entry);
            }
            return lookup.TryGetValue(identity.Replace('\\', '/'), out var value) ? value : null;
        }
        public UnityEngine.Object Load(string identity)
        {
            var entry = Find(identity) ?? throw new FileNotFoundException("Unknown native content identity", identity);
            return entry.Kind switch {
                AssetKind.Model => FFOne.Content.ContentAssets.Load<GameObject>(entry.Resource),
                AssetKind.Data => FFOne.Content.ContentAssets.Load<NativeDataDocument>(entry.Resource),
                AssetKind.Texture => FFOne.Content.ContentAssets.Load<Texture2D>(entry.Resource),
                AssetKind.Audio => FFOne.Content.ContentAssets.Load<AudioClip>(entry.Resource),
                AssetKind.Font => FFOne.Content.ContentAssets.Load<Font>(entry.Resource),
                _ => FFOne.Content.ContentAssets.Load<TextAsset>(entry.Resource)
            };
        }
        public Entry Resolve(string owner, string relative)
        {
            var direct = Find(relative); if (direct != null) return direct;
            var parts = new List<string>((owner.Contains('/') ? owner.Substring(0, owner.LastIndexOf('/') + 1) : "").Split('/', StringSplitOptions.RemoveEmptyEntries));
            foreach (string part in relative.Replace('\\', '/').Split('/'))
            {
                if (part == "" || part == ".") continue;
                if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
                else parts.Add(part);
            }
            return Find(string.Join("/", parts));
        }
        void OnEnable() => lookup = null;
    }
}
