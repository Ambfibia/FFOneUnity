using System;
using UnityEngine;

namespace FFOne.Content
{
    /// <summary>Editable native prefabs take precedence while retaining original resource identities.</summary>
    public static class NativeModelAssets
    {
        public const string EditableRoot = "NativeEditable";
        static NativeModelCatalog indexedCatalog;
        static System.Collections.Generic.Dictionary<string, string> published;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetIndex() { indexedCatalog = null; published = null; }

        /// <summary>Resolve serialized model references without adding source paths to runtime data.</summary>
        public static GameObject Resolve(GameObject original)
        {
            if (!original) return original;
#if UNITY_EDITOR
            string path = ContentPaths.Legacy(UnityEditor.AssetDatabase.GetAssetPath(original));
            if (path.StartsWith("Assets/Resources/", StringComparison.Ordinal) && path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                return Load(path.Substring("Assets/Resources/".Length)) ?? original;
#endif
            // Serialized consumers are rebound to published prefabs by publication.
            // No global catalogue of loaded GameObjects is needed in a player.
            return original;
        }

        public static string EditableResourcePath(string resource) => EditableRoot + "/" + Normalize(resource) + "/model";
        public static string PublishedResourcePath(string resource) => OverrideResource(Normalize(resource));

        public static GameObject Load(string resource)
        {
            resource = Normalize(resource);
            return FFOne.Content.ContentAssets.Load<GameObject>(OverrideResource(resource)) ?? FFOne.Content.ContentAssets.Load<GameObject>(resource);
        }

        static string OverrideResource(string resource)
        {
            var catalog = FFOne.Content.ContentAssets.Load<NativeModelCatalog>(EditableRoot + "/Index");
            return OverrideResource(resource, catalog);
        }
        static string OverrideResource(string resource, NativeModelCatalog catalog)
        {
            if (catalog && (!Application.isPlaying || indexedCatalog != catalog || published == null))
            {
                published = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var binding in catalog.Bindings) published.TryAdd(binding.OriginalResource, binding.EditableResource);
                indexedCatalog = catalog;
            }
            if (catalog && published.TryGetValue(resource, out string path)) return path;
            // The Nano table omits the trailing N used by Belladonna's published model.
            if (resource == "characters/nanos/nano_belladonna/nano_belladonna")
                return EditableResourcePath(resource + "N");
            return EditableResourcePath(resource);
        }

        public static Request LoadAsync(string resource) => new Request(Normalize(resource));

        public static async System.Threading.Tasks.Task<GameObject> PreloadAsync(string resource, System.Threading.CancellationToken cancellation = default)
        {
            resource = Normalize(resource);
            await ContentAssets.GetAsync<NativeModelCatalog>(EditableRoot + "/Index", cancellation);
            var prefab = await ContentAssets.GetAsync<GameObject>(OverrideResource(resource), cancellation);
            return prefab ? prefab : await ContentAssets.GetAsync<GameObject>(resource, cancellation);
        }

        static string Normalize(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource)) throw new ArgumentException("A model resource path is required.", nameof(resource));
            resource = resource.Replace('\\', '/');
            if (resource.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) resource = resource.Substring(0, resource.Length - 4);
            foreach (var part in resource.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Contains(':'))
                    throw new ArgumentException("Expected a Resources-relative model path without traversal.", nameof(resource));
            if (resource.StartsWith(EditableRoot + "/", StringComparison.Ordinal))
                throw new ArgumentException("Use the original model resource identity, not its override path.", nameof(resource));
            return resource;
        }

        /// <summary>Yield this request before reading asset, as with ResourceRequest.</summary>
        public sealed class Request : CustomYieldInstruction, IDisposable
        {
            readonly string original;
            FFOne.Content.ContentAssets.Request pending;
            bool catalogPending = true, fallback, disposed;
            internal Request(string resource)
            {
                original = resource;
                pending = FFOne.Content.ContentAssets.LoadAsync<NativeModelCatalog>(EditableRoot + "/Index");
            }
            public UnityEngine.Object asset => isDone ? pending.asset : null;
            public bool isDone
            {
                get
                {
                    if (disposed) return true;
                    if (!pending.isDone) return false;
                    if (catalogPending)
                    {
                        catalogPending = false;
                        string path = OverrideResource(original, pending.asset as NativeModelCatalog);
                        pending.Dispose();
                        pending = FFOne.Content.ContentAssets.AcquireAsync<GameObject>(path);
                        if (!pending.isDone) return false;
                    }
                    if (!pending.asset && !fallback)
                    {
                        fallback = true;
                        pending.Dispose();
                        pending = FFOne.Content.ContentAssets.AcquireAsync<GameObject>(original);
                    }
                    return pending.isDone;
                }
            }
            public override bool keepWaiting => !isDone;
            public void Dispose() { if(disposed)return; disposed=true; pending.Dispose(); }
        }
    }
}
