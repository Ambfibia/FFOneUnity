using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FFOne.World
{
    // Original face events select a texture on the face material without replacing the body atlas.
    [DisallowMultipleComponent]
    internal sealed class NativeNanoFaceTrack : MonoBehaviour
    {
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        readonly List<(SkinnedMeshRenderer skin, int slot, Texture original)> slots = new();
        MaterialPropertyBlock properties;
        Animation player;
        NanoPresentationCatalog.ModelData definition;
        NanoPresentationCatalog.Cue[] cues;
        string clip;
        int cursor;
        float previousTime;
        bool changed;

        internal static void Attach(GameObject model, NanoPresentationCatalog.ModelData definition)
        {
            if (definition?.clips == null || !definition.clips.Values.Any(events =>
                events.Any(cue => cue.function == "emote_face_on"))) return;
            var animation = model.GetComponentInChildren<Animation>(true);
            if (!animation) return;
            var track = model.AddComponent<NativeNanoFaceTrack>();
            track.properties = new MaterialPropertyBlock();
            track.player = animation;
            track.definition = definition;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = skin.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (!material || !material.HasProperty(MainTexture)) continue;
                    var texture = material.GetTexture(MainTexture);
                    if (texture && texture.name.EndsWith("_face", StringComparison.OrdinalIgnoreCase))
                        track.slots.Add((skin, i, texture));
                }
            }
        }

        void LateUpdate()
        {
            AnimationState dominant = null;
            foreach (AnimationState state in player)
                if (state.enabled && state.weight > 0 && (dominant == null || state.weight > dominant.weight))
                    dominant = state;
            float time = dominant?.time ?? 0;
            if (dominant != null && dominant.wrapMode == WrapMode.Loop && dominant.length > 0)
                time = Mathf.Repeat(time, dominant.length);
            if (clip != dominant?.name || time < previousTime)
            {
                Apply(null);
                clip = dominant?.name;
                cursor = 0;
                cues = null;
                if (clip != null) definition.clips.TryGetValue(clip, out cues);
            }
            previousTime = time;
            while (cues != null && cursor < cues.Length && cues[cursor].time <= time)
            {
                var cue = cues[cursor++];
                if (cue.function == "emote_face_off") Apply(null);
                else if (cue.function == "emote_face_on")
                {
                    var face = Path.GetFileNameWithoutExtension(cue.payload);
                    if (!string.IsNullOrEmpty(face)) Apply(Resources.Load<Texture2D>("NanoFaces/" + face.ToLowerInvariant()));
                }
            }
        }

        void Apply(Texture face)
        {
            if (!face && !changed) return;
            foreach (var (skin, slot, original) in slots)
            {
                if (!skin) continue;
                skin.GetPropertyBlock(properties, slot);
                properties.SetTexture(MainTexture, face ? face : original);
                skin.SetPropertyBlock(properties, slot);
            }
            changed = face;
        }

        void OnDisable()
        {
            Apply(null);
            clip = null;
            cues = null;
            cursor = 0;
            previousTime = 0;
        }
    }
}
