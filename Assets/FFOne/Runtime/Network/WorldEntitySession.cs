using System;
using System.Collections.Generic;

namespace FFOne.Network
{
    /// <summary>One shard's interest set. Unknown movement never invents an entity; IDs are scoped by kind.</summary>
    public sealed class WorldEntitySession
    {
        readonly Dictionary<(WorldEntityKind, int), WorldEntity> entities = new();
        public int LocalPlayerId { get; }
        public IEnumerable<WorldEntity> Entities => entities.Values;
        public int Count => entities.Count;
        public event Action<WorldUpdate> Changed;
        public WorldEntitySession(int localPlayerId) { LocalPlayerId = localPlayerId; }
        public bool TryGet(WorldEntityKind kind, int id, out WorldEntity entity) => entities.TryGetValue((kind, id), out entity);
        public bool Apply(ProtocolFrame frame)
        {
            if (frame.PacketType == ProtocolTravel.FlightMove)
            {
                ProtocolTravel.TryDecode(frame, out var flight);
                if (TryGet(WorldEntityKind.Player, flight.PlayerId, out var rider))
                {
                    float dx = flight.X - rider.X, dy = flight.Y - rider.Y, dz = flight.Z - rider.Z;
                    Apply(new WorldUpdate { Kind = WorldUpdateKind.Move, Entity = new WorldEntity { Kind = WorldEntityKind.Player, Id = flight.PlayerId,
                        X = flight.X, Y = flight.Y, Z = flight.Z, Speed = (int)Math.Sqrt(dx * dx + dy * dy + dz * dz) } });
                }
                return true;
            }
            IReadOnlyList<NanoSkillEffect> effects = null;
            if (ProtocolItemUse.TryDecode(frame, out var item)) effects = item.Effects;
            else if (ProtocolBuff.TryEffects(frame, out var periodic)) effects = periodic;
            else if (ProtocolNpcSkill.TryDecode(frame, out var npc)) effects = npc;
            else if (ProtocolNanoSkill.TryDecode(frame, out var nano)) effects = nano.Effects;
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    if (effect.Condition.HasValue) SetCondition(effect.Kind, effect.Id, effect.Condition.Value);
                    if (effect.Hp.HasValue && !effect.Resurrected) SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
                }
                return true;
            }
            if (!ProtocolWorld.TryDecode(frame, out var updates)) return false;
            foreach (var update in updates) Apply(update);
            return true;
        }
        void Apply(WorldUpdate update)
        {
            var value = update.Entity;
            if (value.Kind == WorldEntityKind.Player && value.Id == LocalPlayerId) return;
            var key = (value.Kind, value.Id);
            if (update.Kind == WorldUpdateKind.Leave)
            { if (entities.Remove(key)) Changed?.Invoke(update); return; }
            if (update.Kind == WorldUpdateKind.Enter)
            { entities[key] = value; Changed?.Invoke(update); return; }
            if (!entities.TryGetValue(key, out var old)) return;
            var next = old.Copy();
            switch (update.Kind)
            {
                case WorldUpdateKind.Move:
                    if (value.ServerTime != 0 && old.ServerTime > value.ServerTime) return;
                    next.X = value.X; next.Y = value.Y; next.Z = value.Z; next.Speed = value.Speed; next.Jumping = value.Jumping;
                    next.Ziplining = value.Ziplining;
                    next.NpcRunning = value.NpcRunning;
                    next.TransportId = value.TransportId; next.LocalX = value.LocalX; next.LocalY = value.LocalY; next.LocalZ = value.LocalZ;
                    if (value.ServerTime != 0) next.ServerTime = value.ServerTime;
                    if (update.HasHeading) next.Angle = value.Angle;
                    break;
                case WorldUpdateKind.Health: next.Hp = value.Hp; break;
                case WorldUpdateKind.State: next.StateFlags = value.StateFlags; break;
                case WorldUpdateKind.Condition: next.ConditionFlags = value.ConditionFlags; break;
                case WorldUpdateKind.Nano: next.NanoId = value.NanoId; next.NanoSkill = value.NanoSkill; next.NanoStamina = value.NanoStamina; break;
                case WorldUpdateKind.Settings: next.Hidden = value.Hidden; next.Interactable = value.Interactable; next.MapIcon = value.MapIcon; next.Scale = value.Scale; break;
                case WorldUpdateKind.Appearance:
                    if (next.Character.Uid != value.Character.Uid) return;
                    var style = value.Character.Style;
                    style.AppearanceComplete = next.Character.Style.AppearanceComplete;
                    style.TutorialComplete = next.Character.Style.TutorialComplete;
                    style.Payzone = next.Character.Style.Payzone;
                    next.Character.Style = style;
                    break;
                case WorldUpdateKind.Equipment:
                    // CharacterInfo belongs to this session, never the account roster or another shard.
                    var character = next.Character;
                    character.Equipment = (EquipmentItem[])character.Equipment.Clone();
                    character.Equipment[update.EquipmentSlot] = update.EquipmentItem;
                    break;
            }
            entities[key] = next;
            Changed?.Invoke(new WorldUpdate { Kind = update.Kind, Entity = next, HasHeading = update.HasHeading,
                EquipmentSlot = update.EquipmentSlot, EquipmentItem = update.EquipmentItem });
        }
        public void SetCondition(WorldEntityKind kind, int id, int flags) => Apply(new WorldUpdate { Kind = WorldUpdateKind.Condition,
            Entity = new WorldEntity { Kind = kind, Id = id, ConditionFlags = flags } });
        public void SetHealth(WorldEntityKind kind, int id, int hp) => Apply(new WorldUpdate { Kind = WorldUpdateKind.Health,
            Entity = new WorldEntity { Kind = kind, Id = id, Hp = hp } });
        public void Clear()
        {
            var old = new List<WorldEntity>(entities.Values); entities.Clear();
            foreach (var entity in old) Changed?.Invoke(new WorldUpdate { Kind = WorldUpdateKind.Leave, Entity = entity });
        }
    }
}
