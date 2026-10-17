using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Linq;
using FFOne.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeDataDocumentTests
    {
        [Test]
        public void SavedDocumentRetainsExactWhitespaceAfterUnitySerialization()
        {
            string folder = "Assets/__NativeDataRoundTrip_" + Guid.NewGuid().ToString("N");
            ContentPaths.CreateFolder("Assets", Path.GetFileName(folder));
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            string path = folder + "/document.asset";
            const string source = "{\"  key\\r\\n\":\"  Long descriptive text which must retain double  spaces when Unity wraps the YAML line at its preferred column.\\r\\n\\r\\n\\tРусский текст 😀  \",\"number\":18446744073709551615,\"array\":[\"\",null,true,-0,0.123456789123456789123456789]}";
            try
            {
                document.Import(source); string expected = document.ToJson();
                AssetDatabase.CreateAsset(document, ContentPaths.Resolve(path)); AssetDatabase.SaveAssetIfDirty(document);
                Resources.UnloadAsset(document); document = null;
                AssetDatabase.ImportAsset(ContentPaths.Resolve(path), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                document = AssetDatabase.LoadAssetAtPath<NativeDataDocument>(ContentPaths.Resolve(path));
                Assert.That(document.TextEncodingVersion, Is.EqualTo(1));
                Assert.That(document.ToJson(), Is.EqualTo(expected));
                int index = document.Children(0).First(); var node = document.Nodes[index];
                node.Value = "Edited  text\\n"; document.Nodes[index] = node;
                Assert.That(document.ToJson(), Does.Contain("Edited  text"));
            }
            finally { if (document && !EditorUtility.IsPersistent(document)) UnityEngine.Object.DestroyImmediate(document); AssetDatabase.DeleteAsset(ContentPaths.Resolve(folder)); }
        }

        [Test]
        public void StructuredRoundTripPreservesPreciseIdsNumbersUnicodeOrderAndNulls()
        {
            const string source = "{\"uid\":18446744073709551615,\"signed\":-9223372036854775808,\"precise\":0.123456789123456789123456789,\"exponent\":-1.2500e-29,\"negativeZero\":-0,\"names\":[\"Русский текст\",\"a\\nb\",null,true,false,{},[]]}";
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                document.Import(source);
                Assert.That(document.ToJson(), Is.EqualTo(source));
                Assert.That(document.Children(0).Select(i => document.Nodes[i].Key), Is.EqualTo(new[] { "uid", "signed", "precise", "exponent", "negativeZero", "names" }));
                int names = document.Children(0).Last();
                Assert.That(document.Children(names).Count(), Is.EqualTo(7));
                var node = document.Nodes[document.Children(names).First()]; node.Value = "Новый текст";
                document.Nodes[document.Children(names).First()] = node;
                Assert.That(document.ToJson(), Does.Contain("Новый текст"));
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }
        [TestCase("{\"x\":1,\"x\":2}")]
        [TestCase("[1,]")]
        [TestCase("{\"x\":01}")]
        [TestCase("true false")]
        public void InvalidOrAmbiguousInputDoesNotReplaceExistingData(string input)
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try { document.Import("[42]"); Assert.Throws<InvalidDataException>(() => document.Import(input)); Assert.That(document.ToJson(), Is.EqualTo("[42]")); }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }
        [Test]
        public void ContentLinksResolveRelativePathsWithoutEscapingTheRegistry()
        {
            var catalog = ScriptableObject.CreateInstance<NativeContentCatalog>();
            try
            {
                catalog.Entries = new[] { new NativeContentCatalog.Entry { Identity = "characters/a/texture.png", Resource = "characters/a/texture", Kind = NativeContentCatalog.AssetKind.Texture } };
                Assert.That(catalog.Resolve("characters/a/models/model.json", "../texture.png"), Is.SameAs(catalog.Entries[0]));
                Assert.That(catalog.Resolve("root.json", "../characters/a/texture.png"), Is.Null);
                Assert.That(catalog.Resolve("root.json", "characters/a/texture.png"), Is.SameAs(catalog.Entries[0]));
                Assert.That(typeof(NativeContentCatalog.Entry).GetFields().Any(f => typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
    }
}
