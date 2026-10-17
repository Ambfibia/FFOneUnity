using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    // Sharing is restricted to environment publications. Names are provenance, not content.
    public static class EnvironmentSharedAssets
    {
        public const string Root = "Assets/Resources/Environment/Shared";
        public const string IndexPath = "Assets/Editor/EnvironmentShared/index.json";
        static Dictionary<string, string> index;
        sealed class Pending { public Object Asset; public string Path; }
        static readonly Dictionary<string, Pending> pending = new();
        static readonly Regex Name = new("\"m_Name\":\"(?:\\\\.|[^\"\\\\])*\",", RegexOptions.Compiled);
        public static string Fingerprint(Object value)
        {
            if (!(value is Mesh) && !(value is Material)) throw new ArgumentException("Only meshes and materials can be shared.");
            string json = Name.Replace(EditorJsonUtility.ToJson(value), "", 1);
            // Persistent references serialize as GUID/fileID; transient references cannot form a stable key.
            if (Regex.IsMatch(json, "\"instanceID\":-?[1-9]")) throw new InvalidDataException("Transient reference in shared resource: " + value.name);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").ToLowerInvariant();
        }
        static Dictionary<string, string> Index => index ??= File.Exists(ContentPaths.FilePath(IndexPath))
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(ContentPaths.FilePath(IndexPath)))
            : new Dictionary<string, string>();
        public static void ResetIndex() { index = null; pending.Clear(); }
        internal static void BeginBatch() => pending.Clear();
        public static void Register(Object asset) => Index[Fingerprint(asset)] = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(asset))));
        public static void SaveIndex()
        {
            foreach (var pair in pending)
                if (AssetDatabase.LoadAssetAtPath<Object>(ContentPaths.Resolve(pair.Value.Path))) Index[pair.Key] = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(pair.Value.Path));
            pending.Clear();
            foreach (string key in Index.Where(p => string.IsNullOrEmpty(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(p.Value)))).Select(p => p.Key).ToArray()) Index.Remove(key);
            EnvironmentLibraryBuilder.Folder(Path.GetDirectoryName(IndexPath));
            File.WriteAllText(ContentPaths.FilePath(IndexPath), JsonConvert.SerializeObject(Index, Formatting.Indented));
        }
        internal static Object Acquire(Object source, List<string> created, out string assetPath)
        {
            string key = Fingerprint(source);
            // CreateAsset imports are deferred in a publication batch. Keep the known path and object
            // until StopAssetEditing; GetAssetPath/AssetPathToGUID may still return empty strings here.
            if (pending.TryGetValue(key, out var staged) && staged.Asset)
            { assetPath = staged.Path; return staged.Asset; }
            if (Index.TryGetValue(key, out string guid))
            {
                var candidate = AssetDatabase.LoadAssetAtPath(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(guid))), source.GetType());
                // An artist may have edited the shared asset. Never mistake its old key for its current content.
                if (candidate && Fingerprint(candidate) == key) { assetPath = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(guid)); return candidate; }
            }
            string directory = Root + (source is Mesh ? "/Meshes" : "/Materials");
            EnvironmentLibraryBuilder.Folder(directory);
            string path = ContentPaths.Legacy(AssetDatabase.GenerateUniqueAssetPath(ContentPaths.Resolve(directory + "/" + key + (source is Mesh ? ".asset" : ".mat"))));
            if(string.IsNullOrEmpty(path))throw new InvalidDataException("Unable to allocate shared resource in "+ContentPaths.Resolve(directory));
            var copy = Object.Instantiate(source); copy.name = source.name; copy.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(copy, ContentPaths.Resolve(path)); created.Add(path);
            copy.name = source.name; EditorUtility.SetDirty(copy); AssetDatabase.SaveAssetIfDirty(copy);
            pending[key] = new Pending { Asset = copy, Path = path }; assetPath = path;
            return copy;
        }

        public static string SourceContentHash(string source)
        {
            // Importer code and shader revisions may change Unity's dependency hash without changing source data.
            // Source bytes and external image/mip data still detect actual content changes.
            var files = ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(source), true)).Where(p => p == source ||
                p.StartsWith("Assets/Generated/WorldModelTextures/", StringComparison.Ordinal) ||
                new[] { ".png", ".jpg", ".jpeg", ".dds", ".tga" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .Append(source).Distinct().OrderBy(p => AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(p))).ToArray();
            using var sha = SHA256.Create();
            var text = new StringBuilder();
            foreach (string file in files)
            {
                using var stream = File.OpenRead(ContentPaths.FilePath(LongPath(file)));
                text.Append(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(file))).Append(':').Append(BitConverter.ToString(sha.ComputeHash(stream))).Append('\n');
            }
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
        public static Mesh PublishedMesh(Mesh source)
        {
            string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(source));
            const string prefix = "Assets/Resources/";
            if (!path.StartsWith(prefix + "objects/", StringComparison.Ordinal) || !path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) return source;
            string file = NativeAssetPublisher.ManifestRoot + "/" + path.Substring(prefix.Length, path.Length - prefix.Length - 4) + "/publication.json";
            if (!File.Exists(ContentPaths.FilePath(file))) return source;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long local);
            var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(file)));
            var entry = manifest.Assets.SingleOrDefault(e => e.SourceLocalId == local && e.Type == "UnityEngine.Mesh");
            if (entry == null) return source;
            return AssetDatabase.LoadAssetAtPath<Mesh>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(entry.Guid)))) ?? throw new InvalidDataException("Published collider mesh is missing: " + entry.Path);
        }
        public static string LongPath(string path) => Application.platform == RuntimePlatform.WindowsEditor ? @"\\?\" + Path.GetFullPath(path) : Path.GetFullPath(path);
    }
}
