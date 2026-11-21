using System;
using System.Collections.Generic;
using FFOne.Content;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    /// <summary>Source NanoClothes/cnSimpleCharRenderCamera presentation, composited into UI Toolkit.</summary>
    public sealed class GameplayNanoRenderStage : MonoBehaviour
    {
        sealed class Slot
        {
            public string Resource;
            public Image Target;
            public NativeModelAssets.Request Request;
            public GameObject Model;
            public Camera Camera;
            public RenderTexture Texture;
            public Animation Animation;
            public float CameraOffset;
            public string Idle;
            public float LastIdleTime;
            public Vector2Int PendingSize;
            public int ResizeStableFrames;
            public readonly List<Material> Materials = new List<Material>();
        }
        readonly Slot[] slots = { new Slot(), new Slot(), new Slot() };
        bool disposed;

        public void Bind(int index, Image target, string resource, float cameraOffset)
        {
            var slot = slots[index];
            slot.CameraOffset = cameraOffset;
            if (slot.Resource == resource && slot.Target == target) return;
            Release(slot);
            slot.Target = target; slot.Resource = resource;
            if (!string.IsNullOrEmpty(resource)) slot.Request = NativeModelAssets.LoadAsync(resource);
        }

        void LateUpdate()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Request != null && slot.Request.isDone && !slot.Model)
                {
                    var prefab = slot.Request.asset as GameObject;
                    if (!prefab)
                    {
                        Debug.LogError("Nano HUD model could not load: " + slot.Resource);
                        slot.Request.Dispose(); slot.Request = null; continue;
                    }
                    slot.Model = Instantiate(prefab, transform);
                    slot.Model.name = "HUD Nano " + i;
                    slot.Model.transform.localPosition = new Vector3(i * 5, 0, 0);
                    slot.Model.transform.localRotation = Quaternion.identity;
                    foreach (var child in slot.Model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
                    foreach (var collider in slot.Model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    // NanoMoveControllerClone.SetMatterial only repairs zero emission on
                    // its UI clones. Keep the library materials untouched.
                    var copies = new Dictionary<Material, Material>();
                    foreach (var renderer in slot.Model.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials = renderer.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++)
                        {
                            var original = materials[m];
                            if (!original || !original.HasProperty("_Emission")) continue;
                            Color emission = original.GetColor("_Emission");
                            if (emission.r != 0 || emission.g != 0 || emission.b != 0) continue;
                            if (!copies.TryGetValue(original, out var copy))
                            {
                                copy = new Material(original) { name = original.name + " (Nano HUD)", hideFlags = HideFlags.DontSave };
                                copy.SetColor("_Emission", new Color(.25f, .25f, .25f, 0));
                                copies.Add(original, copy); slot.Materials.Add(copy);
                            }
                            materials[m] = copy;
                        }
                        renderer.sharedMaterials = materials;
                    }
                    slot.Animation = slot.Model.GetComponentInChildren<Animation>();
                    if (slot.Animation)
                    {
                        foreach (AnimationState state in slot.Animation) state.wrapMode = WrapMode.Loop;
                        slot.Animation.cullingType = AnimationCullingType.AlwaysAnimate;
                        NextIdle(slot);
                    }
                    var cameraOwner = new GameObject("HUD Nano camera " + i);
                    cameraOwner.transform.SetParent(transform, false);
                    slot.Camera = cameraOwner.AddComponent<Camera>();
                    slot.Camera.cullingMask = 1 << 30;
                    slot.Camera.fieldOfView = 45; slot.Camera.nearClipPlane = .3f; slot.Camera.farClipPlane = 1000;
                    // Retrobution renders straight into the framebuffer with Depth clear.
                    // A transparent target is required for UI Toolkit composition instead.
                    slot.Camera.clearFlags = CameraClearFlags.SolidColor; slot.Camera.backgroundColor = Color.clear;
                    slot.Camera.allowHDR = false; slot.Camera.allowMSAA = true;
                    slot.Camera.allowDynamicResolution = false;
                    var pipeline = slot.Camera.GetUniversalAdditionalCameraData();
                    pipeline.renderPostProcessing = false; pipeline.antialiasing = AntialiasingMode.None;
                }
                if (!slot.Camera) continue;
                var target = slot.Target;
                bool visible = target?.panel != null && target.visible && target.worldBound.width > 0 && target.worldBound.height > 0;
                slot.Camera.enabled = visible;
                if (slot.Animation) slot.Animation.enabled = visible;
                if (!visible) continue;
                if (slot.Animation && slot.Idle != null)
                {
                    float time = slot.Animation[slot.Idle].normalizedTime;
                    // Imported native clips currently lack the original `end` event.
                    // Restore its stand-mode transition at each completed clip cycle.
                    if (time >= 1 || time < slot.LastIdleTime) NextIdle(slot);
                    else slot.LastIdleTime = time;
                }
                var size = PreviewRenderQuality.Resolution(target);
                bool resize = !slot.Texture;
                if (slot.Texture && (Mathf.Abs(slot.Texture.width - size.x) > 1 || Mathf.Abs(slot.Texture.height - size.y) > 1))
                {
                    if (slot.PendingSize != size) { slot.PendingSize = size; slot.ResizeStableFrames = 1; }
                    else slot.ResizeStableFrames++;
                    resize = slot.ResizeStableFrames >= 3;
                }
                else slot.ResizeStableFrames = 0;
                if (resize)
                {
                    var next = PreviewRenderQuality.Create(size, "Animated Nano HUD " + i);
                    if (!next.Create()) { Destroy(next); continue; }
                    var previous = slot.Texture;
                    slot.Texture = next;
                    slot.Camera.targetTexture = slot.Texture;
                    target.image = slot.Texture;
                    slot.ResizeStableFrames = 0;
                    if (previous) { previous.Release(); Destroy(previous); }
                }
                Vector3 origin = slot.Model.transform.position;
                var cameraTransform = slot.Camera.transform;
                cameraTransform.position = origin + Quaternion.Euler(-10, 0, 0) * Vector3.forward * .8f;
                cameraTransform.LookAt(origin);
                cameraTransform.position += Vector3.up * (.3f + slot.CameraOffset);
            }
        }

        public static float StaminaCameraOffset(float? stamina, bool active) => stamina.HasValue && !active && stamina.Value < .2f
            ? .48f * (1 - Mathf.Clamp01(stamina.Value / .2f)) : 0;

        static void NextIdle(Slot slot)
        {
            string idle = FFOne.World.NanoClipNames.Resolve(slot.Resource, "stand" + UnityEngine.Random.Range(1, 4));
            if (!slot.Animation[idle]) idle = FFOne.World.NanoClipNames.Resolve(slot.Resource, "stand1");
            if (!slot.Animation[idle]) return;
            slot.Idle = idle; slot.LastIdleTime = 0;
            slot.Animation[idle].time = 0;
            slot.Animation.CrossFade(idle, .1f);
        }

        static void Release(Slot slot)
        {
            if (slot.Target != null && slot.Target.image == slot.Texture) slot.Target.image = null;
            if (slot.Camera) { slot.Camera.enabled = false; slot.Camera.targetTexture = null; Destroy(slot.Camera.gameObject); }
            if (slot.Model) { slot.Model.SetActive(false); Destroy(slot.Model); }
            if (slot.Texture) { slot.Texture.Release(); Destroy(slot.Texture); }
            foreach (var material in slot.Materials) Destroy(material);
            slot.Materials.Clear(); slot.Idle = null;
            slot.Request?.Dispose(); slot.Request = null;
            slot.Camera = null; slot.Model = null; slot.Texture = null; slot.Animation = null;
            slot.Resource = null; slot.Target = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (this) enabled = false;
            foreach (var slot in slots) Release(slot);
            if (this) Destroy(gameObject);
        }
        void OnDestroy()
        {
            // Scene/application teardown can destroy this stage before the
            // frontend disposes its HUD. Mark released ownership in either order.
            if (disposed) return;
            disposed = true;
            foreach (var slot in slots) Release(slot);
        }
    }
}
