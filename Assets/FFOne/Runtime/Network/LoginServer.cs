using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FFOne.Network
{
    // FFOneClient ui/login/browser.rs is the authority for server identity and probing.
    public sealed class LoginServer
    {
        [JsonProperty("address")] public string Address;
        [JsonProperty("api")] public string Api;
        [JsonIgnore] public string Key => Api ?? Address;
        [JsonIgnore] public string Name => Address == "localhost:23000" ? "localhost" : Address == "api.slavicfall.ru" ? "SlavicFall" : Address;
        [JsonIgnore] public Uri Endpoint => new Uri("tcp://" + Address);

        public static LoginServer Parse(string input)
        {
            input = input?.Trim() ?? "";
            if (input.Length == 0 || input.Length > 240) throw new ArgumentException("Enter a server address.");
            if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || (!input.Contains(":") && input != "localhost"))
            {
                var origin = input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? input : "https://" + input;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
                    uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    throw new ArgumentException("Use an HTTPS API host or host:port.");
                return new LoginServer { Address = uri.Host, Api = uri.GetLeftPart(UriPartial.Authority) };
            }
            var address = input == "localhost" ? "localhost:23000" : input;
            if (!Uri.TryCreate("tcp://" + address, UriKind.Absolute, out var endpoint) || endpoint.Port < 1 ||
                endpoint.Port > 65535 || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" ||
                endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || address.Contains("/"))
                throw new ArgumentException("Use host:port for direct login.");
            return new LoginServer { Address = address };
        }

        public async Task<ServerHealth> ProbeAsync(CancellationToken cancellation)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(Api == null ? 2000 : 8000);
                if (Api == null)
                {
                    using var socket = new TcpClient();
                    using var registration = timeout.Token.Register(socket.Dispose);
                    await socket.ConnectAsync(Endpoint.Host, Endpoint.Port).ConfigureAwait(false);
                    return new ServerHealth(true);
                }
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 65536 };
                using var info = await http.GetAsync(Api, timeout.Token).ConfigureAwait(false);
                if (!info.IsSuccessStatusCode) return new ServerHealth(false);
                using var response = await http.GetAsync(Api + "/status", timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new ServerHealth(false);
                var status = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return new ServerHealth(true, (long?)status["player_count"]);
            }
            catch (Exception) { cancellation.ThrowIfCancellationRequested(); return new ServerHealth(false); }
        }
    }

    public sealed class ServerHealth
    {
        public readonly bool? Online;
        public readonly long? PlayerCount;
        public ServerHealth(bool? online = null, long? playerCount = null) { Online = online; PlayerCount = playerCount; }
    }
}
