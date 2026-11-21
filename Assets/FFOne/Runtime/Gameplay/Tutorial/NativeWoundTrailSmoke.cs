#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FFOne.Tutorial
{
    // Development-only reference rasterizer. Production playback owns no Mesh.
    internal static class NativeWoundTrailComparison
    {
        [Serializable] internal sealed class Result
        {
            public string shader;
            public int effect, points, cases, visiblePixels, changedPixels, maximumError;
            public float meanError;
        }
        internal static Result Verify(TutorialEffectAssets assets, string output = null)
        {
            var root=new GameObject("Wound trail comparison");
            var cameraObject=new GameObject("Wound trail comparison camera");var camera=cameraObject.AddComponent<Camera>();
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=2.4f;camera.aspect=1;
            camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;camera.cullingMask=1<<31;
            var target=new RenderTexture(384,384,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var readback=new Texture2D(384,384,TextureFormat.RGB24,false);
            var referenceMaterial=new Material(assets.TrailMaterial);referenceMaterial.DisableKeyword(NativeWoundTrail.Keyword);
            var mesh=new Mesh();var reference=new GameObject("Former ribbon reference");reference.layer=31;
            reference.AddComponent<MeshFilter>().sharedMesh=mesh;var referenceRenderer=reference.AddComponent<MeshRenderer>();referenceRenderer.sharedMaterial=referenceMaterial;
            var vertices=new Vector3[assets.TrailLength*2];var colors=new Color[vertices.Length];var uv=new Vector2[vertices.Length];var triangles=new int[(assets.TrailLength-1)*6];
            var result=new Result {effect=assets.Id,points=assets.TrailLength,shader=assets.TrailMaterial.shader.name};long error=0;long components=0;
            var previousFog=Shader.GetGlobalVector("_FFWorldFog");
            if(result.shader=="FFOne/WorldParticle")Shader.SetGlobalVector("_FFWorldFog",new Vector4(0,0,0,.12f));
            NativeWoundTrail trail=null;
            try
            {
                for(int scenario=0;scenario<5;scenario++)
                {
                    float scale=scenario==3?2.3f:scenario==4?.4f:1;
                    trail=new NativeWoundTrail(root.transform,assets.TrailMaterial,assets.TrailLength,assets.TrailHeight*scale,assets.TrailStep,Vector3.zero);
                    trail.Renderer.gameObject.layer=31;
                    if(scenario==3) {root.transform.position=new Vector3(9,-3,2);root.transform.rotation=Quaternion.Euler(23,47,13);root.transform.localScale=new Vector3(-2,3,4);}
                    else {root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;}
                    camera.transform.SetPositionAndRotation(scenario==4?new Vector3(1,1,-7):new Vector3(0,0,-7),Quaternion.identity);
                    camera.transform.LookAt(Vector3.zero);
                    Vector3 current=Vector3.zero;float age=0;
                    for(int frame=0;frame<24;frame++)
                    {
                        age+=frame%3==0?.05f:.016f;
                        current=scenario==0?new Vector3((frame-12)*.12f,0,0)
                            :scenario==1?new Vector3(Mathf.Sin(frame*.3f)*1.3f,Mathf.Cos(frame*.23f)*.7f,Mathf.Sin(frame*.19f)*.4f)
                            :scenario==2?(frame<12?new Vector3((frame-12)*.12f,0,0):Vector3.zero)
                            :new Vector3(Mathf.Sin(frame*.22f)*.8f,(frame-12)*.08f,Mathf.Cos(frame*.2f)*.5f);
                        trail.Update(current,age,camera);
                        if(frame!=11 && frame!=17 && frame!=23)continue;
                        Vector3 direction=(current-trail.Points[trail.Points.Length-2]).normalized;
                        for(int i=0;i<trail.Points.Length;i++)
                        {
                            float fraction=(float)i/(trail.Points.Length-1);
                            var axis=direction==Vector3.zero?camera.transform.right:Quaternion.AngleAxis(fraction*360+age*720,direction)*camera.transform.right;
                            var half=axis*assets.TrailHeight*scale*.5f;
                            vertices[i*2]=trail.Points[i]+half;vertices[i*2+1]=trail.Points[i]+(i+1==trail.Points.Length?half:-half);
                            float u=i+1==trail.Points.Length?0:1-(float)(i+1)/trail.Points.Length;
                            uv[i*2]=new Vector2(u,0);uv[i*2+1]=new Vector2(u,1);
                            float shade=(float)Mathf.Min(i,trail.Points.Length-2)/(trail.Points.Length-1);
                            colors[i*2]=colors[i*2+1]=new Color(shade,shade,shade,shade);
                            if(i+1<trail.Points.Length){int t=i*6,n=i*2;triangles[t]=n;triangles[t+1]=n+1;triangles[t+2]=n+2;triangles[t+3]=n+2;triangles[t+4]=n+1;triangles[t+5]=n+3;}
                        }
                        mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
                        Color32[] Capture(bool native)
                        {
                            trail.Renderer.enabled=native;referenceRenderer.enabled=!native;
                            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
                            if(RenderPipeline.SupportsRenderRequest(camera,request))RenderPipeline.SubmitRenderRequest(camera,request);else camera.Render();
                            var previous=RenderTexture.active;
                            try{RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,384,384),0,0);readback.Apply();return readback.GetPixels32();}
                            finally{RenderTexture.active=previous;}
                        }
                        var actual=Capture(true);var expected=Capture(false);
                        for(int i=0;i<actual.Length;i++)
                        {
                            int r=Math.Abs(actual[i].r-expected[i].r),g=Math.Abs(actual[i].g-expected[i].g),b=Math.Abs(actual[i].b-expected[i].b);
                            int maximum=Math.Max(r,Math.Max(g,b));result.maximumError=Math.Max(result.maximumError,maximum);error+=r+g+b;components+=3;
                            if(maximum>2)result.changedPixels++;
                            if(actual[i].r>2||actual[i].g>2||actual[i].b>2)result.visiblePixels++;
                        }
                        if(output!=null && scenario==1 && frame==17)
                        {
                            Directory.CreateDirectory(output);
                            string prefix="es"+assets.Id+(result.shader=="FFOne/WorldParticle"?"-world":"-tutorial");
                            readback.SetPixels32(actual);readback.Apply();File.WriteAllBytes(Path.Combine(output,prefix+"-native.png"),readback.EncodeToPNG());
                            readback.SetPixels32(expected);readback.Apply();File.WriteAllBytes(Path.Combine(output,prefix+"-reference.png"),readback.EncodeToPNG());
                        }
                        result.cases++;
                    }
                    trail.Dispose();trail=null;
                }
                result.meanError=components==0?0:(float)((double)error/components);
                if(result.visiblePixels==0 || result.meanError>.03f || result.changedPixels>100)
                    throw new InvalidOperationException("Wound trail differs from source ES"+assets.Id+": "+JsonUtility.ToJson(result));
                return result;
            }
            finally
            {
                Shader.SetGlobalVector("_FFWorldFog",previousFog);
                referenceRenderer.enabled=false;
                trail?.Dispose();TutorialEffectAssets.Release(reference);TutorialEffectAssets.Release(mesh);TutorialEffectAssets.Release(referenceMaterial);
                TutorialEffectAssets.Release(root);TutorialEffectAssets.Release(cameraObject);target.Release();TutorialEffectAssets.Release(target);TutorialEffectAssets.Release(readback);
            }
        }
    }

    internal sealed class NativeWoundTrailSmoke : MonoBehaviour
    {
        internal static readonly int[] EffectIds={15,321,391,747,788,789,797,798,808};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if(Environment.GetCommandLineArgs().Contains("--native-wound-trail-smoke"))new GameObject("Wound trail acceptance").AddComponent<NativeWoundTrailSmoke>();
        }
        async void Start()
        {
            string output=Environment.GetEnvironmentVariable("FFONE_WOUND_TRAIL_REPORT")??Path.GetFullPath("Logs/NativeWoundTrail/ClientProbe");
            var results=new List<NativeWoundTrailComparison.Result>();
            try
            {
                await ContentAssets.InitializeAsync();
                using(var shader=ContentAssets.LoadAsync<Shader>("Tutorial/Particle"))await shader.WaitAsync();
                using(var shader=ContentAssets.LoadAsync<Shader>("World/Particle"))await shader.WaitAsync();
                foreach(int id in EffectIds)
                {
                    await ContentAssets.GetAsync<NativeEffectDefinition>(NativeEffectDefinition.Resource(id,true));
                    foreach(bool world in new[]{false,true})
                    {
                        using var assets=TutorialEffectAssets.Load(id,null,true,world);
                        results.Add(NativeWoundTrailComparison.Verify(assets,output));
                        Debug.Log("NATIVE WOUND TRAIL VERIFIED: "+id+" world="+world);await System.Threading.Tasks.Task.Yield();
                    }
                }
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,results},Newtonsoft.Json.Formatting.Indented));
                Debug.Log("NATIVE WOUND TRAIL PLAYER PASSED");Application.Quit(0);
            }
            catch(Exception error)
            {
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=false,error=error.ToString(),results},Newtonsoft.Json.Formatting.Indented));
                Debug.LogException(error);Application.Quit(1);
            }
        }
    }
}
#endif
