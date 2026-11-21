using UnityEngine;

namespace FFOne.Integration
{
    public enum AvatarPreviewView { Selection, Creation, Roster, Hud, Inventory }

    public static class AvatarPreviewCamera
    {
        // Primary sharedassets0: Camera0..3 1529/1527/1531/1533,
        // Selection RTCamera1525, Creation RTCamera1540. The height offset is
        // applied AFTER LookAt, exactly as cnSimpleCharRenderCamera.Update.
        public static void Apply(Camera camera, AvatarPreviewView kind, Vector3 target, Quaternion avatarRotation, float zoom = 3)
        {
            bool portrait = kind is AvatarPreviewView.Roster or AvatarPreviewView.Hud;
            float distance = portrait ? .5f : kind == AvatarPreviewView.Inventory ? 2.65f : 2 * zoom / 3;
            float height = kind switch {
                AvatarPreviewView.Roster => .12f, AvatarPreviewView.Hud => .2f,
                AvatarPreviewView.Creation => .7f, AvatarPreviewView.Inventory => 1, _ => .75f
            };
            Quaternion angle = kind == AvatarPreviewView.Hud ? avatarRotation * Quaternion.Euler(0, -20, 0)
                : Quaternion.Euler(portrait ? 0 : -10, 0, 0);
            camera.fieldOfView = 45;
            camera.nearClipPlane = kind == AvatarPreviewView.Hud ? .001f : portrait ? .01f : .3f;
            camera.farClipPlane = kind == AvatarPreviewView.Hud ? 100 : 1000;
            camera.transform.position = target + angle * Vector3.forward * distance;
            camera.transform.LookAt(target);
            camera.transform.position += Vector3.up * height;
        }
    }
}
