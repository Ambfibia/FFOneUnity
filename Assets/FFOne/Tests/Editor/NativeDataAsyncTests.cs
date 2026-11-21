using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Content;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class NativeDataAsyncTests
    {
        const string Source = "{\"uid\":18446744073709551615,\"signed\":-9223372036854775808,\"precise\":0.123456789123456789,\"  name\\r\\n\":\"  Декстер 😀\\n\\t  \",\"values\":[null,true,false,{},[],\"\"]}";

        [Test]
        public async Task AsyncTokenMatchesSynchronousNumbersUnicodeWhitespaceAndContainers()
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                document.Import(Source);
                var expected = document.ToToken();
                var actual = await document.ToTokenAsync();
                Assert.That(JToken.DeepEquals(actual, expected), Is.True);
                Assert.That((string)actual["  name\r\n"], Is.EqualTo("  Декстер 😀\n\t  "));
                Assert.That(actual["uid"].ToString(), Is.EqualTo("18446744073709551615"));
                Assert.That(actual["values"][0].Type, Is.EqualTo(JTokenType.Null));
                Assert.That(document.ToJson(), Is.EqualTo(Source), "Parsing must not rewrite the exact editable source tree.");
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public async Task ReplacementAndDestructionAfterDispatchDoNotChangeTheCapturedManagedTree()
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                document.Identity = "captured document"; document.Import(Source);
                var work = document.ToTokenAsync();
                document.Identity = "replacement document"; document.Import("{\"replacement\":true}");
                UnityEngine.Object.DestroyImmediate(document); document = null;
                var actual = await work;
                Assert.That(JToken.DeepEquals(actual, JToken.Parse(Source)), Is.True);
                Assert.That(actual["replacement"], Is.Null);
            }
            finally { if (document) UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public async Task InvalidCapturedSpanReportsTheOriginalIdentityAfterDocumentReplacement()
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            try
            {
                document.Identity = "bad captured span";
                document.Nodes = new[] { new NativeDataDocument.Node { Kind = NativeDataDocument.ValueKind.Object, End = 9 } };
                var work = document.ToTokenAsync();
                document.Identity = "valid replacement"; document.Import("[42]");
                try { await work; Assert.Fail("The captured malformed tree must fail asynchronously."); }
                catch (InvalidDataException error)
                {
                    Assert.That(error.Message, Does.Contain("bad captured span"));
                    Assert.That(error.Message, Does.Not.Contain("valid replacement"));
                }
                Assert.That(document.ToJson(), Is.EqualTo("[42]"));
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public async Task PreCanceledParsingHasACanceledOutcomeWithoutChangingOrPoisoningTheDocument()
        {
            var document = ScriptableObject.CreateInstance<NativeDataDocument>();
            using var cancellation = new CancellationTokenSource();
            try
            {
                document.Import(Source); var nodes = document.Nodes;
                cancellation.Cancel();
                var canceled = document.ToTokenAsync(cancellation.Token);
                try { await canceled; Assert.Fail("A pre-canceled parse must not publish a result."); }
                catch (OperationCanceledException error) { Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token)); }
                Assert.That(canceled.IsCanceled, Is.True);
                Assert.That(document.Nodes, Is.SameAs(nodes));
                Assert.That(document.ToJson(), Is.EqualTo(Source));
                Assert.That(JToken.DeepEquals(await document.ToTokenAsync(), JToken.Parse(Source)), Is.True, "A canceled caller must not poison a subsequent parse.");
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }
    }
}
