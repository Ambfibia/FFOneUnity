using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FFOne.Editor
{
    public static class EnvironmentCompoundBuilder
    {
        public const string MetadataRoot = "Assets/Editor/EnvironmentHierarchy";
        public const string PrefabRoot = EnvironmentAssetRules.Root + "/Assemblies";
        public sealed class Pose { public float[] Position, Rotation, Scale; }
        public sealed class Part { public string SourceNode, Role, Name; public Pose[] Chain; }
        public sealed class Group { public string SourceRoot, Name; public Pose[] RootChain; public Part[] Parts; }
        public sealed class Document { public int Version; public string Tile; public Group[] Groups; }
        sealed class Resolved
        {
            public Part Source;
            public GameObject Model;
            public Mesh Mesh;
            public bool Trigger;
            public string Identity;
            public Matrix4x4 Local, World, Root;
        }
        static Queue<Document> pending;
        static Document current;
        static WorldTileContent currentContent;
        static int currentIndex, completed;
        public static string Status { get; private set; } = "";
        public static bool Running => pending != null;
        static Dictionary<string, EnvironmentLibrary.Entry> modelEntries;
        static Dictionary<string, EnvironmentLibrary.Entry> compounds;
        static EnvironmentLibrary library;
        static readonly Dictionary<string, string> stagedPaths = new Dictionary<string, string>();
        static readonly Dictionary<int, string> renderKeys = new Dictionary<int, string>();

        public static Matrix4x4 Matrix(Pose[] chain)
        {
            var result = Matrix4x4.identity;
            foreach (var p in chain)
            {
                var rotation = new Quaternion(p.Rotation[0], p.Rotation[1], p.Rotation[2], p.Rotation[3]);
                // Three shipped nodes have an empty quaternion; the native importer treats it as identity.
                if (Quaternion.Dot(rotation, rotation) < 1e-10f) rotation = Quaternion.identity;
                result *= Matrix4x4.TRS(new Vector3(p.Position[0], p.Position[1], p.Position[2]),
                    rotation,
                    new Vector3(p.Scale[0], p.Scale[1], p.Scale[2]));
            }
            return result;
        }
        static void PoseTransform(Transform target, Matrix4x4 matrix)
        {
            // Reject shear/mirroring instead of silently changing a part's geometry.
            Vector3 scale = matrix.lossyScale;
            Quaternion rotation = matrix.rotation;
            Vector3 position = matrix.GetColumn(3);
            var reconstructed = Matrix4x4.TRS(position, rotation, scale);
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(reconstructed[i] - matrix[i]) > .002f) throw new InvalidDataException("Unsupported sheared compound Transform.");
            target.localPosition = position; target.localRotation = rotation; target.localScale = scale;
        }
        public static Document Read(string tile)
        {
            string path = MetadataRoot + "/" + tile + ".json";
            if (!File.Exists(ContentPaths.FilePath(path))) return null;
            var doc = JsonConvert.DeserializeObject<Document>(File.ReadAllText(ContentPaths.FilePath(path)));
            if (doc.Version != 1 || doc.Tile != tile) throw new InvalidDataException("Invalid environment hierarchy: " + path);
            return doc;
        }
        static void Initialize()
        {
            library = EnvironmentLibraryBuilder.Load();
            modelEntries = library.Entries.GroupBy(e => e.PrefabGuid).ToDictionary(g => g.Key, g => g.First());
            compounds = library.CompoundEntries.ToDictionary(e => e.Identity);
            stagedPaths.Clear(); renderKeys.Clear();
        }
        static Resolved[] Resolve(Group group, WorldTileContent content)
        {
            var result = new List<Resolved>();
            foreach (var part in group.Parts)
            {
                var r = new Resolved { Source = part, Local = Matrix(part.Chain) };
                if (part.Role == "visual")
                {
                    var visual = content.Visuals.SingleOrDefault(v => v.SourceNode == part.SourceNode);
                    if (visual == null) return null;
                    r.Model = NativeModelAssets.Resolve(visual.Model);
                    string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(r.Model))));
                    if (!modelEntries.TryGetValue(guid, out var entry) || !EnvironmentAssetRules.IsEnvironment(entry.Identity)) return null;
                    r.Identity = entry.Identity;
                    r.World = Matrix4x4.TRS(visual.Position, visual.Rotation, visual.Scale);
                }
                else
                {
                    var collision = content.Colliders.SingleOrDefault(c => c.SourceNode == part.SourceNode);
                    if (collision == null || !collision.Mesh) return null;
                    r.Mesh = collision.Mesh; r.Trigger = collision.Trigger;
                    r.World = Matrix4x4.TRS(collision.Position, collision.Rotation, collision.Scale);
                }
                result.Add(r);
            }
            var root = ApplyAcceptedTransforms(Matrix(group.RootChain), result.Select(r => r.Local).ToArray(), result.Select(r => r.World).ToArray(), out var locals);
            for (int i = 0; i < result.Count; i++) { result[i].Local = locals[i]; result[i].Root = root; }
            return result.ToArray();
        }
        public static Matrix4x4 ApplyAcceptedTransforms(Matrix4x4 root, Matrix4x4[] sourceLocal, Matrix4x4[] acceptedWorld, out Matrix4x4[] local)
        {
            local = sourceLocal;
            bool changed = false;
            for (int p = 0; p < local.Length; p++)
            {
                var original = root * local[p];
                for (int i = 0; i < 16; i++) changed |= Mathf.Abs(original[i] - acceptedWorld[p][i]) > .002f;
            }
            if (!changed) return root;
            // Accepted native repairs take precedence over bad scale/height in the shipped map.
            Vector3 offset = acceptedWorld[0].GetColumn(3) - (root * local[0]).GetColumn(3);
            bool sameOffset = true;
            for (int p = 1; p < local.Length; p++)
                sameOffset &= Vector3.Distance(acceptedWorld[p].GetColumn(3) - (root * local[p]).GetColumn(3), offset) < .002f;
            if (sameOffset) root.SetColumn(3, root.GetColumn(3) + new Vector4(offset.x, offset.y, offset.z, 0));
            var inverse = root.inverse;
            local = acceptedWorld.Select(w => inverse * w).ToArray();
            return root;
        }
        static string Key(Resolved[] parts)
        {
            var text = new StringBuilder("compound-v1\n");
            foreach (var part in parts)
            {
                string renderKey = "";
                if (part.Model && !renderKeys.TryGetValue(part.Model.GetInstanceID(), out renderKey))
                    renderKeys[part.Model.GetInstanceID()] = renderKey = modelEntries[AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(part.Model))))].RenderKey;
                text.Append(part.Source.Role).Append('|').Append(string.IsNullOrEmpty(renderKey) ? part.Identity : renderKey).Append('|');
                if (part.Mesh) text.Append(AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(part.Mesh))))).Append('|').Append(part.Trigger);
                for (int i = 0; i < 16; i++) text.Append('|').Append(Math.Round(part.Local[i], 5).ToString("0.#####", CultureInfo.InvariantCulture));
                text.Append('\n');
            }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
        static string Safe(string name) => new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()).Trim('_');
        static GameObject Build(Group group, Resolved[] parts)
        {
            string key = Key(parts), identity = "assemblies/" + key;
            var candidates = parts.Where(p => p.Model).Select(p => modelEntries[AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(p.Model))))]).ToArray();
            string category = candidates.GroupBy(e => e.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            string path = PrefabRoot + "/" + category + "/" + Safe(group.Name) + "/" + key.Substring(0, 16) + "/environment.prefab";
            if (compounds.TryGetValue(identity, out var existingEntry) && !string.IsNullOrEmpty(existingEntry.PrefabGuid)) path = ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(existingEntry.PrefabGuid));
            if (stagedPaths.TryGetValue(identity, out var stagedPath)) path = stagedPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(path));
            if (prefab)
            {
                var owner = prefab.GetComponent<EnvironmentCompound>();
                if (!owner || owner.Key != key) throw new InvalidDataException("Compound path is owned by another object: " + path);
            }
            else if (!stagedPaths.ContainsKey(identity))
            {
                EnvironmentLibraryBuilder.Folder(Path.GetDirectoryName(path));
                var staging = new GameObject("Compound staging") { hideFlags = HideFlags.HideAndDontSave }; staging.SetActive(false);
                var root = new GameObject(group.Name); root.transform.SetParent(staging.transform, false);
                try
                {
                    var component = root.AddComponent<EnvironmentCompound>(); component.Key = key;
                    var bindings = new List<EnvironmentCompound.Part>();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var part = parts[i];
                        var go = part.Model ? (GameObject)PrefabUtility.InstantiatePrefab(part.Model) : new GameObject();
                        go.name = part.Source.Role + "_" + i.ToString("D3"); go.transform.SetParent(root.transform, false);
                        PoseTransform(go.transform, part.Local);
                        if (part.Model) WorldModelBasis.ApplySource(go);
                        else { var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = part.Mesh; collider.enabled = !part.Trigger; }
                        bindings.Add(new EnvironmentCompound.Part { Role = part.Source.Role, Target = go.transform });
                    }
                    component.Parts = bindings.ToArray();
                    var metadata = root.AddComponent<EnvironmentObject>(); metadata.SourceIdentity = identity;
                    metadata.CollisionNotes = "Исходный составной объект и его коллизия. Поведение карты подключается через EnvironmentCellBehaviour по исходным ID деталей.";
                    metadata.OriginalAnimations = root.GetComponentsInChildren<Animation>(true);
                    metadata.OriginalStates = metadata.OriginalAnimations.SelectMany(a => a.Cast<AnimationState>().Select(s => s.name)).Distinct().ToArray();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, ContentPaths.Resolve(path));
                    stagedPaths[identity] = path;
                }
                finally { Object.DestroyImmediate(staging); }
            }
            if (!compounds.ContainsKey(identity))
            {
                string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(path));
                compounds.Add(identity, new EnvironmentLibrary.Entry { Identity = identity, Name = group.Name, Category = category,
                    Family = group.Name, PrefabGuid = guid, AssemblyGuid = guid, CardKey = "assembly/" + group.Name.ToLowerInvariant(), RenderKey = key,
                    AssemblyStatus = "Исходный составной объект: " + parts.Count(p => p.Model) + " визуальных частей, " + parts.Count(p => p.Mesh) + " коллизий.",
                    PartIdentities = parts.Where(p => p.Model).Select(p => p.Identity).Distinct().ToArray() });
            }
            else compounds[identity].PartIdentities = compounds[identity].PartIdentities.Concat(parts.Where(p => p.Model).Select(p => p.Identity)).Distinct().ToArray();
            return prefab;
        }
        static void SaveLibrary()
        {
            library.CompoundEntries = compounds.Values.OrderBy(e => e.Identity).ToArray();
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        }
        static void FinishImports()
        {
            foreach (var pair in stagedPaths)
            {
                string guid = AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(pair.Value));
                if (string.IsNullOrEmpty(guid)) throw new InvalidDataException("Compound failed to import: " + pair.Value);
                compounds[pair.Key].PrefabGuid = compounds[pair.Key].AssemblyGuid = guid;
            }
            stagedPaths.Clear();
        }
        [MenuItem("FFOne/Environment/Build Original Compound Prefabs")]
        public static void Begin()
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Initialize();
            current = null; currentIndex = completed = 0;
            foreach (var entry in compounds.Values) entry.SourcePlacements = 0;
            pending = new Queue<Document>(ContentPaths.GetFiles(MetadataRoot, "*.json").OrderBy(p => p).Select(p => Read(Path.GetFileNameWithoutExtension(p))));
            EditorApplication.update += Advance;
        }
        static void Advance()
        {
            if (!Running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (current == null)
                {
                    current = pending.Dequeue(); currentIndex = 0;
                    currentContent = AssetDatabase.LoadAssetAtPath<WorldTileContent>(ContentPaths.Resolve("Assets/Resources/World/Tiles/" + current.Tile + "/Content.asset"));
                }
                var timer = System.Diagnostics.Stopwatch.StartNew();
                int batchCount = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    while (currentIndex < current.Groups.Length && timer.ElapsedMilliseconds < 400 && batchCount++ < 48)
                    {
                        var group = current.Groups[currentIndex++];
                        var parts = Resolve(group, currentContent);
                        if (parts == null) continue;
                        Build(group, parts); completed++;
                        compounds["assemblies/" + Key(parts)].SourcePlacements++;
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                FinishImports();
                Status = current.Tile + ": " + currentIndex + "/" + current.Groups.Length + "; всего " + completed + " объектов; " + compounds.Count + " prefab; осталось карт: " + pending.Count;
                if (currentIndex == current.Groups.Length) { current = null; SaveLibrary(); }
                if (current == null && pending.Count == 0) { pending = null; EditorApplication.update -= Advance; Debug.Log(Status); }
            }
            catch (Exception e) { pending = null; EditorApplication.update -= Advance; SaveLibrary(); Status = e.ToString(); Debug.LogException(e); }
        }
        public static void Connect(GameObject root, GameObject prefab, string sourceRoot, Transform[] children, string[] names, Matrix4x4[] matrices)
        {
            PrefabUtility.ConvertToPrefabInstance(root, prefab, new ConvertToPrefabInstanceSettings {
                objectMatchMode = ObjectMatchMode.ByHierarchy, componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true, recordPropertyOverridesOfMatches = true,
                changeRootNameToAssetName = false }, InteractionMode.AutomatedAction);
            var component = root.GetComponent<EnvironmentCompound>(); component.SourceRoot = sourceRoot;
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i] || component.Parts[i].Target != children[i]) throw new InvalidOperationException("Compound conversion changed part identity.");
                children[i].name = names[i];
                PrefabUtility.RecordPrefabInstancePropertyModifications(children[i].gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(children[i]);
                for (int j = 0; j < 16; j++) if (Mathf.Abs(children[i].localToWorldMatrix[j] - matrices[i][j]) > .002f)
                    throw new InvalidOperationException("Compound conversion changed a world Transform: " + names[i]);
            }
        }
        public static int Configure(Scene scene, string tile)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Assemble outside Play Mode.");
            if (Running) throw new InvalidOperationException("Wait for compound publication to finish before changing a cell.");
            var doc = Read(tile); if (doc == null) return 0;
            Initialize();
            var content = AssetDatabase.LoadAssetAtPath<WorldTileContent>(ContentPaths.Resolve("Assets/Resources/World/Tiles/" + tile + "/Content.asset"));
            var roots = scene.GetRootGameObjects(); var environment = roots.Single(r => r.name == "Environment");
            var collision = roots.FirstOrDefault(r => r.name == "Collision");
            var existing = environment.GetComponentsInChildren<EnvironmentCompound>(true).Select(c => c.SourceRoot).ToHashSet();
            var targets = environment.GetComponentsInChildren<Transform>(true).Concat(collision ? collision.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>())
                .GroupBy(t => t.name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
            int count = 0;
            foreach (var group in doc.Groups)
            {
                if (existing.Contains(group.SourceRoot)) continue;
                var parts = Resolve(group, content); if (parts == null || group.Parts.Any(p => !targets.ContainsKey(p.Name))) continue;
                var children = group.Parts.Select(p => targets[p.Name]).ToArray();
                // A previous manual hierarchy is authored work, not an invitation to regroup it.
                if (children.Any(t => t.parent != environment.transform && (!collision || t.parent != collision.transform))) continue;
                var prefab = Build(group, parts);
                var root = new GameObject(group.Name); SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetParent(environment.transform, false);
                PoseTransform(root.transform, parts[0].Root);
                var matrices = children.Select(c => c.localToWorldMatrix).ToArray();
                var names = children.Select(c => c.name).ToArray();
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].SetParent(root.transform, true);
                    children[i].name = group.Parts[i].Role + "_" + i.ToString("D3");
                }
                Connect(root, prefab, group.SourceRoot, children, names, matrices);
                count++;
            }
            SaveLibrary();
            EnvironmentCellBinding.Configure(scene, tile);
            if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
            return count;
        }
    }
}
