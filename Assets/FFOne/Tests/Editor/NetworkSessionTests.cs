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
    public sealed partial class NetworkSessionTests
    {
        [Test]
        public async Task FragmentedLoginAndCoalescedRosterServiceHeartbeatWithContinuousSequence()
        {
            using (var fixture = new LoginFixture())
            using (var client = new FusionClient())
            {
                var server = Task.Run(async () =>
                {
                    using (var socket = await fixture.Accept())
                    {
                        var stream = socket.GetStream();
                        var request = Protocol0104.DecodeFrame(await ReadFrame(stream), Protocol0104.DefaultKey, 0);
                        Assert.AreEqual(0x12000001U, request.PacketType);
                        await SendLogin(stream, 1, true);
                        byte[] heartbeat = { 0x12, 0x34, 0x56, 0x78 };
                        await WriteFrame(stream, 0x21000016, heartbeat, LoginFixture.Key);
                        var reply = Protocol0104.DecodeFrame(await ReadFrame(stream), LoginFixture.Key, 1);
                        Assert.AreEqual(0x1200000cU, reply.PacketType);
                        CollectionAssert.AreEqual(heartbeat, reply.Payload);
                        var delete = Protocol0104.DecodeFrame(await ReadFrame(stream), LoginFixture.Key, 2);
                        Assert.AreEqual(0x12000006U, delete.PacketType);
                        Assert.AreEqual(1234L, Protocol0104.Read64(delete.Payload, 0));
                        await WriteFrame(stream, 0x2100000d, new byte[] { 1 }, LoginFixture.Key);
                    }
                });
                var roster = await client.LoginAsync("127.0.0.1", fixture.Port, "tester", "password");
                Assert.AreEqual(1, roster.Count); Assert.AreEqual(1234L, roster[0].Uid);
                // The server waits for the heartbeat before reading DELETE, so packet order is proven.
                await Task.Delay(30);
                await client.DeleteCharacterAsync(1234);
                Assert.AreEqual(0, client.Characters.Count);
                await server;
            }
        }

        [Test]
        public async Task CanceledPartialFrameClosesConnectionPromptly()
        {
            using (var fixture = new LoginFixture())
            using (var client = new FusionClient())
            using (var cancel = new CancellationTokenSource())
            {
                var started = client.LoginAsync("127.0.0.1", fixture.Port, "tester", "password", cancel.Token);
                using (var server = await fixture.Accept())
                {
                    var stream = server.GetStream(); await ReadFrame(stream);
                    await stream.WriteAsync(new byte[] { 88, 0, 0, 0, 1 }, 0, 5);
                    cancel.Cancel();
                    try { await started; Assert.Fail("Canceled login unexpectedly succeeded."); }
                    catch (OperationCanceledException) { }
                    Assert.IsFalse(client.IsLoggedIn);
                    await AssertPeerClosed(stream);
                }
            }
        }

        [Test]
        public async Task ReplyTimeoutDisconnectsInsteadOfReusingLateTransactionReplies()
        {
            using (var fixture = new LoginFixture())
            using (var client = new FusionClient())
            {
                var started = client.LoginAsync("127.0.0.1", fixture.Port, "tester", "password");
                using (var server = await fixture.Accept())
                {
                    var stream = server.GetStream(); await ReadFrame(stream); await SendLogin(stream, 1, false); await started;
                    // The Unity editor pumps test awaits once per frame. Only the unanswered
                    // transaction under test gets the short deadline, not the fixture login.
                    client.ReplyTimeout = TimeSpan.FromMilliseconds(150);
                    var deleting = client.DeleteCharacterAsync(1234); await ReadFrame(stream);
                    try { await deleting; Assert.Fail("Missing reply unexpectedly succeeded."); }
                    catch (TimeoutException) { }
                    Assert.IsFalse(client.IsLoggedIn);
                    Assert.AreEqual(1, client.Characters.Count, "No authoritative deletion acknowledgement arrived.");
                }
            }
        }

        [Test]
        public async Task WrongDeleteSlotCannotMutateRoster()
        {
            using (var fixture = new LoginFixture())
            using (var client = new FusionClient())
            {
                var started = client.LoginAsync("127.0.0.1", fixture.Port, "tester", "password");
                using (var server = await fixture.Accept())
                {
                    var stream = server.GetStream(); await ReadFrame(stream); await SendLogin(stream, 1, false); await started;
                    var deleting = client.DeleteCharacterAsync(1234); await ReadFrame(stream);
                    await WriteFrame(stream, 0x2100000d, new byte[] { 2 }, LoginFixture.Key);
                    try { await deleting; Assert.Fail("Mismatched slot unexpectedly accepted."); }
                    catch (ProtocolException) { }
                    Assert.AreEqual(1, client.Characters.Count); Assert.IsFalse(client.IsLoggedIn);
                }
            }
        }

        [Test]
        public async Task BadNameEchoCannotAdvanceIntoPersistentSave()
        {
            using (var fixture = new LoginFixture())
            using (var client = new FusionClient())
            {
                var started = client.LoginAsync("127.0.0.1", fixture.Port, "tester", "password");
                using (var server = await fixture.Accept())
                {
                    var stream = server.GetStream(); await ReadFrame(stream); await SendLogin(stream, 0, false); await started;
                    var creating = client.CreateCharacterAsync(new CharacterCreation { FirstName = "Alice", LastName = "Test", UpperBodyId = 1, LowerBodyId = 1, FootId = 1 });
                    await ReadFrame(stream);
                    byte[] wrong = new byte[52]; Protocol0104.WriteUtf16(wrong, 0, 9, "Other");
                    await WriteFrame(stream, 0x21000005, wrong, LoginFixture.EmptyKey);
                    try { await creating; Assert.Fail("Mismatched name unexpectedly accepted."); }
                    catch (ProtocolException) { }
                    Assert.AreEqual(0, client.Characters.Count); Assert.IsFalse(client.IsLoggedIn);
                    await AssertPeerClosed(stream);
                }
            }
        }

        private sealed class LoginFixture : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            internal const ulong Time = 0x1122334455667788UL;
            internal static ulong Key => Protocol0104.DeriveKey(Time, 2, 2);
            internal static ulong EmptyKey => Protocol0104.DeriveKey(Time, 1, 2);
            internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
            internal LoginFixture() { listener.Start(); }
            internal Task<TcpClient> Accept() => listener.AcceptTcpClientAsync();
            public void Dispose() => listener.Stop();
        }

        private static async Task SendLogin(NetworkStream stream, int count, bool fragmented)
        {
            byte[] login = new byte[84]; login[0] = (byte)count; login[1] = 1;
            Protocol0104.Write64(login, 4, unchecked((long)LoginFixture.Time)); Protocol0104.WriteUtf16(login, 12, 33, "tester");
            byte[] success = Protocol0104.EncodeFrame(0x21000001, login, Protocol0104.DefaultKey);
            byte[] roster = Array.Empty<byte>();
            if (count > 0)
            {
                byte[] character = new byte[204]; character[0] = 1; character[80] = 1;
                Protocol0104.Write64(character, 4, 1234); Protocol0104.WriteUtf16(character, 14, 9, "Alice");
                roster = Protocol0104.EncodeFrame(0x21000003, character, LoginFixture.Key);
            }
            byte[] combined = new byte[success.Length + roster.Length];
            Buffer.BlockCopy(success, 0, combined, 0, success.Length); Buffer.BlockCopy(roster, 0, combined, success.Length, roster.Length);
            if (fragmented)
            {
                await stream.WriteAsync(combined, 0, 2); await Task.Delay(5);
                await stream.WriteAsync(combined, 2, 5); await Task.Delay(5);
                await stream.WriteAsync(combined, 7, combined.Length - 7);
            }
            else await stream.WriteAsync(combined, 0, combined.Length);
        }

        private static async Task WriteFrame(NetworkStream stream, uint type, byte[] payload, ulong key)
        { byte[] frame = Protocol0104.EncodeFrame(type, payload, key); await stream.WriteAsync(frame, 0, frame.Length); }
        private static async Task AssertPeerClosed(NetworkStream stream)
        {
            try { byte[] eof = new byte[1]; Assert.AreEqual(0, await stream.ReadAsync(eof, 0, 1)); }
            catch (IOException error) when (error.InnerException is SocketException socket &&
                (socket.SocketErrorCode == SocketError.ConnectionReset || socket.SocketErrorCode == SocketError.ConnectionAborted))
            { /* Windows may reset a socket that is closed while a partial frame remains unread. */ }
        }
        private static async Task<byte[]> ReadFrame(NetworkStream stream)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            using (timeout.Token.Register(() => stream.Close()))
            {
                byte[] header = new byte[4]; await Exact(stream, header, 0, timeout.Token);
                int length = Protocol0104.Read32(header, 0); Protocol0104.ValidateLength(unchecked((uint)length));
                byte[] frame = new byte[length + 4]; Buffer.BlockCopy(header, 0, frame, 0, 4);
                await Exact(stream, frame, 4, timeout.Token); return frame;
            }
        }
        private static async Task Exact(NetworkStream stream, byte[] buffer, int offset, CancellationToken cancellation)
        {
            while (offset < buffer.Length)
            { int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellation); if (count == 0) throw new EndOfStreamException(); offset += count; }
        }
    }
}
