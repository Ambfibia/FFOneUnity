using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace FFOne.Content
{
    /// <summary>Stable content addresses shared by the imported catalogs and authored content.</summary>
    public static class ContentAssets
    {
        static AsyncOperationHandle<IResourceLocator> initialization;
        static Task catalogLoading;
        static CancellationTokenSource catalogLifetime = new();
        static bool initialized;
        static int catalogVersion;
        static readonly Dictionary<(string,Type),AsyncOperationHandle> retained = new();
        public static async Task InitializeAsync(CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (initialized) return;
            if (catalogLoading == null || catalogLoading.IsFaulted || catalogLoading.IsCanceled)
                catalogLoading = LoadCatalogAsync(catalogVersion, catalogLifetime.Token);
            await AwaitSharedAsync(catalogLoading, cancellation);
        }
        internal static async Task AwaitSharedAsync(Task pending, CancellationToken cancellation)
        {
            if (!pending.IsCompleted && cancellation.CanBeCanceled)
            {
                var canceled = new TaskCompletionSource<bool>();
                using var registration = cancellation.Register(() => canceled.TrySetCanceled(cancellation));
                var completed = await Task.WhenAny(pending, canceled.Task);
                await completed;
            }
            else await pending;
            cancellation.ThrowIfCancellationRequested();
        }
        static AsyncOperationHandle<IResourceLocator> BeginInitialization()
        {
            if (initialization.IsValid() && initialization.IsDone && initialization.Status == AsyncOperationStatus.Failed)
            {
                Addressables.Release(initialization);
                initialization = default;
            }
            if (!initialization.IsValid()) initialization = Addressables.InitializeAsync(false);
            return initialization;
        }
        static async Task LoadCatalogAsync(int version, CancellationToken lifetime)
        {
            var operation = BeginInitialization();
            using var request = new Request(operation, false);
            await request.WaitAsync(lifetime);
            if (version != catalogVersion) throw new OperationCanceledException("Content catalog lifetime ended.");
            if (operation.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException("Unable to initialize Addressables.", operation.OperationException);
            initialized = true;
        }
        static void EnsureInitialized()
        {
            if (initialized) return;
            var operation = BeginInitialization();
            if (!operation.IsDone) { TraceWait("Addressables initialization"); operation.WaitForCompletion(); }
            if (operation.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException("Unable to initialize Addressables.", operation.OperationException);
            initialized = true;
        }
        internal static bool Known(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureInitialized();
            return Addressables.ResourceLocators.Any(locator => locator.Locate(key, null, out _));
        }
        static string[] Addresses<T>(string prefix) where T : UnityEngine.Object
        {
            EnsureInitialized();
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var locator in Addressables.ResourceLocators)
                foreach (var key in locator.Keys.OfType<string>())
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && locator.Locate(key, typeof(T), out var locations))
                        foreach (var location in locations)
                            if (location.PrimaryKey.StartsWith(prefix, StringComparison.Ordinal)) addresses.Add(location.PrimaryKey);
            return addresses.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }
        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;
#if UNITY_EDITOR
            // AssetDatabase permits inspection and import without a content build.
            if (!Application.isPlaying)
            {
                string path=ContentPaths.ResourcePath(key);
                if(typeof(T)==typeof(NativeEffectDefinition)&&key.StartsWith("vfx/",StringComparison.Ordinal))
                {
                    var parts=key.Split('/');
                    if(parts.Length==3)path="Assets/FFOne/Content/VFX/Published/"+(parts[1]=="effects"?"Effects":parts[1]=="projectiles"?"Projectiles":"Vehicles")+"/"+(parts[1]=="vehicles"?parts[2]:parts[2].ToUpperInvariant())+".asset";
                }
                if (path != null)
                {
                    if (typeof(T)==typeof(Sprite)) return UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().FirstOrDefault();
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                }
                return null;
            }
#endif
            if (!Known(key)) return null;
            var id=(key,typeof(T));
            if (!retained.TryGetValue(id,out var handle))
            {
                Trace(key);
                handle=Addressables.LoadAssetAsync<T>(key);
                retained.Add(id,handle);
            }
            if (!handle.IsDone) { TraceWait(key); handle.WaitForCompletion(); }
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
            if(typeof(T)!=typeof(Font)) throw new NotSupportedException("Enumerate typed catalog entries instead of loading an untyped address folder.");
            return Addresses<T>(prefix.TrimEnd('/')+"/").Select(Load<T>).Where(a=>a).ToArray();
        }
        public static Request LoadAsync<T>(string key) where T : UnityEngine.Object => Start<T>(key,true);
        // Await on the Unity context so scene activation and bundle providers can advance.
        // A loading screen cannot animate while WaitForCompletion holds the player loop.
        public static async Task<T> GetAsync<T>(string key, CancellationToken cancellation = default) where T : UnityEngine.Object
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(key)) return null;
#if UNITY_EDITOR
            if (!Application.isPlaying) return Load<T>(key);
