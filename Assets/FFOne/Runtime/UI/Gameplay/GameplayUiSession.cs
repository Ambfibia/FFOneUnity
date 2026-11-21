using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using CharacterInfo = FFOne.Network.CharacterInfo;

namespace FFOne.UI
{
    /// <summary>
    /// Main-thread UI adapter for the accepted server 0104 entry snapshot. No command is sent and no
    /// shared NetworkEvent queue is drained. The gameplay owner must supply subsequent state changes.
    /// </summary>
    public sealed partial class GameplayUiSession
    {
        readonly Func<int, GameplayLevelLimits?> levelLimits;
        Dictionary<int, GameplayLevelLimits> cachedLevelLimits;
        TradeUpdate appliedTradeReceipt;
        public GameplayUiState Snapshot { get; private set; } = GameplayUiState.Empty;
        public event Action<GameplayUiState> Changed;
        internal event Action<GameplayUiState, GameplayUiState> RewardReceived;
        internal event Action<int, int, bool> QuestLootReceived;

        public GameplayUiSession() { levelLimits = ContentLevelLimits; }
        public GameplayUiSession(Func<int, GameplayLevelLimits?> levelLimits)
        { this.levelLimits = levelLimits ?? throw new ArgumentNullException(nameof(levelLimits)); }

        internal int? MaxHpAtLevel(int level) => levelLimits(level)?.MaxHp;

        public void SetWorld(WorldEntry entry, CharacterInfo character = null)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (character != null && character.Uid != entry.CharacterUid)
                throw new ArgumentException("The roster character does not own this world entry.", nameof(character));
            // Decode everything before publication: a malformed page cannot leave a half-applied session.
            Protocol0104.RequireSize(entry.RawLoadData, 2688);
            var limits = levelLimits(entry.Level);
            if (entry.RawLoadData[81] == 0 && limits.HasValue)
            {
                // cnOwnAvatarStatus.UpdateAttrib uses the level-one FM threshold throughout tutorial.
                var tutorial = levelLimits(1);
                if (tutorial.HasValue) limits = new GameplayLevelLimits(limits.Value.MaxHp, tutorial.Value.MaxFusionMatter);
            }
            var next = Decode(entry, limits); appliedTradeReceipt = null;
            Publish(next.WithMissionFlags(entry.RawLoadData).WithCondition(ProtocolGameplay.I(entry.RawLoadData, 1676)));
            completionBranches.Clear(); automaticMissionRequests.Clear();
            if (entry.BootstrapPackets != null) foreach (var frame in entry.BootstrapPackets) ApplyFrame(frame);
        }

        public void Clear()
        {
            automaticMissionRequests.Clear();
            appliedTradeReceipt = null;
            if (Snapshot.IsActive) Publish(GameplayUiState.Empty);
        }

