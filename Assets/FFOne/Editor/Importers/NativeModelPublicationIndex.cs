using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    internal static class NativeModelPublicationIndex
    {
        internal const string Path = NativeAssetPublisher.DefaultDestination + "/Index.asset";
        internal static string Resource(string path)
        {
            const string prefix = "Assets/Resources/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Runtime override must be in Resources: " + path);
            return path.Substring(prefix.Length, path.LastIndexOf('.') - prefix.Length);
        }
        internal static void Migrate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(Path));
            if (!catalog) return;
            bool changed = false;
            foreach (var binding in catalog.Bindings)
            {
                if (binding.Original && binding.Editable)
                {
                    binding.OriginalResource = Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(binding.Original)));
                    binding.EditableResource = Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(binding.Editable)));
                    binding.Original = binding.Editable = null; changed = true;
                }
            }
            if (changed) { EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
        }
        internal static void Preflight(GameObject source, string prefabPath)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(Path));
            if (!catalog)
            {
                if (File.Exists(ContentPaths.FilePath(Path)) || File.Exists(ContentPaths.FilePath(Path + ".meta")) || ContentPaths.DirectoryExists(Path))
                    throw new InvalidDataException("Runtime native model index path is occupied: " + Path);
                return;
            }
            string original = Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(source)));
            var existing = catalog.Bindings.SingleOrDefault(b => b.OriginalResource == original || b.Original == source);
            if (existing != null && (existing.Editable ? Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(existing.Editable))) : existing.EditableResource) != Resource(prefabPath))
                throw new InvalidDataException("Runtime native model already has an authored override; no assets changed: " + source.name);
        }
        internal static void Register(GameObject source, GameObject editable)
        {
            Preflight(source, ContentPaths.Legacy(AssetDatabase.GetAssetPath(editable)));
            var catalog = AssetDatabase.LoadAssetAtPath<NativeModelCatalog>(ContentPaths.Resolve(Path));
            bool fresh = !catalog;
            if (fresh) catalog = ScriptableObject.CreateInstance<NativeModelCatalog>();
            string original = Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(source)));
            if (catalog.Bindings.Any(b => b.OriginalResource == original || b.Original == source)) return;
            catalog.Bindings = catalog.Bindings.Concat(new[] { new NativeModelCatalog.Binding {
                OriginalResource = original, EditableResource = Resource(ContentPaths.Legacy(AssetDatabase.GetAssetPath(editable))) } }).ToArray();
            if (fresh) AssetDatabase.CreateAsset(catalog, ContentPaths.Resolve(Path));
            else { EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog); }
        }
    }
}