#endif
            await InitializeAsync(cancellation);
            using var request = LoadAsync<T>(key);
            await request.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (request.asset is T asset) return asset;
            if (Known(key))
            {
                if (retained.TryGetValue((key, typeof(T)), out var failed) && request.Matches(failed))
                {
                    retained.Remove((key, typeof(T)));
                    if (failed.IsValid()) Addressables.Release(failed);
                }
                throw new InvalidOperationException("Unable to load content address: " + key);
            }
            return null;
        }
        public static Request AcquireAsync<T>(string key) where T : UnityEngine.Object => Start<T>(key,false);
        public static TResult Read<T,TResult>(string key,Func<T,TResult> read) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if(!Application.isPlaying)return read(Load<T>(key));
#endif
            if(!Known(key))return read(null);
            if (retained.TryGetValue((key, typeof(T)), out var cached) && cached.IsDone && cached.Status == AsyncOperationStatus.Succeeded)
                return read(cached.Result as T);
            Trace(key);
            var handle=Addressables.LoadAssetAsync<T>(key);
            try
            {
                if (!handle.IsDone) TraceWait(key);
                var asset=handle.WaitForCompletion();
                if(handle.Status!=AsyncOperationStatus.Succeeded)throw new InvalidOperationException("Unable to read content address: "+key);
                return read(asset);
            }
            finally { if(handle.IsValid())Addressables.Release(handle); }
        }
        public static async Task PreloadTexturesAsync(string prefix, CancellationToken cancellation = default)
        {
            await InitializeAsync(cancellation);
            var batch = new List<Task>();
            foreach (string key in Addresses<Texture2D>(prefix))
            {
                batch.Add(GetAsync<Texture2D>(key, cancellation));
                // UI skins resolve Sprite first to retain their authored nine-slice border.
                // Creator artwork is imported as single sprites, also used by Image controls.
                if (prefix == "ui/en/character/creation/" && Addressables.ResourceLocators.Any(locator => locator.Locate(key, typeof(Sprite), out _)))
                    batch.Add(GetAsync<Sprite>(key, cancellation));
                if (batch.Count >= 16) { await Task.WhenAll(batch); batch.Clear(); cancellation.ThrowIfCancellationRequested(); }
            }
            await Task.WhenAll(batch);
        }
        static Request Start<T>(string key,bool cache) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return new Request(Load<T>(key));
#endif
            if (!Known(key)) return new Request((UnityEngine.Object)null);
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
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        static void TraceWait(string key)
        {
            if (Environment.GetCommandLineArgs().Contains("--content-loading-profile"))
                Debug.Log("CONTENT SYNC WAIT: " + key);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            ReleaseCached();
            catalogLifetime.Cancel(); catalogLifetime.Dispose(); catalogLifetime = new();
            if (initialization.IsValid()) Addressables.Release(initialization);
            initialization = default; catalogLoading = null; initialized = false; catalogVersion++;
        }
        public sealed class Request : CustomYieldInstruction, IDisposable
        {
            AsyncOperationHandle handle;
            UnityEngine.Object immediate;
            bool owns, disposed;
            TaskCompletionSource<bool> disposedSignal;
            internal Request(AsyncOperationHandle value,bool ownership) { handle=value; owns=ownership; }
            internal Request(UnityEngine.Object value) { immediate=value; }
            public bool isDone => disposed || !handle.IsValid() || handle.IsDone;
            public UnityEngine.Object asset => disposed ? null : handle.IsValid() ? handle.IsDone && handle.Status==AsyncOperationStatus.Succeeded ? handle.Result as UnityEngine.Object : null : immediate;
            public override bool keepWaiting => !isDone;
            internal bool Matches(AsyncOperationHandle value) => handle.Equals(value);
            /// <summary>One completion subscription per waiter; cancellation never releases another consumer's cached load.</summary>
            public async Task WaitAsync(CancellationToken cancellation = default)
            {
                cancellation.ThrowIfCancellationRequested();
                if (disposed) throw new ObjectDisposedException(nameof(Request));
                if (isDone) return;
                // Resume consumers outside the provider callback. A synchronous
                // compatibility load in their continuation must not reenter ResourceManager.Update.
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pendingHandle = handle;
                Action<AsyncOperationHandle> completedHandle = _ => ready.TrySetResult(true);
                pendingHandle.Completed += completedHandle;
                disposedSignal ??= new TaskCompletionSource<bool>();
                // A disposed owning request must also wake a waiter if its operation
                // has been released before dispatching its completion callback.
                var ended = disposedSignal.Task;
                using var registration = cancellation.Register(() => ready.TrySetCanceled(cancellation));
                try
                {
                    await Task.WhenAny(ready.Task, ended);
                    if (disposed) throw new ObjectDisposedException(nameof(Request));
                    await ready.Task;
                }
                finally
                {
                    if (pendingHandle.IsValid()) pendingHandle.Completed -= completedHandle;
                }
            }
            public void Dispose()
            {
                if(disposed)return;
                disposed=true; immediate=null;
                disposedSignal?.TrySetResult(true);
                if(owns && handle.IsValid()) Addressables.Release(handle);
            }
        }
    }
}
