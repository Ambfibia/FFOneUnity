using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FFOne.Content;
using FFOne.Tutorial;
using FFOne.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class CombatBuffContentTests
    {
        static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }

        [TestCase(366)] [TestCase(368)] [TestCase(370)] [TestCase(376)] [TestCase(411)]
        [TestCase(412)] [TestCase(429)] [TestCase(431)] [TestCase(434)] [TestCase(435)]
        [TestCase(436)] [TestCase(441)] [TestCase(447)] [TestCase(714)] [TestCase(805)]
        public void EveryNativeBuffClosureResolvesItsProvenanceTexturesControllersAndRenderers(int id)
        {
            string identity = "map/shared/effects/es" + id + ".closure.json";
            var document = NativeData.Load(identity);
            Assert.That(document, Is.Not.Null, "Publish exact buff closure ES" + id);
            Assert.That(document.Identity, Is.EqualTo(identity));
            Assert.That((int)document.ToToken()["effectId"], Is.EqualTo(id));
            string asset = AssetDatabase.GetAssetPath(document);
            var receipt = JObject.Parse(File.ReadAllText("Assets/FFOne/Editor/ImportMetadata/NativeDataPublications/" + identity));
            Assert.That((string)receipt["Guid"], Is.EqualTo(AssetDatabase.AssetPathToGUID(asset)));
            Assert.That((string)receipt["SourceHash"], Is.EqualTo(Hash(File.ReadAllBytes("../FFOneClient/assets/game/" + identity))));
            Assert.That((string)receipt["InitialValueHash"], Is.EqualTo(Hash(Encoding.UTF8.GetBytes(document.ToJson()))));
            var parent = new GameObject("Buff closure contract ES" + id);
            try
            {
                var spawned = (GameObject)typeof(NativeCombatEffect).GetMethod("Spawn", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { parent.transform, id, 1f, false, null, -1f, null });
                Assert.That(spawned, Is.Not.Null);
                var playback = spawned.GetComponent<TutorialEffectPlayback>();
                Assert.That(playback, Is.Not.Null);
                // Edit-mode rendering still exercises initial emit, shader decoding,
                // authored material curves and mesh geometry without a frame timer.
                typeof(TutorialEffectPlayback).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(playback, null);
                var renderers = spawned.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0), "No native geometry for ES" + id);
                foreach (var renderer in renderers)
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null);
                        Assert.That(material.shader, Is.Not.Null);
                        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False, "ES" + id + " " + material.name);
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }
    }
}
