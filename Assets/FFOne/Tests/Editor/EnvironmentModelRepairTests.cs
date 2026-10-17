using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Text;
using FFOne.Editor;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class EnvironmentModelRepairTests
    {
        [Test]
        public void GlbBakeChangesGeometryBoundsNormalsAndTangentsOnce()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".glb");
            try
            {
                var json = JObject.Parse(@"{'asset':{'version':'2.0'},'nodes':[{'mesh':0}],'meshes':[{'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TANGENT':2}}]}],
                    'buffers':[{'byteLength':40}],'bufferViews':[{'buffer':0,'byteLength':40,'byteStride':40}],
                    'accessors':[{'bufferView':0,'byteOffset':0,'componentType':5126,'count':1,'type':'VEC3','min':[1,2,3],'max':[1,2,3]},
                    {'bufferView':0,'byteOffset':12,'componentType':5126,'count':1,'type':'VEC3'},
                    {'bufferView':0,'byteOffset':24,'componentType':5126,'count':1,'type':'VEC4'}]}");
                string text=json.ToString(Newtonsoft.Json.Formatting.None); while(Encoding.UTF8.GetByteCount(text)%4!=0)text+=" ";
                byte[] encoded=Encoding.UTF8.GetBytes(text);
                using(var writer=new BinaryWriter(File.Create(ContentPaths.FilePath(path))))
                {
                    writer.Write(0x46546C67u);writer.Write(2u);writer.Write((uint)(28+encoded.Length+40));
                    writer.Write(encoded.Length);writer.Write(0x4E4F534Au);writer.Write(encoded);writer.Write(40);writer.Write(0x004E4942u);
                    foreach(float v in new float[]{1,2,3,0,0,1,0,1,0,-1})writer.Write(v);
                }
                Assert.That(EnvironmentGlbBasis.Bake(path),Is.True);
                var doc=NativeGlbDocument.Read(path);
                Assert.That(doc.Values(0)[0],Is.EqualTo(new float[]{1,3,-2}));
                Assert.That(doc.Values(1)[0],Is.EqualTo(new float[]{0,1,0}));
                Assert.That(doc.Values(2)[0],Is.EqualTo(new float[]{0,0,-1,-1}));
                Assert.That(doc.Json["accessors"][0]["min"].ToObject<float[]>(),Is.EqualTo(new float[]{1,3,-2}));
                var bytes=File.ReadAllBytes(ContentPaths.FilePath(path)); Assert.That(EnvironmentGlbBasis.Bake(path),Is.False);
                Assert.That(File.ReadAllBytes(ContentPaths.FilePath(path)),Is.EqualTo(bytes));
            }
            finally { if(File.Exists(ContentPaths.FilePath(path)))File.Delete(ContentPaths.FilePath(path)); }
        }
        [Test]
        public void MapCompensationPreservesWorldVerticesWithNonuniformScaleAndTilt()
        {
            var root=new GameObject("map placement"); var node=new GameObject("mesh");node.transform.SetParent(root.transform,false);
            try
            {
                root.AddComponent<WorldModelBasis>();root.transform.SetPositionAndRotation(new Vector3(123,-67,890),Quaternion.Euler(41,72,-13));
                root.transform.localScale=new Vector3(-2,3,.7f);
                var old=new Vector3(4,-2,7);var expected=root.transform.TransformPoint(old);
                WorldModelBasis.ApplySource(root);WorldModelBasis.ApplySource(root);
                Assert.That(Vector3.Distance(node.transform.TransformPoint(EnvironmentGlbBasis.Rotate(old)),expected),Is.LessThan(.0001f));
                root.transform.rotation=Quaternion.Euler(15,120,63);
                Assert.That(Vector3.Distance(node.transform.TransformPoint(EnvironmentGlbBasis.Rotate(old)),root.transform.TransformPoint(old)),Is.LessThan(.0001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test]
        public void SkeletalAndTransformedSourcesAreRejectedBeforeWriting()
        {
            Assert.Throws<InvalidDataException>(()=>EnvironmentGlbBasis.Validate(JObject.Parse("{'nodes':[],'skins':[{}]}")));
            Assert.Throws<InvalidDataException>(()=>EnvironmentGlbBasis.Validate(JObject.Parse("{'nodes':[{'rotation':[0,0,0,1]}]}")));
        }
    }
}
