using System.Text.Json;
using FFOne.Network;

static partial class Program
{
    sealed class BuffPlan
    {
        public BuffCase[] passives { get; set; }
        public BuffCase[] active { get; set; }
        public BuffItem[] items { get; set; }
        public int[] effects { get; set; }
        public int[] unresolvedSkills { get; set; }
        public int[] unresolvedConsumers { get; set; }
        public int[] unsupportedConsumers { get; set; }
    }
    sealed class BuffCase { public int nano { get; set; } public int skill { get; set; } public int buff { get; set; } public int value { get; set; } public int boostedValue { get; set; } public bool group { get; set; } }
    sealed class BuffItem { public int slot { get; set; } public int item { get; set; } public int skill { get; set; } public bool supported { get; set; } }
    static readonly Dictionary<Probe, BuffState> liveBuffs = new();
    static readonly Dictionary<Probe, int> buffOffsets = new();
    static async Task Buffs(string[] args)
    {
        var plan = JsonSerializer.Deserialize<BuffPlan>(File.ReadAllText(args[3]));
        bool passed = false; string error = null;
        try
        {
            if (args[0] == "create-buffs")
            {
                using (var admin = new FusionClient()) await admin.LoginAsync("127.0.0.1", port, "unitybuffadmin", "test-only-password");
                foreach (string name in new[] { "Buffa", "Buffb", "Buffd" })
                {
                    using var client = new FusionClient(); await client.LoginAsync("127.0.0.1", port, "unity" + name.ToLowerInvariant(), "test-only-password");
                    await client.CreateCharacterAsync(new CharacterCreation { FirstName = name, LastName = "Unity", Slot = 1,
                        UpperBodyId = 67, LowerBodyId = 47, FootId = 34, UpperBodyIndex = 2, LowerBodyIndex = 2, FootIndex = 2, FaceStyleIndex = 2, HairStyleIndex = 2 });
                    Check(true, name + " normal buff fixture created");
                }
            }
            else await ExerciseBuffs(plan);
            passed = true;
        }
        catch (Exception exception) { error = exception.ToString(); Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        finally
        {
            foreach (var p in peers) { try { if (p.Client.IsInWorld) await p.Client.LeaveWorldAsync(); } catch { } p.Client.Dispose(); }
            peers.Clear();
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed, checks, error, plan.unresolvedSkills, plan.unresolvedConsumers, plan.unsupportedConsumers,
                scope = "Real server transactions for implemented authored semantics; original unresolved behavior remains excluded." }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    static BuffState BuffsOf(Probe p)
    {
        if (!liveBuffs.TryGetValue(p, out var state)) { state = ProtocolBuff.FromEntry(p.Entry, 0); liveBuffs[p] = state; buffOffsets[p] = 0; }
        for (int i = buffOffsets[p]; i < p.Frames.Count; i++)
        {
            var frame = p.Frames[i];
            if (ProtocolBuff.TryUpdate(frame, out var buff)) state.Apply(buff, 0);
            if (ProtocolBuff.TryEffects(frame, out var effects)) foreach (var effect in effects)
                if (effect.Id == p.Entry.PlayerId && effect.Kind == WorldEntityKind.Player && effect.Condition.HasValue) state.SetCondition(effect.Condition.Value);
        }
        buffOffsets[p] = p.Frames.Count; return state;
    }
    static async Task BuffPump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            foreach (var p in peers) while (p.Client.TryDequeueEvent(out var e)) if (e.IsShard && e.Frame != null) p.Accept(e.Frame);
            await Task.Delay(10);
        }
    }
    static async Task BuffNano(Probe p, int nano)
    {
        await p.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        int before = p.Frames.Count;
        await p.Client.ActivateNanoAsync(-1);
        await Until(() => p.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess), "buff Nano deactivation acknowledged");
        before = p.Frames.Count; await p.Client.EquipNanoAsync(nano, 0);
        await Until(() => p.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.NanoEquipSuccess), "buff Nano " + nano + " equipped by server");
        before = p.Frames.Count; await p.Client.ActivateNanoAsync(0);
        await Until(() => p.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess), "buff Nano " + nano + " activated by server");
    }
    static async Task ExerciseBuffs(BuffPlan plan)
    {
        var a = await Join("Buffa"); var b = await Join("Buffb");
        await Until(() => a.World.TryGet(WorldEntityKind.Player, b.Entry.PlayerId, out _), "buff actors share real server interest");
        Check(Protocol0104.Read16(a.Entry.RawLoadData, 0) == 99 && Protocol0104.Read16(b.Entry.RawLoadData, 0) == 99, "buff actors retain normal account privileges");
        await a.Client.InviteGroupAsync(b.Entry.PlayerId);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.Invite), "buff group invitation delivered");
        await b.Client.AnswerGroupAsync(a.Entry.PlayerId, true);
        await Until(() => a.Social.Members.Count == 2 && b.Social.Members.Count == 2, "buff group membership acknowledged");
        foreach (var item in plan.passives)
        {
            await BuffNano(a, item.nano);
            await Until(() => BuffsOf(a).Has(item.buff) && BuffsOf(a).Value(item.buff) == item.value, "passive skill " + item.skill + " keeps authored value and condition");
            if (item.group) await Until(() => BuffsOf(b).Has(item.buff), "passive skill " + item.skill + " reaches actual group recipient");
            else { await BuffPump(120); Check(!BuffsOf(b).Has(item.buff), "self passive skill " + item.skill + " does not leak to a group member"); }
            await a.Client.ActivateNanoAsync(-1);
            await Until(() => !BuffsOf(a).Has(item.buff) && !BuffsOf(b).Has(item.buff), "passive skill " + item.skill + " source removal reconciles both recipients");
        }
        await BuffNano(a, 3);
        await Until(() => BuffsOf(a).Has(1) && BuffsOf(b).Has(1), "group run aura is source owned on both players");
        await b.Client.LeaveGroupAsync();
        await Until(() => !BuffsOf(b).Has(1) && BuffsOf(a).Has(1), "group leave removes received aura while preserving holder aura");
        await a.Client.ActivateNanoAsync(-1);
        await Until(() => !BuffsOf(a).Has(1), "group run holder deactivation removes own source");
        foreach (var item in plan.active)
        {
            await BuffNano(a, item.nano);
            var target = a.World.Entities.Where(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 59 && e.Hp > 0).OrderBy(e => e.X).First();
            await a.Client.SendMovementAsync(target.X - 650, target.Y, target.Z, 0, 0, 0, 0, 0, 0, true); await BuffPump(100);
            int before = a.Frames.Count, peerBefore = b.Frames.Count;
            await a.Client.UseNanoSkillAsync(new[] { target.Id }, target.Id);
            await Until(() => a.Frames.Skip(before).Any(f => f.PacketType == ProtocolNanoSkill.Success) && b.Frames.Skip(peerBefore).Any(f => f.PacketType == ProtocolNanoSkill.Broadcast), "active skill " + item.skill + " reaches owner and observer");
            ProtocolNanoSkill.TryDecode(a.Frames.Last(f => f.PacketType == ProtocolNanoSkill.Success), out var receipt);
            Check(receipt.SkillId == item.skill && receipt.Stamina < 150, "active skill " + item.skill + " exact identity and authoritative Nano cost");
            if (item.buff > 0)
            {
                await Until(() => b.World.TryGet(WorldEntityKind.Npc, target.Id, out var entity) && (entity.ConditionFlags & (1 << (item.buff - 1))) != 0, "active skill " + item.skill + " condition reaches observer entity");
                await Until(() => b.World.TryGet(WorldEntityKind.Npc, target.Id, out var entity) && (entity.ConditionFlags & (1 << (item.buff - 1))) == 0, "active skill " + item.skill + " expires through authoritative timeout", 20);
            }
            else Check(receipt.Effects.Count == 2 && receipt.Effects[0].Id == a.Entry.PlayerId && receipt.Effects[1].Id == target.Id && receipt.Effects[1].Hp.HasValue,
                "Leech preserves Heal16 caster + Damage20 NPC result identities");
            await a.Client.ActivateNanoAsync(-1);
        }
        await a.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await BuffNano(a, 1); await a.Client.ActivateNanoAsync(-1);
        // The five-minute item126 is exercised after the separate ninety-second
        // source transition so it cannot legitimately keep that stronger stack alive.
        foreach (var item in plan.items.Where(i => i.item != 126).OrderBy(i => i.item == 122 ? 1 : 0))
        {
            int before = a.Frames.Count, peerBefore = b.Frames.Count;
            await a.Client.UseItemAsync(item.slot, 0);
            await Until(() => a.Frames.Skip(before).Any(f => f.PacketType == (item.supported ? ProtocolItemUse.Success : ProtocolItemUse.Fail)), "item " + item.item + " returns its authored supported/rejected result");
            if (item.supported)
            {
                ProtocolItemUse.TryDecode(a.Frames.Last(f => f.PacketType == ProtocolItemUse.Success), out var result);
                Check(result.SkillId == item.skill && result.Slot == item.slot && result.Remaining.Option == 2 && result.Effects.Count == 1, "item " + item.item + " consumes exactly one and keeps complete typed trailer");
                await Until(() => b.Frames.Skip(peerBefore).Any(f => f.PacketType == ProtocolItemUse.Broadcast), "item " + item.item + " broadcasts its actual effect to observer");
                before = a.Frames.Count; await a.Client.UseItemAsync(item.slot, 0);
                await Until(() => a.Frames.Skip(before).Any(f => f.PacketType == ProtocolItemUse.Fail), "item " + item.item + " immediate cooldown replay is rejected");
                await BuffPump(1100);
            }
        }
        await BuffNano(a, 33);
        await Until(() => BuffsOf(a).Value(15) == 500, "strong CashItem RewardBlob500 remains effective over weaker real Nano source");
        await Until(() => !BuffsOf(a).Has(20), "timed Invulnerable item expires from server state", 110);
        var reward = plan.passives.Single(c => c.nano == 33);
        await Until(() => BuffsOf(a).Has(15) && BuffsOf(a).Value(15) == (BuffsOf(a).Has(21) ? reward.boostedValue : reward.value),
            "strong timed CashItem expiry sends Change to surviving authored Nano RewardBlob value", 20);
        await a.Client.ActivateNanoAsync(-1);
        await Until(() => !BuffsOf(a).Has(15), "last weaker Nano source removal clears RewardBlob after strong item expiry");
        var longItem = plan.items.Single(i => i.item == 126); int itemBefore = a.Frames.Count, observerBefore = b.Frames.Count;
        await a.Client.UseItemAsync(longItem.slot, 0);
        await Until(() => a.Frames.Skip(itemBefore).Any(f => f.PacketType == ProtocolItemUse.Success) && b.Frames.Skip(observerBefore).Any(f => f.PacketType == ProtocolItemUse.Broadcast),
            "item126 five-minute original source succeeds and broadcasts after independent ninety-second stack acceptance");
        ProtocolItemUse.TryDecode(a.Frames.Last(f => f.PacketType == ProtocolItemUse.Success), out var longReceipt);
        Check(longReceipt.SkillId == longItem.skill && longReceipt.Remaining.Option == 2 && longReceipt.Effects.Count == 1,
            "item126 retains exact linked skill187, typed trailer and one-item consumption");
        itemBefore = a.Frames.Count; await a.Client.UseItemAsync(longItem.slot, 0);
        await Until(() => a.Frames.Skip(itemBefore).Any(f => f.PacketType == ProtocolItemUse.Fail), "item126 immediate source cooldown replay is rejected");
        await BuffPhoenix(a);
        // Reload is the durable inventory evidence for rejected items and replay.
        await a.Client.LeaveWorldAsync(); a.Client.Dispose(); peers.Remove(a); a = await Join("Buffa");
        foreach (var item in plan.items.Where(i => !i.supported)) Check(a.Inventory[item.slot].ItemId == item.item && a.Inventory[item.slot].Option == 3, "rejected item " + item.item + " remains unconsumed across saved reconnect");
    }
    static async Task BuffPhoenix(Probe a)
    {
        var d = await Join("Buffd"); int before = d.Frames.Count;
        await d.Client.RevivePhoenixAsync(true); await BuffPump(200);
        Check(!d.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "group Phoenix request without a real cast is rejected");
        await d.Client.ActivateNanoAsync(1);
        await Until(() => BuffsOf(d).Has(5), "Phoenix self aura is granted by real active Nano8");
        await d.Client.SendMovementAsync(635032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await Until(() => d.Hp == 0, "source type3337 combat kills the Phoenix holder", 120);
        before = d.Frames.Count; await d.Client.RevivePhoenixAsync(false);
        await Until(() => d.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "self Phoenix regen3 is accepted after actual death");
        var result = d.Frames.Last(f => f.PacketType == ProtocolGameplay.RegenSuccess).Payload;
        Check(ProtocolGameplay.I(result, 0) > 0 && ProtocolGameplay.I(result, 8) == 635032 && ProtocolGameplay.I(result, 12) == 187177,
            "self Phoenix restores living HP at the exact current location");
        Check(Protocol0104.Read16(result, 22 + 6 + 4) == 0 && Protocol0104.Read16(result, 22 + 12 + 4) == 150,
            "self Phoenix drains Nano8 while preserving carried Nano3 stamina");
        d.Hp = ProtocolGameplay.I(result, 0);
        await d.Client.ActivateNanoAsync(-1);
        await Until(() => d.Hp == 0, "source combat creates a second real death for group Phoenix", 120);
        await a.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await a.Client.InviteGroupAsync(d.Entry.PlayerId);
        await Until(() => d.Frames.Any(f => f.PacketType == ProtocolSocial.Invite), "dead group Phoenix target receives invitation");
        await d.Client.AnswerGroupAsync(a.Entry.PlayerId, true);
        await Until(() => a.Social.Members.Count == 2 && d.Social.Members.Count == 2, "group Phoenix target joins source holder");
        await BuffNano(a, 7); before = d.Frames.Count;
        await a.Client.UseNanoSkillAsync(Array.Empty<int>());
        await Until(() => d.Frames.Skip(before).Any(f => ProtocolNanoSkill.TryDecode(f, out var skill) && skill.SkillId == 20 && skill.Effects.Any(e => e.Id == d.Entry.PlayerId && e.Resurrected)),
            "real PhoenixGroup skill20 authorizes dead target via native Resurrect trailer");
        before = d.Frames.Count; await d.Client.RevivePhoenixAsync(true);
        await Until(() => d.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "authorized group Phoenix regen4 returns authoritative living post-state");
        before = d.Frames.Count; await d.Client.RevivePhoenixAsync(true); await BuffPump(200);
        Check(!d.Frames.Skip(before).Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "group Phoenix authorization is consumed and replay fails");
        await d.Client.LeaveWorldAsync(); d.Client.Dispose(); peers.Remove(d);
    }
}