        /// <summary>
        /// Apply only from GameplayTradeSession.Completed, with that completed snapshot's OwnItems.
        /// ItemStay describes the peer's destination slots; outgoing source slots come from the accepted
        /// local offers. Received item expiry/appearance metadata is absent in this protocol receipt.
        /// </summary>
        public bool ApplyTradeReceipt(TradeUpdate receipt, IReadOnlyList<TradeItem> ownOffers)
        {
            RequireWorld();
            if (ReferenceEquals(receipt, appliedTradeReceipt)) return false;
            if (receipt == null || receipt.Kind != TradeUpdateKind.Completed ||
                (receipt.FromId != Snapshot.PlayerId && receipt.ToId != Snapshot.PlayerId))
                throw new ArgumentException("Only this player's authoritative completed trade can update inventory.", nameof(receipt));
            if (ownOffers == null || ownOffers.Count != ProtocolTrade.OfferSlots) throw new ArgumentException("The five accepted local trade offer slots are required.", nameof(ownOffers));
            var inventory = new GameplayItemState[Snapshot.Inventory.Count];
            for (int i = 0; i < inventory.Length; i++) inventory[i] = Snapshot.Inventory[i];
            var spentBySlot = new Dictionary<int, int>();
            var acknowledgedOffers = new HashSet<int>();
            foreach (var sent in receipt.PeerReceivedItems)
            {
                if (sent.IsEmpty) continue;
                if (sent.TradeSlot < 0 || sent.TradeSlot >= ownOffers.Count || !acknowledgedOffers.Add(sent.TradeSlot))
                    throw new ProtocolException("Trade receipt refers to an invalid or repeated local offer slot.");
                var offered = ownOffers[sent.TradeSlot];
                if (offered.IsEmpty || offered.TradeSlot != sent.TradeSlot || offered.ItemId != sent.ItemId ||
                    offered.ItemType != sent.ItemType || offered.Quantity != sent.Quantity)
                    throw new ProtocolException("Trade receipt does not match the accepted local offer.");
                ProtocolInventory.ValidateSlot(ItemContainer.Inventory, offered.InventorySlot);
                var source = inventory[offered.InventorySlot];
                if (source.ItemType != offered.ItemType || source.ItemId != offered.ItemId)
                    throw new ProtocolException("The local trade source no longer matches its accepted item identity.");
                spentBySlot.TryGetValue(offered.InventorySlot, out int amount);
                spentBySlot[offered.InventorySlot] = checked(amount + sent.Quantity);
            }
            for (int i = 0; i < ownOffers.Count; i++)
                if (!ownOffers[i].IsEmpty && !acknowledgedOffers.Contains(i)) throw new ProtocolException("Trade receipt omitted an accepted local offer.");
            foreach (var spent in spentBySlot)
            {
                var source = inventory[spent.Key]; int remaining = source.Quantity - spent.Value;
                if (remaining < 0) throw new ProtocolException("Trade receipt exceeds the known source quantity.");
                // RustyFusion Item.split_items decreases the count and retains source appearance/expiry.
                inventory[spent.Key] = remaining == 0 ? new GameplayItemState(spent.Key, 0, 0, 0, 0) :
                    new GameplayItemState(spent.Key, source.ItemType, source.ItemId, (source.Option & unchecked((int)0xffff0000)) | remaining,
                        source.TimeLimit, source.MetadataComplete);
            }
            var receivedSlots = new HashSet<int>();
            foreach (var received in receipt.ReceivedItems)
            {
                if (received.IsEmpty) continue;
                ProtocolInventory.ValidateSlot(ItemContainer.Inventory, received.InventorySlot);
                if (!receivedSlots.Add(received.InventorySlot)) throw new ProtocolException("Trade receipt repeats a destination slot.");
                // The quantity and destination are known. Zero here is storage only: MetadataComplete
                // is false and KnownTimeLimit returns null, never a claim that the item is permanent.
                inventory[received.InventorySlot] = new GameplayItemState(received.InventorySlot, received.ItemType, received.ItemId,
                    received.Quantity, 0, false);
            }
            var next = Snapshot.WithTradeSettlement(inventory, receipt.Taros);
            appliedTradeReceipt = receipt; Publish(next); return true;
        }

        /// <summary>Call from the active shard's existing event owner; do not drain the client queue a second time.</summary>
        public bool ApplyFrame(ProtocolFrame frame)
        {
            if (!Snapshot.IsActive) return false;
            if (ApplyVehicleFrame(frame)) return true;
            if (ProtocolCroc.TryDecode(frame, out var croc))
            { if (!croc.Failed) Publish(Snapshot.WithCroc(croc)); return true; }
            if (ProtocolTravel.TryDecode(frame, out var travel))
            { if (!travel.Failed) Publish(Snapshot.WithTravel(travel)); return true; }
            if (frame.PacketType == 0x310000c5) { Publish(Snapshot.WithOperatorValue(frame.Payload)); return true; }
            if (frame.PacketType == 0x31000061) { Publish(Snapshot.WithGivenItem(frame.Payload)); return true; }
            if (ProtocolMail.TryDecode(frame, out var mail))
            {
                if (!mail.Failed && (mail.Type == ProtocolMail.SendOk || mail.Type == ProtocolMail.TarosOk)) Publish(Snapshot.WithMailBalance(mail.Taros));
                return true;
            }
            if (ProtocolServices.TryDecode(frame, out var service))
            { if (!service.Failed) Publish(Snapshot.WithService(service)); return true; }
            if (ApplyGameplayFrame(frame)) return true;
            if (!ProtocolInventory.TryDecode(frame, out var update)) return false;
            if (update.Kind == InventoryUpdateKind.EquipmentChanged && update.PlayerId != Snapshot.PlayerId) return false;
            Publish(Snapshot.WithItems(update));
            return true;
        }

