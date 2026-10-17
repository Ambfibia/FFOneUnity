using System;
using System.Linq;
using UnityEngine;

namespace FFOne.Tutorial
{
    /// <summary>The temporary tutorial weapon. Server inventory changes only on tutorial save.</summary>
    public sealed class TutorialPlayerEquipment : MonoBehaviour
    {
        const string Model = "characters/player/items/weapon/rifle_electricgun/models/rifle_electricgun/model";
        const string Texture = "characters/player/items/weapon/rifle_electricgun/textures/rifle_electricgun";
        const string Socket = "Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Neck/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand/Bip01 Rweapon01";
        GameObject weapon;
        Material[] materials = Array.Empty<Material>();
        public static void Equip(Transform player)
        {
            var equipment = player.GetComponent<TutorialPlayerEquipment>() ?? player.gameObject.AddComponent<TutorialPlayerEquipment>();
            equipment.Attach();
        }
        void Attach()
        {
            if (weapon) { weapon.SetActive(true); return; }
            var prefab = FFOne.Content.NativeModelAssets.Load(Model);
            var texture = FFOne.Content.ContentAssets.Load<Texture2D>(Texture);
            if (!prefab || !texture) throw new InvalidOperationException("Tutorial weapon 328 has not been imported.");
            // The assembled avatar retains duplicate joints in its skin parts.
            // Only the animation-owning shared skeleton may own the attachment.
            var roots = GetComponentsInChildren<FFOne.Content.NativePlayerAnimator>(true).Select(a => a.transform)
                .Concat(GetComponentsInChildren<Animation>(true).Select(a => a.transform));
            var sockets = roots.SelectMany(root => new[] { root.Find("m/" + Socket), root.Find("w/" + Socket) })
                .Where(s => s != null).Distinct().ToArray();
            if (sockets.Length != 1) throw new InvalidOperationException("Expected one exact tutorial right-hand socket; found " + sockets.Length);
            var socket = sockets[0]; socket.localScale = Vector3.one;
            weapon = Instantiate(prefab, socket, false); weapon.name = "Tutorial weapon 328";
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.Euler(90, 0, 0);
            weapon.transform.localScale = Vector3.one;
            materials = weapon.GetComponentsInChildren<Renderer>(true).SelectMany(r => {
                var owned = r.sharedMaterials.Select(source => {
                    var material = new Material(source); material.SetTexture("_MainTex", texture); return material;
                }).ToArray(); r.sharedMaterials = owned; return owned;
            }).ToArray();
        }
        public void SetVisible(bool visible) { if (weapon) weapon.SetActive(visible); }
        void OnDestroy() { foreach (var material in materials) if (material) Destroy(material); }
    }
}
