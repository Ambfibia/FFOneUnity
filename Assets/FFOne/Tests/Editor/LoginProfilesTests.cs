using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFOne.Content;
using FFOne.Network;
using FFOne.UI;
using NUnit.Framework;
using UnityEngine;

namespace FFOne.Tests
{
    public sealed class LoginProfilesTests
    {
        sealed class TestVault : ILoginSecretStore
        {
            readonly Dictionary<string, string> values = new Dictionary<string, string>();
            public string Read(string server, string username) => values[server + "\n" + username];
            public void Write(string server, string username, string secret) => values[server + "\n" + username] = secret;
        }
        [TestCase("localhost", "localhost:23000", null)]
        [TestCase("127.0.0.1:24300", "127.0.0.1:24300", null)]
        [TestCase("[::1]:23000", "[::1]:23000", null)]
        [TestCase("api.slavicfall.ru", "api.slavicfall.ru", "https://api.slavicfall.ru")]
        [TestCase("https://api.slavicfall.ru/", "api.slavicfall.ru", "https://api.slavicfall.ru")]
        public void ServerIdentityMatchesRustBrowser(string input, string address, string api)
        {
            var server = LoginServer.Parse(input);
            Assert.That(server.Address, Is.EqualTo(address)); Assert.That(server.Api, Is.EqualTo(api));
        }
        [TestCase("")]
        [TestCase("https://user:pass@example.org")]
        [TestCase("https://example.org/auth")]
        [TestCase("https://example.org/?token=x")]
        [TestCase("http://example.org")]
        [TestCase("host:0")]
        [TestCase("host:65536")]
        public void RejectsAmbiguousEndpointsAndEmbeddedCredentials(string input)
            => Assert.Throws<ArgumentException>(() => LoginServer.Parse(input));

        [Test]
        public void AccountsStayServerScopedAndMetadataNeverContainsSecrets()
        {
            string directory = Path.Combine(Path.GetTempPath(), "FFOneProfiles-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "login-profiles.json"); var vault = new TestVault();
            try
            {
                var profiles = new LoginProfiles(path, vault);
                Assert.That(profiles.Selected, Is.Zero);
                Assert.That(profiles.CurrentAccounts, Is.Empty);
                profiles.RememberAccepted(profiles.Current, "same-user", "direct-password-secret");
                profiles.Select(1); Assert.That(profiles.CurrentAccounts, Is.Empty);
                Assert.Throws<InvalidOperationException>(() => profiles.ReadSecret("same-user"));
                profiles.RememberAccepted(profiles.Current, "same-user", "refresh-token-secret");
                Assert.That(profiles.ReadSecret("same-user"), Is.EqualTo("refresh-token-secret"));
                profiles.Add("https://api.slavicfall.ru/"); Assert.That(profiles.Servers.Count, Is.EqualTo(2));
                var text = File.ReadAllText(ContentPaths.FilePath(path));
                Assert.That(text, Does.Not.Contain("direct-password-secret").And.Not.Contain("refresh-token-secret"));
                var loaded = new LoginProfiles(path, vault);
                Assert.That(loaded.Selected, Is.EqualTo(1)); Assert.That(loaded.CurrentAccounts.Count(), Is.EqualTo(1));
                loaded.Select(0); Assert.That(loaded.ReadSecret("same-user"), Is.EqualTo("direct-password-secret"));
            }
            finally { if (ContentPaths.DirectoryExists(directory)) Directory.Delete(ContentPaths.FilePath(directory), true); }
        }
        [Test]
        public void WindowsVaultRoundTripUsesOnlyDedicatedTestCredentials()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows credential store test.");
            var vault = new WindowsLoginSecrets("FFOneUnity-Test-" + Guid.NewGuid().ToString("N"));
            try
            {
                vault.Write("localhost:23000", "test-user", "Секрет-0123");
                Assert.That(vault.Read("localhost:23000", "test-user"), Is.EqualTo("Секрет-0123"));
                Assert.Throws<InvalidOperationException>(() => vault.Read("localhost:23001", "test-user"));
            }
            finally { vault.Remove("localhost:23000", "test-user"); }
        }
        [Test]
        public void LoginKeepsRustRememberRowAndHeightBasedScale()
        {
            Assert.That(FrontendLayout.Login(1280, 720), Is.EqualTo(new Rect(455, 210.5f, 370, 299)));
            Assert.That(FrontendLayout.UiScale(681), Is.EqualTo(1));
            Assert.That(FrontendLayout.UiScale(720), Is.EqualTo(1));
            Assert.That(FrontendLayout.UiScale(1080), Is.EqualTo(1.4765625f).Within(.00001));
        }
    }
}
