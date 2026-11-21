using FFOne.Tutorial;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeWoundTrailTests
    {
        [TestCase(15,false)] [TestCase(15,true)]
        [TestCase(321,false)] [TestCase(321,true)]
        [TestCase(391,false)] [TestCase(391,true)]
        [TestCase(747,false)] [TestCase(747,true)]
        [TestCase(788,false)] [TestCase(788,true)]
        [TestCase(789,false)] [TestCase(789,true)]
        [TestCase(797,false)] [TestCase(797,true)]
        [TestCase(798,false)] [TestCase(798,true)]
        [TestCase(808,false)] [TestCase(808,true)]
        public void NativeRibbonMatchesSourceShapeTwistUvAndFade(int id, bool world)
        {
            bool previous=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            try
            {
                using var assets=TutorialEffectAssets.Load(id,null,true,world);
                Assert.That(assets.TrailMaterial,Is.Not.Null);Assert.That(ShaderUtil.ShaderHasError(assets.TrailMaterial.shader),Is.False);
                var result=NativeWoundTrailComparison.Verify(assets,"Logs/NativeWoundTrail/EditorProbe");
                Assert.AreEqual(15,result.cases);Assert.Greater(result.visiblePixels,0);
            }
            finally {ShaderUtil.allowAsyncCompilation=previous;}
        }
        [Test]
        public void LineRendererOwnsFixedTopologyAndSourceHistorySurvivesMovement()
        {
            var root=new GameObject("History fixture");var material=new Material(Shader.Find("FFOne/TutorialParticle"));
            NativeWoundTrail trail=null;
            try
            {
                trail=new NativeWoundTrail(root.transform,material,16,.3f,.25f,Vector3.zero);
                trail.Update(Vector3.right*4,0,null);
                Assert.That(trail.Points[12],Is.EqualTo(Vector3.right));Assert.That(trail.Points[13],Is.EqualTo(Vector3.right*2));
                Assert.That(trail.Points[14],Is.EqualTo(Vector3.right*3));Assert.That(trail.Points[15],Is.EqualTo(Vector3.right*4));
                Assert.AreEqual(16,trail.Renderer.positionCount);Assert.That(root.GetComponentsInChildren<MeshFilter>(),Is.Empty);
                Assert.That(trail.Renderer.bounds.Contains(Vector3.right*4+Vector3.up*.15f),Is.True);
                var camera=new GameObject("Topology camera").AddComponent<Camera>();var mesh=new Mesh();
                try
                {
                    trail.Renderer.BakeMesh(mesh,camera,true);
                    File.WriteAllText("Logs/NativeWoundTrail/topology.json",Newtonsoft.Json.JsonConvert.SerializeObject(new {
                        triangles=mesh.triangles.Take(12).ToArray(),uv=mesh.uv.Take(8).Select(v=>new[]{v.x,v.y}).ToArray(),vertices=mesh.vertexCount
                    },Newtonsoft.Json.Formatting.Indented));
                }
                finally{Object.DestroyImmediate(mesh);Object.DestroyImmediate(camera.gameObject);}
            }
            finally{trail?.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(material);}
        }
    }
}
