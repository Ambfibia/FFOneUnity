using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class CharacterLifecycleTests
    {
        const long Uid = 2345;
        const int PlayerId = 42;
        const ulong LoginTime = 12345, ShardTime = 67890;
        static ulong LoginKey(int count) => Protocol0104.DeriveKey(LoginTime, count + 1, 2);
        static ulong ShardKey => Protocol0104.DeriveKey(ShardTime, PlayerId + 1, 1);

        [Test]
        public async Task ReservedNameSurvivesAppearanceRejectionAndRetryWithoutAnotherSave()
        {
            using (var listener = new Listener())
            using (var client = new FusionClient())
            {
                var login = client.LoginAsync("127.0.0.1", listener.Port, "tester", "password");
                using (var socket = await listener.Accept())
                {
                    var stream = socket.GetStream();
                    await Read(stream, Protocol0104.DefaultKey, 0, 0x12000001);
                    await Login(stream, 0); await login;
                    var draft = new CharacterCreation { FirstName = "Alice", LastName = "Test", Slot = 1 };
                    var reserving = client.ReserveCharacterNameAsync(draft);
                    var check = await Read(stream, LoginKey(0), 1, 0x12000002);
                    byte[] checkedName = new byte[52]; Buffer.BlockCopy(check.Payload, 12, checkedName, 0, 52);
                    await Write(stream, 0x21000005, checkedName, LoginKey(0));
                    await Read(stream, LoginKey(0), 2, 0x12000003);
                    byte[] saved = new byte[64]; Protocol0104.Write64(saved, 0, Uid); saved[8] = 1; saved[9] = 1;
                    Buffer.BlockCopy(checkedName, 0, saved, 10, 52);
                    await Write(stream, 0x21000007, saved, LoginKey(0));
                    var pending = await reserving;
                    Assert.AreEqual(Uid, pending.Uid); Assert.IsFalse(pending.Style.AppearanceComplete);
                    Assert.AreEqual(1, client.Characters.Count);
                    Assert.IsFalse(stream.DataAvailable, "Name screen must not submit appearance.");
                    draft.ExistingUid = pending.Uid;
                    draft.UpperBodyId = draft.LowerBodyId = draft.FootId = 1;
                    var rejected = client.CreateCharacterAsync(draft);
                    await Read(stream, LoginKey(0), 3, 0x12000004);
                    await Write(stream, 0x2100000a, new byte[4], LoginKey(0));
                    try { await rejected; Assert.Fail("Rejected appearance accepted."); } catch (ServerRejectedException) { }
                    Assert.AreEqual(1, client.Characters.Count); Assert.IsFalse(pending.Style.AppearanceComplete);
                    var retry = client.CreateCharacterAsync(draft);
                    await Read(stream, LoginKey(0), 4, 0x12000004);
                    byte[] created = new byte[100]; Protocol0104.Write64(created, 4, Uid);
                    Buffer.BlockCopy(checkedName, 0, created, 14, 52); created[80] = 1;
                    await Write(stream, 0x21000009, created, LoginKey(0));
                    Assert.IsTrue((await retry).Style.AppearanceComplete);
                    Assert.AreEqual(1, client.Characters.Count);
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task TutorialSaveWaitsForShardPersistenceAndReentersWithAuthoritativeRewards(bool acknowledgeExit)
        {
            using (var loginListener = new Listener())
            using (var shardListener = new Listener())
            using (var client = new FusionClient())
            {
                var logging = client.LoginAsync("127.0.0.1", loginListener.Port, "tester", "password");
                using (var loginSocket = await loginListener.Accept())
                {
                    var loginStream = loginSocket.GetStream();
                    await Read(loginStream, Protocol0104.DefaultKey, 0, 0x12000001);
                    await Login(loginStream, 1); await logging;
                    var entering = client.EnterWorldAsync(Uid);
                    await Select(loginStream, shardListener.Port, 1);
                    using (var shardSocket = await shardListener.Accept())
                    {
                        var shardStream = shardSocket.GetStream();
                        await Enter(shardStream, false); await entering;
                        var loading = client.CompleteLoadingAsync();
                        await Read(shardStream, ShardKey, 1, 0x1300008d);
                        await Write(shardStream, 0x310000f9, new byte[4], Protocol0104.FrontendKey); await loading;
                        var completion = client.CompleteTutorialAsync(Uid);
                        await Read(shardStream, ShardKey, 2, 0x13000002);
                        Assert.IsFalse(loginStream.DataAvailable, "SAVE_CHAR_TUTOR must wait for the old shard's save acknowledgement.");
                        if (!acknowledgeExit)
                        {
                            shardSocket.Close();
                            try { await completion; Assert.Fail("EOF cannot acknowledge persistence."); } catch (IOException) { }
                            Assert.IsFalse(loginStream.DataAvailable);
                            Assert.IsFalse(client.Characters[0].Style.TutorialComplete);
                            return;
                        }
                        byte[] exit = new byte[8]; Protocol0104.Write32(exit, 0, PlayerId);
                        await Write(shardStream, 0x31000005, exit, Protocol0104.FrontendKey);
                        var save = await Read(loginStream, LoginKey(1), 3, 0x1200000a);
                        Assert.AreEqual(12, save.Payload.Length); Assert.AreEqual(Uid, Protocol0104.Read64(save.Payload, 0));
                        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0 }, new ArraySegment<byte>(save.Payload, 8, 4));
                        // No SAVE_CHAR_TUTOR reply exists; the next request must be SELECT.
                        await Select(loginStream, shardListener.Port, 4);
                        using (var freshSocket = await shardListener.Accept())
                        {
                            await Enter(freshSocket.GetStream(), true);
                            var fresh = await completion;
                            Assert.AreEqual(Uid, fresh.CharacterUid);
                            Assert.IsFalse(client.IsWorldLoaded, "The new scene must load before its loading ACK.");
                            Assert.IsTrue(client.Characters[0].Style.TutorialComplete);
                            Assert.AreEqual(328, client.Characters[0].Equipment[0].ItemId);
                            while (client.TryDequeueEvent(out var evt)) Assert.AreNotEqual(NetworkEventKind.Disconnected, evt.Kind);
                        }
                    }
                }
            }
        }

        static async Task Login(NetworkStream stream, int count)
        {
            byte[] reply = new byte[84]; reply[0] = (byte)count; reply[1] = 1;
            Protocol0104.Write64(reply, 4, (long)LoginTime); Protocol0104.WriteUtf16(reply, 12, 33, "tester");
            await Write(stream, 0x21000001, reply, Protocol0104.DefaultKey);
            if (count == 0) return;
            byte[] character = new byte[204]; character[0] = 1; character[80] = 1;
            Protocol0104.Write64(character, 4, Uid); Protocol0104.WriteUtf16(character, 14, 9, "Alice");
            await Write(stream, 0x21000003, character, LoginKey(count));
        }
        static async Task Select(NetworkStream stream, int port, ushort sequence)
        {
            await Read(stream, LoginKey(1), sequence, 0x12000005);
            await Write(stream, 0x2100000b, new byte[1], LoginKey(1));
            await Read(stream, LoginKey(1), (ushort)(sequence + 1), 0x12000007);
            byte[] ticket = new byte[28]; var ip = System.Text.Encoding.ASCII.GetBytes("127.0.0.1");
            Buffer.BlockCopy(ip, 0, ticket, 0, ip.Length); Protocol0104.Write32(ticket, 16, port);
            Protocol0104.Write64(ticket, 20, 777); await Write(stream, 0x2100000f, ticket, LoginKey(1));
        }
        static async Task Enter(NetworkStream stream, bool completed)
        {
            await Read(stream, Protocol0104.DefaultKey, 0, 0x13000001);
            byte[] entry = new byte[2700]; Protocol0104.Write32(entry, 0, PlayerId); Protocol0104.Write64(entry, 8, Uid);
            entry[84] = 1; entry[85] = (byte)(completed ? 1 : 0);
            Protocol0104.Write16(entry, 142, completed ? 328 : 0);
            Protocol0104.Write64(entry, 2692, (long)ShardTime);
            await Write(stream, 0x31000002, entry, Protocol0104.FrontendKey);
        }
        static async Task Write(NetworkStream stream, uint type, byte[] payload, ulong key)
        { var bytes = Protocol0104.EncodeFrame(type, payload, key); await stream.WriteAsync(bytes, 0, bytes.Length); }
        static async Task<ProtocolFrame> Read(NetworkStream stream, ulong key, ushort sequence, uint expected)
        {
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (deadline.Token.Register(() => stream.Close()))
            {
                byte[] header = new byte[4]; await Exact(stream, header, 0, deadline.Token);
                int size = Protocol0104.Read32(header, 0); Protocol0104.ValidateLength((uint)size);
                byte[] bytes = new byte[size + 4]; Buffer.BlockCopy(header, 0, bytes, 0, 4);
                await Exact(stream, bytes, 4, deadline.Token);
                var frame = Protocol0104.DecodeFrame(bytes, key, sequence); Assert.AreEqual(expected, frame.PacketType); return frame;
            }
        }
        static async Task Exact(NetworkStream stream, byte[] bytes, int offset, CancellationToken token)
        {
            while (offset < bytes.Length)
            { int n = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token); if (n == 0) throw new EndOfStreamException(); offset += n; }
        }
        sealed class Listener : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
            public Listener() { listener.Start(); }
            public Task<TcpClient> Accept() => listener.AcceptTcpClientAsync();
            public void Dispose() => listener.Stop();
        }
    }
}
