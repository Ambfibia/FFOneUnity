using FFOne.Network;
namespace FFOne.UI
{
    public sealed partial class GameplayUiState
    {
        public int ConditionFlags { get; private set; }
        public bool HasBuff(int id) => id >= 1 && id <= 25 && (ConditionFlags & (1 << (id - 1))) != 0;
        internal GameplayUiState WithUsedItem(ItemUseUpdate update)
        {
            var next = (GameplayUiState)MemberwiseClone();
            var inventory = new GameplayItemState[Inventory.Count];
            for (int i = 0; i < inventory.Length; i++) inventory[i] = Inventory[i];
            var item = update.Remaining;
            inventory[update.Slot] = new GameplayItemState(update.Slot, item.Type, item.Id, item.Option, item.TimeLimit);
            next.Inventory = System.Array.AsReadOnly(inventory); return next;
        }
        internal GameplayUiState WithBuffEffect(NanoSkillEffect effect)
        {
            var next = this;
            if (effect.WeaponBattery.HasValue) next = next.WithHealth(next.Hp, effect.WeaponBattery);
            if (effect.NanoBattery.HasValue) next = next.WithNanos(System.Array.Empty<GameplayNanoState>(), battery: effect.NanoBattery);
            if (effect.NanoStamina.HasValue)
            {
                int id = effect.NanoId ?? (next.ActiveNanoSlot >= 0 ? next.NanoSlots[next.ActiveNanoSlot] : 0);
                foreach (var nano in next.Nanos) if (nano.Id == id && id > 0)
                { next = next.WithNanos(new[] { new GameplayNanoState(nano.BankIndex, nano.Id, nano.SkillId, effect.NanoStamina.Value) }, active: effect.NanoDeactivated ? -1 : null); break; }
            }
            if (effect.Condition.HasValue) next = next.WithCondition(effect.Condition.Value);
            return next;
        }
        internal GameplayUiState WithCondition(int flags)
        { var next = (GameplayUiState)MemberwiseClone(); next.ConditionFlags = flags; return next; }
    }
    public sealed partial class GameplayUiSession
    {
        bool ApplyBuffFrame(ProtocolFrame frame)
        {
            if (ProtocolItemUse.TryDecode(frame, out var item))
            {
                if (item.Failed) return true;
                var usedState = Snapshot;
                if (item.Own && item.PlayerId == usedState.PlayerId) usedState = usedState.WithUsedItem(item);
                foreach (var effect in item.Effects)
                    if (effect.Kind == WorldEntityKind.Player && effect.Id == usedState.PlayerId)
                    { usedState = usedState.WithBuffEffect(effect); if (effect.Hp.HasValue) usedState = usedState.WithHealth(effect.Hp.Value); }
                Publish(usedState); return true;
            }
            if (ProtocolBuff.TryUpdate(frame, out var buff)) { Publish(Snapshot.WithCondition(buff.Condition)); return true; }
            if (ProtocolBuff.TryNanoCondition(frame, Snapshot.PlayerId, out int nanoCondition))
            { Publish(Snapshot.WithCondition(nanoCondition)); return true; }
            if (!ProtocolBuff.TryEffects(frame, out var effects)) return false;
            var next = Snapshot;
            foreach (var effect in effects)
                if (effect.Kind == WorldEntityKind.Player && effect.Id == next.PlayerId)
                {
                    if (effect.Hp.HasValue) next = next.WithHealth(effect.Hp.Value);
                    if (effect.Condition.HasValue) next = next.WithCondition(effect.Condition.Value);
                    if (effect.NanoStamina.HasValue && next.ActiveNanoSlot >= 0)
                    {
                        int id = next.NanoSlots[next.ActiveNanoSlot];
                        foreach (var nano in next.Nanos) if (nano.Id == id)
                        { next = next.WithNanos(new[] { new GameplayNanoState(nano.BankIndex, nano.Id, nano.SkillId, effect.NanoStamina.Value) }, active: effect.NanoDeactivated ? -1 : null); break; }
                    }
                }
            Publish(next); return true;
        }
    }
}
