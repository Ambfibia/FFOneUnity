using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.World
{
    // Bevy uses an infinite raster projection with a finite custom culling far
    // plane. Large terrain meshes crossing that plane retain their far vertices.
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class WorldCameraProjection : MonoBehaviour
    {
        Camera view;
        void Awake() => view = GetComponent<Camera>();
        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeRender;

        // Portals, reward shots and manual camera renders can change pose after LateUpdate.
        // Build the frustum from the same view matrix that will draw this frame.
        void BeforeRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view) Apply(view);
        }
        void LateUpdate() => Apply(view);

        public static void Apply(Camera camera)
        {
            if (!camera || camera.orthographic) return;
            var finite = Matrix4x4.Perspective(camera.fieldOfView, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            var infinite = finite;
            // Camera.projectionMatrix takes OpenGL clip coordinates; Unity
            // performs the API-specific depth conversion, including reverse Z.
            infinite.m22 = -1;
            infinite.m23 = -2 * camera.nearClipPlane;
            camera.projectionMatrix = infinite;
            camera.cullingMatrix = finite * camera.worldToCameraMatrix;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            if (!view) return;
            view.ResetProjectionMatrix();
            view.ResetCullingMatrix();
        }
    }
}
