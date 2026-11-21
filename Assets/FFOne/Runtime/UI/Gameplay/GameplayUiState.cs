using System;
using System.Collections.Generic;
using UnityEngine;
using FFOne.Network;

namespace FFOne.UI
{
    [Flags]
    public enum GameplayUiCapabilities
    {
        None = 0,
        ReadEntryEquipment = 1,
        ReadEntryInventory = 2,
        ReadEntryNanos = 4,
        ReadEntryMissions = 8
    }

    public enum GameplayHealthOrigin { Unknown, ServerEntry, LocalTutorial, ServerUpdate }

    public readonly struct GameplayLevelLimits
    {
        public int MaxHp { get; }
        public int? MaxFusionMatter { get; }
        public GameplayLevelLimits(int maxHp, int? maxFusionMatter)
        {
            if (maxHp <= 0 || maxFusionMatter < 0) throw new ArgumentOutOfRangeException(nameof(maxHp));
            MaxHp = maxHp; MaxFusionMatter = maxFusionMatter > 0 ? maxFusionMatter : null;
        }
    }

    /// <summary>Slot and wire identity are retained, including empty slots. Option is not universally a stack count.</summary>
    public readonly struct GameplayItemState
    {
        public int Slot { get; }
        public int ItemType { get; }
        public int ItemId { get; }
        public int Option { get; }
        public int TimeLimit { get; }
        readonly bool metadataIncomplete;
        public bool MetadataComplete => !metadataIncomplete;
        public int? KnownTimeLimit => MetadataComplete ? TimeLimit : (int?)null;
        // RustyFusion sItemBase packs the count in the low 16 bits and appearance ID in the high 16.
        public int Quantity => IsEmpty ? 0 : Option & 0xffff;
        public bool IsEmpty => ItemId == 0;
        public GameplayItemState(int slot, int itemType, int itemId, int option, int timeLimit, bool metadataComplete = true)
        { Slot = slot; ItemType = itemType; ItemId = itemId; Option = option; TimeLimit = timeLimit; metadataIncomplete = !metadataComplete; }
    }

    public readonly struct GameplayNanoState
    {
        public int BankIndex { get; }
        public int Id { get; }
        public int SkillId { get; }
        public int Stamina { get; }
        public bool IsOwned => Id > 0;
        public GameplayNanoState(int bankIndex, int id, int skillId, int stamina)
        { BankIndex = bankIndex; Id = id; SkillId = skillId; Stamina = stamina; }
    }

    /// <summary>Objective counts are the server's remaining counts, not the number already completed.</summary>
    public sealed class GameplayQuestState
    {
        public int Slot { get; }
        public int TaskId { get; }
        public int GrantedTimeSeconds { get; }
        public IReadOnlyList<int> KillNpcIds { get; }
        public IReadOnlyList<int> RemainingKillCounts { get; }
        public IReadOnlyList<int> NeededItemIds { get; }
        public IReadOnlyList<int> NeededItemCounts { get; }
        public GameplayQuestState(int slot, int taskId, int[] killNpcIds, int[] remainingKillCounts, int[] neededItemIds, int[] neededItemCounts, int grantedTimeSeconds = 0)
        {
            Slot = slot; TaskId = taskId; GrantedTimeSeconds = grantedTimeSeconds;
            KillNpcIds = Freeze(killNpcIds); RemainingKillCounts = Freeze(remainingKillCounts);
            NeededItemIds = Freeze(neededItemIds); NeededItemCounts = Freeze(neededItemCounts);
        }
        static IReadOnlyList<int> Freeze(int[] values)
        {
            if (values == null || values.Length != 3) throw new ArgumentException("A running quest has three objective slots.");
            return Array.AsReadOnly((int[])values.Clone());
        }
    }

    /// <summary>
    /// Immutable UI snapshot. Entry inventory/quests are explicitly snapshots, not live gameplay reducers.
    /// Unknown maximum HP stays unknown; the entry packet contains current HP only.
    /// </summary>
    public sealed partial class GameplayUiState
    {
        public static GameplayUiState Empty { get; } = new GameplayUiState();
        public bool IsActive { get; private set; }
        public bool IsTutorial { get; private set; }
        public int PlayerId { get; private set; }
        public long CharacterUid { get; private set; }
        public string DisplayName { get; private set; } = "";
        public int Level { get; private set; }
        public int Gender { get; private set; }
        public int Hp { get; private set; }
        public int? MaxHp { get; private set; }
        public float? HealthFraction => MaxHp.HasValue && MaxHp.Value > 0 ? Mathf.Clamp01((float)Hp / MaxHp.Value) : (float?)null;
        public GameplayHealthOrigin HealthOrigin { get; private set; }
        public int FusionMatter { get; private set; }
        public int? MaxFusionMatter { get; private set; }
        public float? FusionMatterFraction => MaxFusionMatter.HasValue && MaxFusionMatter.Value > 0 ? Mathf.Clamp01((float)FusionMatter / MaxFusionMatter.Value) : (float?)null;
        public int Taros { get; private set; }
        public int WeaponBattery { get; private set; }
        public int NanoBattery { get; private set; }
        public int MapNumber { get; private set; }
        public Vector3 WorldPosition { get; private set; }
        public GameplayUiCapabilities Capabilities { get; private set; }
        public IReadOnlyList<GameplayItemState> Equipment { get; private set; } = Array.Empty<GameplayItemState>();
        public IReadOnlyList<GameplayItemState> Inventory { get; private set; } = Array.Empty<GameplayItemState>();
        public IReadOnlyList<GameplayItemState> QuestInventory { get; private set; } = Array.Empty<GameplayItemState>();
        public IReadOnlyList<GameplayNanoState> Nanos { get; private set; } = Array.Empty<GameplayNanoState>();
        public IReadOnlyList<int> NanoSlots { get; private set; } = Array.Empty<int>();
        public int ActiveNanoSlot { get; private set; } = -1;
        public bool NanoBankComplete { get; private set; }
        public IReadOnlyList<GameplayQuestState> RunningQuests { get; private set; } = Array.Empty<GameplayQuestState>();
        public int CurrentMissionId { get; private set; }

