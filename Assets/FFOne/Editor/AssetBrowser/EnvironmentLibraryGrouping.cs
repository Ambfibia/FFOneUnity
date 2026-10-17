using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor
{
    public static class EnvironmentLibraryGrouping
    {
        public static string FamilyKey(string identity)
        {
            var parts = identity.Split('/');
            // Only the exporter suffix denotes a variant. A collection may contain unrelated objects.
            return string.Join("/", parts.Select(p => Regex.Replace(p, @"_variant_\d+$", "")));
        }
        public static void Rebuild(EnvironmentLibrary library)
        {
            var parent = library.Entries.ToDictionary(e => e.Identity, e => e.Identity);
            string Find(string id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
            void Join(string a, string b) { a = Find(a); b = Find(b); if (a != b) parent[string.CompareOrdinal(a, b) < 0 ? b : a] = string.CompareOrdinal(a, b) < 0 ? a : b; }
            var geometries = new Dictionary<string, string>();
            var families = new Dictionary<string, string>();
            foreach (var entry in library.Entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GUIDToAssetPath(string.IsNullOrEmpty(entry.AssemblyGuid) ? entry.PrefabGuid : entry.AssemblyGuid))));
                if (!prefab) throw new InvalidOperationException("Missing environment prefab " + entry.Identity);
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
                bool staticModel = prefab.GetComponentsInChildren<Animation>(true).Length == 0 && prefab.GetComponentsInChildren<Animator>(true).Length == 0 && prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0;
                string geometry = Hash128.Compute(string.Join("|", meshes.Select(m => AssetDatabase.AssetPathToGUID(ContentPaths.Resolve(ContentPaths.Legacy(AssetDatabase.GetAssetPath(m.sharedMesh)))) + ":" + JsonUtility.ToJson(new MatrixData { Value = prefab.transform.worldToLocalMatrix * m.transform.localToWorldMatrix }) + ":" + m.gameObject.activeSelf))).ToString();
                // Compare the editable assembly, including material, collision and custom behaviour overrides.
                // Metadata identifying a source must not turn otherwise equal aliases into fake variants.
                var components = prefab.GetComponentsInChildren<Component>(true).Where(c => c is Renderer || c is Collider || c is Rigidbody ||
                    c is MonoBehaviour && !(c is FFOne.World.EnvironmentObject) && !(c is FFOne.World.WorldModelBasis));
                entry.RenderKey = staticModel ? Hash128.Compute(geometry + "|" + string.Join("|", components.Select(c => ComponentSignature(c, prefab.transform)))).ToString() : entry.Identity;
                string family = entry.Category + ":" + FamilyKey(entry.Identity);
                string other;
                // Anonymous collision chunks are different parts of the world, not variants of one prop.
                if (!entry.Category.StartsWith("Technical/") && !entry.Identity.Contains("/unnamed_static_object/"))
                { if (families.TryGetValue(family, out other)) Join(entry.Identity, other); else families.Add(family, entry.Identity); }
                if (staticModel && meshes.Length > 0)
                {
                    string key = entry.Category + ":" + geometry;
                    if (geometries.TryGetValue(key, out other)) Join(entry.Identity, other); else geometries.Add(key, entry.Identity);
                }
            }
            foreach (var entry in library.Entries) entry.CardKey = Find(entry.Identity);
        }
        static string ComponentSignature(Component component, Transform root)
        {
            var document = JObject.Parse(EditorJsonUtility.ToJson(component));
            var body = (JObject)document.Properties().First().Value;
            foreach (var property in body.Properties().Where(p => p.Name == "m_GameObject" || p.Name == "m_ObjectHideFlags" ||
                p.Name.StartsWith("m_Prefab", StringComparison.Ordinal) || p.Name == "m_CorrespondingSourceObject").ToArray()) property.Remove();
            return component.GetType().FullName + ":" + component.gameObject.layer + ":" +
                JsonUtility.ToJson(new MatrixData { Value = root.worldToLocalMatrix * component.transform.localToWorldMatrix }) + ":" + document.ToString(Newtonsoft.Json.Formatting.None);
        }
        [Serializable] sealed class MatrixData { public Matrix4x4 Value; }
    }
}
