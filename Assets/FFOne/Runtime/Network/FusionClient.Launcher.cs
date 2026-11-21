using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        /// <summary>Matches the existing FFOne launcher/ofapi refresh → session → cookie handshake.</summary>
        public async Task<IReadOnlyList<CharacterInfo>> LoginWithLauncherAsync(string apiOrigin, string username, string password, CancellationToken cancellationToken = default)
            => (await LoginWithLauncherCredentialAsync(apiOrigin, username, password, false, cancellationToken).ConfigureAwait(false)).Characters;

        public async Task<LauncherLogin> LoginWithLauncherCredentialAsync(string apiOrigin, string username, string secret, bool savedRefreshToken, CancellationToken cancellationToken = default)
        {
            Uri origin = ValidateApiOrigin(apiOrigin);
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var http = new HttpClient(handler) { Timeout = ReplyTimeout, MaxResponseContentBufferSize = 65536 })
            {
                var info = ReadJson<ApiInfo>(await ApiRequest(http, new HttpRequestMessage(HttpMethod.Get, origin), token.Token).ConfigureAwait(false));
                if (!Uri.TryCreate("tcp://" + info.login_address, UriKind.Absolute, out var endpoint) || endpoint.Port < 1 || endpoint.Port > 65535 || !string.IsNullOrEmpty(endpoint.UserInfo))
                    throw new ProtocolException("Launcher API did not provide a valid game login address.");
                string refresh = secret.Trim();
                if (!savedRefreshToken)
                {
                    var auth = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, origin.AbsolutePath.TrimEnd('/') + "/auth"));
                    auth.Content = new StringContent(WriteJson(new ApiCredentials { username = username, password = secret }), Encoding.UTF8, "application/json");
                    refresh = (await ApiRequest(http, auth, token.Token).ConfigureAwait(false)).Trim();
                }
                if (refresh.Length == 0 || refresh.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new ProtocolException("Launcher API did not provide a refresh token.");
                var sessionRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, origin.AbsolutePath.TrimEnd('/') + "/auth/session"));
                sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refresh);
                var session = ReadJson<ApiSession>(await ApiRequest(http, sessionRequest, token.Token).ConfigureAwait(false));
                if (string.IsNullOrWhiteSpace(session.session_token)) throw new ProtocolException("Launcher API did not provide a session token.");
                var cookieRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, origin.AbsolutePath.TrimEnd('/') + "/cookie"));
                cookieRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.session_token);
                var cookie = ReadJson<ApiCookie>(await ApiRequest(http, cookieRequest, token.Token).ConfigureAwait(false));
                var characters = await LoginCookieAsync(endpoint.Host, endpoint.Port, cookie.username, cookie.cookie, token.Token).ConfigureAwait(false);
                return new LauncherLogin(characters, cookie.username, refresh);
            }
        }

        public static Uri ValidateApiOrigin(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Use an HTTPS launcher API origin.");
            // Local isolated fixtures may use HTTP. Remote credentials always require HTTPS.
            bool loopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip));
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && loopback)) throw new ArgumentException("Remote launcher authentication requires HTTPS.");
            return uri;
        }

        private static async Task<string> ApiRequest(HttpClient http, HttpRequestMessage request, CancellationToken cancellation)
        {
            using (request)
            {
                try
                {
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation).ConfigureAwait(false);
                    // Do not echo response bodies or URLs: servers may include submitted credentials.
                    if (!response.IsSuccessStatusCode) throw new IOException($"Launcher authentication failed (HTTP {(int)response.StatusCode}).");
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new TimeoutException("Launcher authentication timed out.");
                }
                catch (HttpRequestException error) when (error.InnerException is WebException { Status: WebExceptionStatus.Timeout })
                {
                    // Unity's Mono HTTP handler wraps an expired request as a WebException.
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Launcher authentication timed out.", error);
                }
            }
        }
        private static T ReadJson<T>(string text)
        {
            try { using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
            catch (SerializationException) { throw new ProtocolException("Launcher API returned invalid JSON."); }
        }
        private static string WriteJson<T>(T value)
        { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); } }
        [DataContract] private sealed class ApiInfo { [DataMember] public string login_address; }
        [DataContract] private sealed class ApiCredentials { [DataMember] public string username; [DataMember] public string password; }
        [DataContract] private sealed class ApiSession { [DataMember] public string session_token; }
        [DataContract] private sealed class ApiCookie { [DataMember] public string username; [DataMember] public string cookie; }
    }

    public sealed class LauncherLogin
    {
        public IReadOnlyList<CharacterInfo> Characters { get; }
        public string Username { get; }
        public string RefreshToken { get; }
        internal LauncherLogin(IReadOnlyList<CharacterInfo> characters, string username, string refreshToken)
        { Characters = characters; Username = username; RefreshToken = refreshToken; }
    }
}
