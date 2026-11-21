using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            public readonly Dictionary<int, NativeAvatarPreview> Genders = new();
            public Camera Camera;
            public RenderTexture Texture, PresentedTexture;
            public Image Target;
            public string AppearanceKey;
            public string PendingKey, FramingKey;
            public string PresentedKey, Error;
            public float PreloadProgress;
            public Transform Focus;
            public int Revision;
            public int Gender;
            public bool Visible, HasFrame, NeedsFraming;
            public int FrameDelay, ResizeStableFrames;
            public int RenderedFrames;
            public Vector2Int PendingSize;
            public void Dispose()
            {
                if (Target != null && Target.image == Texture) Target.image = null;
                if (Target != null && Target.image == PresentedTexture) Target.image = null;
                if (Camera) Camera.targetTexture = null;
                if (PresentedTexture && PresentedTexture != Texture) ReleaseTexture(PresentedTexture);
                if (Texture) ReleaseTexture(Texture);
                if (Root) Release(Root);
            }
        }
        [SerializeField] FrontendController frontend;
        readonly List<View> slots = new List<View>();
        View main;
        Image creatorTarget;
        Image gameplayPortrait;
        Image gameplayAvatar;
        int gameplaySlot;
        readonly CancellationTokenSource lifetime = new();
        float zoom = 3;
        public RenderTexture Texture => main?.Texture;
        static bool Presented(View view) => view != null && view.PendingKey == null && view.Avatar.IsReady &&
            view.Camera.enabled && view.AppearanceKey != null && view.PresentedKey == view.AppearanceKey;
        public bool DraftReady => creatorTarget != null && Presented(main);
        public string DraftError => main?.Error;
        public bool GameplayPortraitReady => !Displayed(gameplayPortrait) || slots.Exists(view => view.Target == gameplayPortrait && Presented(view));
        public bool SelectedPreviewReady => main?.Target == frontend?.PreviewImage && Presented(main);
        public bool SelectedPreviewFailed => main?.Target == frontend?.PreviewImage && main?.Error != null;
        public bool SelectionReady => (frontend?.SelectedCharacter == null || SelectedPreviewReady) &&
            slots.TrueForAll(view => view.AppearanceKey == null && view.PendingKey == null || Presented(view));
        public string SelectionError => main?.Error ?? slots.Find(view => view.Error != null)?.Error;
        public float SelectionResourceProgress
        {
            get
            {
                float total = 0; int count = 0;
                if (frontend?.SelectedCharacter != null) { total += main.PreloadProgress; count++; }
                foreach (var view in slots) if (view.AppearanceKey != null || view.PendingKey != null) { total += view.PreloadProgress; count++; }
                return count == 0 ? 1 : total / count;
            }
        }
        public int DraftGender => main?.Gender ?? 0;
        public Camera DraftCamera => main?.Camera;
        public void SetGameplayPortrait(Image image)
        {
            gameplayPortrait = image;
            if (image == null) gameplaySlot = 0;
        }
        public void SetGameplayAvatar(Image image) { gameplayAvatar = image; RequestFrame(main, 0); }
        void Start()
        {
            if (!frontend) TryGetComponent(out frontend);
            frontend.BindRosterPreview(this);
            main = CreateView(0);
            for (var i = 0; i < 4; i++) slots.Add(CreateView(i + 1));
            frontend.CharacterSelected += Select;
            frontend.RosterChanged += Roster;
            frontend.RotatePreview += Rotate;
            RenderPipelineManager.endCameraRendering += PresentedCamera;
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
        void Select(CharacterInfo info) { if (main != null) Apply(main, info, true); }
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
                Apply(slots[i], character, true);
            }
        }
        void Apply(View view, CharacterInfo character, bool roster = false)
        {
            if (character == null)
            {
                view.Revision++; view.PendingKey = null;
                view.AppearanceKey = null;
                view.Avatar.Clear();
                foreach (var cached in view.Genders.Values) if (cached != view.Avatar) cached.Clear();
                view.FramingKey = null;
                view.PresentedKey = view.Error = null; view.Focus = null; view.PreloadProgress = 0;
                view.HasFrame = view.NeedsFraming = false;
                view.Camera.enabled = false;
                if (view.Target != null) view.Target.image = null;
                return;
            }
            var s = character.Style;
            var key = $"{character.Uid}:{roster}:{s.AppearanceComplete}:{s.Gender}:{s.FaceStyle}:{s.HairStyle}:{s.SkinColor}:{s.HairColor}:{s.EyeColor}:{s.Height}:{s.Body}";
            foreach (var item in character.Equipment) key += $":{item.ItemType}/{item.ItemId}/{(uint)item.Option >> 16}";
            if (key == view.AppearanceKey && view.Avatar.IsReady && view.Avatar.LastError == null) return;
            if (key == view.PendingKey) return;
            view.PendingKey = key; view.Error = null; view.PreloadProgress = 0;
            _ = ApplyAsync(view, NativeAvatarPreview.Snapshot(character), key, ++view.Revision, roster);
        }
        async Task ApplyAsync(View view, CharacterInfo character, string key, int revision, bool roster)
        {
            try
            {
                if (roster && !character.Style.AppearanceComplete)
                    character = await FFOne.Creation.CreationCatalog.RosterPreviewAsync(character, lifetime.Token);
                await NativeAvatarPreview.PreloadAsync(character, lifetime.Token,
                    value => { if (revision == view.Revision) view.PreloadProgress = value; });
                if (!this || revision != view.Revision || lifetime.IsCancellationRequested) return;
                var previous = view.Avatar;
                if (!view.Genders.TryGetValue(character.Style.Gender, out var next))
                {
                    if (view.Genders.Count == 0) next = previous;
                    else
                    {
                        var root = new GameObject("Avatar " + character.Style.Gender);
                        root.transform.SetParent(view.Root.transform, false);
                        next = root.AddComponent<NativeAvatarPreview>();
                    }
                    view.Genders.Add(character.Style.Gender, next);
                }
                next.gameObject.SetActive(true);
                next.transform.localRotation = previous.transform.localRotation;
                next.Apply(character);
                if (!next.IsReady || next.LastError != null)
                {
                    if (next != previous) next.gameObject.SetActive(false);
                    throw new InvalidOperationException(next.LastError ?? "Avatar dependencies are not imported.");
                }
                if (next != previous) previous.gameObject.SetActive(false);
                view.Avatar = next;
                view.Gender = character.Style.Gender;
                view.AppearanceKey = key;
                view.PresentedKey = null; view.Focus = null;
                foreach (var child in view.Avatar.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                var s = character.Style;
                string framing = $"{s.Gender}:{s.Height}:{s.Body}";
                // Clothing and idle bounds are not a camera pivot. Keep the established body
                // framing until the body shape or viewport actually changes.
                if (view.FramingKey != framing || !view.HasFrame) RequestFrame(view, 2);
                view.FramingKey = framing;
            }
            // Apply commits new meshes atomically and retains the previous rig on missing assets.
            // Clearing here would erase the player's portrait for an unsupported equipment model.
            catch (OperationCanceledException) { }
            catch (Exception e) { if (this && revision == view.Revision) { view.Error = e.Message; frontend.ShowError(e.Message); } }
            finally { if (revision == view.Revision) view.PendingKey = null; }
        }
        public void Rotate(float degrees) { if (main != null) main.Avatar.Rotate(degrees); }
        public void Zoom(float delta) { zoom = Mathf.Clamp(zoom + delta, 1, 3); RequestFrame(main, 0); }
        static void RequestFrame(View view, int delay)
        {
            if (view == null) return;
            view.NeedsFraming = true;
            view.PresentedKey = null;
            view.FrameDelay = delay;
        }
        void Frame(View view)
        {
            var kind = view != main ? view.Target == gameplayPortrait ? AvatarPreviewView.Hud : AvatarPreviewView.Roster
                : view.Target == gameplayAvatar ? AvatarPreviewView.Inventory : creatorTarget != null ? AvatarPreviewView.Creation : AvatarPreviewView.Selection;
            Vector3 target = view.Avatar.transform.position;
            if (view != main)
            {
                if (!view.Focus)
                {
                    string boneName = kind == AvatarPreviewView.Hud ? "Bip01 Neck" : "Bip01 Head";
                    foreach (var bone in view.Avatar.GetComponentsInChildren<Transform>())
                        if (bone.name == boneName) { view.Focus = bone; break; }
                }
                if (!view.Focus) { view.Error = "Avatar portrait target is missing."; view.HasFrame = false; return; }
                target = view.Focus.position;
            }
            AvatarPreviewCamera.Apply(view.Camera, kind, target, view.Avatar.transform.rotation, view == main ? zoom : 3);
            view.HasFrame = true; view.NeedsFraming = false;
        }
        void PresentedCamera(ScriptableRenderContext context, Camera camera)
        {
            View view = main?.Camera == camera ? main : slots.Find(item => item.Camera == camera);
            if (view != null && view.PendingKey == null && view.HasFrame && !view.NeedsFraming && view.Texture && view.Avatar.IsReady)
            {
                var previous = view.PresentedTexture;
                view.PresentedTexture = view.Texture;
                if (view.Target != null && view.Target.image != view.Texture) view.Target.image = view.Texture;
                view.PresentedKey = view.AppearanceKey;
                if (previous && previous != view.Texture) ReleaseTexture(previous);
            }
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
            if (view.Target != null && view.Target.image == view.PresentedTexture) view.Target.image = null;
            view.Target = target;
            view.Focus = null; RequestFrame(view, 0);
            // Appearance controls rebuild their Image. Keep its last rendered avatar while the new
            // element acquires its layout, rather than presenting an empty image for that frame.
            if (target != null && view.Avatar.IsReady && view.PresentedTexture && view.PresentedTexture.IsCreated())
                target.image = view.PresentedTexture;
        }
        void Bind(View view)
        {
            bool visible = Displayed(view.Target);
            view.Visible = visible && view.Avatar.IsReady;
            if (view.Visible)
            {
                Vector2 physicalSize = view.Target.worldBound.size;
                // New or temporarily detached UI elements have no usable pixel rectangle yet.
                if (float.IsFinite(physicalSize.x) && float.IsFinite(physicalSize.y) && physicalSize.x >= 1 && physicalSize.y >= 1)
                    ResizeTexture(view, PreviewRenderQuality.Resolution(view.Target));
                if (view.Texture != null && view.NeedsFraming && view.FrameDelay-- <= 0) Frame(view);
                else if (view.Texture != null && view != main && view.HasFrame) Frame(view);
            }
            view.Camera.enabled = view.Visible && view.HasFrame && view.Texture != null;
        }
        static bool Displayed(VisualElement target)
        {
            bool visible = target != null && target.panel != null;
            for (var element = target; visible && element != null; element = element.parent)
                visible &= element.resolvedStyle.display != DisplayStyle.None && element.resolvedStyle.visibility != Visibility.Hidden;
            return visible;
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
            view.ResizeStableFrames = 0;
            view.NeedsFraming = true;
            view.PresentedKey = null;
            // Preserve the delay requested by a freshly assembled avatar until animation is evaluated.
            if (previous && previous != view.PresentedTexture) ReleaseTexture(previous);
        }
        static void ReleaseTexture(RenderTexture texture)
        {
            texture.Release();
            Release(texture);
        }
        static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        public static Vector2Int PreviewResolution(Vector2 physicalSize) => PreviewRenderQuality.Resolution(physicalSize);
        void OnDestroy()
        {
            lifetime.Cancel(); lifetime.Dispose();
            RenderPipelineManager.endCameraRendering -= PresentedCamera;
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
