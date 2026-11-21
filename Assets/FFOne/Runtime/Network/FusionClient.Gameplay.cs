using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task SetOwnOperatorValueAsync(int kind, int value, CancellationToken token)
            => SendGameplayAsync(0x1300006b, ProtocolTravel.Words(World.PlayerId, kind, value), token);

        public Task SetPoisonGroundAsync(bool poisoned, CancellationToken token = default)
            => SendGameplayAsync(0x13000049, ProtocolTravel.Words(poisoned ? 1 : 0), token);

        public Task OperatorWarpAsync(int tileX, int tileY, CancellationToken token)
        {
            int ToCoordinate(int tile) => checked((tile * 512 + 256) * 100);
            return SendGameplayAsync(0x13000014, ProtocolTravel.Words(ToCoordinate(tileX), ToCoordinate(tileY), 10000), token);
        }

        public Task GiveOwnItemAsync(int slot, int type, int id, int count, int timeLeft, CancellationToken token)
        {
            var payload = new byte[24];
            Protocol0104.Write32(payload, 0, (int)ItemContainer.Inventory);
            Protocol0104.Write32(payload, 4, slot);
            Protocol0104.Write16(payload, 8, type);
            Protocol0104.Write16(payload, 10, id);
            Protocol0104.Write32(payload, 12, count);
            Protocol0104.Write32(payload, 20, timeLeft);
            return SendGameplayAsync(0x1300001a, payload, token);
        }

        public Task GiveOwnNanoAsync(int id, CancellationToken token)
        {
            var payload = new byte[2];
            Protocol0104.Write16(payload, 0, id);
            return SendGameplayAsync(0x13000044, payload, token);
        }

        public Task AttackNpcsAsync(IReadOnlyList<int> targets, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.AttackRequest, ProtocolGameplay.AttackPayload(targets), cancellationToken);
        public Task ActivateNanoAsync(int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoActiveRequest, ProtocolGameplay.NanoSlotPayload(slot, true), cancellationToken);
        public Task EquipNanoAsync(int nanoId, int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoEquipRequest, ProtocolGameplay.EquipNanoPayload(nanoId, slot), cancellationToken);
        public Task UnequipNanoAsync(int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoUnequipRequest, ProtocolGameplay.NanoSlotPayload(slot), cancellationToken);
        public Task TuneNanoAsync(int nanoId, int tuneId, int[] slots, CancellationToken token = default)
        {
            if (slots.Length > 10) throw new System.ArgumentOutOfRangeException(nameof(slots));
            var bytes = new byte[44];
            Protocol0104.Write16(bytes,0,nanoId); Protocol0104.Write16(bytes,2,tuneId);
            for (int i=0;i<10;i++) Protocol0104.Write32(bytes,4+4*i,i<slots.Length?slots[i]:-1);
            return SendGameplayAsync(0x13000010,bytes,token);
        }
        public Task ReviveAsync(CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolGameplay.RegenPayload(false), cancellationToken);
        public Task RevivePhoenixAsync(bool group, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolGameplay.PhoenixRegenPayload(group), cancellationToken);
        public Task ReviveWithItemAsync(int slot, CancellationToken cancellationToken = default)
        {
            ProtocolInventory.ValidateSlot(ItemContainer.Inventory, slot);
            return SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolTravel.Words(6, (int)ItemContainer.Inventory, slot), cancellationToken);
        }
        public Task UnstickAsync(CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolGameplay.RegenPayload(true), cancellationToken);
        public Task ChargeNanoAsync(CancellationToken cancellationToken = default)
            => SendGameplayAsync(0x13000015, new byte[4], cancellationToken);
        public Task UseNanoSkillAsync(IReadOnlyList<int> targets, int focus = 0, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolNanoSkill.Request, ProtocolNanoSkill.Payload(targets, focus), cancellationToken);

        async Task SendGameplayAsync(uint type, byte[] payload, CancellationToken cancellationToken)
        {
            long generation = Interlocked.Read(ref shardGeneration);
            int playerId = World?.PlayerId ?? 0;
            using (var token = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                await operations.WaitAsync(token.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    if (!IsWorldLoaded || shard == null) throw new InvalidOperationException("The world has not finished loading.");
                    if (playerId == 0 || world.PlayerId != playerId || generation != Interlocked.Read(ref shardGeneration))
                        throw new OperationCanceledException("The gameplay command belongs to a retired shard.");
                    await shard.SendAsync(type, payload, token.Token).ConfigureAwait(false);
                }
                finally { operations.Release(); }
            }
        }
    }
}
