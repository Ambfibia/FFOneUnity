using System;
using System.Collections;
using System.Collections.Generic;
using FFOne.Content;
using UnityEngine;

namespace FFOne.World
{
    public sealed class EnvironmentCellBehaviour : MonoBehaviour
    {
        [Serializable] public sealed class Binding { public string LegacyId, SourceNode; public Transform Target; public bool Visual; public float PresentationEnd; }
        public string TileId;
        public Binding[] Bindings = Array.Empty<Binding>();
        public Camera View;
        public WorldCellDefinition Definition;
        public bool InitializeOnStart = true;
        public bool Initialized { get; private set; }
        bool initializing;
        ContentAssets.Request sourceLease;
        public WorldBehaviour Runtime { get; private set; }
        public IEnumerator Initialize()
        {
            if (Initialized) yield break;
            if (initializing) { while (!Initialized) yield return null; yield break; }
            initializing = true;
            var legacy = new Dictionary<string, List<Transform>>();
            var nodes = new Dictionary<string, List<Transform>>();
            foreach (var binding in Bindings)
            {
                if (!binding.Target) continue;
                if (binding.Visual)
                {
                    WorldMaterialState.Prepare(binding.Target.gameObject);
                    WorldObjectRange.Attach(binding.Target.gameObject, binding.PresentationEnd);
                }
                Add(legacy, binding.LegacyId, binding.Target); Add(nodes, binding.SourceNode, binding.Target);
            }
            string document = NativeData.Read("map/tiles/" + TileId + "/behaviour");
            ISet<string> sourceOmissions = null;
            if (Definition)
            {
                sourceLease = ContentAssets.AcquireAsync<WorldTileContent>("World/Tiles/" + TileId + "/Content");
                yield return sourceLease;
                var accepted = sourceLease.asset as WorldTileContent;
                if (!accepted) throw new InvalidOperationException("Missing accepted source manifest for " + TileId);
                sourceOmissions = WorldSourceBindings.FindUnpublishedModels(document, accepted);
                sourceLease.Dispose(); sourceLease = null;
            }
            yield return WorldBehaviour.AttachAsync(gameObject, document, legacy, nodes,
                View ? View : Camera.main, value => Runtime = value, sourceOmissions);
            if (Runtime && Runtime.Errors.Count > 0) Debug.LogWarning(TileId + " behaviour: " + string.Join("; ", Runtime.Errors), this);
            foreach (Transform child in transform) WorldObjectRange.FinalizeTile(child.gameObject);
            Initialized = true;
        }
        IEnumerator Start() { if (InitializeOnStart) yield return Initialize(); }
        void OnDestroy() { sourceLease?.Dispose(); sourceLease = null; }
        static void Add(Dictionary<string, List<Transform>> table, string id, Transform target)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!table.TryGetValue(id, out var values)) table.Add(id, values = new List<Transform>());
            values.Add(target);
        }
    }
}
