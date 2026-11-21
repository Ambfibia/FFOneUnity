using System;
using System.Collections.Generic;
using System.Linq;

namespace FFOne.Network
{
    public sealed class FriendInfo
    {
        public int Slot, PlayerId, State;
        public long Uid;
        public string First, Last;
        public bool Blocked;
        public string Name => (First + " " + Last).Trim();
    }
    public sealed class GroupMember
    {
        public int Id, Level, Hp, MaxHp, Map, X, Y, Z;
        public int NanoId, NanoSkill, NanoStamina;
        public long Uid;
        public string Name;
    }
    public sealed class SocialUpdate
    {
        public uint Type;
        public int Id, Slot, Error, ListStart;
        public long Uid, ToUid;
        public string First, Last, Text;
        public bool Failed;
        public IReadOnlyList<FriendInfo> Friends;
        public IReadOnlyList<GroupMember> Members;
        public IReadOnlyList<GroupNpc> Npcs;
        public IReadOnlyList<int> FriendIds, FriendStates;
        public int NpcCount;
    }
    public sealed class GroupNpc { public int Id, Type, Hp, Map, X, Y, Z; }
    /// <summary>0104 buddy lifecycle, group counted rosters and social freechat. No guessed legacy layouts.</summary>
    public static class ProtocolSocial
    {
        public const uint List = 0x31000063, Added = 0x31000067, States = 0x3100006f,
            Blocked = 0x31000071, Removed = 0x31000073, Request = 0x3100007b, NamedRequest = 0x310000fe,
            Invite = 0x31000086, Join = 0x3100008b, Leave = 0x3100008c, Left = 0x3100008e, Members = 0x3100008f,
            WhisperOk = 0x31000069, GroupChatOk = 0x310000b5, WhisperMenuOk = 0x3100006b, GroupMenuOk = 0x310000ba;
        public static byte[] Id(int id) { if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id)); var p = new byte[4]; Protocol0104.Write32(p, 0, id); return p; }
        public static byte[] Identity(int id, long uid)
        { if (uid <= 0) throw new ArgumentOutOfRangeException(nameof(uid)); var p = new byte[12]; Array.Copy(Id(id), p, 4); Protocol0104.Write64(p, 4, uid); return p; }
        public static byte[] Accept(int id, bool accept)
        { var p = new byte[16]; p[0] = (byte)(accept ? 1 : 0); Array.Copy(Id(id), 0, p, 4, 4); return p; }
        public static byte[] Name(string first, string last)
        { var p = new byte[52]; Protocol0104.WriteUtf16(p, 0, 9, first); Protocol0104.WriteUtf16(p, 18, 17, last ?? ""); return p; }
        public static byte[] AcceptName(long uid, string first, string last, bool accept)
        { if (uid <= 0) throw new ArgumentOutOfRangeException(nameof(uid)); var p = new byte[64]; Protocol0104.Write32(p, 0, accept ? 1 : 0); Protocol0104.Write64(p, 4, uid); Array.Copy(Name(first, last), 0, p, 12, 52); return p; }
        public static byte[] Slot(int slot, long uid)
        { if (slot < 0 || slot >= 50 || uid <= 0) throw new ArgumentOutOfRangeException(); var p = new byte[12]; Protocol0104.Write64(p, 0, uid); p[8] = (byte)slot; return p; }
        public static byte[] Whisper(int slot, long uid, string text)
        { var p = new byte[272]; Array.Copy(ProtocolChat.FreeChatPayload(text), p, 260); Array.Copy(Slot(slot, uid), 0, p, 260, 9); return p; }
        static FriendInfo Friend(byte[] p, int o, int slot) => new FriendInfo { Slot = slot, PlayerId = I(p, o), Uid = Protocol0104.Read64(p, o + 4),
            Blocked = p[o + 12] != 0, State = p[o + 14], First = Protocol0104.ReadUtf16(p, o + 16, 9), Last = Protocol0104.ReadUtf16(p, o + 34, 17) };
        public static bool TryDecode(ProtocolFrame frame, out SocialUpdate update)
        {
            update = null; if (frame == null) return false; var p = frame.Payload; var u = new SocialUpdate { Type = frame.PacketType };
            switch (u.Type)
            {
                case List:
                    if (p.Length < 16) throw new ProtocolException("Truncated buddy list.");
                    u.Id = I(p, 0); u.Uid = Protocol0104.Read64(p, 4); u.ListStart = p[12]; int count = p[13];
                    if (u.ListStart + count > 50) throw new ProtocolException("Buddy list range exceeds capacity.");
                    Size(p, 16 + count * 72); u.Friends = Array.AsReadOnly(Enumerable.Range(0, count).Select(i => Friend(p, 16 + i * 72, u.ListStart + i)).ToArray()); break;
                case Added:
                    Size(p, 76); u.Slot = p[0]; if (u.Slot >= 50) throw new ProtocolException("Invalid buddy slot."); u.Friends = Array.AsReadOnly(new[] { Friend(p, 4, u.Slot) }); break;
                case States:
                    Size(p, 252); u.FriendIds = Array.AsReadOnly(Enumerable.Range(0, 50).Select(i => I(p, i * 4)).ToArray());
                    u.FriendStates = Array.AsReadOnly(Enumerable.Range(0, 50).Select(i => (int)p[200 + i]).ToArray()); break;
                case Blocked: case Removed:
                    Size(p, 12); u.Uid = Protocol0104.Read64(p, 0); u.Slot = p[8]; if (u.Slot >= 50) throw new ProtocolException("Invalid buddy removal slot."); break;
                case Request:
                    Size(p, 60); u.Id = I(p, 0); u.First = Protocol0104.ReadUtf16(p, 8, 9); u.Last = Protocol0104.ReadUtf16(p, 26, 17); break;
                case NamedRequest:
                    Size(p, 64); u.First = Protocol0104.ReadUtf16(p, 0, 9); u.Last = Protocol0104.ReadUtf16(p, 18, 17); u.Uid = Protocol0104.Read64(p, 52); break;
                case Invite: case 0x31000088:
                    Size(p, 4); u.Id = I(p, 0); break;
                case Left: Size(p, 1); u.Members = Array.Empty<GroupMember>(); u.Npcs = Array.Empty<GroupNpc>(); break;
                case 0x31000089: case Join: case Leave: case Members: case 0x310000f1: case 0x310000f3:
                    int h = u.Type == 0x310000f1 || u.Type == 0x310000f3 ? 16 : 12;
                    if (p.Length < h) throw new ProtocolException("Truncated group roster.");
                    u.Id = I(p, 0); int pcs = I(p, h - 8), npcs = I(p, h - 4);
                    if (pcs < 0 || pcs > 4 || npcs < 0 || npcs > 5) throw new ProtocolException("Invalid group member counts.");
                    Size(p, h + pcs * 112 + npcs * 32); var members = new List<GroupMember>(); var ids = new HashSet<int>();
                    for (int i = 0; i < pcs; i++)
                    {
                        int o = h + i * 112; int id = I(p, o); if (id <= 0 || !ids.Add(id)) throw new ProtocolException("Invalid/repeated group player.");
                        members.Add(new GroupMember { Id = id, Uid = Protocol0104.Read64(p, o + 4), Name = (Protocol0104.ReadUtf16(p, o + 14, 9) + " " + Protocol0104.ReadUtf16(p, o + 32, 17)).Trim(),
                            Level = Protocol0104.Read16(p, o + 68), Hp = I(p, o + 72), MaxHp = I(p, o + 76), Map = I(p, o + 84), X = I(p, o + 88), Y = I(p, o + 92), Z = I(p, o + 96),
                            NanoId = I(p,o+100)==0 ? 0 : Protocol0104.Read16(p,o+104), NanoSkill = Protocol0104.Read16(p,o+106), NanoStamina = Protocol0104.Read16(p,o+108) });
                    }
                    var companions = new List<GroupNpc>(); ids.Clear();
                    for (int i = 0; i < npcs; i++)
                    {
                        int o = h + pcs * 112 + i * 32, id = I(p, o);
                        if (id <= 0 || !ids.Add(id)) throw new ProtocolException("Invalid/repeated group NPC.");
                        companions.Add(new GroupNpc { Id = id, Type = I(p, o + 4), Hp = I(p, o + 8), Map = I(p, o + 16), X = I(p, o + 20), Y = I(p, o + 24), Z = I(p, o + 28) });
                    }
                    u.Members = members.AsReadOnly(); u.Npcs = companions.AsReadOnly(); u.NpcCount = npcs; break;
                case WhisperOk: case WhisperMenuOk:
                    Size(p, 276); u.Uid = Protocol0104.Read64(p, 0); u.ToUid = Protocol0104.Read64(p, 8); u.Text = Protocol0104.ReadUtf16(p, 16, 128); break;
                case GroupChatOk: case GroupMenuOk:
                    Size(p, 264); u.Id = I(p, 0); u.Text = Protocol0104.ReadUtf16(p, 4, 128); break;
                case 0x83000065: Size(p, 16); u.Id = I(p, 4); u.Uid = Protocol0104.Read64(p, 8); break;
                case 0x31000064: case 0x31000070: case 0x31000087: case 0x310000f0: case 0x310000f2: Size(p, 4); u.Failed = true; u.Error = I(p, 0); break;
                case 0x31000066: case 0x31000068: Size(p, 16); u.Failed = true; u.Error = I(p, 12); break;
                case 0x31000072: case 0x31000074: Size(p, 12); u.Failed = true; u.Error = I(p, 8); break;
                case 0x3100008a: case 0x3100008d: Size(p, 8); u.Failed = true; u.Error = I(p, 4); break;
                case 0x310000ff: Size(p, 56); u.Failed = true; u.Error = I(p, 52); break;
                default: return false;
            }
            update = u; return true;
        }
        static int I(byte[] p, int o) => Protocol0104.Read32(p, o);
        static void Size(byte[] p, int n) => Protocol0104.RequireSize(p, n);
    }
    /// <summary>World-scoped projection; no UI or optimistic friendship/group mutations.</summary>
    public sealed class SocialSession
    {
        readonly FriendInfo[] friends = new FriendInfo[50];
        public IReadOnlyList<FriendInfo> Friends => Array.AsReadOnly(friends);
        public IReadOnlyList<GroupMember> Members { get; private set; } = Array.Empty<GroupMember>();
        public int NpcCount { get; private set; }
        public IReadOnlyList<GroupNpc> Npcs { get; private set; } = Array.Empty<GroupNpc>();
        public void Apply(SocialUpdate u)
        {
            if (u.Failed) return;
            if (u.Type == ProtocolSocial.List && u.ListStart == 0) Array.Clear(friends, 0, friends.Length);
            if (u.Friends != null) foreach (var f in u.Friends) friends[f.Slot] = f.Uid > 0 ? f : null;
            if (u.Type == ProtocolSocial.States)
            {
                // RustyFusion state arrays are packed in occupied-slot order, not indexed by buddy slot.
                int i = 0; for (int slot = 0; slot < friends.Length; slot++) if (friends[slot] != null)
                { var f = friends[slot]; friends[slot] = new FriendInfo { Slot = slot, Uid = f.Uid, First = f.First, Last = f.Last, Blocked = f.Blocked, PlayerId = u.FriendIds[i], State = u.FriendStates[i] }; i++; }
            }
            if (u.Type == ProtocolSocial.Removed && friends[u.Slot]?.Uid == u.Uid) friends[u.Slot] = null;
            if (u.Type == ProtocolSocial.Blocked && friends[u.Slot]?.Uid == u.Uid)
            { var f = friends[u.Slot]; friends[u.Slot] = new FriendInfo { Slot = f.Slot, Uid = f.Uid, First = f.First, Last = f.Last, Blocked = true }; }
            if (u.Members != null) { Members = u.Members; Npcs = u.Npcs ?? Array.Empty<GroupNpc>(); NpcCount = u.NpcCount; }
        }
    }
}
