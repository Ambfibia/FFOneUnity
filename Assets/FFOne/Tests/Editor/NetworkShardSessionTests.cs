using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FFOne.Network;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed partial class NetworkSessionTests
    {
        private const ulong ShardTime = 0x0102030405060708UL;
        private static ulong ShardKey => Protocol0104.DeriveKey(ShardTime, 43, 988);

        [Test]
        public async Task ShardLifecyclePreservesBootstrapHeartbeatsMovementAndSaveAcknowledgement()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234);
                    await AssertSelection(loginStream, 1);
                    // Exercise the login server's explicit shard-selection variant on the same 0104 protocol.
                    await WriteFrame(loginStream, 0x2100000b, new byte[1], LoginFixture.Key);
                    var selectShard = Protocol0104.DecodeFrame(await ReadFrame(loginStream), LoginFixture.Key, 2);
                    Assert.AreEqual(0x12000007U, selectShard.PacketType);
                    await SendTicket(loginStream, shardListener.Port);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var stream = shardSocket.GetStream();
                        await AcceptShardEntry(stream, false);
                        var entry = await entering;
                        Assert.IsFalse(client.IsWorldLoaded);
                        Assert.AreEqual(0, entry.MapNumber);
                        Assert.AreEqual(-100, entry.X);
                        Assert.AreEqual(-90, entry.Angle);

                        // Heartbeats are serviced during scene loading, with the derived outbound key.
                        byte[] heartbeat = { 9, 8, 7, 6 };
                        byte[] bootstrapGoto = GotoPayload(-123, 456, 789);
                        await WriteFrame(stream, 0x31000031, bootstrapGoto, Protocol0104.FrontendKey);
                        await WriteFrame(stream, 0x310000d0, heartbeat, Protocol0104.FrontendKey);
                        var reply = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 1);
                        Assert.AreEqual(0x13000075U, reply.PacketType);
                        CollectionAssert.AreEqual(heartbeat, reply.Payload);
                        Assert.IsFalse(client.TryDequeueEvent(out _), "Scene prelude belongs to the bootstrap snapshot.");

                        var loading = client.CompleteLoadingAsync();
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 2);
                        Assert.AreEqual(0x1300008dU, request.PacketType);
                        Assert.AreEqual(4, request.Payload.Length);
                        await WriteFrame(stream, 0x310000f9, new byte[4], Protocol0104.FrontendKey);
                        Assert.AreSame(entry, await loading);
                        Assert.IsTrue(client.IsWorldLoaded);
                        CollectionAssert.AreEqual(new uint[] { 0x31000134, 0x31000031 }, entry.BootstrapPackets.Select(p => p.PacketType).ToArray());
                        Assert.AreEqual(-123, entry.X);
                        Assert.AreEqual(456, entry.Y);
                        Assert.AreEqual(789, entry.Z);

                        // A later authoritative teleport is delivered once, with decoded coordinates.
                        await WriteFrame(stream, 0x31000031, GotoPayload(101, -202, 303), Protocol0104.FrontendKey);
                        await WriteFrame(stream, 0x310000d0, heartbeat, Protocol0104.FrontendKey);
                        reply = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 3);
                        Assert.AreEqual(0x13000075U, reply.PacketType);
                        Assert.IsTrue(client.TryDequeueEvent(out var correction));
                        Assert.AreEqual(101, correction.PlayerPosition.X);
                        Assert.AreEqual(-202, correction.PlayerPosition.Y);
                        Assert.AreEqual(303, correction.PlayerPosition.Z);
                        Assert.IsFalse(client.TryDequeueEvent(out _));

                        await client.SendMovementAsync(101, -202, 303, -90, 1.5f, 3.25f, -2f, 600, 8, false);
                        request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 4);
                        Assert.AreEqual(0x13000003U, request.PacketType);
                        CollectionAssert.AreEqual(Protocol0104.MovementPayload(101, -202, 303, -90, 1.5f, 3.25f, -2f, 600, 8, false), request.Payload);
                        await client.SendJumpAsync(101, -202, 303, -90, 150, 325, -200, 700, 8, 7.25f, 0x456789ab);
                        request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 5);
                        Assert.AreEqual(0x13000005U, request.PacketType);
                        Assert.AreEqual(0x000002d5456789abL, Protocol0104.Read64(request.Payload, 0));
                        await client.SendMovementAsync(101, -202, 303, 0, 0, 0, 0, 0, 0, true);
                        request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 6);
                        Assert.AreEqual(0x13000004U, request.PacketType); Assert.AreEqual(20, request.Payload.Length);

                        var leaving = client.LeaveWorldAsync();
                        request = Protocol0104.DecodeFrame(await ReadFrame(stream), ShardKey, 7);
                        Assert.AreEqual(0x13000002U, request.PacketType);
                        Assert.AreEqual(42, Protocol0104.Read32(request.Payload, 0));
                        Assert.IsFalse(leaving.IsCompleted, "Must wait for the server's persisted-exit acknowledgement.");
                        await SendExitSuccess(stream); await leaving;
                        Assert.IsFalse(client.IsInWorld); Assert.IsFalse(client.IsWorldLoaded);
                        Assert.IsTrue(client.IsLoggedIn, "Shard exit retains the authenticated login connection.");
                        Assert.IsFalse(client.TryDequeueEvent(out _), "An intentional shard exit is not a network failure.");
                    }
                }
            }
        }

        [Test]
        public async Task TutorialCompletionFlushesOldShardBeforeSaveAndUsesNewAuthoritativeSnapshot()
        {
            using (var loginListener = new LoginFixture())
            using (var shardListener = new LoginFixture())
            using (var client = new FusionClient())
            {
                var loggingIn = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await ReadFrame(loginStream); await SendLogin(loginStream, 1, false); await loggingIn;
                    var entering = client.EnterWorldAsync(1234);
                    await AssertSelection(loginStream, 1); await SendTicket(loginStream, shardListener.Port);
                    using (var oldShard = await shardListener.Accept())
                    {
                        var oldStream = oldShard.GetStream(); await AcceptShardEntry(oldStream, false);
                        var oldEntry = await entering;
                        var loaded = client.CompleteLoadingAsync();
                        var loading = Protocol0104.DecodeFrame(await ReadFrame(oldStream), ShardKey, 1);
                        Assert.AreEqual(0x1300008dU, loading.PacketType);
                        await WriteFrame(oldStream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loaded;
                        // Queue a teleport without draining it. The following heartbeat proves the pump
                        // processed it before retirement, so checking only the callback source is insufficient.
                        await WriteFrame(oldStream, 0x31000031, GotoPayload(900, 901, 902), Protocol0104.FrontendKey);
                        await WriteFrame(oldStream, 0x310000d0, new byte[4], Protocol0104.FrontendKey);
                        Assert.AreEqual(0x13000075U, Protocol0104.DecodeFrame(await ReadFrame(oldStream), ShardKey, 2).PacketType);
                        // Login events belong to the retained connection and must survive shard replacement.
                        await WriteFrame(loginStream, 0x310000c8, new byte[1032], LoginFixture.Key);
                        await WriteFrame(loginStream, 0x21000016, new byte[4], LoginFixture.Key);
                        Assert.AreEqual(0x1200000cU, Protocol0104.DecodeFrame(await ReadFrame(loginStream), LoginFixture.Key, 2).PacketType);
                        var finishing = client.CompleteTutorialAsync(1234);
                        var exit = Protocol0104.DecodeFrame(await ReadFrame(oldStream), ShardKey, 3);
                        Assert.AreEqual(0x13000002U, exit.PacketType);
                        Assert.IsFalse(finishing.IsCompleted);
                        await SendExitSuccess(oldStream);
                        var save = Protocol0104.DecodeFrame(await ReadFrame(loginStream), LoginFixture.Key, 3);
                        Assert.AreEqual(0x1200000aU, save.PacketType);
                        Assert.AreEqual(12, save.Payload.Length);
                        Assert.AreEqual(1234, Protocol0104.Read64(save.Payload, 0));
                        Assert.AreEqual(1, save.Payload[8]);
                        await AssertSelection(loginStream, 4); await SendTicket(loginStream, shardListener.Port);
                        using (var newShard = await shardListener.Accept())
                        {
                            await AcceptShardEntry(newShard.GetStream(), true, 12345);
                            var newEntry = await finishing;
                            Assert.AreNotSame(oldEntry, newEntry);
                            Assert.AreEqual(12345, newEntry.X);
                            Assert.AreEqual(12345, client.Characters[0].X);
                            Assert.IsTrue(client.Characters[0].Style.TutorialComplete);
                            Assert.IsTrue(client.IsInWorld);
                            Assert.IsFalse(client.IsWorldLoaded, "The replacement scene must finish loading before simulation.");
                            Assert.IsTrue(client.TryDequeueEvent(out var retained));
                            Assert.IsFalse(retained.IsShard); Assert.AreEqual(0x310000c8U, retained.Frame.PacketType);
                            Assert.IsFalse(client.TryDequeueEvent(out _), "A retired shard's queued GOTO must never teleport the new player.");
                        }
                    }
                }
            }
        }

        private static async Task AssertSelection(NetworkStream stream, ushort sequence)
        {
            var select = Protocol0104.DecodeFrame(await ReadFrame(stream), LoginFixture.Key, sequence);
            Assert.AreEqual(0x12000005U, select.PacketType); Assert.AreEqual(1234, Protocol0104.Read64(select.Payload, 0));
        }

        private static async Task SendTicket(NetworkStream stream, int port)
        {
            byte[] ticket = new byte[28];
            Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(ticket, 0);
            Protocol0104.Write32(ticket, 16, port); Protocol0104.Write64(ticket, 20, 5678);
            await WriteFrame(stream, 0x2100000f, ticket, LoginFixture.Key);
        }

        private static async Task AcceptShardEntry(NetworkStream stream, bool tutorialComplete, int x = -100)
        {
            var enter = Protocol0104.DecodeFrame(await ReadFrame(stream), Protocol0104.DefaultKey, 0);
            Assert.AreEqual(0x13000001U, enter.PacketType); Assert.AreEqual(80, enter.Payload.Length);
            Assert.AreEqual("tester", Protocol0104.ReadUtf16(enter.Payload, 0, 33));
            Assert.AreEqual(5678, Protocol0104.Read64(enter.Payload, 72));
            await WriteFrame(stream, 0x31000134, new byte[76], Protocol0104.FrontendKey);
            byte[] payload = new byte[2700];
            Protocol0104.Write32(payload, 0, 42); Protocol0104.Write64(payload, 8, 1234);
            payload[84] = 1; payload[85] = tutorialComplete ? (byte)1 : (byte)0;
            Protocol0104.Write32(payload, 112, 987);
            Protocol0104.Write32(payload, 124, x); Protocol0104.Write32(payload, 128, 200); Protocol0104.Write32(payload, 132, 300);
            Protocol0104.Write32(payload, 136, -90);
            Protocol0104.Write64(payload, 2692, unchecked((long)ShardTime));
            await WriteFrame(stream, 0x31000002, payload, Protocol0104.FrontendKey);
        }

        private static Task SendExitSuccess(NetworkStream stream)
        {
            byte[] payload = new byte[8]; Protocol0104.Write32(payload, 0, 42);
            return WriteFrame(stream, 0x31000005, payload, Protocol0104.FrontendKey);
        }

        private static byte[] GotoPayload(int x, int y, int z)
        {
            byte[] payload = new byte[12];
            Protocol0104.Write32(payload, 0, x); Protocol0104.Write32(payload, 4, y); Protocol0104.Write32(payload, 8, z);
            return payload;
        }
    }
}
