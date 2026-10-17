using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFOne.Network;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class LauncherSavedLoginTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PasswordAndSavedRefreshBothCompleteTheRealCookieHandshake(bool saved)
        {
            var api = new TcpListener(IPAddress.Loopback, 0);
            var game = new TcpListener(IPAddress.Loopback, 0);
            api.Start(); game.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var stop = deadline.Token.Register(() => { api.Stop(); game.Stop(); });
            using var client = new FusionClient();
            try
            {
                int gamePort = ((IPEndPoint)game.LocalEndpoint).Port;
                var server = Task.Run(async () => {
                    await Reply(api, "/", "", "{\"login_address\":\"127.0.0.1:" + gamePort + "\"}");
                    if (!saved) await Reply(api, "/auth", "", "saved-refresh", true);
                    await Reply(api, "/auth/session", "saved-refresh", "{\"session_token\":\"session-only\"}");
                    await Reply(api, "/cookie", "session-only", "{\"username\":\"canonical\",\"cookie\":\"single-use\"}");
                    using var peer = await game.AcceptTcpClientAsync();
                    using var close = deadline.Token.Register(peer.Dispose);
                    var stream = peer.GetStream();
                    byte[] header = new byte[4]; await ReadExact(stream, header, deadline.Token);
                    int count = Protocol0104.Read32(header, 0);
                    Assert.That(count, Is.InRange(4, 4096));
                    byte[] payload = new byte[count]; await ReadExact(stream, payload, deadline.Token);
                    // Roster acceptance is required before credentials are exposed for remembering.
                    byte[] login = new byte[84]; login[1] = 1;
                    Protocol0104.Write64(login, 4, 123456789); Protocol0104.WriteUtf16(login, 12, 33, "canonical");
                    byte[] frame = Protocol0104.EncodeFrame(0x21000001, login, Protocol0104.DefaultKey);
                    await stream.WriteAsync(frame, 0, frame.Length, deadline.Token);
                    // Keep the socket open until the caller has observed the accepted result.
                    await Task.Delay(250, deadline.Token);
                });
                var accepted = await client.LoginWithLauncherCredentialAsync("http://127.0.0.1:" + ((IPEndPoint)api.LocalEndpoint).Port,
                    "typed-name", saved ? "saved-refresh" : "typed-password", saved, deadline.Token);
                Assert.That(accepted.Username, Is.EqualTo("canonical"));
                Assert.That(accepted.RefreshToken, Is.EqualTo("saved-refresh"));
                Assert.That(accepted.Characters, Is.Empty);
                await server;
            }
            finally { api.Stop(); game.Stop(); }
        }
        static async Task Reply(TcpListener listener, string path, string bearer, string body, bool password = false)
        {
            using var peer = await listener.AcceptTcpClientAsync();
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            string request = await reader.ReadLineAsync();
            Assert.That(request.Split(' ')[1], Is.EqualTo(path), "A saved token must skip the password endpoint.");
            string headers = "", line; int length = 0;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                headers += line.ToLowerInvariant() + "\n";
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15));
            }
            var requestBody = new char[length];
            for (int offset = 0; offset < length;) offset += await reader.ReadAsync(requestBody, offset, length - offset);
            if (bearer.Length > 0) Assert.That(headers, Does.Contain("authorization: bearer " + bearer));
            if (password) Assert.That(new string(requestBody), Does.Contain("typed-password"));
            else Assert.That(new string(requestBody), Does.Not.Contain("typed-password"));
            byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
            await stream.WriteAsync(response, 0, response.Length);
        }
        static async Task ReadExact(NetworkStream stream, byte[] bytes, CancellationToken token)
        {
            for (int offset = 0; offset < bytes.Length;)
            {
                int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token);
                if (count == 0) throw new EndOfStreamException(); offset += count;
            }
        }
    }
}