        private GameplayUiState() { }

        internal GameplayUiState(int playerId, long characterUid, string displayName, int level, int hp,
            int fusionMatter, GameplayLevelLimits? limits, int taros, int weaponBattery, int nanoBattery, int mapNumber, Vector3 worldPosition,
            GameplayItemState[] equipment, GameplayItemState[] inventory, GameplayItemState[] questInventory,
            GameplayNanoState[] nanos, int[] nanoSlots, int activeNanoSlot, bool nanoBankComplete,
            GameplayQuestState[] quests, int currentMissionId, bool isTutorial = false, int gender = 0)
        {
            IsTutorial = isTutorial; Gender = gender;
            IsActive = true; PlayerId = playerId; CharacterUid = characterUid; DisplayName = displayName;
            Level = level; Hp = hp; HealthOrigin = GameplayHealthOrigin.ServerEntry;
            MaxHp = limits?.MaxHp; MaxFusionMatter = limits?.MaxFusionMatter;
            FusionMatter = fusionMatter; Taros = taros; WeaponBattery = weaponBattery; NanoBattery = nanoBattery;
            MapNumber = mapNumber; WorldPosition = worldPosition;
            Equipment = Array.AsReadOnly((GameplayItemState[])equipment.Clone());
            Inventory = Array.AsReadOnly((GameplayItemState[])inventory.Clone());
            QuestInventory = Array.AsReadOnly((GameplayItemState[])questInventory.Clone());
            Nanos = Array.AsReadOnly((GameplayNanoState[])nanos.Clone());
            NanoSlots = Array.AsReadOnly((int[])nanoSlots.Clone());
            ActiveNanoSlot = activeNanoSlot; NanoBankComplete = nanoBankComplete;
            RunningQuests = Array.AsReadOnly((GameplayQuestState[])quests.Clone()); CurrentMissionId = currentMissionId;
            Capabilities = GameplayUiCapabilities.ReadEntryEquipment | GameplayUiCapabilities.ReadEntryInventory |
                GameplayUiCapabilities.ReadEntryNanos | GameplayUiCapabilities.ReadEntryMissions;
        }

        internal GameplayUiState WithTutorialHealth(int hp, int maxHp)
        {
            var next = (GameplayUiState)MemberwiseClone();
            next.Hp = hp; next.MaxHp = maxHp; next.HealthOrigin = GameplayHealthOrigin.LocalTutorial;
            return next;
        }

        internal GameplayUiState WithPosition(Vector3 position)
        {
            var next = (GameplayUiState)MemberwiseClone(); next.WorldPosition = position; return next;
        }

        internal GameplayUiState WithItems(InventoryUpdate update)
        {
            var next = (GameplayUiState)MemberwiseClone();
            var equipment = new GameplayItemState[Equipment.Count]; var inventory = new GameplayItemState[Inventory.Count];
            for (int i = 0; i < equipment.Length; i++) equipment[i] = Equipment[i];
            for (int i = 0; i < inventory.Length; i++) inventory[i] = Inventory[i];
            var bank = new List<GameplayItemState>(Bank).ToArray();
            void Set(ItemContainer container, int slot, EquipmentItem item)
            {
                var value = new GameplayItemState(slot, item.ItemType, item.ItemId, item.Option, item.TimeLimit);
                if (container == ItemContainer.Equipment) equipment[slot] = value;
                else if (container == ItemContainer.Inventory) inventory[slot] = value;
                else if (BankNumber >= 0 && bank.Length == ProtocolServices.BankCapacity) bank[slot] = value;
                else throw new ProtocolException("Bank item update arrived without an open bank snapshot.");
            }
            Set(update.FromContainer, update.FromSlot, update.FromItem);
            if (update.Kind == InventoryUpdateKind.Move) Set(update.ToContainer, update.ToSlot, update.ToItem);
            next.Equipment = Array.AsReadOnly(equipment); next.Inventory = Array.AsReadOnly(inventory);
            next.Bank = Array.AsReadOnly(bank);
            return next;
        }

        internal GameplayUiState WithTradeSettlement(GameplayItemState[] inventory, int taros)
        {
            var next = (GameplayUiState)MemberwiseClone();
            next.Inventory = Array.AsReadOnly((GameplayItemState[])inventory.Clone()); next.Taros = taros;
            return next;
        }
    }
}
