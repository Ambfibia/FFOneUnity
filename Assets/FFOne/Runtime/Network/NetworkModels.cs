using System;
using System.Collections.Generic;
using System.IO;

namespace FFOne.Network
{
    // Protocol coordinates are centimetres, with Z up. Renderers convert to Unity metres (X,Z,Y).
    public sealed class CharacterInfo
    {
        public long Uid { get; internal set; }
        public int Slot { get; internal set; }
        public int Level { get; internal set; }
        public string FirstName { get; internal set; } = "";
        public string LastName { get; internal set; } = "";
        public string DisplayName => (FirstName + " " + LastName).Trim();
        public CharacterStyle Style { get; internal set; } = new CharacterStyle();
        public EquipmentItem[] Equipment { get; internal set; } = new EquipmentItem[9];
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        public byte[] RawData { get; internal set; }
    }

    public sealed class CharacterStyle
    {
        public int NameCheck;
        public int Gender = 1;
        public int FaceStyle = 1;
        public int HairStyle = 1;
        public int HairColor = 1;
        public int SkinColor = 1;
        public int EyeColor = 1;
        public int Height = 1;
        public int Body = 1;
        public int Class;
        public bool AppearanceComplete;
        public bool TutorialComplete;
        public bool Payzone;
    }

    public struct EquipmentItem
    {
        public int ItemType;
        public int ItemId;
        public int Option;
        public int TimeLimit;
    }

    public sealed class CharacterCreation
    {
        public int Slot = 1;
        public string FirstName = "";
        public string LastName = "";
        public int FirstNameCode;
        public int MiddleNameCode;
        public int LastNameCode;
        public CharacterStyle Style = new CharacterStyle();
        // IDs come from the accepted starter item tables, never UI row indices.
        public int UpperBodyId;
        public int LowerBodyId;
        public int FootId;
        public int UpperBodyIndex;
        public int LowerBodyIndex;
        public int FootIndex;
        public int FaceStyleIndex;
        public int HairStyleIndex;
        // A roster entry with AppearanceComplete=false can resume after a disconnect.
        public long ExistingUid;
    }

    public sealed class WorldEntry
    {
        public int PlayerId { get; internal set; }
        public long CharacterUid { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        public int Angle { get; internal set; }
        public int UserLevel { get; internal set; } = 100;
        public int Level { get; internal set; }
        public int Hp { get; internal set; }
        public int FusionMatter { get; internal set; }
        public int MapNumber { get; internal set; }
        public ulong ServerTime { get; internal set; }
        public byte[] RawLoadData { get; internal set; }
        public IReadOnlyList<ProtocolFrame> BootstrapPackets { get; internal set; }
    }

    public sealed class ProtocolFrame
    {
        public uint PacketType { get; }
        public byte[] Payload { get; }
        public ProtocolFrame(uint packetType, byte[] payload) { PacketType = packetType; Payload = payload; }
    }

    public enum NetworkEventKind { Packet, Disconnected }

    /// <summary>Authoritative GOTO position in protocol centimetres (Z up); preserves the current heading and map.</summary>
    public sealed class PlayerPosition
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        internal PlayerPosition(int x, int y, int z) { X = x; Y = y; Z = z; }
    }

    public sealed class NetworkEvent
    {
        public NetworkEventKind Kind { get; }
        public bool IsShard { get; }
        public ProtocolFrame Frame { get; }
        public string Message { get; }
        public PlayerPosition PlayerPosition { get; }
        internal long ShardGeneration { get; }
        internal NetworkEvent(bool shard, ProtocolFrame frame, long generation = 0)
        {
            Kind = NetworkEventKind.Packet; IsShard = shard; Frame = frame; Message = "";
            ShardGeneration = generation;
            if (shard && frame.PacketType == PacketId.GotoSuccess)
                PlayerPosition = Protocol0104.DecodePlayerPosition(frame.Payload);
        }
        internal NetworkEvent(bool shard, Exception error, long generation = 0)
        { Kind = NetworkEventKind.Disconnected; IsShard = shard; Message = error.Message; ShardGeneration = generation; }
    }

    public sealed class ProtocolException : IOException
    {
        public ProtocolException(string message) : base(message) { }
    }

    public sealed class ServerRejectedException : IOException
    {
        public string Phase { get; }
        public int ErrorCode { get; }
        public ServerRejectedException(string phase, int code) : base($"{phase}: server error {code}.")
        { Phase = phase; ErrorCode = code; }
    }
}
