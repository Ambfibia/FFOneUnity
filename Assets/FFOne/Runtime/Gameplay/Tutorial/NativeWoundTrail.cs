using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FFOne.Tutorial
{
    // LineRenderer owns the strip topology. Its immutable, uniformly spaced
    // template supplies the vertex index/side; the shader winds it around the
    // accepted world-space history, without rebuilding mesh vertices on the CPU.
    internal sealed class NativeWoundTrail : IDisposable
    {
        internal const string Keyword = "FFONE_NATIVE_WOUND_TRAIL";
        internal LineRenderer Renderer { get; }
        internal Vector3[] Points { get; }
        readonly Vector4[] upload;
        readonly GraphicsBuffer history;
        readonly MaterialPropertyBlock properties = new();
        readonly float height, step;
        bool disposed;

        internal NativeWoundTrail(Transform parent, Material material, int count, float width, float samplingStep, Vector3 initial)
        {
            if (count < 2 || samplingStep <= 0 || float.IsNaN(samplingStep)) throw new ArgumentException("Invalid source projectile trail history");
            height = width; step = samplingStep; Points = new Vector3[count]; upload = new Vector4[count];
            var child = new GameObject("Native wound projectile trail"); child.transform.SetParent(parent, false);
            Renderer = child.AddComponent<LineRenderer>();
            Renderer.useWorldSpace = false; Renderer.alignment = LineAlignment.TransformZ;
            Renderer.textureMode = LineTextureMode.Stretch;
            Renderer.numCornerVertices = 0; Renderer.numCapVertices = 0;
            Renderer.widthMultiplier = 1; Renderer.widthCurve = AnimationCurve.Constant(0,1,1);
            Renderer.startColor = Renderer.endColor = Color.white;
            Renderer.shadowCastingMode = ShadowCastingMode.Off; Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off; Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.generateLightingData = false;
            var template = new Vector3[count];
            for (int i=0;i<count;i++) { template[i]=Vector3.right*i; Points[i]=initial; }
            Renderer.positionCount=count; Renderer.SetPositions(template);
            Renderer.sharedMaterial=material;
            history=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,16);
            properties.SetBuffer("_FFTrailPoints",history);
            Present(0,null);
        }
        internal void Update(Vector3 current, float age, Camera view)
        {
            Vector3 previous=Points[Points.Length-1];
            for(float fraction=step;fraction<1;fraction+=step) Push(Vector3.Lerp(previous,current,fraction));
            Push(current); Present(age,view);
        }
        void Push(Vector3 point) { Array.Copy(Points,1,Points,0,Points.Length-1);Points[Points.Length-1]=point; }
        void Present(float age, Camera view)
        {
            var bounds=new Bounds(Points[0],Vector3.zero);
            for(int i=0;i<Points.Length;i++) { var p=Points[i];upload[i]=new Vector4(p.x,p.y,p.z,0);bounds.Encapsulate(p); }
            history.SetData(upload);
            Vector3 direction=(Points[Points.Length-1]-Points[Points.Length-2]).normalized;
            Vector3 right=view?view.transform.right:Vector3.right;
            properties.SetVector("_FFTrailShape",new Vector4(Points.Length,height,age*720,0));
            properties.SetVector("_FFTrailDirection",direction);
            properties.SetVector("_FFTrailRight",right);
            Renderer.SetPropertyBlock(properties);
            // Shader deformation is in world space, so the template's local bounds
            // cannot cull the actual ribbon. Preserve its full radial extent.
            bounds.Expand(Mathf.Abs(height)); Renderer.bounds=bounds;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(Renderer) { Renderer.enabled=false;Renderer.SetPropertyBlock(null);TutorialEffectAssets.Release(Renderer.gameObject); }
            history.Dispose();
        }
    }
}
