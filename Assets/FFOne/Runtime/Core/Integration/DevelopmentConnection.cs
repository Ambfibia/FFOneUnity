using System;
using System.IO;
using FFOne.Content;
using FFOne.Network;

namespace FFOne.Integration
{
    // Explicit Editor/development-player launch context. Regular launches retain their
    // existing account profiles and selected server.
    public static class DevelopmentConnection
    {
        public const string ServerVariable = "FFONE_DEV_SERVER";
        public const string ProfilesVariable = "FFONE_DEV_PROFILES";
        public static bool Enabled {
            get {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServerVariable)) &&
                    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProfilesVariable));
#else
                return false;
#endif
            }
        }
        public static string ProfilesPath(string ordinaryDirectory) => Path.Combine(Enabled ?
            Environment.GetEnvironmentVariable(ProfilesVariable) : ordinaryDirectory, "login-profiles.json");
        public static void SelectServer(LoginProfiles profiles)
        {
            if (!Enabled) return;
            var endpoint = LoginServer.Parse(Environment.GetEnvironmentVariable(ServerVariable));
            if (endpoint.Api != null || !endpoint.Endpoint.IsLoopback)
                throw new InvalidOperationException("Development launches require a local server endpoint.");
            profiles.Add(endpoint.Address);
        }
    }
}
