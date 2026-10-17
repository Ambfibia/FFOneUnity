using ContentPaths = FFOne.Content.ContentPaths;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FFOne.Editor.Integration
{
    public static class DevelopmentSettings
    {
        static string Prefix => "FFOne.Development." + Application.dataPath + ".";
        public static string ProjectDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string CacheDirectory => Path.Combine(ProjectDirectory, "Library", "FFOneDevelopment");
        public static string ServerDirectory {
            get => EditorPrefs.GetString(Prefix + "Server", Path.GetFullPath(Path.Combine(ProjectDirectory, "..", "RustyFusion")));
            set => EditorPrefs.SetString(Prefix + "Server", value);
        }
        public static string CargoExecutable {
            get => EditorPrefs.GetString(Prefix + "Cargo", DefaultCargo());
            set => EditorPrefs.SetString(Prefix + "Cargo", value);
        }
        public static bool IsolatedDatabase {
            get => EditorPrefs.GetBool(Prefix + "IsolatedDatabase", true);
            set => EditorPrefs.SetBool(Prefix + "IsolatedDatabase", value);
        }
        public static int LoginPort {
            get => EditorPrefs.GetInt(Prefix + "LoginPort", 24300);
            set => EditorPrefs.SetInt(Prefix + "LoginPort", value);
        }
        public static int ShardPort {
            get => EditorPrefs.GetInt(Prefix + "ShardPort", 24301);
            set => EditorPrefs.SetInt(Prefix + "ShardPort", value);
        }
        public static string BuildDirectory {
            get => EditorPrefs.GetString(Prefix + "Build", Path.Combine(ProjectDirectory, "Builds", "Windows"));
            set => EditorPrefs.SetString(Prefix + "Build", value);
        }
        public static bool StartForPlay {
            get => EditorPrefs.GetBool(Prefix + "StartForPlay", false);
            set => EditorPrefs.SetBool(Prefix + "StartForPlay", value);
        }
        public static string Endpoint => "127.0.0.1:" + LoginPort;
        public static string DataDirectory => Path.Combine(ServerDirectory, "target", "ffone-unity");
        public static string ProfilesDirectory => Path.Combine(CacheDirectory, IsolatedDatabase ? "IsolatedProfiles" : "LocalProfiles");

        static string DefaultCargo()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "cargo.exe");
            return File.Exists(ContentPaths.FilePath(path)) ? path : "cargo.exe";
        }

        public static string[] Arguments(bool isolated, int loginPort, int shardPort)
        {
            if (loginPort < 1024 || loginPort > 65535 || shardPort < 1024 || shardPort > 65535 || loginPort == shardPort)
                throw new ArgumentException("Use two distinct local ports between 1024 and 65535.");
            var result = new List<string> {
                "dev", "--", "general.enable_tui=false", "general.logging_level_console=2", "general.combined_log_path=",
                "login.log_path=", "shard.log_path=", "login.monitor_enabled=false",
                "login.listen_addr=127.0.0.1:" + loginPort,
                "shard.listen_addr=127.0.0.1:" + shardPort,
                "shard.external_addr=127.0.0.1:" + shardPort,
                "shard.login_server_addr=127.0.0.1:" + loginPort,
                "login.allow_plaintext_passwords=true"
            };
            if (isolated)
            {
                result.Add("general.db_path=target/ffone-unity/database.db");
                result.Add("login.auto_create_accounts=true");
            }
            return result.ToArray();
        }
    }
}
