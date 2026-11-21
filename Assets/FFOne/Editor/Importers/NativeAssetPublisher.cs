using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using FFOne.Content;

namespace FFOne.Editor
{
    /// <summary>
    /// Publishes an imported GLB as independent, editable Unity assets. This is an
    /// explicit authoring operation: importer reimports never overwrite published work.
    /// </summary>
    public static class NativeAssetPublisher
    {
        public const string DefaultDestination = "Assets/Resources/" + NativeModelAssets.EditableRoot;
        public const string ManifestRoot = "Assets/Editor/NativePublications";
        const int Version = 1;

        [Serializable]
        public sealed class Entry
        {
            public long SourceLocalId;
            public string Name, Type, Path, Guid;
        }

        [Serializable]
        public sealed class Manifest
        {
            public int Version;
            public string SourcePath, SourceGuid, SourceDependencyHash, SourceContentHash, PrefabPath, PrefabGuid;
            public Entry[] Assets;
        }

        public sealed class Plan
        {
            public string SourcePath { get; internal set; }
            public string Directory { get; internal set; }
            public string ManifestPath { get; internal set; }
            public string PrefabPath => Directory + "/model.prefab";
            public bool AlreadyPublished { get; internal set; }
            public int MeshCount => Objects.Count(o => o is Mesh);
            public int MaterialCount => Objects.Count(o => o is Material);
            public int AnimationCount => Objects.Count(o => o is AnimationClip);
            internal Object[] Objects;
            internal Manifest Manifest;
            internal bool RegisterRuntime;
            internal bool ShareEnvironment;
            public override string ToString() => $"{SourcePath}: {MeshCount} meshes, {MaterialCount} materials, {AnimationCount} clips. " +
                (AlreadyPublished ? "Published assets and edits will be preserved." : "New editable prefab: " + PrefabPath);
        }

