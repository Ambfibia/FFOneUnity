using System;
using System.Collections.Generic;
using System.Linq;
using FFOne.Network;
using UnityEngine;

namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        internal GameplayUiState WithHealth(int hp, int? weaponBattery = null)
        {
            if (hp < 0 || weaponBattery < 0) throw new ProtocolException("Invalid combat health/battery.");
            var next = (GameplayUiState)MemberwiseClone(); next.Hp = hp; next.HealthOrigin = GameplayHealthOrigin.ServerUpdate;
            if (weaponBattery.HasValue) next.WeaponBattery = weaponBattery.Value;
            return next;
        }
        internal GameplayUiState WithOperatorValue(byte[] payload)
        {
            Protocol0104.RequireSize(payload, 12);
            if (Protocol0104.Read32(payload, 0) != PlayerId) return this;
            int kind = Protocol0104.Read32(payload, 4), value = Protocol0104.Read32(payload, 8);
            if (kind == 1) return WithHealth(value);
            if (kind == 5) return WithMailBalance(value);
            if (kind != 4) return this;
            var next = (GameplayUiState)MemberwiseClone();
            next.FusionMatter = value;
            return next;
        }

        internal GameplayUiState WithServerLocation(int map, int x, int y, int z)
        {
            var next = WithPosition(new Vector3(x, z, y) / 100f); next.MapNumber = map; return next;
        }
        internal GameplayUiState WithNanos(IEnumerable<GameplayNanoState> changes, int[] slots = null, int? active = null, int? battery = null)
        {
            var bank = Nanos.ToArray();
            foreach (var nano in changes)
            {
                if (nano.Id == 0) continue;
                int index = Array.FindIndex(bank, n => n.Id == nano.Id);
                if (index < 0)
                {
                    // IDs are not bank indices. Native Nano additions can extend the original 37-entry bank.
                    index = bank.Length; Array.Resize(ref bank, index + 1);
                }
                bank[index] = new GameplayNanoState(index, nano.Id, nano.SkillId, nano.Stamina);
            }
            var next = (GameplayUiState)MemberwiseClone(); next.Nanos = Array.AsReadOnly(bank);
            if (slots != null) next.NanoSlots = Array.AsReadOnly((int[])slots.Clone());
            if (active.HasValue) next.ActiveNanoSlot = active.Value;
            if (battery.HasValue) next.NanoBattery = battery.Value;
            return next;
        }
        internal GameplayUiState WithRegen(byte[] p)
        {
            var carried = new GameplayNanoState[3]; var slots = new int[3];
            for (int i = 0; i < 3; i++) { carried[i] = GameplayUiSession.LiveNano(p, 22 + 6 * i); slots[i] = carried[i].Id; }
            int active = Protocol0104.Read16(p, 20); GameplayUiSession.ValidateActive(active, slots);
            int hp = ProtocolGameplay.I(p, 0), fm = ProtocolGameplay.I(p, 44);
            if (hp < 0 || fm < 0) throw new ProtocolException("Invalid revive post-state.");
            var next = WithNanos(carried, slots, active).WithHealth(hp);
            next.MapNumber = ProtocolGameplay.I(p, 4);
            next.WorldPosition = new Vector3(ProtocolGameplay.I(p, 8), ProtocolGameplay.I(p, 16), ProtocolGameplay.I(p, 12)) / 100f;
            next.FusionMatter = fm; return next;
        }
        internal GameplayUiState WithReward(byte[] p)
        {
            if (p == null || p.Length < 36) throw new ProtocolException("Missing reward header.");
            int count = (sbyte)p[16];
            if (count < 0 || p.Length != 36 + count * 20) throw new ProtocolException("Invalid reward trailer count.");
            var equipment = Equipment.ToArray(); var inventory = Inventory.ToArray(); var quest = QuestInventory.ToArray();
            for (int i = 0; i < count; i++)
            {
                int o = 36 + i * 20, location = ProtocolGameplay.I(p, o + 12), slot = ProtocolGameplay.I(p, o + 16);
                GameplayItemState[] destination = location == 0 ? equipment : location == 1 ? inventory : location == 2 ? quest : null;
                if (destination == null || slot < 0 || slot >= destination.Length) throw new ProtocolException("Reward slot is outside its inventory.");
                int type = Protocol0104.Read16(p, o), id = Protocol0104.Read16(p, o + 2);
                if (type < 0 || id < 0 || (location == 0 && !ProtocolInventory.Fits(ItemContainer.Equipment, slot, type, id)))
                    throw new ProtocolException("Invalid reward item identity/type.");
                destination[slot] = new GameplayItemState(slot, type, id, ProtocolGameplay.I(p, o + 4), ProtocolGameplay.I(p, o + 8));
            }
            var next = (GameplayUiState)MemberwiseClone();
            next.Taros = ProtocolGameplay.I(p, 0); next.FusionMatter = ProtocolGameplay.I(p, 4);
            next.NanoBattery = ProtocolGameplay.I(p, 8); next.WeaponBattery = ProtocolGameplay.I(p, 12);
            if (next.Taros < 0 || next.FusionMatter < 0 || next.NanoBattery < 0 || next.WeaponBattery < 0) throw new ProtocolException("Negative reward post-state.");
            next.Equipment = Array.AsReadOnly(equipment); next.Inventory = Array.AsReadOnly(inventory); next.QuestInventory = Array.AsReadOnly(quest);
            return next;
        }
    }

    public sealed partial class GameplayUiSession
    {
        bool ApplyGameplayFrame(ProtocolFrame frame)
        {
            if (frame == null) return false;
            if (ApplyBuffFrame(frame)) return true;
            var p = frame.Payload;
            if (ProtocolProjectiles.TryDecode(frame, out var projectile) && projectile.Phase == ProjectilePhase.Fire && projectile.WeaponBattery.HasValue)
            { Publish(Snapshot.WithHealth(Snapshot.Hp, projectile.WeaponBattery)); return true; }
            if (ProtocolNpcSkill.TryDecode(frame, out var npcEffects))
            {
                var next = Snapshot;
                foreach (var effect in npcEffects)
                    if (effect.Kind == WorldEntityKind.Player && effect.Id == next.PlayerId)
                    {
                        if (effect.Hp.HasValue && !effect.Resurrected) next = next.WithHealth(effect.Hp.Value);
                        next = next.WithBuffEffect(effect);
                        if (effect.NanoId > 0 && effect.NanoStamina.HasValue)
                        {
                            var nano = next.Nanos.FirstOrDefault(n => n.Id == effect.NanoId);
                            if (nano.IsOwned) next = next.WithNanos(new[] { new GameplayNanoState(nano.BankIndex, nano.Id, nano.SkillId, effect.NanoStamina.Value) }, active: effect.NanoDeactivated ? -1 : null);
                        }
                        else if (effect.NanoDeactivated) next = next.WithNanos(Array.Empty<GameplayNanoState>(), active: -1);
                        if (effect.Map.HasValue) next = next.WithServerLocation(effect.Map.Value, effect.X, effect.Y, effect.Z);
                    }
                Publish(next); return true;
            }
            if (ProtocolNanoSkill.TryDecode(frame, out var skill))
            {
                var next = Snapshot;
                if (skill.CasterId == next.PlayerId)
                    next = next.WithNanos(new[] { new GameplayNanoState(-1, skill.NanoId, skill.SkillId, skill.Stamina) }, active: skill.Deactivated ? -1 : null);
                foreach (var effect in skill.Effects)
                    if (effect.Kind == WorldEntityKind.Player && effect.Id == next.PlayerId)
                    {
                        if (effect.Hp.HasValue && !effect.Resurrected) next = next.WithHealth(effect.Hp.Value);
                        next = next.WithBuffEffect(effect);
                        if (effect.NanoId > 0 && effect.NanoStamina.HasValue)
                        {
                            var nano = next.Nanos.FirstOrDefault(n => n.Id == effect.NanoId);
                            if (nano.IsOwned) next = next.WithNanos(new[] { new GameplayNanoState(nano.BankIndex, nano.Id, nano.SkillId, effect.NanoStamina.Value) });
                        }
                        if (effect.Map.HasValue) next = next.WithServerLocation(effect.Map.Value, effect.X, effect.Y, effect.Z);
                    }
                Publish(next); return true;
            }
            if (ProtocolGameplay.TryCombat(frame, out var combat))
            {
                int hp = Snapshot.Hp;
                foreach (var hit in combat.Hits) if (hit.Kind == WorldEntityKind.Player && hit.Id == Snapshot.PlayerId) hp = hit.Hp;
                Publish(Snapshot.WithHealth(hp, combat.WeaponBattery)); return true;
            }
            switch (frame.PacketType)
            {
                case ProtocolGameplay.Tick:
                    Protocol0104.RequireSize(p, 32);
                    var nanos = new GameplayNanoState[3]; var slots = new int[3];
                    for (int i = 0; i < 3; i++) { nanos[i] = LiveNano(p, 4 + i * 6); slots[i] = nanos[i].Id; }
                    int active = Snapshot.ActiveNanoSlot;
                    if (active >= 0 && (slots[active] == 0 || nanos[active].Stamina == 0)) active = -1;
                    int battery = ProtocolGameplay.I(p, 24); if (battery < 0) throw new ProtocolException("Negative Nano battery.");
                    Publish(Snapshot.WithNanos(nanos, slots, active, battery).WithHealth(ProtocolGameplay.I(p, 0))); return true;
                case ProtocolGameplay.NanoActiveSuccess:
                    Protocol0104.RequireSize(p, 8); int selected = Protocol0104.Read16(p, 0);
                    ValidateActive(selected, Snapshot.NanoSlots);
                    Publish(Snapshot.WithNanos(Array.Empty<GameplayNanoState>(), active: selected)); return true;
                case ProtocolGameplay.NanoEquipSuccess:
                    Protocol0104.RequireSize(p, 8); int slot = Protocol0104.Read16(p, 2), id = Protocol0104.Read16(p, 0);
                    if (slot < 0 || slot > 2 || id <= 0) throw new ProtocolException("Invalid equipped Nano slot/ID.");
                    var equipped = Snapshot.NanoSlots.ToArray();
                    // This acknowledgement describes only one slot; never infer changes to other slots.
                    equipped[slot] = id;
                    Publish(Snapshot.WithNanos(Array.Empty<GameplayNanoState>(), equipped,
                        ProtocolGameplay.I(p, 4) != 0 ? -1 : Snapshot.ActiveNanoSlot)); return true;
                case ProtocolGameplay.NanoUnequipSuccess:
                    Protocol0104.RequireSize(p, 8); int removed = Protocol0104.Read16(p, 0);
                    if (removed < 0 || removed > 2) throw new ProtocolException("Invalid unequipped Nano slot.");
                    var remaining = Snapshot.NanoSlots.ToArray(); remaining[removed] = 0;
                    Publish(Snapshot.WithNanos(Array.Empty<GameplayNanoState>(), remaining,
                        ProtocolGameplay.I(p, 4) != 0 ? -1 : Snapshot.ActiveNanoSlot)); return true;
                case ProtocolGameplay.ChargeNano:
                    Protocol0104.RequireSize(p, 8); int nanoId = Protocol0104.Read16(p, 4), stamina = Protocol0104.Read16(p, 6);
                    var known = Snapshot.Nanos.FirstOrDefault(n => n.Id == nanoId);
                    if (!known.IsOwned || stamina < 0 || ProtocolGameplay.I(p, 0) < 0) throw new ProtocolException("Invalid Nano recharge result.");
                    Publish(Snapshot.WithNanos(new[] { new GameplayNanoState(known.BankIndex, nanoId, known.SkillId, stamina) }, battery: ProtocolGameplay.I(p, 0))); return true;
                case ProtocolGameplay.RegenSuccess:
                    Protocol0104.RequireSize(p, 48); Publish(Snapshot.WithRegen(p)); return true;
                case ProtocolGameplay.Reward:
                {
                    var previous = Snapshot;
                    var reward = previous.WithReward(p);
                    Publish(reward);
                    RewardReceived?.Invoke(previous, reward);
                    ReportQuestLoot(previous, p);
                    return true;
                }
                case ProtocolGameplay.NanoCreate:
                    Protocol0104.RequireSize(p, 28);
                    Publish(Snapshot.WithNanoCreation(p, levelLimits(Protocol0104.Read16(p, 26)))); return true;
                default: return ApplyMissionFrame(frame);
            }
        }
        void ReportQuestLoot(GameplayUiState previous, byte[] reward)
        {
            int npc = ProtocolGameplay.I(reward, 28);
            if (npc <= 0) return;
            bool found = false;
            for (int i = 0; i < reward[16]; i++)
            {
                int row = 36 + i * 20;
                if (Protocol0104.Read16(reward, row) != 8 || ProtocolGameplay.I(reward, row + 12) != 2) continue;
                int item = Protocol0104.Read16(reward, row + 2);
                if (item <= 0) continue;
                found = true;
                QuestLootReceived?.Invoke(npc, item, true);
            }
            if (found) return;
            int task = ProtocolGameplay.I(reward, 32);
            if (task <= 0 || !previous.RunningQuests.Any(q => q.TaskId == task)) return;
            var rule = FFOne.World.WorldMissionCatalog.Load().Find(task);
            if (rule == null) return;
            for (int i = 0; i < rule.KillTypes.Length; i++)
            {
                if (rule.KillTypes[i] != npc || rule.KillCounts[i] != 0 || rule.ItemIds[i] <= 0) continue;
                if (previous.QuestInventory.Any(v => v.ItemId == rule.ItemIds[i] && v.Quantity >= rule.ItemCounts[i])) continue;
                QuestLootReceived?.Invoke(npc, rule.ItemIds[i], false);
                return;
            }
        }

        internal static GameplayNanoState LiveNano(byte[] p, int o)
        {
            int id = Protocol0104.Read16(p, o), skill = Protocol0104.Read16(p, o + 2), stamina = Protocol0104.Read16(p, o + 4);
            if (id < 0 || skill < 0 || stamina < 0) throw new ProtocolException("Negative Nano post-state.");
            return new GameplayNanoState(-1, id, skill, stamina);
        }
        internal static void ValidateActive(int slot, IReadOnlyList<int> carried)
        { if (slot < -1 || slot > 2 || (slot >= 0 && carried[slot] == 0)) throw new ProtocolException("Invalid active Nano slot."); }
    }
}
