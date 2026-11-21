using System;
using UnityEngine;

namespace FFOne.World
{
    /// <summary>Material choices for NPC types sharing this particular mesh and skeleton.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldNpcSkins : MonoBehaviour
    {
        [Serializable]
        public sealed class Choice
        {
            public int[] Types;
            public Material Material;
        }

        [Serializable]
        public sealed class Surface
        {
            public Renderer Renderer;
            public int Slot;
            public Material Default;
            public Choice[] Choices;
        }

        [SerializeField] Surface[] surfaces;

        public static void Apply(GameObject model, int npcType)
        {
            if (model.TryGetComponent<WorldNpcSkins>(out var skins)) skins.Apply(npcType);
        }

        public void Apply(int npcType)
        {
            foreach (var surface in surfaces)
            {
                var selected = surface.Default;
                foreach (var choice in surface.Choices)
                {
                    if (Array.IndexOf(choice.Types, npcType) < 0) continue;
                    selected = choice.Material;
                    break;
                }
                var materials = surface.Renderer.sharedMaterials;
                if (materials[surface.Slot] == selected) continue;
                materials[surface.Slot] = selected;
                surface.Renderer.sharedMaterials = materials;
            }
        }
    }
}
