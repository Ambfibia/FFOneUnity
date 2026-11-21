using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Tutorial;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FFOne.Tests
{
    public sealed class NativeEffectPublicationTests
    {
        [Test]
        public void PublishedDefinitionsOwnPersistentSharedResourcesAndCellsDoNotCarryPayloads()
        {
            var effects=AssetDatabase.FindAssets("t:NativeEffectDefinition",new[]{"Assets/FFOne/Content/VFX/Published"});
            Assert.AreEqual(252,effects.Length);
            foreach(var guid in effects)
            {
                var definition=AssetDatabase.LoadAssetAtPath<NativeEffectDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(definition.SourceHash,Is.Not.Empty,definition.name);
                foreach(var plan in definition.Emitters)
                {
                    Assert.That(plan.Keys,Is.Not.Empty,definition.name);
                    foreach(var material in new[]{plan.WorldMaterial,plan.TutorialMaterial}.Where(m=>m))
                    {Assert.True(AssetDatabase.Contains(material));Assert.True(AssetDatabase.Contains(material.mainTexture));}
                }
                foreach(var material in new[]{definition.WorldTrailMaterial,definition.TutorialTrailMaterial}.Where(m=>m))
                {Assert.True(AssetDatabase.Contains(material));Assert.True(material.IsKeywordEnabled(NativeWoundTrail.Keyword));}
            }
            foreach(var guid in AssetDatabase.FindAssets("t:WorldCellDefinition",new[]{"Assets/FFOne/Worlds"}))
            {
                var cell=AssetDatabase.LoadAssetAtPath<WorldCellDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.True(cell.Effects,cell.TileId);Assert.True(AssetDatabase.Contains(cell.Effects));
                var data=JObject.Parse(cell.Effects.Behaviour);
                Assert.Null(data["effectPrefabClosures"],cell.TileId);
                Assert.That(cell.Effects.Behaviour,Does.Not.Contain("\"base64\""),cell.TileId);
                foreach(var plan in cell.Effects.Particles){Assert.True(plan);Assert.True(AssetDatabase.Contains(plan.Settings.WorldMaterial.mainTexture));}
            }
        }
        [TestCase(15,true)] [TestCase(391,true)] [TestCase(366,false)] [TestCase(668,false)] [TestCase(740,false)]
        public void PublishedPixelPayloadsMatchAcceptedTextureData(int id,bool projectile)
        {
            var definition=ContentAssets.Load<NativeEffectDefinition>(NativeEffectDefinition.Resource(id,projectile));Assert.True(definition);
            var data=NativeData.Object((projectile?"map/shared/projectiles/effects/es":"map/shared/effects/es")+id+".closure");
            var objects=(JArray)data["objects"];int index=0;
            foreach(var entry in objects.Where(o=>(string)o["objectType"]=="MonoBehaviour"&&o["value"]?["numberPerGeneration"]!=null))
            {
                var settings=entry["value"];long actor=(long)settings["m_GameObject"]["pathId"];bool trail=(int)settings["m_iGenType"]==12;
                var renderer=objects.Single(o=>(string)o["objectType"]==(trail?"MeshRenderer":"ParticleRenderer")&&(long?)o["value"]?["m_GameObject"]?["pathId"]==actor)["value"];
                var source=objects.Single(o=>(string)o["objectType"]=="Material"&&(long)o["pathId"]==(long)renderer["m_Materials"][0]["pathId"])["value"];
                var env=source["m_SavedProperties"]["m_TexEnvs"].Single(v=>(string)v[0]["name"]=="_MainTex")[1];
                var t=objects.Single(o=>(string)o["objectType"]=="Texture2D"&&(long)o["pathId"]==(long)env["m_Texture"]["pathId"])["value"];
                int f=(int)t["m_TextureFormat"];var format=f switch{10=>TextureFormat.DXT1,12=>TextureFormat.DXT5,2=>TextureFormat.ARGB4444,3=>TextureFormat.RGB24,4=>TextureFormat.RGBA32,_=>throw new Exception("Unknown source texture")};
                var original=new Texture2D((int)t["m_Width"],(int)t["m_Height"],format,(bool)t["m_MipMap"],false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Repeat};
                try
                {
                    original.LoadRawTextureData(Convert.FromBase64String((string)t["image data"]["base64"]));original.Apply(false,true);
                    var material=trail?definition.TutorialTrailMaterial:definition.Emitters[index++].TutorialMaterial;
                    Assert.True(material);var published=(Texture2D)material.mainTexture;
                    Assert.AreEqual(original.width,published.width);Assert.AreEqual(original.height,published.height);Assert.AreEqual(original.format,published.format);Assert.AreEqual(original.mipmapCount,published.mipmapCount);
                    var rt=RenderTexture.GetTemporary(original.width,original.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                    var read=new Texture2D(original.width,original.height,TextureFormat.RGBA32,false,true);var previous=RenderTexture.active;
                    try
                    {
                        Color32[] Pixels(Texture texture){Graphics.Blit(texture,rt);RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,read.width,read.height),0,0);read.Apply();return read.GetPixels32();}
                        Assert.That(Pixels(published),Is.EqualTo(Pixels(original)),"Published pixel data ES"+id);
                    }
                    finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(read);}
                }
                finally{Object.DestroyImmediate(original);}
            }
        }
        [Test]
        public void RepeatedEffectSpawnsReuseTexturesAndImmutableMaterials()
        {
            using var first=TutorialEffectAssets.Load(15,null,true,true);
            using var second=TutorialEffectAssets.Load(15,null,true,true);
            Assert.That(first.Definition,Is.SameAs(second.Definition));Assert.That(first.TrailMaterial,Is.SameAs(second.TrailMaterial));
            var root=new GameObject("Shared effect fixture");
            try
            {
                for(int i=0;i<8;i++)root.AddComponent<TutorialEffectPlayback>().Initialize(i%2==0?first:second,1,null);
                foreach(var renderer in root.GetComponentsInChildren<LineRenderer>())Assert.That(renderer.sharedMaterial,Is.SameAs(first.TrailMaterial));
            }
            finally
            {
                foreach(var playback in root.GetComponents<TutorialEffectPlayback>())typeof(TutorialEffectPlayback).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(playback,null);
                Object.DestroyImmediate(root);
            }
            Assert.True(first.TrailMaterial);Assert.True(first.TrailMaterial.mainTexture);
        }
    }
}
