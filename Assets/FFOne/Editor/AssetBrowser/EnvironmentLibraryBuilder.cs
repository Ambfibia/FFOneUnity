using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class EnvironmentLibraryBuilder
    {
        public const string CatalogPath = "Assets/FFOne/Editor/Environment/EnvironmentLibrary.asset";
        public static string Status { get; private set; } = "";
        static Queue<EnvironmentLibrary.Entry> pending;
        static int moved;
        public static bool IsRunning => pending != null;
        public static EnvironmentLibrary Load() => AssetDatabase.LoadAssetAtPath<EnvironmentLibrary>(ContentPaths.Resolve(CatalogPath));

        [MenuItem("FFOne/Environment/Rebuild Library Index")]
        public static void Build()
        {
            if (IsRunning) throw new InvalidOperationException("Environment organization is running.");
            var index = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeModelPublicationIndex.Path));
            if (!index) throw new InvalidDataException("Publish native models first.");
            var library = Load();
            var old = library ? library.Entries.ToDictionary(e => e.Identity, StringComparer.Ordinal) : new Dictionary<string, EnvironmentLibrary.Entry>();
            if (!library) { Folder(Path.GetDirectoryName(CatalogPath)); library = ScriptableObject.CreateInstance<EnvironmentLibrary>(); AssetDatabase.CreateAsset(library, ContentPaths.Resolve(CatalogPath)); }
            var entries = new List<EnvironmentLibrary.Entry>();
            foreach (var binding in index.Bindings.Where(b => EnvironmentAssetRules.IsEnvironment(b.OriginalResource)))
            {
                string identity = binding.OriginalResource;
                string path = "Assets/Resources/" + binding.EditableResource + ".prefab";
                string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path));
                if (string.IsNullOrEmpty(guid)) throw new FileNotFoundException("Missing published environment prefab", path);
                string definition = Path.GetDirectoryName("Assets/Resources/" + identity) + "/object.json";
                var data = File.Exists(ContentPaths.FilePath(definition)) ? JObject.Parse(File.ReadAllText(ContentPaths.FilePath(definition))) : new JObject();
                var entry = old.TryGetValue(identity, out var saved) ? saved : new EnvironmentLibrary.Entry { Identity = identity };
                entry.Name = (string)data["name"] ?? identity.Split('/').Reverse().Skip(1).First();
                entry.Family = (string)data["family"] ?? "";
                entry.SourcePlacements = (data["sourceNodes"] as JArray)?.Count ?? 0;
                entry.PrefabGuid = guid;
                if (!entry.ManuallyClassified) entry.Category = EnvironmentAssetRules.Classify(identity, entry.Name);
                entries.Add(entry);
            }
            library.Entries = entries.OrderBy(e => e.Category).ThenBy(e => e.Name).ThenBy(e => e.Identity).ToArray();
            EnvironmentLibraryGrouping.Rebuild(library);
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
            Status = $"{entries.Count} объектов окружения; {entries.Select(e => e.Category).Distinct().Count()} категорий.";
            Debug.Log(Status);
        }

        [MenuItem("FFOne/Environment/Organize Published Environment")]
        public static void BeginOrganize()
        {
            if (System.IO.File.Exists(FFOne.Content.ContentPaths.Receipt) && System.IO.Directory.Exists("Assets/FFOne/Content/Environment"))
                throw new InvalidOperationException("The content layout is already migrated. Classification is saved in the library; use the migration manifest for reviewed physical moves so resource identities remain stable.");
            if (IsRunning) return;
            var library = Load(); if (!library) { Build(); library = Load(); }
            // Preflight the entire move set before the first move. No snapshots or duplicate assets.
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in library.Entries)
            {
                string target = EnvironmentAssetRules.DirectoryFor(entry.Identity, entry.Category);
                if (!targets.Add(target)) throw new InvalidDataException("Environment destination collision: " + target);
                CheckMove(entry, target);
            }
            foreach (string parent in library.Entries.Select(e => Path.GetDirectoryName(EnvironmentAssetRules.DirectoryFor(e.Identity, e.Category))).Distinct())
                Directory.CreateDirectory(ContentPaths.FilePath(parent));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            pending = new Queue<EnvironmentLibrary.Entry>(library.Entries);
            moved = 0; EditorApplication.update += Advance;
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { OrganizeBatch(400); }
            catch (Exception error) { Stop(); Status = error.Message; Debug.LogException(error); }
        }
        public static void Stop() { pending = null; EditorApplication.update -= Advance; }
        public static void OrganizeBatch(int count)
        {
            if (pending == null) return;
            var index = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(NativeModelPublicationIndex.Path));
            var bindings = index.Bindings.ToDictionary(b => b.OriginalResource, StringComparer.Ordinal);
            var content = AssetDatabase.LoadAssetAtPath<NativeContentCatalog>(ContentPaths.Resolve("Assets/Resources/NativeContent.asset"));
            var entries = content ? content.Entries.Concat(content.Supplements).Where(e => e.Kind == NativeContentCatalog.AssetKind.Model).GroupBy(e => e.Identity).ToDictionary(g => g.Key, g => g.ToArray()) : null;
            // CreateFolder must finish importing parents before MoveAsset runs inside an editing batch.
            foreach (var entry in pending.Take(count)) Folder(Path.GetDirectoryName(EnvironmentAssetRules.DirectoryFor(entry.Identity, entry.Category)));
            AssetDatabase.StartAssetEditing();
            try
            {
                while (count-- > 0 && pending.Count > 0)
                {
                    var entry = pending.Peek();
                    string path = Move(entry);
                    string resource = NativeModelPublicationIndex.Resource(path);
                    bindings[entry.Identity].EditableResource = resource;
                    if (entries != null && entries.TryGetValue(entry.Identity + ".glb", out var matches)) foreach (var match in matches) match.Resource = resource;
                    pending.Dequeue(); moved++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.SetDirty(index); AssetDatabase.SaveAssetIfDirty(index);
                if (content) { EditorUtility.SetDirty(content); AssetDatabase.SaveAssetIfDirty(content); }
            }
            Status = $"Распределено {moved}; осталось {pending.Count}.";
            if (pending.Count == 0) { Stop(); AssetDatabase.Refresh(); Debug.Log("ENVIRONMENT ORGANIZED: " + moved); }
        }
        static string CurrentDirectory(EnvironmentLibrary.Entry entry) => Path.GetDirectoryName(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(entry.PrefabGuid))).Replace('\\', '/');
        static string ManifestPath(EnvironmentLibrary.Entry entry) => NativeAssetPublisher.ManifestRoot + "/" + entry.Identity + "/publication.json";
        static void CheckMove(EnvironmentLibrary.Entry entry, string target)
        {
            if (!EnvironmentAssetRules.IsEnvironment(entry.Identity)) throw new InvalidDataException("Character assets are excluded.");
            string current = CurrentDirectory(entry);
            if (!current.StartsWith(NativeAssetPublisher.DefaultDestination + "/objects/", StringComparison.Ordinal) && !current.StartsWith(EnvironmentAssetRules.Root + "/", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected environment asset owner: " + current);
            if (current != target && (ContentPaths.DirectoryExists(target) || File.Exists(ContentPaths.FilePath(target + ".meta")))) throw new IOException("Destination occupied: " + target);
            if (!File.Exists(ContentPaths.FilePath(ManifestPath(entry)))) throw new FileNotFoundException("Publication metadata missing", ManifestPath(entry));
        }
        static string Move(EnvironmentLibrary.Entry entry)
        {
            string target = EnvironmentAssetRules.DirectoryFor(entry.Identity, entry.Category);
            CheckMove(entry, target);
            string current = CurrentDirectory(entry), file = ManifestPath(entry);
            var manifest = JsonConvert.DeserializeObject<NativeAssetPublisher.Manifest>(File.ReadAllText(ContentPaths.FilePath(file)));
            if (manifest.PrefabGuid != entry.PrefabGuid) throw new InvalidDataException("Publication GUID mismatch.");
            if (current != target)
            {
                Folder(Path.GetDirectoryName(target));
                string error = AssetDatabase.MoveAsset(ContentPaths.Resolve(current), ContentPaths.Resolve(target));
                if (error != "") throw new IOException(error);
            }
            // Use GUIDs, including on replay after an interrupted metadata write.
            manifest.PrefabPath = target + "/model.prefab";
            foreach (var asset in manifest.Assets) asset.Path = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(asset.Guid));
            File.WriteAllText(ContentPaths.FilePath(file), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            return manifest.PrefabPath;
        }
        public static void Folder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path))) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); ContentPaths.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
