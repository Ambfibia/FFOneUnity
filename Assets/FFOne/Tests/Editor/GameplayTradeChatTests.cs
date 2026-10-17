using System;
using System.Text;
using System.Threading.Tasks;
using FFOne.Network;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class GameplayTradeChatTests
    {
        [Test]
        public void RequestKeepsTradeParticipantsFreeChatFlagAndOriginalInputLimit()
        {
            var bytes = ProtocolTradeChat.Payload(51, 51, 52, new string('я', 64));
            Assert.That(bytes.Length, Is.EqualTo(276));
            Assert.That(Protocol0104.Read32(bytes, 0), Is.EqualTo(51));
            Assert.That(Protocol0104.Read32(bytes, 4), Is.EqualTo(51));
            Assert.That(Protocol0104.Read32(bytes, 8), Is.EqualTo(52));
            Assert.That(Protocol0104.ReadUtf16(bytes, 12, 128), Is.EqualTo(new string('я', 64)));
            Assert.That(Protocol0104.Read32(bytes, 268), Is.Zero); Assert.That(bytes[272], Is.EqualTo(1));
            Assert.That(bytes[273] | bytes[274] | bytes[275], Is.Zero);
            Assert.Throws<ArgumentException>(() => ProtocolTradeChat.Payload(51, 51, 52, new string('a', 65)));
            Assert.Throws<ArgumentException>(() => ProtocolTradeChat.Payload(53, 51, 52, "hello"));
            Assert.Throws<ArgumentException>(() => ProtocolTradeChat.Payload(51, 51, 52, "bad\0suffix"));
            Assert.Throws<ArgumentException>(() => ProtocolTradeChat.Payload(51, 51, 52, " "));
            Assert.Throws<EncoderFallbackException>(() => ProtocolTradeChat.Payload(51, 51, 52, "\ud800"));
        }

        [Test]
        public void RepliesUseActualFailureIdAndFullWireTextWithStrictValidation()
        {
            var bytes = new byte[272];
            Protocol0104.Write32(bytes, 0, 52); Protocol0104.Write32(bytes, 4, 51); Protocol0104.Write32(bytes, 8, 52);
            Protocol0104.WriteUtf16(bytes, 12, 128, new string('z', 127));
            Assert.That(ProtocolTradeChat.TryDecode(new ProtocolFrame(0x31000052, bytes), out var message), Is.True);
            Assert.That(message.SenderId, Is.EqualTo(52)); Assert.That(message.Text.Length, Is.EqualTo(127));
            Assert.That(message.Rejected, Is.False);
            var rejected = new byte[276]; Array.Copy(bytes, rejected, bytes.Length); Protocol0104.Write32(rejected, 272, 7);
            Assert.That(ProtocolTradeChat.TryDecode(new ProtocolFrame(0x310000f6, rejected), out message), Is.True);
            Assert.That(message.Rejected, Is.True); Assert.That(message.ErrorCode, Is.EqualTo(7));
            Assert.That(ProtocolTradeChat.TryDecode(new ProtocolFrame(0x31000053, bytes), out _), Is.False, "Nano create is not trade chat failure.");
            Assert.Throws<ProtocolException>(() => ProtocolTradeChat.TryDecode(new ProtocolFrame(ProtocolTradeChat.Success, rejected), out _));
            Protocol0104.Write32(bytes, 0, 99);
            Assert.Throws<ProtocolException>(() => ProtocolTradeChat.TryDecode(new ProtocolFrame(ProtocolTradeChat.Success, bytes), out _));
            Protocol0104.Write32(bytes, 0, 52); Protocol0104.Write16(bytes, 12 + 127 * 2, 'z');
            Assert.Throws<ProtocolException>(() => ProtocolTradeChat.TryDecode(new ProtocolFrame(ProtocolTradeChat.Success, bytes), out _));
            Array.Clear(bytes, 12, 256); Protocol0104.Write16(bytes, 12, 0xd800);
            Assert.Throws<ProtocolException>(() => ProtocolTradeChat.TryDecode(new ProtocolFrame(ProtocolTradeChat.Success, bytes), out _));
        }
    }

    public sealed partial class NetworkSessionTests
    {
        [Test]
        public async Task TradeChatSendsToLoadedShardAndWaitsForParticipantEcho()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                Assert.Throws<InvalidOperationException>(() => client.SendTradeChatAsync(51, 52, "Hello"));
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream(); await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234); await AssertSelection(loginStream, 1); await SendTicket(loginStream, shardListener.Port);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var stream = shardSocket.GetStream(); await AcceptShardEntry(stream, true); await entering;
                        var loading = client.CompleteLoadingAsync(); await ReadFrame(stream);
                        await WriteFrame(stream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loading;
                        int local = client.World.PlayerId;
                        await client.SendTradeChatAsync(local, local + 1, "<b>Привет!</b>");
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 2);
                        Assert.That(request.PacketType, Is.EqualTo(ProtocolTradeChat.Request));
                        CollectionAssert.AreEqual(ProtocolTradeChat.Payload(local, local, local + 1, "<b>Привет!</b>"), request.Payload);
                        Assert.That(client.TryDequeueEvent(out _), Is.False);
                        var reply = new byte[272]; Array.Copy(request.Payload, reply, reply.Length);
                        await WriteFrame(stream, ProtocolTradeChat.Success, reply, Protocol0104.FrontendKey);
                        await WriteFrame(stream, 0x310000d0, new byte[4], Protocol0104.FrontendKey);
                        await ReadFrame(stream);
                        Assert.That(client.TryDequeueEvent(out var received), Is.True);
                        Assert.That(ProtocolTradeChat.TryDecode(received.Frame, out var message), Is.True);
                        Assert.That(message.Text, Is.EqualTo("<b>Привет!</b>")); Assert.That(message.Rejected, Is.False);
                    }
                }
            }
        }
    }
}
