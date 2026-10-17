using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FFOne.Content
{
    /// <summary>Stable content addresses shared by the imported catalogs and authored content.</summary>
    public static class ContentAssets
    {
        [Serializable] sealed class Index { public string[] keys; public string[] fonts; }
        static HashSet<string> keys;
        static string[] fontKeys=Array.Empty<string>();
        static readonly Dictionary<(string,Type),AsyncOperationHandle> retained = new();
        static bool Known(string key)
        {
            if (keys == null)
            {
                var source=Resources.Load<TextAsset>("FFOneContentIndex");
                var index=source ? JsonUtility.FromJson<Index>(source.text) : new Index();
                keys=new HashSet<string>(index.keys ?? Array.Empty<string>(),StringComparer.Ordinal);
                fontKeys=index.fonts ?? Array.Empty<string>();
                if (source) Resources.UnloadAsset(source);
            }
            return keys.Contains(key);
        }
        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;
#if UNITY_EDITOR
            // AssetDatabase permits inspection and import without a content build.
            if (!Application.isPlaying)
            {
                string path=ContentPaths.ResourcePath(key);
                if (path != null)
                {
                    if (typeof(T)==typeof(Sprite)) return UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().FirstOrDefault();
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }
#endif
            if (!Known(key)) return Resources.Load<T>(key);
            var id=(key,typeof(T));
            if (!retained.TryGetValue(id,out var handle))
            {
                Trace(key);
                handle=Addressables.LoadAssetAsync<T>(key);
                retained.Add(id,handle);
            }
            if (!handle.IsDone) handle.WaitForCompletion();
            if (handle.Status==AsyncOperationStatus.Succeeded) return handle.Result as T;
            retained.Remove(id); if (handle.IsValid()) Addressables.Release(handle);
            throw new InvalidOperationException("Unable to load content address: "+key);
        }
        public static T[] LoadAll<T>(string prefix) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return ContentPaths.Entries.Where(e=>e.old.StartsWith("Assets/Resources/"+prefix.TrimEnd('/')+"/",StringComparison.Ordinal))
                .Select(e=>UnityEditor.AssetDatabase.LoadAssetAtPath<T>(ContentPaths.CurrentPath(e))).Where(e=>e).ToArray();
#endif
            Known("");
            if(typeof(T)!=typeof(Font)) throw new NotSupportedException("Enumerate typed catalog entries instead of loading an untyped address folder.");
            return fontKeys.Where(k=>k.StartsWith(prefix.TrimEnd('/')+"/",StringComparison.Ordinal)).Select(Load<T>).Where(a=>a).ToArray();
        }
        public static Request LoadAsync<T>(string key) where T : UnityEngine.Object => Start<T>(key,true);
        public static Request AcquireAsync<T>(string key) where T : UnityEngine.Object => Start<T>(key,false);
        public static TResult Read<T,TResult>(string key,Func<T,TResult> read) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)return read(Load<T>(key));
#endif
            if(!Known(key))return read(Resources.Load<T>(key));
            Trace(key);
            var handle=Addressables.LoadAssetAsync<T>(key);
            try
            {
                var asset=handle.WaitForCompletion();
                if(handle.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Unable to read content address: "+key);
                return read(asset);
            }
            finally { if(handle.IsValid())Addressables.Release(handle); }
        }
        static Request Start<T>(string key,bool cache) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return new Request(Load<T>(key));
#endif
            if (!Known(key)) return new Request(Resources.LoadAsync<T>(key));
            var id=(key,typeof(T));
            if (cache && retained.TryGetValue(id,out var existing)) return new Request(existing,false);
            AsyncOperationHandle handle=Addressables.LoadAssetAsync<T>(key);
            if (cache) retained.Add(id,handle);
            return new Request(handle,!cache);
        }
        /// <summary>Call only after all consumers of cached assets have been destroyed.</summary>
        public static void ReleaseCached()
        {
            foreach(var handle in retained.Values) if(handle.IsValid()) Addressables.Release(handle);
            retained.Clear();
        }
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        static void Trace(string key)
        {
            if(Environment.GetCommandLineArgs().Contains("--content-layout-smoke"))Debug.Log("CONTENT LOAD: "+key);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { ReleaseCached(); keys=null; }
        public sealed class Request : CustomYieldInstruction, IDisposable
        {
            AsyncOperationHandle handle;
            ResourceRequest resource;
            UnityEngine.Object immediate;
            bool owns, disposed;
            internal Request(AsyncOperationHandle value,bool ownership) { handle=value; owns=ownership; }
            internal Request(ResourceRequest value) { resource=value; }
            internal Request(UnityEngine.Object value) { immediate=value; }
            public bool isDone => disposed || (resource != null ? resource.isDone : !handle.IsValid() || handle.IsDone);
            public UnityEngine.Object asset => disposed ? null : resource != null ? resource.asset : handle.IsValid() ? handle.IsDone && handle.Status==AsyncOperationStatus.Succeeded ? handle.Result as UnityEngine.Object : null : immediate;
            public override bool keepWaiting => !isDone;
            public void Dispose() { if(disposed)return; disposed=true; immediate=null; resource=null; if(owns && handle.IsValid()) Addressables.Release(handle); }
        }
    }
}
