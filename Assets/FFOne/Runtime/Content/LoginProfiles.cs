using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FFOne.Network;
using Newtonsoft.Json;

namespace FFOne.Content
{
    public interface ILoginSecretStore
    {
        string Read(string server, string username);
        void Write(string server, string username, string secret);
    }

    public sealed class SavedLoginAccount
    {
        [JsonProperty("server")] public string Server;
        [JsonProperty("username")] public string Username;
    }

    public sealed class LoginProfiles
    {
        sealed class Document
        {
            public string schema = "ffone.login-profiles.v1";
            public List<LoginServer> servers;
            public int selected;
            public List<SavedLoginAccount> accounts;
        }
        readonly string path;
        readonly ILoginSecretStore vault;
        public List<LoginServer> Servers { get; private set; } = new List<LoginServer> { LoginServer.Parse("localhost"), LoginServer.Parse("api.slavicfall.ru") };
        public List<SavedLoginAccount> Accounts { get; private set; } = new List<SavedLoginAccount>();
        public int Selected { get; private set; }
        public LoginServer Current => Servers[Selected];
        public IEnumerable<SavedLoginAccount> CurrentAccounts => Accounts.Where(a => a.Server == Current.Key);
        public string LoadError { get; private set; }

        public LoginProfiles(string path, ILoginSecretStore vault)
        {
            this.path = path; this.vault = vault;
            if (!File.Exists(path)) return;
            try
            {
                var document = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path));
                if (document?.schema != "ffone.login-profiles.v1" || document.servers == null || document.servers.Count == 0)
                    throw new InvalidDataException("Unsupported account settings.");
                foreach (var server in document.servers)
                {
                    var parsed = LoginServer.Parse(server.Key);
                    if (parsed.Address != server.Address || parsed.Api != server.Api) throw new InvalidDataException("Invalid saved server.");
                }
                Servers = document.servers;
                Selected = Math.Max(0, Math.Min(document.selected, Servers.Count - 1));
                Accounts = (document.accounts ?? new List<SavedLoginAccount>()).Where(a => a != null &&
                    !string.IsNullOrWhiteSpace(a.Username) && Servers.Any(s => s.Key == a.Server)).ToList();
            }
            catch (Exception ex) { LoadError = ex.Message; }
        }
        public void Select(int index)
        {
            if (index < 0 || index >= Servers.Count) throw new ArgumentOutOfRangeException(nameof(index));
            Selected = index; Save();
        }
        public void Add(string address)
        {
            var server = LoginServer.Parse(address);
            int index = Servers.FindIndex(s => s.Key == server.Key);
            if (index < 0) { index = Servers.Count; Servers.Add(server); }
            Select(index);
        }
        public string ReadSecret(string username)
        {
            if (!CurrentAccounts.Any(a => a.Username == username)) throw new InvalidOperationException("Select a saved account first.");
            return vault.Read(Current.Key, username);
        }
        // Called only after the game login server has accepted the account.
        public void RememberAccepted(LoginServer server, string username, string secret)
        {
            vault.Write(server.Key, username, secret);
            if (!Accounts.Any(a => a.Server == server.Key && a.Username == username))
                Accounts.Add(new SavedLoginAccount { Server = server.Key, Username = username });
            Save();
        }
        void Save()
        {
            // A malformed user file remains available for repair instead of being overwritten.
            if (LoadError != null) throw new IOException(LoadError);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var document = new Document { servers = Servers, selected = Selected, accounts = Accounts };
                File.WriteAllText(temp, JsonConvert.SerializeObject(document, Formatting.Indented), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
