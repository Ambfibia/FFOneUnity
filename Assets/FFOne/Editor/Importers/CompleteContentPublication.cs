using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class CompleteContentPublication
    {
        const string DataRoot = "Assets/Resources/NativeData/";
        const string Records = "Assets/Editor/NativeDataPublications/";
        [Serializable] sealed class DataRecord { public string SourceHash, InitialValueHash, Guid; }
        static string[] Sources() => ContentPaths.GetFiles(NativeContentImport.DefaultSource, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(p => Path.GetRelativePath(NativeContentImport.DefaultSource, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static string Source(string identity) => NativeContentImport.ResolveSafe(NativeContentImport.DefaultSource, identity);
        static string Destination(string identity) => NativeContentImport.ResolveSafe(NativeContentImport.Destination, identity);
        static string Hash(string path) { using var stream = File.OpenRead(ContentPaths.FilePath(path)); using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static string TextHash(string text) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
        internal static void Folder(string path)
        {
            path = path.Replace('\\', '/');
            // StartAssetEditing defers the folder's AssetDatabase registration. The
            // directory already exists during this batch; creating it again adds " 1".
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path)) || ContentPaths.DirectoryExists(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            if (string.IsNullOrEmpty(ContentPaths.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Cannot create asset folder: " + path);
        }
        static bool Localization(string path) => path == "localization/en.json" || path == "localization/ru.json";

        [MenuItem("FFOne/Content/Prepare Imported Model Dependencies")]
        public static void PrepareModelDependencies()
        {
            int prepared = 0;
            foreach (string relative in Sources().Where(p => p.EndsWith(".glb", StringComparison.Ordinal)))
            {
                string path = "Assets/Resources/" + relative;
                if (AssetDatabase.GetMainAssetTypeAtPath(ContentPaths.Resolve(path)) == typeof(GameObject)) continue;
                var document = NativeGlbDocument.Read(path);
                WorldModelTextureAssets.Prepare(path, document.Json);
                AssetDatabase.ImportAsset(ContentPaths.Resolve(path), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path))) throw new InvalidDataException("Model dependency preparation did not repair: " + relative);
                prepared++; EditorUtility.UnloadUnusedAssetsImmediate();
            }
            Debug.Log("MODEL DEPENDENCIES PREPARED: " + prepared + "; all source models have Unity hierarchies.");
        }

        [MenuItem("FFOne/Content/Import Complete FFOneClient Set")]
        public static void ImportAll()
        {
            var files = Sources();
            int missing = 0;
            foreach (string relative in files)
            {
                if (!File.Exists(ContentPaths.FilePath(Destination(relative)))) { missing++; continue; }
                if (!Localization(relative) && Hash(Source(relative)) != Hash(Destination(relative)) && !EnvironmentGlbBasis.IsAuthoringOverride(relative, Destination(relative)))
                    throw new IOException("Source/local content conflict, no files copied: " + relative);
            }
            Debug.Log($"Complete content preflight: {files.Length} source files, {missing} missing; local translations and normalized GLBs preserved. Copying missing files only.");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string relative in files)
                {
                    string target = Destination(relative);
                    if (File.Exists(ContentPaths.FilePath(target))) continue;
                    Directory.CreateDirectory(ContentPaths.FilePath(Path.GetDirectoryName(target))); File.Copy(ContentPaths.FilePath(Source(relative)), ContentPaths.FilePath(target), false);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("COMPLETE CONTENT IMPORT FINISHED: " + files.Length);
        }

        static IEnumerable<(string Identity, string File)> Documents(string[] files)
        {
            var names = new HashSet<string>(files.Where(p => p.EndsWith(".json", StringComparison.Ordinal)), StringComparer.Ordinal);
            foreach (string name in names.OrderBy(p => p, StringComparer.Ordinal)) yield return (name, Source(name));
            // Also publish Unity-owned choreography/UI data, without replacing FFOneClient identities.
            foreach (string path in ContentPaths.GetFiles("Assets/Resources", "*.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath("Assets/Resources", path).Replace('\\', '/');
                if (!names.Contains(relative)) yield return (relative, path);
            }
        }
        static void CheckData(string identity, string file)
        {
            string asset = DataRoot + identity + ".asset", record = Records + identity;
            if (File.Exists(ContentPaths.FilePath(record)))
            {
                var saved = JsonConvert.DeserializeObject<DataRecord>(File.ReadAllText(ContentPaths.FilePath(record)));
                if (saved.SourceHash != Hash(file) || saved.Guid != AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(asset)) || !AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(asset)))
                    throw new InvalidDataException("Data publication source/identity drift; authored data preserved: " + identity);
            }
            else if (File.Exists(ContentPaths.FilePath(asset)))
            {
                // Resume a batch interrupted between asset save and provenance save.
                // Adopt only an exact structured match; never rewrite a conflicting asset.
                var existing = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(asset));
                var probe = ScriptableObject.CreateInstance<NativeDataDocument>();
                try { probe.Import(File.ReadAllText(ContentPaths.FilePath(file)));
                    if (!existing || existing.Identity != identity || existing.ToJson() != probe.ToJson()) throw new IOException("Native data destination occupied: " + asset); }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
            else if (File.Exists(ContentPaths.FilePath(asset + ".meta"))) throw new IOException("Native data has an orphan GUID: " + asset);
        }
        [MenuItem("FFOne/Content/Publish All Structured Data")]
        public static void PublishData()
        {
            var source = Sources(); var documents = Documents(source).ToArray();
            int count = 0, totalNodes = 0;
            // Full preflight before publication; bounded memory, even for XDT and world behaviour.
            foreach (var item in documents)
            {
                CheckData(item.Identity, item.File);
                var probe = ScriptableObject.CreateInstance<NativeDataDocument>();
                try { probe.Import(File.ReadAllText(ContentPaths.FilePath(item.File))); } finally { UnityEngine.Object.DestroyImmediate(probe); }
                if (++count % 250 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Data preflight " + count + "/" + documents.Length); }
            }
            count = 0;
            for (int start = 0; start < documents.Length; start += 100)
            {
                var pending = new List<(string Path, string Record, DataRecord Value)>();
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var item in documents.Skip(start).Take(100))
                    {
                        string path = DataRoot + item.Identity + ".asset", record = Records + item.Identity;
                        if (File.Exists(ContentPaths.FilePath(record))) continue;
                        Folder(Path.GetDirectoryName(path)); Folder(Path.GetDirectoryName(record));
                        var document = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(path));
                        if (!document)
                        {
                            document = ScriptableObject.CreateInstance<NativeDataDocument>();
                            document.Identity = item.Identity; document.Import(File.ReadAllText(ContentPaths.FilePath(item.File)));
                            totalNodes += document.Nodes.Length;
                            AssetDatabase.CreateAsset(document, ContentPaths.Resolve(path));
                        }
                        pending.Add((path, record, new DataRecord { SourceHash = Hash(item.File), InitialValueHash = TextHash(document.ToJson()) }));
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                foreach (var saved in pending)
                {
                    saved.Value.Guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(saved.Path));
                    if (string.IsNullOrEmpty(saved.Value.Guid)) throw new InvalidDataException("Published data has no GUID: " + saved.Path);
                    File.WriteAllText(ContentPaths.FilePath(saved.Record), JsonConvert.SerializeObject(saved.Value));
                }
                count = Math.Min(start + 100, documents.Length);
                AssetDatabase.SaveAssets(); EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Data published " + count + "/" + documents.Length);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"ALL STRUCTURED DATA PUBLISHED: {documents.Length} documents, {totalNodes} new typed values.");
        }

        public static void PrepareAndPublishData()
        {
            PrepareModelDependencies();
            PublishData();
        }

        static void CheckWhitespaceRepair(NativeDataDocument current, NativeDataDocument original, string identity)
        {
            if (!current || current.Identity != identity || current.Nodes.Length != original.Nodes.Length)
                throw new InvalidDataException("Data layout/identity changed; exact-text repair preserves authored work: " + identity);
            static string Fold(string value) => System.Text.RegularExpressions.Regex.Replace(value ?? "", @"\s+", " ").Trim();
            static bool Same(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.Ordinal) || Fold(a) == Fold(b);
            for (int i = 0; i < current.Nodes.Length; i++)
            {
                var a = current.Nodes[i]; var b = original.Nodes[i];
                if (a.Kind != b.Kind || a.End != b.End || !Same(a.Key, b.Key) || !Same(a.Value, b.Value))
                    throw new InvalidDataException("Non-whitespace authored change; exact-text repair refused: " + identity + " node " + i);
            }
        }

        [MenuItem("FFOne/Content/Upgrade Exact Data Text")]
        public static void RepairDataSerialization()
        {
            var documents = Documents(Sources()).ToArray(); int checkedCount = 0, repaired = 0;
            // Prove every old snapshot is the initial publication and differs at most
            // by YAML whitespace folding before touching any published document.
            foreach (var item in documents)
            {
                CheckData(item.Identity, item.File);
                var current = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(DataRoot + item.Identity + ".asset"));
                if (current.TextEncodingVersion == 0)
                {
                    var record = JsonConvert.DeserializeObject<DataRecord>(File.ReadAllText(ContentPaths.FilePath(Records + item.Identity)));
                    var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                    try
                    {
                        original.Import(File.ReadAllText(ContentPaths.FilePath(item.File)));
                        if (record.InitialValueHash != TextHash(original.ToJson())) throw new InvalidDataException("Initial publication text differs: " + item.Identity);
                        CheckWhitespaceRepair(current, original, item.Identity);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(original); }
                }
                if (++checkedCount % 100 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Exact text preflight " + checkedCount + "/" + documents.Length); }
            }
            for (int start = 0; start < documents.Length; start += 100)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var item in documents.Skip(start).Take(100))
                    {
                        var current = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(DataRoot + item.Identity + ".asset"));
                        if (current.TextEncodingVersion != 0) continue;
                        var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                        try
                        {
                            original.Import(File.ReadAllText(ContentPaths.FilePath(item.File))); CheckWhitespaceRepair(current, original, item.Identity);
                            current.Nodes = original.Nodes; current.TextEncodingVersion = 1;
                            EditorUtility.SetDirty(current); AssetDatabase.SaveAssetIfDirty(current); repaired++;
                        }
                        finally { UnityEngine.Object.DestroyImmediate(original); }
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Exact text saved " + Math.Min(start + 100, documents.Length) + "/" + documents.Length);
            }
            const string workspacePath = "Assets/Editor/FFOneClientData/world-workspace.json.asset";
            var workspace = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(workspacePath));
            if (workspace && workspace.TextEncodingVersion == 0)
            {
                var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                try
                {
                    original.Import(File.ReadAllText(ContentPaths.FilePath(Path.GetFullPath(Path.Combine(NativeContentImport.DefaultSource, "../editor/world-workspace.json")))));
                    CheckWhitespaceRepair(workspace, original, "editor/world-workspace.json");
                    workspace.Nodes = original.Nodes; workspace.TextEncodingVersion = 1;
                    EditorUtility.SetDirty(workspace); AssetDatabase.SaveAssetIfDirty(workspace);
                }
                finally { UnityEngine.Object.DestroyImmediate(original); }
            }
            Debug.Log("EXACT DATA TEXT UPGRADED: " + repaired + "; existing GUIDs retained.");
        }

        [MenuItem("FFOne/Content/Publish All Models")]
        public static void PublishModels()
        {
            NativeModelPublicationIndex.Migrate();
            string[] models = Sources().Where(p => p.EndsWith(".glb", StringComparison.Ordinal)).Select(p => "Assets/Resources/" + p).ToArray();
            int count = 0;
            foreach (string path in models)
            {
                NativeAssetPublisher.Preflight(path);
                if (++count % 50 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Model preflight " + count + "/" + models.Length); }
            }
            count = 0;
            for (int start = 0; start < models.Length; start += 25)
            {
                var plans = models.Skip(start).Take(25).Select(path => { var plan = NativeAssetPublisher.Preflight(path); plan.RegisterRuntime = false; return plan; }).ToArray();
                NativeAssetPublisher.PublishBatch(plans);
                count = Math.Min(start + 25, models.Length);
                AssetDatabase.SaveAssets(); EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Models published " + count + "/" + models.Length);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeModelPublicationIndex.Path));
            if (!catalog) { catalog = ScriptableObject.CreateInstance<NativeModelCatalog>(); AssetDatabase.CreateAsset(catalog, ContentPaths.Resolve(NativeModelPublicationIndex.Path)); }
            var existing = catalog.Bindings.ToDictionary(b => b.OriginalResource, StringComparer.Ordinal);
            foreach (string path in models)
            {
                string resource = NativeModelPublicationIndex.Resource(path);
                if (!existing.ContainsKey(resource))
                {
                    string manifestPath = NativeAssetPublisher.ManifestRoot + "/" + resource + "/publication.json";
                    var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(manifestPath)));
                    existing.Add(resource, new NativeModelCatalog.Binding { OriginalResource = resource, EditableResource = NativeModelPublicationIndex.Resource(manifest.PrefabPath) });
                }
            }
            catalog.Bindings = existing.Values.OrderBy(b => b.OriginalResource, StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("ALL MODELS PUBLISHED: " + models.Length);
        }

        [MenuItem("FFOne/Content/Build Complete Lazy Catalog")]
        public static void BuildCatalog()
        {
            var entries = new List<NativeContentCatalog.Entry>();
            foreach (string relative in Sources())
            {
                string ext = Path.GetExtension(relative), resource = relative.Substring(0, relative.Length - ext.Length);
                NativeContentCatalog.AssetKind kind;
                switch (ext)
                {
                    case ".glb": kind = NativeContentCatalog.AssetKind.Model; resource = NativeModelAssets.PublishedResourcePath(resource); break;
                    case ".json": kind = NativeContentCatalog.AssetKind.Data; resource = NativeData.Resource(relative); break;
                    case ".png": kind = NativeContentCatalog.AssetKind.Texture; break;
                    case ".ogg": kind = NativeContentCatalog.AssetKind.Audio; break;
                    case ".otf": case ".ttf": kind = NativeContentCatalog.AssetKind.Font; break;
                    case ".bin": kind = NativeContentCatalog.AssetKind.Binary; resource = "NativeBinary/" + relative; break;
                    case ".wgsl": kind = NativeContentCatalog.AssetKind.ShaderSource; resource = "NativeShaderSources/" + relative; break;
                    default: throw new InvalidDataException("Unclassified source asset: " + relative);
                }
                if (ext == ".bin" || ext == ".wgsl")
                {
                    string target = "Assets/Resources/" + resource + (ext == ".bin" ? ".bytes" : ".txt");
                    Folder(Path.GetDirectoryName(target));
                    if (!File.Exists(ContentPaths.FilePath(target))) File.Copy(ContentPaths.FilePath(Source(relative)), ContentPaths.FilePath(target));
                    else if (Hash(target) != Hash(Source(relative))) throw new IOException("Authored binary/text conflict: " + target);
                    AssetDatabase.ImportAsset(ContentPaths.Resolve(target));
                }
                entries.Add(new NativeContentCatalog.Entry { Identity = relative, Resource = resource, Kind = kind });
            }
            const string catalogPath = "Assets/Resources/NativeContent.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<NativeContentCatalog>(ContentPaths.Resolve(catalogPath));
            if (!catalog && (File.Exists(ContentPaths.FilePath(catalogPath)) || File.Exists(ContentPaths.FilePath(catalogPath + ".meta")))) throw new IOException("Content catalog destination occupied: " + catalogPath);
            if (!catalog) { catalog = ScriptableObject.CreateInstance<NativeContentCatalog>(); AssetDatabase.CreateAsset(catalog, ContentPaths.Resolve(catalogPath)); }
            var previous = catalog.Entries.ToDictionary(e => e.Identity, StringComparer.Ordinal);
            foreach (var entry in entries) if (previous.TryGetValue(entry.Identity, out var authored))
            {
                if (authored.Kind != entry.Kind) throw new InvalidDataException("Authored content kind differs: " + entry.Identity);
                entry.Resource = authored.Resource;
            }
            var supplements = new List<NativeContentCatalog.Entry>();
            const string aliases = "Assets/Editor/NativePublications/ExactTextureAliases.json";
            if (File.Exists(ContentPaths.FilePath(aliases)))
                foreach (string alias in Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(ContentPaths.FilePath(aliases)))["aliases"].Values<string>())
                    supplements.Add(new NativeContentCatalog.Entry { Identity = alias, Resource = alias.Substring(0, alias.Length - 4), Kind = NativeContentCatalog.AssetKind.Texture });
            const string verifiedAliases = "Assets/Editor/NativePublications/VerifiedContentAliases.json";
            if (File.Exists(ContentPaths.FilePath(verifiedAliases)))
                foreach (var alias in JArray.Parse(File.ReadAllText(ContentPaths.FilePath(verifiedAliases))))
                {
                    string identity = (string)alias["identity"], target = (string)alias["target"], expected = (string)alias["sha256"];
                    if (!string.Equals(Hash(Source(target)), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Verified alias source changed: " + identity);
                    var targetEntry = entries.Single(e => e.Identity == target);
                    if (entries.Any(e => e.Identity == identity) || supplements.Any(e => e.Identity == identity)) throw new InvalidDataException("Alias shadows an existing identity: " + identity);
                    supplements.Add(new NativeContentCatalog.Entry { Identity = identity, Resource = targetEntry.Resource, Kind = targetEntry.Kind });
                }
            var registered = new HashSet<string>(entries.Select(e => e.Identity), StringComparer.Ordinal);
            foreach (var document in Documents(Sources()).Where(d => !registered.Contains(d.Identity)))
                supplements.Add(new NativeContentCatalog.Entry { Identity = document.Identity, Resource = NativeData.Resource(document.Identity), Kind = NativeContentCatalog.AssetKind.Data });
            catalog.Entries = entries.ToArray(); catalog.Supplements = supplements.ToArray(); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            PublishEditorWorkspace();
            Debug.Log("COMPLETE LAZY CATALOG: " + entries.Count);
        }

        static void PublishEditorWorkspace()
        {
            string source = Path.GetFullPath(Path.Combine(NativeContentImport.DefaultSource, "../editor/world-workspace.json"));
            if (!File.Exists(ContentPaths.FilePath(source))) return;
            const string target = "Assets/Editor/FFOneClientData/world-workspace.json.asset";
            if (AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(target))) return;
            if (File.Exists(ContentPaths.FilePath(target)) || File.Exists(ContentPaths.FilePath(target + ".meta"))) throw new IOException("Editor workspace destination occupied.");
            Folder(Path.GetDirectoryName(target));
            var document = ScriptableObject.CreateInstance<NativeDataDocument>(); document.Identity = "editor/world-workspace.json";
            document.Import(File.ReadAllText(ContentPaths.FilePath(source))); AssetDatabase.CreateAsset(document, ContentPaths.Resolve(target));
        }

        [MenuItem("FFOne/Content/Validate Complete Publication")]
        public static void ValidateAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NativeContentCatalog>(ContentPaths.Resolve("Assets/Resources/NativeContent.asset"));
            var files = Sources();
            if (!catalog || !catalog.Entries.Select(e => e.Identity).SequenceEqual(files)) throw new InvalidDataException("Catalog does not cover the source inventory exactly.");
            int count = 0;
            foreach (var entry in catalog.Entries)
            {
                string path = "Assets/Resources/" + entry.Resource;
                string extension = entry.Kind switch { NativeContentCatalog.AssetKind.Model => ".prefab", NativeContentCatalog.AssetKind.Data => ".asset", NativeContentCatalog.AssetKind.Binary => ".bytes", NativeContentCatalog.AssetKind.ShaderSource => ".txt", _ => Path.GetExtension(entry.Identity) };
                var asset = AssetDatabase.LoadMainAssetAtPath(ContentPaths.Resolve(path + extension));
                if (!asset) throw new InvalidDataException("Missing imported Unity asset: " + entry.Identity);
                if (entry.Kind == NativeContentCatalog.AssetKind.Model)
                {
                    if (!(asset is GameObject)) throw new InvalidDataException("Invalid prefab: " + entry.Identity);
                    if (ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(path + extension), true)).Any(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Prefab still depends on GLB: " + entry.Identity);
                }
                if (asset is NativeDataDocument document)
                {
                    var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                    try { original.Import(File.ReadAllText(ContentPaths.FilePath(Source(entry.Identity)))); if (original.ToJson() != document.ToJson()) throw new InvalidDataException("Data differs from FFOneClient (authored edits retained): " + entry.Identity); }
                    finally { UnityEngine.Object.DestroyImmediate(original); }
                }
                if (entry.Kind == NativeContentCatalog.AssetKind.Texture && !(asset is Texture2D)) throw new InvalidDataException("PNG is not Texture2D: " + entry.Identity);
                if (entry.Kind == NativeContentCatalog.AssetKind.Audio && !(asset is AudioClip)) throw new InvalidDataException("OGG is not AudioClip: " + entry.Identity);
                if (entry.Kind == NativeContentCatalog.AssetKind.Font && !(asset is Font)) throw new InvalidDataException("Font is not imported: " + entry.Identity);
                if (entry.Kind == NativeContentCatalog.AssetKind.Data && !(asset is NativeDataDocument)) throw new InvalidDataException("JSON is not structured native data: " + entry.Identity);
                if (entry.Kind == NativeContentCatalog.AssetKind.Binary || entry.Kind == NativeContentCatalog.AssetKind.ShaderSource)
                    if (!(asset is TextAsset) || Hash(path + extension) != Hash(Source(entry.Identity))) throw new InvalidDataException("Binary/shader source differs: " + entry.Identity);
                if (++count % 100 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect(); Debug.Log("Coverage verified " + count + "/" + files.Length); }
            }
            foreach (var entry in catalog.Supplements)
            {
                var asset = catalog.Load(entry.Identity);
                if (!asset) throw new InvalidDataException("Missing supplemental asset: " + entry.Identity);
                if (asset is NativeDataDocument document)
                {
                    var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                    try { original.Import(File.ReadAllText(ContentPaths.FilePath(Destination(entry.Identity)))); if (original.ToJson() != document.ToJson()) throw new InvalidDataException("Supplemental data differs: " + entry.Identity); }
                    finally { UnityEngine.Object.DestroyImmediate(original); }
                }
            }
            string workspace = Path.GetFullPath(Path.Combine(NativeContentImport.DefaultSource, "../editor/world-workspace.json"));
            if (File.Exists(ContentPaths.FilePath(workspace)))
            {
                var published = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve("Assets/Editor/FFOneClientData/world-workspace.json.asset"));
                var original = ScriptableObject.CreateInstance<NativeDataDocument>();
                try { original.Import(File.ReadAllText(ContentPaths.FilePath(workspace))); if (!published || published.ToJson() != original.ToJson()) throw new InvalidDataException("Editor workspace publication differs."); }
                finally { UnityEngine.Object.DestroyImmediate(original); }
            }
            var models = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeModelPublicationIndex.Path));
            if (!models || models.Bindings.Length != files.Count(p => p.EndsWith(".glb", StringComparison.Ordinal)) || models.Bindings.Any(b => b.Original || b.Editable))
                throw new InvalidDataException("Model catalog must cover all source models without eager object references.");
            Debug.Log("COMPLETE CONTENT VALIDATED: " + count + " / " + files.Length + "; all JSON values/types/order, supplemental/editor data, lazy bindings and native model dependencies checked.");
        }
    }
}
