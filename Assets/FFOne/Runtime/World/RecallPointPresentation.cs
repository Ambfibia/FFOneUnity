using System;
using FFOne.Tutorial;
using UnityEngine;

namespace FFOne.World
{
    /// <summary>The RXcom body has no Renderer; its original GameIcon effect supplies the visible marker.</summary>
    public sealed class RecallPointPresentation : MonoBehaviour
    {
        public const int SelectedEffect = 811;
        public int EffectId { get; private set; }
        public Transform EffectRoot => effect ? effect.transform : null;
        TutorialEffectAssets assets;
        GameObject effect;
        WorldNpcDefinition definition;
        Camera view;

        public void Initialize(WorldNpcDefinition npc, Camera camera, bool selected = false)
        {
            if (npc == null || npc.Category != 17 || npc.Type != 1386)
                throw new ArgumentException("Expected the accepted Recall Point NPC definition.", nameof(npc));
            definition = npc; view = camera;
            SetSelected(selected);
        }

        // Original NpcMoveController.MakeGameIcon compares player iRXCom with this
        // NPC's network instance ID. The caller owns that server-selected identity.
        public void SetSelected(bool selected)
        {
            if (definition == null) throw new InvalidOperationException("Recall Point presentation is not initialized.");
            int id = selected ? SelectedEffect : definition.Effect;
            if (effect && EffectId == id) return;
            var nextAssets = TutorialEffectAssets.Load(id, null);
            var next = new GameObject("Recall Point effect " + id);
            try
            {
                next.transform.SetParent(transform, false);
                // RXcom has neither GameIcon nor Bip01 Head. Preserve the exact
                // original coroutine's root fallback, height offset and rotation.
                next.transform.localPosition = Vector3.up * (definition.Height * .9f);
                next.transform.localRotation = Quaternion.Euler(90, 0, 0);
                next.AddComponent<TutorialEffectPlayback>().Initialize(nextAssets, 1, view);
            }
            catch { TutorialEffectAssets.Release(next); nextAssets.Dispose(); throw; }
            Clear(); assets = nextAssets; effect = next; EffectId = id;
        }

        void Clear()
        {
            if (effect) TutorialEffectAssets.Release(effect);
            assets?.Dispose(); assets = null; effect = null; EffectId = 0;
        }
        void OnDestroy() => Clear();
    }
}
