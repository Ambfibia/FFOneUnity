using System;
using System.Text;

namespace FFOne.Network
{
    /// <summary>Exact FFOne / Retrobution 0104 wire codec, independent of Unity.</summary>
    public static class Protocol0104
    {
        public const ulong DefaultKey = 0x51217e5472243e62UL;
        public const int MaximumBodySize = 4096;
        public const uint TypeMask = 0xff000fff;

        public static ulong DeriveKey(ulong seed, int iv1, int iv2)
        { unchecked { return DefaultKey * seed * (ulong)(iv1 + 1) * (ulong)(iv2 + 1); } }
        public static ulong FrontendKey => DeriveKey(DefaultKey, 44, 1);

        public static byte[] EncodeFrame(uint type, byte[] payload, ulong key, ushort sequence = 0)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ValidateLength((uint)payload.Length + 4);
            byte[] body = new byte[payload.Length + 4];
            Buffer.BlockCopy(payload, 0, body, 4, payload.Length);
            ushort flags = (ushort)(Checksum(body) ^ (sequence & 4095));
            Write32(body, 0, unchecked((int)((type & TypeMask) | ((uint)flags << 12))));
            Xor(body, key); Swap(body);
            byte[] result = new byte[body.Length + 4];
            Write32(result, 0, body.Length);
            Buffer.BlockCopy(body, 0, result, 4, body.Length);
            return result;
        }

        public static ProtocolFrame DecodeFrame(byte[] frame, ulong key, ushort sequence = 0)
        {
            if (frame == null || frame.Length < 4) throw new ProtocolException("Missing frame length.");
            uint length = unchecked((uint)Read32(frame, 0));
            ValidateLength(length);
            if (frame.Length != length + 4) throw new ProtocolException("Incomplete frame or trailing data.");
            byte[] body = new byte[length];
            Buffer.BlockCopy(frame, 4, body, 0, body.Length);
            Swap(body); Xor(body, key);
            uint rawType = unchecked((uint)Read32(body, 0));
            ushort flags = (ushort)((rawType >> 12) & 4095);
            if ((flags ^ (sequence & 4095)) != Checksum(body)) throw new ProtocolException("Packet checksum mismatch.");
            byte[] payload = new byte[length - 4];
            Buffer.BlockCopy(body, 4, payload, 0, payload.Length);
            return new ProtocolFrame(rawType & TypeMask, payload);
        }

        public static void ValidateLength(uint length)
        {
            if (length < 4 || length > MaximumBodySize)
                throw new ProtocolException($"Invalid frame body length {length}; expected 4..{MaximumBodySize}.");
        }

        public static ushort Checksum(byte[] body)
        {
            uint a = 0, b = 0;
            for (int i = 4; i < body.Length; i++) { a = (a + body[i]) % 255; b = (b + a) % 255; }
            return (ushort)(((b << 8) | a) & 4095);
        }

        private static void Xor(byte[] body, ulong key)
        { for (int i = 0; i < body.Length; i++) body[i] ^= (byte)(key >> ((i % 8) * 8)); }
        private static void Swap(byte[] body)
        {
            int size = body.Length % 5 * 2 + 8, offset = 0;
            for (int start = 0; start + size <= body.Length; start += size)
            {
                int a = start + offset, b = start + size - 1 - offset;
                byte value = body[a]; body[a] = body[b]; body[b] = value;
                if (++offset > size / 2) offset = 0;
            }
        }

        public static byte[] LoginPayload(string username, string secret, bool cookie)
        {
            byte[] result = new byte[468];
            Write32(result, 132, 1); Write32(result, 140, 44); Write32(result, 144, cookie ? 2 : 1);
            if (cookie)
            { WriteUtf8(result, 148, 64, username); WriteUtf8(result, 212, 255, secret); }
            else
            { WriteUtf16(result, 0, 33, username); WriteUtf16(result, 66, 33, secret); }
            return result;
        }

        public static CharacterInfo DecodeCharacter(byte[] payload)
        {
            RequireSize(payload, 204);
            return new CharacterInfo
            {
                Slot = (sbyte)payload[0], Level = Read16(payload, 2), Uid = Read64(payload, 4),
                FirstName = ReadUtf16(payload, 14, 9), LastName = ReadUtf16(payload, 32, 17),
                Style = ReadStyle(payload, 4, 80), Equipment = ReadEquipment(payload, 96),
                X = Read32(payload, 84), Y = Read32(payload, 88), Z = Read32(payload, 92),
                RawData = (byte[])payload.Clone()
            };
        }

