using System;
using System.Collections.Generic;
using FFOne.Content;
using FFOne.Network;
using FFOne.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.Integration
{
    [RequireComponent(typeof(FrontendController))]
    public sealed class PreviewStage : MonoBehaviour
    {
        sealed class View : IDisposable
        {
            public GameObject Root;
            public NativeAvatarPreview Avatar;
            public Camera Camera;
            public RenderTexture Texture;
            public Image Target;
            public string AppearanceKey;
            public bool Visible, HasFrame, NeedsFraming;
            public int FrameDelay, ResizeStableFrames;
            public int RenderedFrames;
            public Vector2Int PendingSize;
            public void Dispose()
            {
                if (Target != null && Target.image == Texture) Target.image = null;
                if (Camera) Camera.targetTexture = null;
                if (Texture) { Texture.Release(); Release(Texture); }
                if (Root) Release(Root);
            }
        }
        FrontendController frontend;
        readonly List<View> slots = new List<View>();
        View main;
        Image creatorTarget;
        Image gameplayPortrait;
        Image gameplayAvatar;
        int gameplaySlot;
        float zoom = 3;
        public RenderTexture Texture => main?.Texture;
        public void SetGameplayPortrait(Image image)
        {
            gameplayPortrait = image;
            if (image == null) gameplaySlot = 0;
        }
        public void SetGameplayAvatar(Image image) { gameplayAvatar = image; RequestFrame(main, 0); }
        void Start()
        {
            frontend = GetComponent<FrontendController>();
            main = CreateView(0);
            for (var i = 0; i < 4; i++) slots.Add(CreateView(i + 1));
            frontend.CharacterSelected += Select;
            frontend.RosterChanged += Roster;
            frontend.RotatePreview += Rotate;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--gameplay-ui-smoke") >= 0)
                RenderPipelineManager.endCameraRendering += DiagnoseRenderedCamera;
#endif
        }
        View CreateView(int index)
        {
            var view = new View { Root = new GameObject("Avatar preview " + index) };
            view.Root.transform.SetParent(transform, false);
            view.Root.transform.localPosition = new Vector3(index * 50, 1000, 0);
            var avatar = new GameObject("Avatar");
            avatar.transform.SetParent(view.Root.transform, false);
            view.Avatar = avatar.AddComponent<NativeAvatarPreview>();
            var camera = new GameObject("Preview Camera");
            camera.transform.SetParent(view.Root.transform, false);
            view.Camera = camera.AddComponent<Camera>();
            view.Camera.cullingMask = 1 << 31;
            view.Camera.clearFlags = CameraClearFlags.SolidColor;
            view.Camera.backgroundColor = Color.clear;
            view.Camera.fieldOfView = 30;
            view.Camera.nearClipPlane = .01f;
            view.Camera.farClipPlane = 100;
            view.Camera.allowHDR = false;
            view.Camera.allowMSAA = true;
            view.Camera.allowDynamicResolution = false;
            var data = view.Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            view.Camera.enabled = false;
            return view;
        }
        public void ShowDraft(CharacterInfo info, Image image)
        {
            if (creatorTarget == null) { zoom = 3; RequestFrame(main, 2); }
            creatorTarget = image;
            SetTarget(main, image);
            Apply(main, info);
        }
        public void EndDraft()
        {
            creatorTarget = null;
            zoom = 3;
            RequestFrame(main, 2);
            Select(frontend.SelectedCharacter);
        }
        void Select(CharacterInfo info) { if (main != null) Apply(main, info); }
        public void RefreshGameplayAppearance(CharacterInfo character)
        {
            if (character == null) return;
            gameplaySlot = character.Slot;
            Select(character);
            int index = character.Slot - 1;
            if (index >= 0 && index < slots.Count) Apply(slots[index], character);
        }
        void Roster(IReadOnlyList<CharacterInfo> roster)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                CharacterInfo character = null;
                foreach (var entry in roster) if (entry.Slot == i + 1) character = entry;
                Apply(slots[i], character);
            }
        }
        void Apply(View view, CharacterInfo character)
        {
            if (character == null)
            {
                view.AppearanceKey = null;
                view.Avatar.Clear();
                view.HasFrame = view.NeedsFraming = false;
                view.Camera.enabled = false;
                if (view.Target != null) view.Target.image = null;
                return;
            }
            var s = character.Style;
            var key = $"{character.Uid}:{s.Gender}:{s.FaceStyle}:{s.HairStyle}:{s.SkinColor}:{s.HairColor}:{s.EyeColor}:{s.Height}:{s.Body}";
            foreach (var item in character.Equipment) key += $":{item.ItemType}/{item.ItemId}/{(uint)item.Option >> 16}";
            if (key == view.AppearanceKey && view.Avatar.IsReady && view.Avatar.LastError == null) return;
            try
            {
                view.Avatar.Apply(character);
                if (!view.Avatar.IsReady || view.Avatar.LastError != null) throw new InvalidOperationException(view.Avatar.LastError ?? "Avatar dependencies are not imported.");
                view.AppearanceKey = key;
                foreach (var child in view.Avatar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                RequestFrame(view, 2);
            }
            // Apply commits new meshes atomically and retains the previous rig on missing assets.
            // Clearing here would erase the player's portrait for an unsupported equipment model.
            catch (Exception e) { frontend.ShowError(e.Message); }
        }
        public void Rotate(float degrees) { if (main != null) main.Avatar.Rotate(degrees); }
        public void Zoom(float delta) { zoom = Mathf.Clamp(zoom + delta, 1, 3); RequestFrame(main, 0); }
        static void RequestFrame(View view, int delay)
        {
            if (view == null) return;
            view.NeedsFraming = true;
            view.FrameDelay = delay;
        }
        void Frame(View view)
        {
            // A newly rebound skin's Renderer.bounds may still describe its previous
            // render. Frame the current pose, as the accepted avatar render tests do.
            if (!TryPoseBounds(view.Avatar, out var bounds)) { view.HasFrame = false; return; }
            if (view == main && gameplayAvatar != null && view.Target == gameplayAvatar)
            {
                // InventoryManagerScript.AvatarClothes + cnSimpleCharRenderCamera:
                // source FOV45, distance2.65, look at the clone before adding height1.
                view.Camera.fieldOfView = 45;
                Vector3 origin = view.Avatar.transform.position;
                view.Camera.transform.position = origin + Quaternion.Euler(-10, 0, 0) * Vector3.forward * 2.65f;
                view.Camera.transform.LookAt(origin);
                view.Camera.transform.position += Vector3.up;
                view.Camera.farClipPlane = 1000;
                view.HasFrame = true; view.NeedsFraming = false;
                return;
            }
            view.Camera.fieldOfView = 30;
            var center = bounds.center;
            var halfHeight = Mathf.Max(bounds.extents.y, bounds.extents.x / view.Camera.aspect) * 1.13f;
            if (view != main)
            {
                // The roster and HUD use head portraits, including the equipped helmet.
                // Body preview framing makes the face unreadable in a 63x85 slot.
                foreach (var bone in view.Avatar.GetComponentsInChildren<Transform>())
                {
                    if (bone.name != "Bip01 Head") continue;
                    float bottom = bone.position.y - bounds.size.y * .08f;
                    float portraitHeight = Mathf.Max(.1f, bounds.max.y - bottom);
                    center = new Vector3(bone.position.x, (bounds.max.y + bottom) / 2, bone.position.z);
                    halfHeight = Mathf.Max(portraitHeight / 2, portraitHeight * .4f / view.Camera.aspect) * 1.08f;
                    break;
                }
            }
            var distance = halfHeight / Mathf.Tan(view.Camera.fieldOfView * .5f * Mathf.Deg2Rad) + bounds.extents.z;
            if (view == main) distance *= zoom / 3;
            view.Camera.transform.position = center + new Vector3(0, 0, Mathf.Max(distance, .5f));
            view.Camera.transform.LookAt(center);
            view.Camera.farClipPlane = Mathf.Max(100, distance + bounds.extents.magnitude * 2);
            view.HasFrame = true;
            view.NeedsFraming = false;
        }
        static bool TryPoseBounds(NativeAvatarPreview avatar, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            var baked = new Mesh();
            try
            {
                foreach (var renderer in avatar.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled) continue;
                    if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
                    {
                        skin.BakeMesh(baked); baked.RecalculateBounds();
                        var local = baked.bounds;
                        for (int x = -1; x <= 1; x += 2)
                            for (int y = -1; y <= 1; y += 2)
                                for (int z = -1; z <= 1; z += 2)
                                {
                                    var point = skin.transform.TransformPoint(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                                    if (found) bounds.Encapsulate(point);
                                    else { bounds = new Bounds(point, Vector3.zero); found = true; }
                                }
                    }
                    else if (renderer is MeshRenderer && renderer.GetComponent<MeshFilter>()?.sharedMesh)
                    {
                        if (found) bounds.Encapsulate(renderer.bounds);
                        else { bounds = renderer.bounds; found = true; }
                    }
                }
            }
            finally { Release(baked); }
            return found;
        }
        void LateUpdate()
        {
            if (main == null) return;
            SetTarget(main, gameplayAvatar ?? creatorTarget ?? frontend.PreviewImage);
            Bind(main);
            for (int i = 0; i < slots.Count; i++)
            {
                bool gameplay = gameplayPortrait != null && (gameplaySlot > 0 ? gameplaySlot : frontend.SelectedCharacter?.Slot) == i + 1;
                SetTarget(slots[i], gameplay ? gameplayPortrait : frontend.SlotPreviewImages != null && i < frontend.SlotPreviewImages.Length ? frontend.SlotPreviewImages[i] : null);
                Bind(slots[i]);
            }
        }
        static void SetTarget(View view, Image target)
        {
            if (view == null || view.Target == target) return;
            // The previous target must not retain a texture that this view may resize
            // and release after switching from a creator panel to the inventory.
            if (view.Target != null && view.Target.image == view.Texture) view.Target.image = null;
            view.Target = target;
            // Appearance controls rebuild their Image. Keep its last rendered avatar while the new
            // element acquires its layout, rather than presenting an empty image for that frame.
            if (target != null && view.Avatar.IsReady && view.Texture != null) target.image = view.Texture;
        }
        void Bind(View view)
        {
            bool visible = view.Target != null && view.Target.panel != null;
            for (VisualElement element = view.Target; visible && element != null; element = element.parent)
                visible &= element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility != Visibility.Hidden;
            view.Visible = visible && view.Avatar.IsReady;
            if (view.Visible)
            {
                Vector2 physicalSize = view.Target.worldBound.size;
                // New or temporarily detached UI elements have no usable pixel rectangle yet.
                if (float.IsFinite(physicalSize.x) && float.IsFinite(physicalSize.y) && physicalSize.x >= 1 && physicalSize.y >= 1)
                    ResizeTexture(view, PreviewRenderQuality.Resolution(view.Target));
                if (view.Texture != null && view.Target.image != view.Texture) view.Target.image = view.Texture;
                if (view.Texture != null && view.NeedsFraming && view.FrameDelay-- <= 0) Frame(view);
            }
            view.Camera.enabled = view.Visible && view.HasFrame && view.Texture != null;
        }
        static void ResizeTexture(View view, Vector2Int size)
        {
            if (view.Texture != null)
            {
                // Fractional UI transforms can alternate across ceil()'s pixel boundary. A one-pixel
                // difference needs no allocation; real resizes wait for three stable layout frames.
                if (Mathf.Abs(view.Texture.width - size.x) <= 1 && Mathf.Abs(view.Texture.height - size.y) <= 1)
                {
                    view.ResizeStableFrames = 0;
                    if (!view.Texture.IsCreated()) view.Texture.Create();
                    return;
                }
                if (view.PendingSize != size) { view.PendingSize = size; view.ResizeStableFrames = 1; }
                else view.ResizeStableFrames++;
                if (view.ResizeStableFrames < 3) return;
            }
            var next = PreviewRenderQuality.Create(size, view.Root.name);
            if (!next.Create()) { Release(next); return; }
            var previous = view.Texture;
            view.Texture = next;
            view.Camera.targetTexture = next;
            view.Camera.aspect = (float)size.x / size.y;
            if (view.Target != null) view.Target.image = next;
            view.ResizeStableFrames = 0;
            view.NeedsFraming = true;
            // Preserve the delay requested by a freshly assembled avatar until animation is evaluated.
            if (previous) { previous.Release(); Release(previous); }
        }
        static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        public static Vector2Int PreviewResolution(Vector2 physicalSize) => PreviewRenderQuality.Resolution(physicalSize);
        void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RenderPipelineManager.endCameraRendering -= DiagnoseRenderedCamera;
#endif
            if (frontend != null)
            {
                frontend.CharacterSelected -= Select;
                frontend.RosterChanged -= Roster;
                frontend.RotatePreview -= Rotate;
            }
            main?.Dispose();
            foreach (var view in slots) view.Dispose();
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void DiagnoseRenderedCamera(ScriptableRenderContext context, Camera camera)
        {
            View view = main?.Camera == camera ? main : slots.Find(value => value.Camera == camera);
            if (view == null || ++view.RenderedFrames != 3) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var pipeline = camera.GetUniversalAdditionalCameraData();
            string culling = camera.TryGetCullingParameters(out var parameters)
                ? $"mask={parameters.cullingMask}" : "unavailable";
            Debug.Log($"AVATAR RENDER DIAGNOSTIC {view.Root.name}: frames={view.RenderedFrames}, cameraType={camera.cameraType}, renderType={pipeline.renderType}, renderer={pipeline.scriptableRenderer?.GetType().Name}, culling={culling}, target={camera.targetTexture?.name}, msaa={camera.targetTexture?.antiAliasing}, forward={camera.transform.forward}");
            foreach (var renderer in view.Avatar.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer;
                Debug.Log($"AVATAR RENDERER {renderer.name}: active={renderer.gameObject.activeInHierarchy}, enabled={renderer.enabled}, forceOff={renderer.forceRenderingOff}, layer={renderer.gameObject.layer}, renderingLayers={renderer.renderingLayerMask}, maskMatch={(camera.cullingMask & (1 << renderer.gameObject.layer)) != 0}, visible={renderer.isVisible}, bounds={renderer.bounds}, frustum={GeometryUtility.TestPlanesAABB(planes, renderer.bounds)}, skinMesh={skin?.sharedMesh?.name}, vertices={skin?.sharedMesh?.vertexCount}, bones={skin?.bones.Length}, rootBone={skin?.rootBone?.name}, offscreen={skin?.updateWhenOffscreen}");
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material) { Debug.Log("AVATAR MATERIAL null"); continue; }
                    string Props(string property) => material.HasProperty(property) ? material.GetColor(property).ToString() : "absent";
                    string TextureName(string property) => material.HasProperty(property) ? material.GetTexture(property)?.name ?? "null" : "absent";
                    Debug.Log($"AVATAR MATERIAL {material.name}: shader={material.shader?.name}, supported={material.shader?.isSupported}, queue={material.renderQueue}, passes={material.passCount}, surface={material.FindPass("Surface")}, color={Props("_Color")}, emission={Props("_Emission")}, main={TextureName("_MainTex")}, ramp={TextureName("_ShaderMap")}");
                }
            }
        }
#endif
    }
}
