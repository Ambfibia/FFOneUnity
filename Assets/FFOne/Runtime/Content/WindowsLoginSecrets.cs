using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FFOne.Content
{
    // Same OS-vault policy as the Rust client. Unity uses its own target namespace.
    public sealed class WindowsLoginSecrets : ILoginSecretStore
    {
        readonly string scope;
        public WindowsLoginSecrets(string scope = "FFOneUnity") { this.scope = scope; }
        string Target(string server, string username)
        {
            using var sha = SHA256.Create();
            return scope + ":v1:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(server + "\n" + username))).Replace("-", "");
        }
        static void RequireWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("Saved accounts currently require Windows Credential Manager.");
        }
        public string Read(string server, string username)
        {
            RequireWindows();
            if (!CredReadW(Target(server, username), 1, 0, out var pointer))
                throw new InvalidOperationException("Saved account expired or is unavailable; enter the password again.");
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                return Marshal.PtrToStringUni(credential.Blob, checked((int)credential.BlobSize / 2));
            }
            finally { CredFree(pointer); }
        }
        public void Write(string server, string username, string secret)
        {
            RequireWindows();
            if (string.IsNullOrEmpty(secret) || Encoding.Unicode.GetByteCount(secret) > 2560)
                throw new ArgumentException("Credential is empty or too long.");
            var blob = Marshal.StringToCoTaskMemUni(secret);
            try
            {
                var credential = new Credential { Type = 1, TargetName = Target(server, username),
                    UserName = username, BlobSize = (uint)Encoding.Unicode.GetByteCount(secret), Blob = blob, Persist = 2 };
                if (!CredWriteW(ref credential, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
        }
        public void Remove(string server, string username)
        {
            RequireWindows();
            if (!CredDeleteW(Target(server, username), 1, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint BlobSize;
            public IntPtr Blob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredWriteW(ref Credential credential, uint flags);
        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredDeleteW(string target, uint type, uint flags);
        [DllImport("Advapi32.dll")] static extern void CredFree(IntPtr buffer);
    }
}
