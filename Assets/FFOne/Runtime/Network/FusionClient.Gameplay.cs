using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFOne.Network
{
    public sealed partial class FusionClient
    {
        public Task AttackNpcsAsync(IReadOnlyList<int> targets, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.AttackRequest, ProtocolGameplay.AttackPayload(targets), cancellationToken);
        public Task ActivateNanoAsync(int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoActiveRequest, ProtocolGameplay.NanoSlotPayload(slot, true), cancellationToken);
        public Task EquipNanoAsync(int nanoId, int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoEquipRequest, ProtocolGameplay.EquipNanoPayload(nanoId, slot), cancellationToken);
        public Task UnequipNanoAsync(int slot, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.NanoUnequipRequest, ProtocolGameplay.NanoSlotPayload(slot), cancellationToken);
        public Task ReviveAsync(CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolGameplay.RegenPayload(false), cancellationToken);
        public Task RevivePhoenixAsync(bool group, CancellationToken cancellationToken = default)
            => SendGameplayAsync(ProtocolGameplay.RegenRequest, ProtocolGameplay.PhoenixRegenPayload(group), cancellationToken);
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
