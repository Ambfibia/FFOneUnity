using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FFOne.Editor;
using FFOne.Content;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class NativeEnvironmentEffectsTests
    {
        [Test]
        public void WorldBudgetIsSharedAndRetainsBoundedExpiryCleanup()
        {
            var source = Directory.GetFiles("Assets/FFOne/Worlds/Overworld/Cells","behaviour.json",SearchOption.AllDirectories)
                .Select(f=>JObject.Parse(File.ReadAllText(f))).First(d=>(d["effectPrefabClosures"] as JArray)?.Count > 0);
            var closure = (JObject)source["effectPrefabClosures"][0].DeepClone();
            string id = "particle-budget-fixture"; closure["id"] = id;
            var published=AssetDatabase.LoadAssetAtPath<NativeWorldParticleDefinition>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:NativeWorldParticleDefinition",new[]{NativeEffectPublication.Root})[0]));
            var fixture=ScriptableObject.CreateInstance<NativeWorldParticleDefinition>();
            fixture.Settings=new NativeParticleSettings{Id=id,Name=id,Lifetime=.1f,Rate=1,Count=1500,InitialSize=1,WorldMaterial=published.Settings.WorldMaterial,InitialEmit=true,XTiles=1,YTiles=1};
            var library=ScriptableObject.CreateInstance<NativeWorldEffectLibrary>();library.Particles=new[]{fixture};
            var record = JObject.Parse("{\"node\":\"budget\",\"maxTimer\":1,\"particles\":[{\"scriptKeys\":[{\"time\":0,\"translate\":[0,0,0],\"genType\":1},{\"time\":0.05,\"translate\":[0,0,0],\"genType\":0}]}]}");
            record["resolvedParticlePrefabs"] = new JArray(id);
            var doc = new JObject { ["id"]="fixture",["effectPrefabClosures"]=new JArray(closure),["effectEmitters"]=new JArray(record) };
            var near = new GameObject("Near cell"); var far = new GameObject("Far cell"); far.transform.position=Vector3.right*10;
            var camera = new GameObject("Budget camera").AddComponent<Camera>();camera.farClipPlane=10000;
            var field = typeof(WorldBehaviourParticles).GetField("updatedFrame",BindingFlags.Static|BindingFlags.NonPublic);
            var controllers = new List<WorldBehaviourParticles>();
            try
            {
                var a=WorldBehaviour.Attach(near,doc.ToString(),new(),new(),camera,effects:library);
                var b=WorldBehaviour.Attach(far,doc.ToString(),new(),new(),camera,effects:library);
                Assert.That(a.Errors,Is.Empty);Assert.That(b.Errors,Is.Empty);
                var particles=(WorldBehaviourParticles)typeof(WorldBehaviour).GetField("particles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(a);
                controllers.Add(particles);
                controllers.Add((WorldBehaviourParticles)typeof(WorldBehaviour).GetField("particles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(b));
                void Step(float dt) { field.SetValue(null,-1);particles.Update(dt,camera); }
                Step(.02f);
                Assert.AreEqual(1500,a.ActiveParticles);Assert.AreEqual(548,b.ActiveParticles);
                Step(.2f);
                Assert.AreEqual(0,a.ActiveParticles+b.ActiveParticles);
                Assert.AreEqual(2016,a.RetainedExpiredParticles+b.RetainedExpiredParticles);
                Step(.02f);Assert.AreEqual(1984,a.RetainedExpiredParticles+b.RetainedExpiredParticles);
            }
            finally
            {
                // These edit-mode fixtures never enter Unity's Awake/OnDestroy lifecycle.
                foreach(var controller in controllers)controller.Dispose();
                Object.DestroyImmediate(near);Object.DestroyImmediate(far);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(library);Object.DestroyImmediate(fixture);
            }
            Assert.AreEqual(0,typeof(WorldBehaviourParticles).GetField("budgetParticles",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null));
            Assert.AreEqual(0,WorldBehaviour.SharedParticlePlans);
        }

        [TestCase(0,false)] [TestCase(4,false)] [TestCase(5,false)]
        [TestCase(0,true)] [TestCase(4,true)] [TestCase(5,true)]
        public void NativeParticlesPreserveIntegrationSizeRollAndLifetime(int mode, bool transformed)
        {
            var root = new GameObject("Particle fixture"); var camera = new GameObject("View").AddComponent<Camera>();
            camera.transform.position=new Vector3(5,2,7);
            if(transformed)
            {
                root.transform.position=new Vector3(100,-30,250);
                root.transform.rotation=Quaternion.Euler(23,71,-13);
                root.transform.localScale=new Vector3(-2,3,4);
            }
            var material = new Material(Shader.Find("FFOne/WorldParticle"));
            NativeParticleEmitter emitter = null; var mesh = new Mesh();
            try
            {
                var force = new Vector3(.2f,-1,.3f); var velocity = new Vector3(2,3,4); var position = Vector3.one;
                emitter = new NativeParticleEmitter(root,material,2,1,AnimationCurve.Linear(0,2,1,4),AnimationCurve.Constant(0,1,3),
                    AnimationCurve.Constant(0,1,30),force,2,mode,4,4,600,true,false,Array.Empty<Color>(),2048);
                emitter.Emit(position,velocity);
                foreach (float dt in new[]{.02f,.04f,.01f,.03f})
                {
                    velocity += force*dt; position += velocity*dt;
                    Assert.AreEqual(0,emitter.Step(dt));
                }
                emitter.Face(camera);
                var p = new ParticleSystem.Particle[1]; emitter.System.GetParticles(p);
                Assert.That(Vector3.Distance(position,p[0].position),Is.LessThan(.0001f),"Source uses semi-implicit Euler integration");
                Assert.That(p[0].remainingLifetime,Is.EqualTo(1.9f).Within(.00001f));
                Assert.That(p[0].GetCurrentSize3D(emitter.System).x,Is.EqualTo(4.2f).Within(.002f));
                Assert.That(p[0].GetCurrentSize3D(emitter.System).y,Is.EqualTo(6).Within(.002f));
                root.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh,camera,true);
                Assert.AreEqual(4,mesh.vertexCount);
                var uv=new List<Vector4>();mesh.GetUVs(0,uv);
                Assert.That(uv[0].z,Is.EqualTo(.05f).Within(.00001f),"Shader atlas and source palette must receive normalized native age");
                var edge = (mesh.vertices[1]-mesh.vertices[0]).normalized;
                var right=Vector3.right;var up=Vector3.up;
                if(mode==4){right=Vector3.left;up=Vector3.back;}
                else if(mode==5){var direction=camera.transform.position-position;direction.y=0;right=-(Quaternion.LookRotation(direction)*Vector3.right);}
                Assert.That(Vector3.Distance(edge,right*Mathf.Cos(Mathf.PI/6)+up*Mathf.Sin(Mathf.PI/6)),Is.LessThan(.001f));
                Assert.AreEqual(1,emitter.Step(2)); Assert.AreEqual(0,emitter.Count);
            }
            finally { emitter?.Dispose(); if(root)Object.DestroyImmediate(root); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh); }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void NativePlatformClipPreservesTravelPathSpinAndTurnaround(int mode)
        {
            var source=JObject.FromObject(new { moveType=mode, from=new[]{0,0,0}, to=new[]{512,128,256}, spinVelocity=.37f });
            var path=new[]{Vector3.zero,new Vector3(20,30,50),new Vector3(-10,40,30),Vector3.right*100};
            var clip=WorldMotionPublication.CompilePlatform(source,path);var root=new GameObject("Platform fixture");
            try
            {
                foreach(float phase in new[]{0,.193f,.731f,.99995f,1,1.381f,1.93f})
                {
                    if(mode>=8 && phase>=1)continue;
                    root.transform.localRotation=Quaternion.identity;clip.SampleAnimation(root,phase);
                    bool returning=mode<8 && phase>=1;float t=returning?2-phase:phase;
                    float eased=Mathf.Clamp01(t-Mathf.Sin(t*Mathf.PI*2)*.15f),angle=phase*Mathf.PI+Mathf.PI/2;
                    var delta=new Vector3(512,128,256);float radius=delta.magnitude*.5f;Vector3 point=Vector3.zero;
                    switch(mode)
                    {
                        case 0: case 6: case 7:point=delta*eased;break;
                        case 1:point=delta*.5f+new Vector3(0,Mathf.Sin(angle),Mathf.Cos(angle))*radius;break;
                        case 2:point=delta*.5f+new Vector3(-Mathf.Sin(angle),Mathf.Cos(angle),0)*radius;break;
                        case 3:point=delta*.5f+new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;break;
                        case 8: case 9: case 10:float p=t*(path.Length-1);int i=Mathf.Min((int)p,path.Length-2);point=Vector3.Lerp(path[i],path[i+1],p-i);break;
                    }
                    Assert.That(Vector3.Distance(point,root.transform.localPosition),Is.LessThan(.001f),"phase="+phase);
                    float degrees=(mode>=8?t*360:(returning?-1:1)*eased*180)*.37f;
                    var rotation=mode is 4 or 6 or 9?Quaternion.AngleAxis(degrees,Vector3.right):mode is 5 or 7 or 10?Quaternion.AngleAxis(degrees,Vector3.forward):Quaternion.identity;
                    Assert.That(Quaternion.Angle(root.transform.localRotation,rotation),Is.LessThan(.06f),"phase="+phase);
                }
            }
            finally {Object.DestroyImmediate(clip);Object.DestroyImmediate(root);}
        }

        [Test]
        public void PersistentNativeParticlesKeepSimulatingAndAcceptLaterBirths()
        {
            var root=new GameObject("Persistent particle fixture");var material=new Material(Shader.Find("FFOne/WorldParticle"));
            NativeParticleEmitter emitter=null;
            try
            {
                emitter=new NativeParticleEmitter(root,material,600,1,new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),
                    Vector3.zero,1,0,1,1,1,true,false,Array.Empty<Color>(),2048);
                emitter.Emit(Vector3.zero,Vector3.right*2);emitter.Step(10);
                emitter.Emit(Vector3.zero,Vector3.right);emitter.Step(.25f);
                var particles=new ParticleSystem.Particle[2];Assert.AreEqual(2,emitter.System.GetParticles(particles));
                Assert.That(particles.Select(p=>p.remainingLifetime).OrderBy(v=>v),Is.EqualTo(new[]{589.75f,599.75f}).Within(.001f));
                Assert.That(particles.Select(p=>p.position.x).OrderBy(v=>v),Is.EqualTo(new[]{.25f,20.5f}).Within(.001f));
            }
            finally {emitter?.Dispose();if(root)Object.DestroyImmediate(root);Object.DestroyImmediate(material);}
        }

        [Test]
        public void PublishedTrsCurvesMatchSourceHermiteAtKeysAndBetweenKeys()
        {
            var root = new GameObject("Native curve fixture"); int channels = 0;
            try
            {
                foreach(string file in Directory.GetFiles("Assets/FFOne/Worlds/Overworld/Cells","behaviour.json",SearchOption.AllDirectories))
                    foreach(var source in JObject.Parse(File.ReadAllText(file))["animationClips"] ?? new JArray())
                        foreach(var channel in source["channels"] ?? new JArray())
                        {
                            var clip = WorldMotionPublication.CompileChannel(channel);
                            try
                            {
                                string property = (string)channel["property"]; var times = channel["times"].Select(t=>(float)t).ToArray();
                                var samples = times.Concat(times.Zip(times.Skip(1),(a,b)=>(a+b)*.5f));
                                var scalar = Enumerable.Range(0,property=="rotation"?4:3).Select(a=>WorldBehaviourAnimation.CompileScalar(channel,a)).ToArray();
                                foreach(float time in samples)
                                {
                                    clip.SampleAnimation(root,time);
                                    var expected = scalar.Select(s=>s.Evaluate(time,time,false)).ToArray();
                                    if(property=="rotation") Assert.That(Quaternion.Angle(root.transform.localRotation,new Quaternion(expected[0],-expected[1],-expected[2],expected[3]).normalized),Is.LessThan(.08f));
                                    else
                                    {
                                        var v = new Vector3(expected[0]*(property=="translation"?-1:1),expected[1],expected[2]);
                                        var actual = property=="translation"?root.transform.localPosition:root.transform.localScale;
                                        Assert.That(Vector3.Distance(actual,v),Is.LessThan(Mathf.Max(1,v.magnitude)*.00002f),file+" "+property+" "+time);
                                    }
                                }
                            }
                            finally { Object.DestroyImmediate(clip); }
                            channels++;
                        }
                Assert.Greater(channels,2000);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void EveryAuthoredCellReferencesPersistentMotionClips()
        {
            foreach(string guid in AssetDatabase.FindAssets("t:WorldCellDefinition",new[]{"Assets/FFOne/Worlds"}))
            {
                var cell=AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.AreEqual(1,cell.MotionVersion,cell.TileId);
                foreach(var binding in cell.Motion)
                    foreach(var clip in binding.Channels) Assert.That(clip && AssetDatabase.Contains(clip),Is.True,cell.TileId+" "+binding.SourceId);
            }
        }
    }
}
