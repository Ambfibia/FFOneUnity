using System;
using System.Text;

namespace FFOne.Network
{
    public enum ChatMessageKind { Message, Rejected }

    public sealed class ChatMessage
    {
        public ChatMessageKind Kind { get; }
        public int PlayerId { get; }
        public string Text { get; }
        public int EmoteCode { get; }
        public int ErrorCode { get; }
        internal ChatMessage(ChatMessageKind kind, int playerId, string text, int emoteCode, int errorCode)
        { Kind = kind; PlayerId = playerId; Text = text; EmoteCode = emoteCode; ErrorCode = errorCode; }
    }

    /// <summary>Normal free-chat ABI confirmed by RustyFusion chat.rs and the accepted 0104 client codec.</summary>
    public static class ProtocolChat
    {
        public const uint Request = 0x13000007, Success = 0x31000012, Failure = 0x31000013;
        public const int MaximumTextUnits = 127; // 128 UTF-16 units including the required NUL.

        public static byte[] FreeChatPayload(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Chat message is empty.", nameof(text));
            var bytes = new byte[260];
            // Strict writer rejects NUL, overlong text and unpaired surrogates. No silent truncation.
            Protocol0104.WriteUtf16(bytes, 0, 128, text);
            // iEmoteCode=0: plain text. Avatar/emote commands have their own gameplay semantics.
            return bytes;
        }

        public static bool TryDecode(ProtocolFrame frame, out ChatMessage message)
        {
            message = null;
            if (frame == null || (frame.PacketType != Success && frame.PacketType != Failure)) return false;
            Protocol0104.RequireSize(frame.Payload, 264);
            // Reply: sender/error int32, char16[128], emote int32, pack(4).
            int value = Protocol0104.Read32(frame.Payload, 0);
            string text = ReadText(frame.Payload, 4, 128);
            bool rejected = frame.PacketType == Failure;
            message = new ChatMessage(rejected ? ChatMessageKind.Rejected : ChatMessageKind.Message,
                rejected ? 0 : value, text, Protocol0104.Read32(frame.Payload, 260), rejected ? value : 0);
            return true;
        }

        static string ReadText(byte[] bytes, int offset, int units)
        {
            int length = 0;
            while (length < units && Protocol0104.Read16(bytes, offset + length * 2) != 0) length++;
            if (length == units) throw new ProtocolException("Chat reply is not NUL-terminated.");
            try { return new UnicodeEncoding(false, false, true).GetString(bytes, offset, length * 2); }
            catch (DecoderFallbackException) { throw new ProtocolException("Chat reply contains invalid UTF-16."); }
        }
    }
}
