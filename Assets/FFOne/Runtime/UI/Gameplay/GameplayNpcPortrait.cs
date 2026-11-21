using System;
using System.Collections.Generic;
using FFOne.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    // Render the live actor, including the pose and equipment already applied by replication.
    internal sealed class GameplayNpcPortrait : IDisposable
    {
        readonly Image image;
        readonly Func<NetworkEntityView> resolve;
        readonly int width, height;
        readonly float distance, elevation, angle;
        readonly bool frameHead;
        readonly EventCallback<AttachToPanelEvent> onAttach;
        readonly EventCallback<DetachFromPanelEvent> onDetach;
        NetworkEntityView previous;
        readonly Dictionary<GameObject, int> originalLayers = new();
        readonly List<Renderer> renderers = new();
        const int Layer = 29;
        Camera camera;
        RenderTexture target;
        Transform head;

        public GameplayNpcPortrait(Image image, Func<NetworkEntityView> resolve, int width, int height,
            float distance, float elevation, float angle, bool frameHead = false)
        {
            this.image = image; this.resolve = resolve; this.width = width; this.height = height;
            this.distance = distance; this.elevation = elevation; this.angle = angle;
            this.frameHead = frameHead || image.name == "croc-npc";
            onAttach = _ => Attach();
            onDetach = _ => Release();
            image.RegisterCallback(onAttach);
            image.RegisterCallback(onDetach);
            if (image.panel != null) Attach();
        }

        void Attach()
        {
            if (camera || !Application.isPlaying) return;
            camera = new GameObject("NPC portrait: " + image.name).AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << Layer; camera.fieldOfView = 45; camera.nearClipPlane = .2f;
            camera.farClipPlane = 5; camera.depth = -20; camera.allowHDR = false;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = image.name };
            target.Create(); camera.targetTexture = target; image.image = target;
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }

        void BeforeRender(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering != camera) return;
            RestoreLayers();
            var actor = resolve();
            if (!actor || !actor.gameObject.activeInHierarchy) return;
            var root = actor.transform;
            Vector3 direction = root.rotation * Quaternion.Euler(0, angle, 0) * Vector3.forward;
            float height=elevation;
            // A live HNPC's root is at its feet. Bind the head once its async rig is present.
            if (frameHead)
            {
                if (previous != actor) { previous = actor; head = null; }
                if(!head)
                    foreach(var bone in root.GetComponentsInChildren<Transform>())
                        if(bone.name=="Bip01 Head"){head=bone;break;}
                if(head) height=head.position.y-root.position.y-.12f;
            }
            camera.transform.SetPositionAndRotation(root.position + direction * distance + Vector3.up * height,
                Quaternion.LookRotation(-direction, Vector3.up));
            actor.GetComponentsInChildren(false, renderers);
            foreach (var renderer in renderers)
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || !renderer.enabled) continue;
                var go = renderer.gameObject;
                if (originalLayers.TryAdd(go, go.layer)) go.layer = Layer;
            }
        }

        void AfterRender(ScriptableRenderContext context, Camera rendering)
        { if (rendering == camera) RestoreLayers(); }

        void RestoreLayers()
        {
            foreach (var entry in originalLayers) if (entry.Key) entry.Key.layer = entry.Value;
            originalLayers.Clear();
        }

        public void Dispose()
        {
            image.UnregisterCallback(onAttach);
            image.UnregisterCallback(onDetach);
            Release();
        }

        void Release()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
            RestoreLayers(); image.image = null;
            if (camera) { camera.enabled = false; camera.targetTexture = null; UnityEngine.Object.Destroy(camera.gameObject); }
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            camera = null; target = null;
        }
    }
}