        public static WorldEntry DecodeWorldEntry(byte[] payload, long uid)
        {
            RequireSize(payload, 2700);
            byte[] load = new byte[2688]; Buffer.BlockCopy(payload, 4, load, 0, load.Length);
            if (Read64(load, 4) != uid) throw new ProtocolException("World entry character UID does not match selection.");
            return new WorldEntry
            {
                PlayerId = Read32(payload, 0), CharacterUid = uid, UserLevel = Read16(load, 0),
                X = Read32(load, 120), Y = Read32(load, 124), Z = Read32(load, 128), Angle = Read32(load, 132),
                Level = Read16(load, 84), Hp = Read32(load, 92), FusionMatter = Read32(load, 108),
                MapNumber = Read32(load, 116), ServerTime = unchecked((ulong)Read64(payload, 2692)), RawLoadData = load
            };
        }

        public static PlayerPosition DecodePlayerPosition(byte[] payload)
        {
            RequireSize(payload, 12);
            return new PlayerPosition(Read32(payload, 0), Read32(payload, 4), Read32(payload, 8));
        }

        /// <summary>MOVE uses unscaled float metres/second; positions and speed use centimetres. Angle is the wire heading.</summary>
        public static byte[] MovementPayload(int x, int y, int z, int angle, float velocityX, float velocityY, float velocityZ, int speed, byte keys, bool stopped)
        {
            byte[] payload = MovementPosition(x, y, z, stopped ? 20 : 44);
            if (stopped) return payload;
            if (speed < 0) throw new ArgumentOutOfRangeException(nameof(speed));
            float[] velocity = { velocityX, velocityY, velocityZ };
            for (int i = 0; i < velocity.Length; i++)
            {
                if (float.IsNaN(velocity[i]) || float.IsInfinity(velocity[i]))
                    throw new ArgumentException("Movement velocity must be finite.");
                byte[] bytes = BitConverter.GetBytes(velocity[i]);
                if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                Buffer.BlockCopy(bytes, 0, payload, 20 + i * 4, 4);
            }
            Write32(payload, 32, angle); payload[36] = keys; Write32(payload, 40, speed);
            return payload;
        }

        /// <summary>JUMP uses integer centimetres/second and packs launch velocity plus the landing key into iCliTime.</summary>
        public static byte[] JumpPayload(int x, int y, int z, int angle, int velocityX, int velocityY, int velocityZ, int jumpHeight, byte keys, float launchVelocity, uint jumpKey)
        {
            if (jumpHeight < 0) throw new ArgumentOutOfRangeException(nameof(jumpHeight));
            if (float.IsNaN(launchVelocity) || float.IsInfinity(launchVelocity) || launchVelocity < 0 || (double)launchVelocity * 100 > uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(launchVelocity));
            byte[] payload = MovementPosition(x, y, z, 44);
            ulong launch = (ulong)(launchVelocity * 100f);
            if (launch > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(launchVelocity));
            Write64(payload, 0, unchecked((long)((launch << 32) | jumpKey)));
            Write32(payload, 20, velocityX); Write32(payload, 24, velocityY); Write32(payload, 28, velocityZ);
            Write32(payload, 32, angle); payload[36] = keys; Write32(payload, 40, jumpHeight);
            return payload;
        }

        private static byte[] MovementPosition(int x, int y, int z, int size)
        {
            byte[] payload = new byte[size]; // Original MOVE/STOP iCliTime is zero, not wall-clock time.
            Write32(payload, 8, x); Write32(payload, 12, y); Write32(payload, 16, z);
            return payload;
        }

        internal static CharacterStyle ReadStyle(byte[] p, int offset, int flags)
        {
            var style = ReadAppearance(p, offset);
            style.AppearanceComplete = p[flags] != 0; style.TutorialComplete = p[flags + 1] != 0; style.Payzone = p[flags + 2] != 0;
            return style;
        }

