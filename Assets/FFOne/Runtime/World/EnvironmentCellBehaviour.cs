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
        public string LastError { get; private set; }
        bool initializing;
        public WorldBehaviour Runtime { get; private set; }
        public IEnumerator Initialize()
        {
            if (Initialized || !string.IsNullOrEmpty(LastError)) yield break;
            if (initializing) { while (initializing) yield return null; yield break; }
            initializing = true;
            // Start and the scene streamer can both request initialization. Drive
            // nested iterators here so a failure always releases the other waiter.
            var pending = new Stack<IEnumerator>();
            pending.Push(InitializeSteps());
            try
            {
                while (pending.Count > 0)
                {
                    var iterator = pending.Peek();
                    object next = null; bool advanced = false; Exception failure = null;
                    try { advanced = iterator.MoveNext(); if (advanced) next = iterator.Current; }
                    catch (Exception error) { failure = error; }
                    if (failure != null)
                    {
                        LastError = failure.Message; Debug.LogException(failure, this); yield break;
                    }
                    if (!advanced) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
                    // Unity owns the wait instruction; disposing it as a nested
                    // coroutine clears its asset before InitializeSteps reads it.
                    if (next is IEnumerator nested && next is not CustomYieldInstruction) { pending.Push(nested); continue; }
                    yield return next;
                }
            }
            finally
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                initializing = false;
            }
        }
        IEnumerator InitializeSteps()
        {
            if (Definition && (Definition.TileId != TileId || Definition.SourceBindingsVersion != 1 || Definition.MotionVersion != 1 || Definition.UnpublishedModelIds == null))
                throw new InvalidOperationException("Republish source bindings for authored cell " + TileId);
            if(!Definition || !Definition.Effects)throw new InvalidOperationException("Publish native VFX for cell "+TileId);
            string document=Definition.Effects.Behaviour;

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
            ISet<string> sourceOmissions = Definition
                ? new HashSet<string>(Definition.UnpublishedModelIds, StringComparer.Ordinal) : null;
            yield return WorldBehaviour.AttachAsync(gameObject, document, legacy, nodes,
                View ? View : Camera.main, value => Runtime = value, sourceOmissions, Definition.Motion, Definition.Effects);
            if (Runtime && Definition.AdditionalLaunchers != null) Runtime.AddAuthoredLaunchers(Definition.AdditionalLaunchers);
            if (Runtime && Runtime.Errors.Count > 0) Debug.LogWarning(TileId + " behaviour: " + string.Join("; ", Runtime.Errors), this);
            foreach (Transform child in transform) WorldObjectRange.FinalizeTile(child.gameObject);
            Initialized = true;
        }
        IEnumerator Start() { if (InitializeOnStart) yield return Initialize(); }
        void OnDestroy()
        {
            initializing = false;
        }
        static void Add(Dictionary<string, List<Transform>> table, string id, Transform target)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!table.TryGetValue(id, out var values)) table.Add(id, values = new List<Transform>());
            values.Add(target);
        }
    }
}