        public bool CanMoveItem(ItemContainer from, int fromSlot, ItemContainer to, int toSlot)
        {
            if (!Snapshot.IsActive || (from == to && fromSlot == toSlot)) return false;
            try { ProtocolInventory.ValidateSlot(from, fromSlot); ProtocolInventory.ValidateSlot(to, toSlot); }
            catch (ArgumentException) { return false; }
            if ((from == ItemContainer.Bank || to == ItemContainer.Bank) && Snapshot.BankNumber < 0) return false;
            var first = Items(from)[fromSlot]; var second = Items(to)[toSlot];
            return !first.IsEmpty && ProtocolInventory.Fits(to, toSlot, first.ItemType, first.ItemId) &&
                ProtocolInventory.Fits(from, fromSlot, second.ItemType, second.ItemId);
        }
        public IReadOnlyList<GameplayItemState> Items(ItemContainer container) => container == ItemContainer.Equipment ? Snapshot.Equipment :
            container == ItemContainer.Inventory ? Snapshot.Inventory : container == ItemContainer.Bank ? Snapshot.Bank : throw new ArgumentOutOfRangeException(nameof(container));

        public void SetTutorialHealth(int hp, int maxHp)
        {
            RequireWorld();
            if (maxHp <= 0 || hp < 0 || hp > maxHp) throw new ArgumentOutOfRangeException(nameof(hp));
            if (Snapshot.Hp == hp && Snapshot.MaxHp == maxHp && Snapshot.HealthOrigin == GameplayHealthOrigin.LocalTutorial) return;
            Publish(Snapshot.WithTutorialHealth(hp, maxHp));
        }

