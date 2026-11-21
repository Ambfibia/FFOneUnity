using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        static JObject vehicles;
        GameObject personalVehicle;
        readonly List<Material> vehicleMaterials = new();
        int vehicleId;
        string vehiclePrefix;
        bool vehicleJumpActive;
        float vehicleLandingUntil;
        public int PersonalVehicleId => vehicleId;
        public GameObject PersonalVehicleObject => personalVehicle;
        public string LocomotionClip(string clip) => vehicleId > 0 && clip is "stand1" or "run" or "runback" or "jumpstart" or "jump" or "jumpend" or "jumplandrun" or "inven"
            ? vehiclePrefix + clip : clip;
        public bool PlayLocomotion(string clip, bool restart = false)
        {
            if (!playerAnimation) return false;
            if (emoteCode != 0)
            {
                if (clip == "stand1" && !restart) return true;
                CancelEmote();
            }
            if (vehicleId > 0)
            {
                // AvatarJump uses CrossFade(.15), including a fresh local jump.
                if (clip == "jumpstart") { vehicleJumpActive = true; vehicleLandingUntil = 0; restart = false; }
                else if (clip is "stand1" or "run" or "runback")
                {
                    if (vehicleJumpActive)
                    {
                        vehicleJumpActive = false;
                        clip = clip == "stand1" ? "jumpend" : "jumplandrun";
                        vehicleLandingUntil = Time.time + NativeVehicleAnimation.EndSeconds(vehiclePrefix + clip) + NativeVehicleAnimation.ReturnBlendSeconds;
                        restart = false;
                    }
                    else if (vehicleLandingUntil > 0)
                    {
                        var animator = playerAnimation.Animator;
                        var current = animator.GetCurrentAnimatorStateInfo(0);
                        bool landing = current.IsName(NativePlayerAnimator.BaseLayer + "." + vehiclePrefix + "jumpend") ||
                            current.IsName(NativePlayerAnimator.BaseLayer + "." + vehiclePrefix + "jumplandrun");
                        // Ground movement is reported every frame. Preserve EndAnimation's
                        // .3 return blend after the source .35 landing event.
                        if (Time.time < vehicleLandingUntil || landing) return true;
                        vehicleLandingUntil = 0;
                    }
                }
            }
            return playerAnimation.Play(LocomotionClip(clip), restart);
        }
        public bool SetPersonalVehicle(int itemId)
        {
            if (vehicleId == itemId && (itemId == 0 || personalVehicle)) return true;
            CancelEmote();
            ClearVehicleVisual(); vehicleId = 0; vehiclePrefix = null; vehicleJumpActive = false; vehicleLandingUntil = 0;
            if (itemId == 0 || !actor || actorBones == null) { if (itemId == 0) playerAnimation?.Play("stand1"); return itemId == 0; }
            try
            {
                vehicles ??= Read("characters/player/shared/vehicles");
                var row = vehicles["vehicles"]!.SingleOrDefault(v => (int)v["itemId"] == itemId)
                    ?? throw new InvalidDataException("Missing accepted vehicle appearance: " + itemId);
                var item = GameplayUiCatalog.Load().Item(10, itemId) ?? throw new InvalidDataException("Missing vehicle table row: " + itemId);
                string prefix = item.VehicleEquipType switch { 1 => "board_", 2 or 3 => "scooter_", _ => throw new InvalidDataException("Unsupported vehicle equip type: " + item.VehicleEquipType) };
                string root = actorGender == "male" ? "m" : "w";
                string socketPath = root + "/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 R Thigh/Bip01 R Calf/Bip01 R Foot/vehicle";
                if (!actorBones.TryGetValue(socketPath, out var socket)) throw new InvalidDataException("Missing accepted vehicle socket: " + socketPath);
                var prefab = Load<GameObject>((string)row["model"]);
                // GameFrame.LoadVehicle instantiates at socket.position and socket.rotation * Euler(90,0,0),
                // then reparents retaining world scale. This is different from equipment's scale-one socket policy.
                personalVehicle = Instantiate(prefab, socket.position, socket.rotation * Quaternion.Euler(90, 0, 0));
                personalVehicle.name = "Personal vehicle " + itemId;
                personalVehicle.transform.SetParent(socket, true);
                foreach (var animation in personalVehicle.GetComponentsInChildren<Animation>(true))
                {
                    foreach (AnimationState clip in animation) clip.wrapMode = WrapMode.Loop;
                    animation.wrapMode = WrapMode.Loop; if (animation.clip) animation.Play();
                }
                var texture = Load<Texture2D>((string)row["texture"]);
                foreach (var renderer in personalVehicle.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(source => {
                        var material = new Material(source); vehicleMaterials.Add(material);
                        material.SetTexture("_MainTex", texture); NativeAvatarMaterials.Configure(material, "vehicle"); return material;
                    }).ToArray();
                foreach (var node in personalVehicle.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
                personalVehicle.AddComponent<NativeVehicleExhaust>().Initialize((string)row["model"]);
                vehicleId = itemId; vehiclePrefix = prefix; SetWeaponVisible(false); PlayLocomotion("stand1", true); return true;
            }
            catch (Exception e) { ClearVehicleVisual(); LastError = e.Message; Debug.LogWarning("FFOne vehicle: " + LastError, this); return false; }
        }
        void SetWeaponVisible(bool visible)
        {
            if (actorBones == null) return;
            foreach (var pair in actorBones.Where(p => p.Key.EndsWith("/Bip01 Rweapon01", StringComparison.Ordinal) || p.Key.EndsWith("/Bip01 Lweapon01", StringComparison.Ordinal)))
                foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>(true))
                    if (!ziplineTrolley || !renderer.transform.IsChildOf(ziplineTrolley.transform)) renderer.enabled = visible;
        }
        void RestorePersonalVehicle()
        {
            int wanted = vehicleId;
            if (wanted <= 0) return;
            if (personalVehicle && personalVehicle.transform.IsChildOf(actor.transform))
            {
                // Ordinary equipment updates reuse this rig. Keep the active mount and
                // jump/landing state, but hide any weapon renderers the update replaced.
                SetWeaponVisible(false); return;
            }
            vehicleId = -1; SetPersonalVehicle(wanted);
        }
        void ClearVehicleVisual()
        {
            if (personalVehicle) { personalVehicle.SetActive(false); Release(personalVehicle); }
            personalVehicle = null;
            foreach (var material in vehicleMaterials) if (material) Release(material);
            vehicleMaterials.Clear(); SetWeaponVisible(true);
        }
        void ClearPersonalVehicle() { ClearVehicleVisual(); vehicleId = 0; vehiclePrefix = null; }
    }
}
