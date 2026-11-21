using System.Collections;
using UnityEngine;

namespace FFOne.Content
{
    public sealed partial class NativeAvatarPreview
    {
        const string ZiplineModel = "characters/player/traversal/pistol_trolley/pistol_trolley";
        const string Spine = "/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Neck";
        const string LeftArm = "/Bip01 L Clavicle/Bip01 L UpperArm/Bip01 L Forearm/Bip01 L Hand";
        const string RightArm = "/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand";
        const string LeftToe = "/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 L Thigh/Bip01 L Calf/Bip01 L Foot/Bip01 L Toe0";
        GameObject ziplineTrolley;
        NativeModelAssets.Request ziplineModel;
        Transform ziplineSocket;
        bool ziplinePresentation;
        string RigRoot => actorGender == "male" ? "m" : "w";


        GameObject skywayMount;
        NativeModelAssets.Request skywayModel;

        internal void SetSkywayPresentation(bool active)
        {
            if (!active)
            {
                if (skywayMount) { skywayMount.SetActive(false); Release(skywayMount); }
                skywayMount = null;
                if (skywayModel == null) return;
                skywayModel.Dispose();
                skywayModel = null;
                SetWeaponVisible(PersonalVehicleId == 0);
                playerAnimation?.ResetLocomotion();
                return;
            }
            if (skywayModel != null || !isActiveAndEnabled) return;
            CancelEmote();
            playerAnimation?.CancelUpperMotion();
            SetWeaponVisible(false);
            PlayLocomotion("mount1", true);
            skywayModel = NativeModelAssets.LoadAsync("characters/player/traversal/item_broom_01/item_broom_01");
            StartCoroutine(LoadSkywayMount(skywayModel));
        }

        IEnumerator LoadSkywayMount(NativeModelAssets.Request request)
        {
            yield return request;
            if (request != skywayModel) yield break;
            const string path = "/Bip01/Bip01 NonAccum/Bip01 Pelvis/Bip01 Broomstick";
            if (request.asset is not GameObject prefab || actorBones == null ||
                !actorBones.TryGetValue(RigRoot + path, out var socket))
            {
                Debug.LogWarning("Missing Monkey Skyway mount or Broomstick socket.", this);
                yield break;
            }
            // The mount enters at unit world scale before inheriting the animated socket.
            skywayMount = Instantiate(prefab, socket.position, socket.rotation * Quaternion.Euler(90, 0, 0));
            skywayMount.name = "Monkey Skyway";
            skywayMount.transform.SetParent(socket, true);
            foreach (var node in skywayMount.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
            foreach (var animation in skywayMount.GetComponentsInChildren<Animation>(true))
                if (animation["nif-default"] != null)
                {
                    animation["nif-default"].wrapMode = WrapMode.Loop;
                    animation.Play("nif-default");
                }
        }

        internal void SetZiplinePresentation(bool active)
        {
            if (active == ziplinePresentation) return;
            ziplinePresentation = active;
            if (!active)
            {
                ClearZiplineTrolley();
                SetWeaponVisible(PersonalVehicleId == 0);
                playerAnimation?.ResetLocomotion();
                return;
            }
            CancelEmote();
            playerAnimation?.CancelUpperMotion();
            SetWeaponVisible(false);
            PlayLocomotion("ropedown", true);
            if (isActiveAndEnabled)
            {
                ziplineModel = NativeModelAssets.LoadAsync(ZiplineModel);
                StartCoroutine(LoadZiplineTrolley(ziplineModel));
            }
        }

        IEnumerator LoadZiplineTrolley(NativeModelAssets.Request request)
        {
            yield return request;
            if (!ziplinePresentation || ziplineModel != request || actorBones == null) yield break;
            if (request.asset is not GameObject prefab ||
                !actorBones.TryGetValue(RigRoot + Spine + RightArm + "/Bip01 Rweapon01", out ziplineSocket))
            {
                Debug.LogWarning("Missing published zipline trolley or attachment socket.", this);
                yield break;
            }
            ziplineTrolley = Instantiate(prefab, ziplineSocket, false);
            ziplineTrolley.name = "Zipline trolley";
            ziplineTrolley.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(90, 0, 0));
            ziplineTrolley.transform.localScale = Vector3.one;
            foreach (var node in ziplineTrolley.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = gameObject.layer;
            FinishZiplinePose();
        }

        internal float ZiplineHangHeight(float fallback)
        {
            if (actorBones != null && actorBones.TryGetValue(RigRoot + Spine + LeftArm, out var hand) &&
                actorBones.TryGetValue(RigRoot + LeftToe, out var toe))
            {
                float height = hand.position.y - toe.position.y;
                if (float.IsFinite(height) && height > 0) return height;
            }
            return fallback;
        }

        void FinishZiplinePose()
        {
            if (ziplineSocket) ziplineSocket.localScale = Vector3.one;
        }

        void ClearZiplineTrolley()
        {
            if (ziplineTrolley) { ziplineTrolley.SetActive(false); Release(ziplineTrolley); }
            ziplineTrolley = null;
            ziplineSocket = null;
            ziplineModel?.Dispose();
            ziplineModel = null;
        }
    }
}