        public void SetLocalPosition(Vector3 position)
        {
            RequireWorld();
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)) throw new ArgumentException("Position must be finite.", nameof(position));
            if (Snapshot.WorldPosition.Equals(position)) return;
            Publish(Snapshot.WithPosition(position));
        }

        void RequireWorld() { if (!Snapshot.IsActive) throw new InvalidOperationException("A world entry is required before changing gameplay UI state."); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void Publish(GameplayUiState state) { Snapshot = state; Changed?.Invoke(state); }

        // Server schema: FFOneClient/ffone-protocol PcLoadData0104; RustyFusion Player.get_load_data.
        // This is the existing FusionClient 2688-byte server layout, not Retrobution's 2552-byte client struct.
        // Equipment136[9]*12, inventory244[50]*12, quest inventory844[50]*12, nanos1444[37]*6.
        static GameplayUiState Decode(WorldEntry entry, GameplayLevelLimits? limits)
        {
            byte[] load = entry.RawLoadData;
            Protocol0104.RequireSize(load, 2688);
            if (Protocol0104.Read64(load, 4) != entry.CharacterUid) throw new ProtocolException("World UI entry UID mismatch.");
            var nanos = new List<GameplayNanoState>(37);
            for (int i = 0; i < 37; i++) nanos.Add(ReadNano(load, 1444 + i * 6, i));
            bool complete = ApplyNanoPrelude(entry, nanos);
            var slots = new int[3];
            for (int i = 0; i < slots.Length; i++) slots[i] = Protocol0104.Read16(load, 1666 + i * 2);
            var quests = new GameplayQuestState[9];
            for (int i = 0; i < quests.Length; i++)
            {
                int offset = 2032 + i * 52;
                quests[i] = new GameplayQuestState(i, Protocol0104.Read32(load, offset),
                    Three(load, offset + 4), Three(load, offset + 16), Three(load, offset + 28), Three(load, offset + 40));
            }
            string name = (Protocol0104.ReadUtf16(load, 14, 9) + " " + Protocol0104.ReadUtf16(load, 32, 17)).Trim();
            return new GameplayUiState(entry.PlayerId, entry.CharacterUid, name, entry.Level, entry.Hp,
                entry.FusionMatter, limits, Protocol0104.Read32(load, 104), Protocol0104.Read32(load, 96), Protocol0104.Read32(load, 100),
                entry.MapNumber, new Vector3(entry.X, entry.Z, entry.Y) / 100f,
                Items(load, 136, 9), Items(load, 244, 50), Items(load, 844, 50), nanos.ToArray(), slots,
                Protocol0104.Read16(load, 1672), complete, quests, Protocol0104.Read32(load, 2500), load[81] == 0, Protocol0104.ReadStyle(load, 4, 80).Gender);
        }

        static GameplayItemState[] Items(byte[] bytes, int offset, int count)
        {
            var result = new GameplayItemState[count];
            for (int i = 0; i < count; i++)
            {
                int p = offset + i * 12;
                result[i] = new GameplayItemState(i, Protocol0104.Read16(bytes, p), Protocol0104.Read16(bytes, p + 2),
                    Protocol0104.Read32(bytes, p + 4), Protocol0104.Read32(bytes, p + 8));
            }
            return result;
        }

        static int[] Three(byte[] bytes, int offset) => new[] {
            Protocol0104.Read32(bytes, offset), Protocol0104.Read32(bytes, offset + 4), Protocol0104.Read32(bytes, offset + 8) };
        static GameplayNanoState ReadNano(byte[] bytes, int offset, int index) => new GameplayNanoState(index,
            Protocol0104.Read16(bytes, offset), Protocol0104.Read16(bytes, offset + 2), Protocol0104.Read16(bytes, offset + 4));

        GameplayLevelLimits? ContentLevelLimits(int level)
        {
            if (cachedLevelLimits == null)
                cachedLevelLimits = ContentAssets.Read<NativeDataDocument, Dictionary<int, GameplayLevelLimits>>(
                    NativeData.Resource("data/tables/xdt"), ReadLevelLimits);
            return cachedLevelLimits.TryGetValue(level, out var limits) ? limits : (GameplayLevelLimits?)null;
        }

        /// <summary>Read only the avatar growth subtree; do not materialize the complete XDT as JSON.</summary>
        public static Dictionary<int, GameplayLevelLimits> ReadLevelLimits(NativeDataDocument document)
        {
            var result = new Dictionary<int, GameplayLevelLimits>();
            if (!document || document.Nodes.Length == 0) return result;
            int avatar = Child(document, 0, "m_pAvatarTable");
            int growth = avatar < 0 ? -1 : Child(document, avatar, "m_pAvatarGrowData");
            if (growth < 0) return result;
            foreach (int row in document.Children(growth))
            {
                // Primary localized::.cctor sets LevelExpansion=0. Normal progression uses the nano
                // creation threshold; m_iFMLimit is the storage cap and is not the HUD denominator.
                int level = Number(document, row, "m_iLevel"), hp = Number(document, row, "m_iMaxHP"), fm = Number(document, row, "m_iReqBlob_NanoCreate");
                if (level == 0 && hp == 0 && fm == 0) continue; // source's sentinel row
                if (level <= 0 || hp <= 0 || fm < 0 || result.ContainsKey(level)) throw new InvalidDataException("Invalid or duplicate avatar growth level: " + level);
                result.Add(level, new GameplayLevelLimits(hp, fm));
            }
            return result;
        }

        static int Child(NativeDataDocument document, int parent, string key)
        {
            foreach (int index in document.Children(parent)) if (document.Nodes[index].Key == key) return index;
            return -1;
        }

        static int Number(NativeDataDocument document, int row, string key)
        {
            int index = Child(document, row, key);
            if (index < 0 || !int.TryParse(document.Nodes[index].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new InvalidDataException("Missing or invalid avatar growth field: " + key);
            return value;
        }

        static bool ApplyNanoPrelude(WorldEntry entry, List<GameplayNanoState> nanos)
        {
            // RustyFusion sends a zero-filled resize packet before ENTER, then real pages after ENTER
            // during scene loading. PCUID here is the live PlayerId, not the persistent character UID.
            const uint nanoBookSubset = 0x31000134;
            const int maximumBookSize = short.MaxValue + 1; // IDs and bank entries are signed 16-bit.
            bool first = true;
            var known = new List<bool>(37);
            for (int i = 0; i < 37; i++) known.Add(true);
            if (entry.BootstrapPackets != null)
                foreach (var packet in entry.BootstrapPackets)
                {
                    if (packet.PacketType != nanoBookSubset) continue;
                    var bytes = packet.Payload;
                    Protocol0104.RequireSize(bytes, 76);
                    if (Protocol0104.Read64(bytes, 0) != entry.PlayerId) throw new ProtocolException("Nano book owner does not match the active player.");
                    int size = Protocol0104.Read32(bytes, 8), offset = Protocol0104.Read32(bytes, 12);
                    if (size < 0 || size > maximumBookSize || offset < 0 || offset > size || (size > 0 && offset == size))
                        throw new ProtocolException("Invalid Nano book page range.");
                    if (!first && size != nanos.Count) throw new ProtocolException("Nano book size changed during world entry.");
                    while (nanos.Count < size) { nanos.Add(new GameplayNanoState(nanos.Count, 0, 0, 0)); known.Add(false); }
                    if (size < nanos.Count) { nanos.RemoveRange(size, nanos.Count - size); known.RemoveRange(size, known.Count - size); }
                    bool resizeOnly = first && offset == 0;
                    for (int i = 16; i < 76 && resizeOnly; i++) resizeOnly = bytes[i] == 0;
                    first = false;
                    if (resizeOnly) continue;
                    int count = Math.Min(10, size - offset);
                    for (int i = 0; i < count; i++) { nanos[offset + i] = ReadNano(bytes, 16 + i * 6, offset + i); known[offset + i] = true; }
                }
            foreach (bool value in known) if (!value) return false;
            return true;
        }
    }
}
