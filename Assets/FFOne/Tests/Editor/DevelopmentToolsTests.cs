using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.IO;
using FFOne.Content;
using FFOne.Editor.Integration;
using FFOne.Integration;
using NUnit.Framework;

namespace FFOne.Tests
{
    public sealed class DevelopmentToolsTests
    {
        sealed class NoSecrets : ILoginSecretStore
        {
            public string Read(string server, string username) => null;
            public void Write(string server, string username, string secret) => Assert.Fail("Selecting a development server must not write credentials.");
        }

        [TestCase("", "\"\"")]
        [TestCase("plain", "\"plain\"")]
        [TestCase("C:\\Program Files\\Rust\\", "\"C:\\Program Files\\Rust\\\\\"")]
        [TestCase("a\"b", "\"a\\\"b\"")]
        public void WindowsArgumentsPreserveSpacesQuotesAndTrailingSlashes(string argument, string quoted)
            => Assert.That(DevelopmentProcess.Quote(argument), Is.EqualTo(quoted));

        [Test]
        public void DevelopmentProfileUsesIsolatedDatabaseAndMatchingLocalEndpoints()
        {
            var arguments = DevelopmentSettings.Arguments(true, 24300, 24301);
            Assert.That(arguments[0], Is.EqualTo("dev"));
            Assert.That(arguments[1], Is.EqualTo("--"));
            Assert.That(arguments, Does.Contain("general.db_path=target/ffone-unity/database.db"));
            Assert.That(arguments, Does.Contain("login.listen_addr=127.0.0.1:24300"));
            Assert.That(arguments, Does.Contain("shard.login_server_addr=127.0.0.1:24300"));
            Assert.That(arguments, Does.Contain("shard.listen_addr=127.0.0.1:24301"));
            Assert.That(arguments, Does.Contain("shard.external_addr=127.0.0.1:24301"));
            Assert.That(arguments, Does.Contain("general.enable_tui=false"));
            Assert.That(arguments, Does.Contain("login.allow_plaintext_passwords=true"));
            Assert.That(Array.Exists(DevelopmentSettings.Arguments(false, 24300, 24301),
                argument => argument.StartsWith("general.db_path=", StringComparison.Ordinal)), Is.False);
        }

        [TestCase(24300, 24300)]
        [TestCase(0, 24301)]
        [TestCase(24300, 65536)]
        public void InvalidOrOverlappingPortsAreRejectedBeforeStarting(int login, int shard)
            => Assert.Throws<ArgumentException>(() => DevelopmentSettings.Arguments(true, login, shard));

        [Test]
        public void ExplicitDevelopmentLaunchLeavesOrdinaryAccountProfilesUntouched()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ffone-dev-profiles-" + Guid.NewGuid().ToString("N"));
            string server = Environment.GetEnvironmentVariable(DevelopmentConnection.ServerVariable);
            string profiles = Environment.GetEnvironmentVariable(DevelopmentConnection.ProfilesVariable);
            try
            {
                Directory.CreateDirectory(ContentPaths.FilePath(directory));
                string ordinary = Path.Combine(directory, "ordinary");
                string development = Path.Combine(directory, "development");
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, null);
                Environment.SetEnvironmentVariable(DevelopmentConnection.ProfilesVariable, null);
                Assert.That(DevelopmentConnection.ProfilesPath(ordinary), Is.EqualTo(Path.Combine(ordinary, "login-profiles.json")));
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, "127.0.0.1:24300");
                Environment.SetEnvironmentVariable(DevelopmentConnection.ProfilesVariable, development);
                var selected = new LoginProfiles(DevelopmentConnection.ProfilesPath(ordinary), new NoSecrets());
                DevelopmentConnection.SelectServer(selected);
                Assert.That(selected.Current.Address, Is.EqualTo("127.0.0.1:24300"));
                Assert.That(File.Exists(ContentPaths.FilePath(Path.Combine(development, "login-profiles.json"))), Is.True);
                Assert.That(ContentPaths.DirectoryExists(ordinary), Is.False);
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, "example.org:24300");
                Assert.Throws<InvalidOperationException>(() => DevelopmentConnection.SelectServer(selected));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevelopmentConnection.ServerVariable, server);
                Environment.SetEnvironmentVariable(DevelopmentConnection.ProfilesVariable, profiles);
                if (ContentPaths.DirectoryExists(directory)) Directory.Delete(ContentPaths.FilePath(directory), true);
            }
        }
    }
}