        internal static CharacterStyle ReadAppearance(byte[] p, int offset)
        {
            return new CharacterStyle
            {
                NameCheck = (sbyte)p[offset + 8], Gender = (sbyte)p[offset + 62], FaceStyle = (sbyte)p[offset + 63],
                HairStyle = (sbyte)p[offset + 64], HairColor = (sbyte)p[offset + 65], SkinColor = (sbyte)p[offset + 66],
                EyeColor = (sbyte)p[offset + 67], Height = (sbyte)p[offset + 68], Body = (sbyte)p[offset + 69],
                Class = Read32(p, offset + 72)
            };
        }

        internal static EquipmentItem[] ReadEquipment(byte[] p, int start)
        {
            var equipment = new EquipmentItem[9];
            for (int i = 0; i < 9; i++)
            {
                int o = start + i * 12;
                equipment[i] = new EquipmentItem { ItemType = Read16(p, o), ItemId = Read16(p, o + 2),
                    Option = Read32(p, o + 4), TimeLimit = Read32(p, o + 8) };
            }
            return equipment;
        }

        public static void RequireSize(byte[] bytes, int expected)
        { if (bytes == null || bytes.Length != expected) throw new ProtocolException($"Expected {expected} payload bytes, got {bytes?.Length ?? 0}."); }
        public static short Read16(byte[] b, int o) => unchecked((short)(b[o] | b[o + 1] << 8));
        public static int Read32(byte[] b, int o) => b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24;
        public static long Read64(byte[] b, int o) => unchecked((long)((uint)Read32(b, o) | (ulong)(uint)Read32(b, o + 4) << 32));
        public static void Write16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        public static void Write32(byte[] b, int o, int v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        public static void Write64(byte[] b, int o, long v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }
        public static string ReadUtf16(byte[] b, int o, int units)
        {
            int length = 0;
            while (length < units && Read16(b, o + length * 2) != 0) length++;
            return Encoding.Unicode.GetString(b, o, length * 2);
        }
        public static void WriteUtf16(byte[] b, int o, int units, string value)
        {
            if (value == null || value.Length >= units || value.IndexOf('\0') >= 0)
                throw new ArgumentException($"Text must contain at most {units - 1} UTF-16 units and no NUL characters.");
            byte[] encoded = new UnicodeEncoding(false, false, true).GetBytes(value);
            Buffer.BlockCopy(encoded, 0, b, o, encoded.Length);
        }
        private static void WriteUtf8(byte[] b, int o, int size, string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('\0') >= 0) throw new ArgumentException("Missing or invalid cookie field.");
            byte[] encoded = new UTF8Encoding(false, true).GetBytes(value);
            if (encoded.Length >= size) throw new ArgumentException($"Cookie field exceeds {size - 1} UTF-8 bytes.");
            Buffer.BlockCopy(encoded, 0, b, o, encoded.Length);
        }
    }

    internal static class PacketId
    {
        internal const uint Login = 0x12000001, CheckName = 0x12000002, SaveName = 0x12000003,
            Create = 0x12000004, Select = 0x12000005, Delete = 0x12000006, ShardSelect = 0x12000007,
            SaveTutorial = 0x1200000a, LoginHeartbeatReply = 0x1200000c, LoginSuccess = 0x21000001, LoginFailure = 0x21000002,
            CharacterInfo = 0x21000003, CheckNameSuccess = 0x21000005, CheckNameFailure = 0x21000006,
            SaveNameSuccess = 0x21000007, SaveNameFailure = 0x21000008, CreateSuccess = 0x21000009,
            CreateFailure = 0x2100000a, SelectSuccess = 0x2100000b, SelectFailure = 0x2100000c,
            DeleteSuccess = 0x2100000d, DeleteFailure = 0x2100000e, ShardSelectSuccess = 0x2100000f,
            ShardSelectFailure = 0x21000010, LoginHeartbeat = 0x21000016,
            Enter = 0x13000001, Exit = 0x13000002, Move = 0x13000003, Stop = 0x13000004, Jump = 0x13000005,
            ShardHeartbeatReply = 0x13000075, LoadingComplete = 0x1300008d,
            EnterFailure = 0x31000001, EnterSuccess = 0x31000002, ExitFailure = 0x31000004,
            ExitSuccess = 0x31000005, Announcement = 0x310000c8, ShardHeartbeat = 0x310000d0,
            LoadingSuccess = 0x310000f9, NanoBookSubset = 0x31000134, GotoSuccess = 0x31000031;
    }
}
