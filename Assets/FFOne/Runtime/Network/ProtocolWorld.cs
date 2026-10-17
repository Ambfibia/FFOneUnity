using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    public enum WorldEntityKind { Player, Npc, Transportation }
    public enum WorldUpdateKind { Enter, Leave, Move, Equipment, Health, Nano, Settings, State, Condition }

    public sealed class WorldEntity
    {
        public WorldEntityKind Kind { get; internal set; }
        public int Id { get; internal set; }
        public int NpcType { get; internal set; }
        public int Hp { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        public int Angle { get; internal set; }
        public int Speed { get; internal set; }
        public int ConditionFlags { get; internal set; }
        public int StateFlags { get; internal set; }
        public int RideState { get; internal set; }
        public int TransportationType { get; internal set; }
        public int TransportId { get; internal set; }
        public int LocalX { get; internal set; }
        public int LocalY { get; internal set; }
        public int LocalZ { get; internal set; }
        public bool Jumping { get; internal set; }
        public ulong ServerTime { get; internal set; }
        public CharacterInfo Character { get; internal set; }
        public int NanoId { get; internal set; }
        public int NanoSkill { get; internal set; }
        public int NanoStamina { get; internal set; }
        public bool Hidden { get; internal set; }
        public bool Interactable { get; internal set; } = true;
        public bool MapIcon { get; internal set; }
        public float Scale { get; internal set; } = 1;
        internal WorldEntity Copy() => (WorldEntity)MemberwiseClone();
    }

    public sealed class WorldUpdate
    {
        public WorldUpdateKind Kind { get; internal set; }
        public WorldEntity Entity { get; internal set; }
        public int EquipmentSlot { get; internal set; }
        public EquipmentItem EquipmentItem { get; internal set; }
        public bool HasHeading { get; internal set; }
    }

    /// <summary>RustyFusion/ffproto 0104 appearance and movement ABI (pack 4). Decode whole pages before applying them.</summary>
    public static class ProtocolWorld
    {
        public const uint PlayerEnter = 0x31000003, PlayerLeave = 0x31000006, PlayersAround = 0x31000007;
        public const uint PlayerMove = 0x31000008, PlayerStop = 0x31000009, PlayerJump = 0x3100000a;
        public const uint NpcEnter = 0x3100000b, NpcLeave = 0x3100000c, NpcMove = 0x3100000d, NpcNew = 0x3100000e;
        public const uint NpcsAround = 0x3100000f, PlayersLeave = 0x31000010, NpcsLeave = 0x31000011;

        public static bool TryDecode(ProtocolFrame frame, out IReadOnlyList<WorldUpdate> updates)
        {
            updates = null;
            if (frame == null) return false;
            if (ProtocolSlider.TryDecode(frame, out updates)) return true;
            var p = frame.Payload;
            var result = new List<WorldUpdate>();
            switch (frame.PacketType)
            {
                case PlayerEnter: Size(p, 232); result.Add(Enter(Player(p, 0))); break;
                case NpcEnter: case NpcNew: Size(p, 36); result.Add(Enter(Npc(p, 0))); break;
                case PlayersAround: Page(p, 232, o => result.Add(Enter(Player(p, o)))); break;
                case NpcsAround: Page(p, 36, o => result.Add(Enter(Npc(p, o)))); break;
                case PlayerLeave: Size(p, 8); result.Add(Leave(WorldEntityKind.Player, I(p, 0))); break;
                case NpcLeave: Size(p, 4); result.Add(Leave(WorldEntityKind.Npc, I(p, 0))); break;
                case PlayersLeave: Page(p, 4, o => result.Add(Leave(WorldEntityKind.Player, I(p, o)))); break;
                case NpcsLeave: Page(p, 4, o => result.Add(Leave(WorldEntityKind.Npc, I(p, o)))); break;
                case PlayerMove: case PlayerJump: case PlayerStop:
                    bool stop = frame.PacketType == PlayerStop;
                    Size(p, stop ? 32 : 56);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Move, HasHeading = !stop,
                        Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(I(p, stop ? 20 : 44)),
                            X = I(p, 8), Y = I(p, 12), Z = I(p, 16), Angle = stop ? 0 : I(p, 32),
                            Speed = stop ? 0 : I(p, 40), Jumping = frame.PacketType == PlayerJump,
                            ServerTime = unchecked((ulong)Protocol0104.Read64(p, stop ? 24 : 48)) } });
                    break;
                case NpcMove:
                    Size(p, 24);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Move,
                        Entity = new WorldEntity { Kind = WorldEntityKind.Npc, Id = Id(I(p, 0)),
                            X = I(p, 4), Y = I(p, 8), Z = I(p, 12), Speed = Math.Max(0, I(p, 16)) } });
                    break;
                case ProtocolInventory.EquipmentChanged:
                    if (!ProtocolInventory.TryDecode(frame, out var equipment)) return false;
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Equipment,
                        Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(equipment.PlayerId) },
                        EquipmentSlot = equipment.FromSlot, EquipmentItem = equipment.FromItem });
                    break;
                case ProtocolVehicle.StateChanged:
                    ProtocolVehicle.TryDecode(frame, out var vehicle);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.State, Entity = new WorldEntity { Kind = WorldEntityKind.Player,
                        Id = vehicle.PlayerId, StateFlags = vehicle.StateFlags } }); break;
                case ProtocolGameplay.NanoActiveBroadcast:
                    Size(p, 20);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Nano, Entity = new WorldEntity { Kind = WorldEntityKind.Player,
                        Id = Id(I(p, 0)), NanoId = Protocol0104.Read16(p, 4), NanoSkill = Protocol0104.Read16(p, 6), NanoStamina = Protocol0104.Read16(p, 8) } });
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Condition, Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(I(p, 0)), ConditionFlags = I(p, 12) } });
                    break;
                case 0x3100013c: // Native NPC editor settings, sent immediately after interest entry
                    Size(p, 12); int flags = I(p, 4); float scale = BitConverter.ToSingle(p, 8);
                    if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) throw new ProtocolException("Invalid server NPC scale.");
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Settings, Entity = new WorldEntity { Kind = WorldEntityKind.Npc,
                        Id = Id(I(p, 0)), Hidden = (flags & 1) != 0, MapIcon = (flags & 2) != 0, Interactable = (flags & 4) == 0, Scale = scale } });
                    break;
                case 0x31000099: // PC_REGEN: another player's complete revive position
                    Size(p, 36);
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Move, HasHeading = true,
                        Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(I(p, 0)), Hp = I(p, 4),
                            X = I(p, 8), Y = I(p, 12), Z = I(p, 16), Angle = I(p, 20) } });
                    result.Add(new WorldUpdate { Kind = WorldUpdateKind.Health,
                        Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(I(p, 0)), Hp = I(p, 4) } });
                    break;
                default: return false;
            }
            updates = result.AsReadOnly(); return true;
        }

        static WorldUpdate Enter(WorldEntity entity) => new WorldUpdate { Kind = WorldUpdateKind.Enter, Entity = entity };
        static WorldUpdate Leave(WorldEntityKind kind, int id) => new WorldUpdate { Kind = WorldUpdateKind.Leave,
            Entity = new WorldEntity { Kind = kind, Id = Id(id) } };
        static WorldEntity Npc(byte[] p, int o) => new WorldEntity { Kind = WorldEntityKind.Npc, Id = Id(I(p, o)),
            NpcType = Id(I(p, o + 4)), Hp = I(p, o + 8), ConditionFlags = I(p, o + 12), X = I(p, o + 16), Y = I(p, o + 20), Z = I(p, o + 24), Angle = I(p, o + 28) };
        static WorldEntity Player(byte[] p, int o)
        {
            var style = Protocol0104.ReadStyle(p, o + 4, o + 84);
            style.AppearanceComplete = style.TutorialComplete = true;
            var character = new CharacterInfo { Uid = Protocol0104.Read64(p, o + 4), Style = style,
                FirstName = Protocol0104.ReadUtf16(p, o + 14, 9), LastName = Protocol0104.ReadUtf16(p, o + 32, 17),
                Level = Protocol0104.Read16(p, o + 86), Equipment = Protocol0104.ReadEquipment(p, o + 112) };
            return new WorldEntity { Kind = WorldEntityKind.Player, Id = Id(I(p, o)), Character = character,
                Hp = I(p, o + 88), ConditionFlags = I(p, o + 92), X = I(p, o + 96), Y = I(p, o + 100), Z = I(p, o + 104), Angle = I(p, o + 108),
                StateFlags = p[o + 84], RideState = I(p, o + 228),
                NanoId = Protocol0104.Read16(p, o + 220), NanoSkill = Protocol0104.Read16(p, o + 222), NanoStamina = Protocol0104.Read16(p, o + 224) };
        }
        static void Page(byte[] p, int stride, Action<int> decode)
        {
            if (p == null || p.Length < 4) throw new ProtocolException("Missing entity page count.");
            int count = I(p, 0);
            if (count < 0 || count > (Protocol0104.MaximumBodySize - 8) / stride || p.Length != 4 + count * stride)
                throw new ProtocolException("Invalid entity page length/count.");
            for (int i = 0; i < count; i++) decode(4 + i * stride);
        }
        static int Id(int id) { if (id <= 0) throw new ProtocolException("Entity identity must be positive."); return id; }
        static int I(byte[] p, int o) => Protocol0104.Read32(p, o);
        static void Size(byte[] p, int size) => Protocol0104.RequireSize(p, size);
    }
}
