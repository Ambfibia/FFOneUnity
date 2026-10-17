using System;
using System.Text;

namespace FFOne.Network
{
    public sealed class TradeChatMessage
    {
        public int SenderId { get; }
        public int FromId { get; }
        public int ToId { get; }
        public string Text { get; }
        public int EmoteCode { get; }
        public bool Rejected { get; }
        public int ErrorCode { get; }
        internal TradeChatMessage(int sender, int from, int to, string text, int emote, bool rejected, int error)
        { SenderId = sender; FromId = from; ToId = to; Text = text; EmoteCode = emote; Rejected = rejected; ErrorCode = error; }
    }

    // RustyFusion trade.rs588 + ffproto 0104 (pack4). Trade chat has its own participant header;
    // the failure packet is f6, not the Nano-create packet that follows success52.
    public static class ProtocolTradeChat
    {
        public const uint Request = 0x1300002d, Success = 0x31000052, Failure = 0x310000f6;
        // Panel_Trade.RenderChat IL_01b0: ldc.i4.s 0x40 before GUI.TextField.
        public const int MaximumTextUnits = 64;
        public static byte[] Payload(int sender, int from, int to, string text)
        {
            var participants = ProtocolTrade.Participants(sender, from, to);
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextUnits)
                throw new ArgumentException("Trade chat must contain 1 to 64 UTF-16 units.", nameof(text));
            var bytes = new byte[276]; Array.Copy(participants, bytes, participants.Length);
            Protocol0104.WriteUtf16(bytes, 12, 128, text);
            bytes[272] = 1; // Plain free chat; emote at268 remains zero, trailing pack4 padding zero.
            return bytes;
        }

        public static bool TryDecode(ProtocolFrame frame, out TradeChatMessage message)
        {
            message = null;
            if (frame == null || (frame.PacketType != Success && frame.PacketType != Failure)) return false;
            bool rejected = frame.PacketType == Failure;
            Protocol0104.RequireSize(frame.Payload, rejected ? 276 : 272);
            int sender = Protocol0104.Read32(frame.Payload, 0), from = Protocol0104.Read32(frame.Payload, 4), to = Protocol0104.Read32(frame.Payload, 8);
            try { ProtocolTrade.Participants(sender, from, to); }
            catch (ArgumentException error) { throw new ProtocolException(error.Message); }
            int units = 0;
            while (units < 128 && Protocol0104.Read16(frame.Payload, 12 + units * 2) != 0) units++;
            if (units == 128) throw new ProtocolException("Trade chat is not NUL-terminated.");
            string text;
            try { text = new UnicodeEncoding(false, false, true).GetString(frame.Payload, 12, units * 2); }
            catch (DecoderFallbackException) { throw new ProtocolException("Trade chat contains invalid UTF-16."); }
            message = new TradeChatMessage(sender, from, to, text, Protocol0104.Read32(frame.Payload, 268), rejected,
                rejected ? Protocol0104.Read32(frame.Payload, 272) : 0);
            return true;
        }
    }
}
