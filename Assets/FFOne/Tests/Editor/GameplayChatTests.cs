using System;
using System.Text;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.Content;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace FFOne.Tests
{
    public sealed partial class NetworkSessionTests
    {
        [Test]
        public async Task FreeChatUsesLoadedShardAndDisplaysOnlyAuthoritativeBroadcast()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendFreeChatAsync("Hello"));
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234);
                    await AssertSelection(loginStream, 1); await SendTicket(loginStream, shardListener.Port);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var stream = shardSocket.GetStream(); await AcceptShardEntry(stream, true); await entering;
                        Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SendFreeChatAsync("Loading"));
                        var loading = client.CompleteLoadingAsync();
                        Assert.That(Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 1).PacketType, Is.EqualTo(0x1300008dU));
                        await WriteFrame(stream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loading;
                        await client.SendFreeChatAsync("Привет, мир!");
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 2);
                        Assert.That(request.PacketType, Is.EqualTo(ProtocolChat.Request));
                        Assert.That(request.Payload.Length, Is.EqualTo(260));
                        Assert.That(Protocol0104.ReadUtf16(request.Payload, 0, 128), Is.EqualTo("Привет, мир!"));
                        Assert.That(Protocol0104.Read32(request.Payload, 256), Is.Zero);
                        Assert.That(client.TryDequeueEvent(out _), Is.False, "Sending does not fabricate a successful local echo.");
                        var response = new byte[264]; Protocol0104.Write32(response, 0, 42);
                        Protocol0104.WriteUtf16(response, 4, 128, "Привет, мир!");
                        await WriteFrame(stream, ProtocolChat.Success, response, Protocol0104.FrontendKey);
                        // A subsequent heartbeat is a deterministic barrier proving the receive pump has queued chat.
                        await WriteFrame(stream, 0x310000d0, new byte[4], Protocol0104.FrontendKey);
                        Assert.That(Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 3).PacketType, Is.EqualTo(0x13000075U));
                        Assert.That(client.TryDequeueEvent(out var received), Is.True);
                        Assert.That(ProtocolChat.TryDecode(received.Frame, out var message), Is.True);
                        Assert.That(message.PlayerId, Is.EqualTo(42)); Assert.That(message.Text, Is.EqualTo("Привет, мир!"));
                        Assert.That(message.Kind, Is.EqualTo(ChatMessageKind.Message));
                    }
                }
            }
        }
    }

    public sealed class GameplayChatTests
    {
        [Test]
        public void ChatUsesSourceTypographyWithoutNativeScrollbars()
        {
            var chat = FFView.Instantiate("ui/views/Retrobution/Chat", "chat-root");
            GameplayChatPresentation.Configure(chat);
            var history = chat.Q<ScrollView>("chat-history");
            Assert.That(history.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
            Assert.That(history.verticalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
            var jeffe = ContentAssets.Load<FontAsset>("fonts/jeffe-sdf");
            var chalet = ContentAssets.Load<FontAsset>("fonts/chaletbook-regular-sdf");
            Assert.That(jeffe, Is.Not.Null); Assert.That(chalet, Is.Not.Null);
            foreach (string id in new[] { "chat-all-label", "chat-friend-label", "chat-group-label", "chat-active-tab-label" })
                Assert.That(chat.Q<Label>(id).style.unityFontDefinition.value.fontAsset, Is.EqualTo(jeffe), id);
            Assert.That(chat.Q<Label>("chat-inactive-hint").style.unityFontDefinition.value.fontAsset, Is.EqualTo(chalet));
        }

        [Test]
        public void FixedTextFieldRejectsOverflowNulEmptyAndInvalidSurrogatesWithoutTruncation()
        {
            Assert.That(ProtocolChat.FreeChatPayload(new string('я', 127)).Length, Is.EqualTo(260));
            Assert.Throws<ArgumentException>(() => ProtocolChat.FreeChatPayload(new string('я', 128)));
            Assert.Throws<ArgumentException>(() => ProtocolChat.FreeChatPayload("hidden\0suffix"));
            Assert.Throws<ArgumentException>(() => ProtocolChat.FreeChatPayload(" \t "));
            Assert.Throws<EncoderFallbackException>(() => ProtocolChat.FreeChatPayload("\ud800"));
            Assert.That(Protocol0104.ReadUtf16(ProtocolChat.FreeChatPayload("/help"), 0, 128), Is.EqualTo("/help"));
        }

        [Test]
        public void RepliesKeepRejectionsSeparateAndRejectMalformedTextAndSizes()
        {
            var payload = new byte[264]; Protocol0104.Write32(payload, 0, 17);
            Protocol0104.WriteUtf16(payload, 4, 128, "<b>text</b>");
            Assert.That(ProtocolChat.TryDecode(new ProtocolFrame(ProtocolChat.Failure, payload), out var failure), Is.True);
            Assert.That(failure.Kind, Is.EqualTo(ChatMessageKind.Rejected)); Assert.That(failure.ErrorCode, Is.EqualTo(17));
            Assert.That(failure.PlayerId, Is.Zero); Assert.That(failure.Text, Is.EqualTo("<b>text</b>"));
            Assert.That(ProtocolChat.TryDecode(new ProtocolFrame(0x31000031, new byte[12]), out _), Is.False);
            Assert.Throws<ProtocolException>(() => ProtocolChat.TryDecode(new ProtocolFrame(ProtocolChat.Success, new byte[263]), out _));
            for (int i = 0; i < 128; i++) Protocol0104.Write16(payload, 4 + i * 2, 'a');
            Assert.Throws<ProtocolException>(() => ProtocolChat.TryDecode(new ProtocolFrame(ProtocolChat.Success, payload), out _));
            Array.Clear(payload, 0, payload.Length); Protocol0104.Write16(payload, 4, 0xd800);
            Assert.Throws<ProtocolException>(() => ProtocolChat.TryDecode(new ProtocolFrame(ProtocolChat.Success, payload), out _));
        }
    }
}
