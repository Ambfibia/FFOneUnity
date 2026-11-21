using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FFOne.Content
{
    public sealed class WorldNanoEffectLibrary : ScriptableObject
    {
        public NativeEffectDefinition[] Effects = Array.Empty<NativeEffectDefinition>();
        static WorldNanoEffectLibrary loaded;
        static ResourceRequest pending;

        internal static async Task PrepareAsync(CancellationToken cancellation)
        {
            if (loaded) return;
            pending ??= Resources.LoadAsync<WorldNanoEffectLibrary>(nameof(WorldNanoEffectLibrary));
            var request = pending;
            while (!request.isDone)
            {
                cancellation.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            cancellation.ThrowIfCancellationRequested();
            loaded = request.asset as WorldNanoEffectLibrary;
            pending = null;
        }

        internal static NativeEffectDefinition Find(int id)
        {
            if (loaded)
                foreach (var effect in loaded.Effects)
                    if (effect && effect.Id == id) return effect;
            return null;
        }
    }
}