        /// <summary>Read-only validation of the complete write set for one model.</summary>
        public static Plan Preflight(string sourcePath, string destination = DefaultDestination)
        {
            sourcePath = ContentPaths.Legacy(AssetPath(sourcePath));
            destination = AssetPath(destination);
            if (!sourcePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                !(AssetImporter.GetAtPath(ContentPaths.Resolve(sourcePath)) is NativeGlbImporter))
                throw new InvalidDataException("Select a GLB imported by NativeGlbImporter: " + sourcePath);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(sourcePath));
            if (!root) throw new InvalidDataException("GLB has no successfully imported hierarchy: " + sourcePath);
            string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(sourcePath));
            var objects = AssetDatabase.LoadAllAssetsAtPath(ContentPaths.Resolve(sourcePath))
                .Where(o => o is Mesh || o is Material || o is AnimationClip).OrderBy(LocalId).ToArray();
            const string resources = "Assets/Resources/";
            string relative = sourcePath.StartsWith(resources, StringComparison.Ordinal)
                ? sourcePath.Substring(resources.Length, sourcePath.Length - resources.Length - 4)
                : guid + "/" + Path.GetFileNameWithoutExtension(sourcePath);
            var plan = new Plan { SourcePath = sourcePath, Directory = destination + "/" + relative, Objects = objects, RegisterRuntime = destination == DefaultDestination,
                ManifestPath = ManifestRoot + (destination == DefaultDestination ? "" : "/Variants/" + Hash128.Compute(destination)) + "/" + relative + "/publication.json" };
            if (File.Exists(ContentPaths.FilePath(plan.ManifestPath)))
            {
                var location = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(ContentPaths.FilePath(plan.ManifestPath)));
                // Organized environment assets retain their original source identity and publication GUIDs.
                if (location != null && location.SourceGuid == guid && !string.IsNullOrEmpty(location.PrefabGuid))
                {
                    string published = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(location.PrefabGuid));
                    if (published == location.PrefabPath && published.EndsWith("/model.prefab", StringComparison.Ordinal))
                        plan.Directory = Path.GetDirectoryName(published).Replace('\\', '/');
                }
            }
            else if (plan.RegisterRuntime && EnvironmentAssetRules.IsEnvironment(relative))
                plan.Directory = EnvironmentAssetRules.DirectoryFor(relative, EnvironmentAssetRules.Classify(relative));
            plan.ShareEnvironment = plan.RegisterRuntime && EnvironmentAssetRules.IsEnvironment(relative);
            ValidateFolderChain(plan.Directory);
            ValidateFolderChain(Path.GetDirectoryName(plan.ManifestPath).Replace('\\', '/'));
            if (plan.RegisterRuntime) NativeModelPublicationIndex.Preflight(root, plan.PrefabPath);
            if (sourcePath.StartsWith(plan.Directory + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Publication cannot contain its own source.");
            plan.Manifest = new Manifest {
                Version = Version, SourcePath = sourcePath, SourceGuid = guid,
                SourceDependencyHash = AssetDatabase.GetAssetDependencyHash(ContentPaths.Resolve(sourcePath)).ToString(),
                SourceContentHash = plan.ShareEnvironment ? EnvironmentSharedAssets.SourceContentHash(sourcePath) : null,
                PrefabPath = plan.PrefabPath,
                Assets = objects.Select(o => new Entry { SourceLocalId = LocalId(o), Name = o.name,
                    Type = o.GetType().FullName, Path = plan.Directory + "/" + Kind(o) + "_" +
                        LocalId(o).ToString(CultureInfo.InvariantCulture) + Extension(o) }).ToArray()
            };
            // Reject unsupported internal asset references rather than produce a prefab
            // which silently still depends on immutable GLB sub-assets.
            var supported = new HashSet<Object>(objects);
            foreach (var value in objects.Concat(root.GetComponentsInChildren<Component>(true).Cast<Object>()))
                VisitReferences(value, reference => {
                    if (reference && ContentPaths.Legacy(AssetDatabase.GetAssetPath(reference)) == sourcePath &&
                        !(reference is GameObject) && !(reference is Component) && !supported.Contains(reference))
                        throw new InvalidDataException("Unsupported embedded GLB dependency: " + reference.GetType().Name + " " + reference.name);
                    return reference;
                }, false);

            if (File.Exists(ContentPaths.FilePath(plan.ManifestPath)))
            {
                var saved = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(ContentPaths.FilePath(plan.ManifestPath)));
                if (saved == null || saved.Version != Version || saved.SourceGuid != guid || saved.Assets == null)
                    throw new InvalidDataException("Publication identity/version mismatch: " + plan.ManifestPath);
                if (plan.ShareEnvironment && !string.IsNullOrEmpty(saved.SourceContentHash)
                    ? saved.SourceContentHash != plan.Manifest.SourceContentHash
                    : saved.SourceDependencyHash != plan.Manifest.SourceDependencyHash && !ContentPaths.AcceptedSourceUnchanged(sourcePath))
                    throw new InvalidDataException("Source or importer dependencies changed. Existing edits are preserved. Publish to a separate destination for review: " + sourcePath);
                if (saved.PrefabPath != plan.PrefabPath || saved.Assets.Length != objects.Length)
                    throw new InvalidDataException("Publication layout mismatch: " + plan.ManifestPath);
                ValidatePublished(saved.PrefabPath, saved.PrefabGuid, typeof(GameObject));
                foreach (var expected in plan.Manifest.Assets)
                {
                    var entry = saved.Assets.SingleOrDefault(e => e.SourceLocalId == expected.SourceLocalId);
                    bool matchesPath = entry != null &&
                        (entry.Path == expected.Path ||
                         ContentPaths.Resolve(entry.Path) == ContentPaths.Resolve(expected.Path) ||
                         (plan.ShareEnvironment && entry.Path.StartsWith(EnvironmentSharedAssets.Root + "/", StringComparison.Ordinal)));
                    if (!matchesPath || entry.Type != expected.Type)
                        throw new InvalidDataException("Publication asset identity mismatch: " + expected.Path);
                    ValidatePublished(entry.Path, entry.Guid, objects.Single(o => LocalId(o) == entry.SourceLocalId).GetType());
                }
                plan.Manifest = saved;
                plan.AlreadyPublished = true;
            }
            else
            {
                // Any occupied leaf (including an orphan .meta) is a conflict. Never
                // adopt a user-created asset merely because its filename happens to match.
                foreach (var path in plan.Manifest.Assets.Select(e => e.Path).Concat(new[] { plan.PrefabPath, plan.ManifestPath }))
                    if (File.Exists(ContentPaths.FilePath(path)) || File.Exists(ContentPaths.FilePath(path + ".meta")) || ContentPaths.DirectoryExists(path))
                        throw new IOException("Publication destination is occupied; no assets changed: " + path);
            }
            return plan;
        }

        public static GameObject Publish(string sourcePath, string destination = DefaultDestination)
        {
            return PublishBatch(new[] { Preflight(sourcePath, destination) })[0];
        }

        /// <summary>Validate every model before any publication writes occur.</summary>
        public static Plan[] PreflightBatch(IEnumerable<string> sources, string destination = DefaultDestination)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var paths = sources.Select(AssetPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (paths.Length == 0 || paths.Length > 512)
                throw new InvalidDataException("Select a bounded batch of 1 to 512 models; split larger content into reviewable batches.");
            var plans = paths.Select(path => Preflight(path, destination)).ToArray();
            if (plans.Select(p => p.Directory).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Length)
                throw new InvalidDataException("Several source identities resolve to the same publication directory.");
            return plans;
        }

        public static GameObject[] PublishBatch(IEnumerable<string> sources, string destination = DefaultDestination)
        {
            var plans = PreflightBatch(sources, destination);
            return PublishBatch(plans);
        }

        internal static GameObject[] PublishBatch(Plan[] plans)
        {
            var pending = plans.Where(p => !p.AlreadyPublished).Select(p => new Publication(p)).ToArray();
            var folders = new List<string>();
            EnvironmentSharedAssets.BeginBatch();
            try
            {
                AssetDatabase.StartAssetEditing();
                try { foreach (var publication in pending) publication.Write(folders); }
                finally { AssetDatabase.StopAssetEditing(); }
                // Import once per batch, then validate every native dependency before
                // recording the publication. SaveAsPrefabAsset returns null while
                // imports are suspended, so Write uses its explicit success flag.
                foreach (var publication in pending) publication.Validate();
                AssetDatabase.StartAssetEditing();
                try { foreach (var publication in pending) publication.Record(); }
                finally { AssetDatabase.StopAssetEditing(); }
            }
            catch
            {
                foreach (var publication in pending.Reverse()) publication.Rollback();
                foreach (var folder in folders.AsEnumerable().Reverse())
                    if (ContentPaths.DirectoryExists(folder) && !Directory.EnumerateFileSystemEntries(ContentPaths.Resolve(folder)).Any()) AssetDatabase.DeleteAsset(ContentPaths.Resolve(folder));
                throw;
            }
            finally { foreach (var publication in pending) publication.Dispose(); }
            if (pending.Any() && plans.Any(p => p.ShareEnvironment)) EnvironmentSharedAssets.SaveIndex();
            var result = plans.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(p.PrefabPath))).ToArray();
            // Complete assets/provenance survive an index-write failure and can be
            // registered on replay without overwriting any authored content.
            for (int i = 0; i < plans.Length; i++)
                if (plans[i].RegisterRuntime) NativeModelPublicationIndex.Register(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(plans[i].SourcePath)), result[i]);
            Debug.Log($"FFOne native publication: {plans.Length} models, {plans.Count(p => !p.AlreadyPublished)} new, " +
                $"{plans.Count(p => p.AlreadyPublished)} preserved; {plans.Sum(p => p.MeshCount)} meshes, " +
                $"{plans.Sum(p => p.MaterialCount)} materials, {plans.Sum(p => p.AnimationCount)} clips. Editor provenance: {ManifestRoot}");
            return result;
        }

        sealed class Publication : IDisposable
        {
            readonly Plan plan;
            readonly List<string> created = new();
            readonly Dictionary<Object, Object> copies = new();
            GameObject instance;
            public Publication(Plan plan) => this.plan = plan;
            public void Write(List<string> folders)
            {
                EnsureFolder(plan.Directory, folders);
                EnsureFolder(Path.GetDirectoryName(plan.ManifestPath).Replace('\\', '/'), folders);
                // Clone every asset before remapping, so forward references resolve too.
                foreach (var original in plan.Objects)
                {
                    var entry = plan.Manifest.Assets.Single(e => e.SourceLocalId == LocalId(original));
                    if (plan.ShareEnvironment && (original is Mesh || original is Material))
                    {
                        var shared = EnvironmentSharedAssets.Acquire(original, created, out string sharedPath);
                        entry.Path = sharedPath;
                        copies.Add(original, shared);
                        continue;
                    }
                    var copy = Object.Instantiate(original);
                    copy.name = original.name;
                    copy.hideFlags = HideFlags.None;
                    copies.Add(original, copy);
                    created.Add(entry.Path);
                    AssetDatabase.CreateAsset(copy, ContentPaths.Resolve(entry.Path));
                    // CreateAsset can replace the object name with the identity-based
                    // filename. Legacy Animation looks clips up by object name, and
                    // avatar material rules also use authored names (e.g. *_sub).
                    copy.name = original.name;
                    EditorUtility.SetDirty(copy);
                }
                foreach (var copy in copies.Values) VisitReferences(copy, reference => Remap(reference, copies), true);

                instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(plan.SourcePath)));
                instance.name = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(plan.SourcePath)).name;
                // Instantiate ordinarily yields a disconnected copy, but explicitly
                // unpack if Unity retained a model prefab link in this editor version.
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var component in instance.GetComponentsInChildren<Component>(true))
                    VisitReferences(component, reference => Remap(reference, copies), true);
                created.Add(plan.PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(instance, ContentPaths.Resolve(plan.PrefabPath), out bool success);
                if (!success) throw new IOException("Could not save editable native prefab: " + plan.PrefabPath);
                foreach (var copy in copies.Values) AssetDatabase.SaveAssetIfDirty(copy);
            }
            public void Validate()
            {
                plan.Manifest.PrefabGuid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(plan.PrefabPath));
                ValidatePublished(plan.PrefabPath, plan.Manifest.PrefabGuid, typeof(GameObject));
                foreach (var entry in plan.Manifest.Assets)
                {
                    entry.Guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(entry.Path));
                    ValidatePublished(entry.Path, entry.Guid, plan.Objects.Single(o => LocalId(o) == entry.SourceLocalId).GetType());
                }
                if (ContentPaths.Legacy(AssetDatabase.GetDependencies(ContentPaths.Resolve(plan.PrefabPath), true)).Any(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Published prefab retained a GLB dependency: " + plan.SourcePath);
            }
            public void Record()
            {
                created.Add(plan.ManifestPath);
                using (var writer = new StreamWriter(new FileStream(ContentPaths.FilePath(plan.ManifestPath), FileMode.CreateNew, FileAccess.Write)))
                    writer.Write(JsonConvert.SerializeObject(plan.Manifest, Formatting.Indented));
                AssetDatabase.ImportAsset(ContentPaths.Resolve(plan.ManifestPath));
            }
            public void Rollback()
            {
                // Only paths created by this operation are eligible for rollback.
                foreach (var path in created.AsEnumerable().Reverse())
                {
                    if (!AssetDatabase.DeleteAsset(ContentPaths.Resolve(path)) && File.Exists(ContentPaths.FilePath(path))) File.Delete(ContentPaths.FilePath(path));
                }
            }
            public void Dispose()
            {
                if (instance) Object.DestroyImmediate(instance);
                foreach (var copy in copies.Values)
                    if (copy && !EditorUtility.IsPersistent(copy)) Object.DestroyImmediate(copy);
            }
        }

        static Object Remap(Object value, Dictionary<Object, Object> copies) => value && copies.TryGetValue(value, out var copy) ? copy : value;

        static void VisitReferences(Object value, Func<Object, Object> map, bool write)
        {
            if (!value) throw new InvalidDataException("Missing script/component in imported hierarchy.");
            // A Mesh owns numeric buffers, not references to imported assets. Walking
            // every byte of its vertex/index arrays makes whole-world publication quadratic
            // in serialized buffer overhead. Its referencing renderer is still checked.
            if (value is Mesh) return;
            using var serialized = new SerializedObject(value);
            var property = serialized.GetIterator();
            bool changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                var original = property.objectReferenceValue;
                // Unity's model-prefab metadata is an opaque native Object, not a
                // content sub-asset. Observed on m_PrefabAsset of imported GLB
                // Components (Unity 6000.4). Only exempt that exact importer-owned
                // metadata field during source validation. The cloned hierarchy is
                // unpacked below and its final dependency graph must omit the GLB.
                if (!write && property.propertyPath == "m_PrefabAsset" && value is Component &&
                    original && original.GetType() == typeof(Object) &&
                    ContentPaths.Legacy(AssetDatabase.GetAssetPath(original)) == ContentPaths.Legacy(AssetDatabase.GetAssetPath(value)) &&
                    AssetImporter.GetAtPath(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(value)))) is NativeGlbImporter)
                    continue;
                var replacement = map(original);
                if (write && replacement != original) { property.objectReferenceValue = replacement; changed = true; }
            }
            if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ValidatePublished(string path, string guid, Type type)
        {
            if (string.IsNullOrEmpty(guid) || AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path)) != guid || !AssetDatabase.LoadAssetAtPath(ContentPaths.Resolve(path), type))
                throw new InvalidDataException("Published asset moved, removed, or replaced; existing edits are preserved: " + path);
        }

        static long LocalId(Object value)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string _, out long id))
                throw new InvalidDataException("Imported asset has no persistent identity: " + value.name);
            return id;
        }
        static string Kind(Object value) => value is Mesh ? "mesh" : value is Material ? "material" : "animation";
        static string Extension(Object value) => value is Material ? ".mat" : value is AnimationClip ? ".anim" : ".asset";

        static string AssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("An Assets-relative path is required.");
            path = path.Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Split('/').Any(p => p == "." || p == ".." || p.Length == 0) || path.Contains(':'))
                throw new InvalidDataException("An Assets-relative path without traversal is required: " + path);
            return path;
        }

        static void EnsureFolder(string path, List<string> created)
        {
            if (AssetDatabase.IsValidFolder(ContentPaths.Resolve(path)) || created.Contains(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent, created);
            if (string.IsNullOrEmpty(ContentPaths.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException("Cannot create publication folder: " + path);
            created.Add(path);
        }

        static void ValidateFolderChain(string path)
        {
            while (path != "Assets")
            {
                if (File.Exists(ContentPaths.FilePath(path)) || (!ContentPaths.DirectoryExists(path) && File.Exists(ContentPaths.FilePath(path + ".meta"))) ||
                    (ContentPaths.DirectoryExists(path) && !AssetDatabase.IsValidFolder(ContentPaths.Resolve(path))))
                    throw new IOException("Publication folder is occupied or not imported; no assets changed: " + path);
                path = Path.GetDirectoryName(path).Replace('\\', '/');
            }
        }
    }

    public sealed class NativeAssetPublisherWindow : EditorWindow
    {
        GameObject source;
        string destination = NativeAssetPublisher.DefaultDestination;
        string status = "Select one imported GLB. Meshes, materials and animations become editable Unity assets; external textures retain their native references.";

        [MenuItem("FFOne/Content/Publish Editable Native Model")]
        static void Open() => GetWindow<NativeAssetPublisherWindow>("Native publication");
        void OnGUI()
        {
            source = (GameObject)EditorGUILayout.ObjectField("Imported GLB", source, typeof(GameObject), false);
            destination = EditorGUILayout.TextField("Destination", destination);
            EditorGUILayout.HelpBox(status, MessageType.Info);
            if (GUILayout.Button("Preflight")) Run(false);
            if (GUILayout.Button("Publish editable assets")) Run(true);
            EditorGUILayout.LabelField("Repeat publication preserves all edits. A changed source needs a separate destination for review. The default destination enables runtime overrides through NativeModelAssets.", EditorStyles.wordWrappedLabel);
        }
        void Run(bool publish)
        {
            try
            {
                string path = ContentPaths.Legacy(AssetDatabase.GetAssetPath(source));
                status = NativeAssetPublisher.Preflight(path, destination).ToString();
                if (publish) EditorGUIUtility.PingObject(NativeAssetPublisher.Publish(path, destination));
            }
            catch (Exception exception) { status = exception.Message; Debug.LogException(exception); }
        }
    }
}
