using System.Diagnostics;
using System.Text.Json;
using FFOne.Network;

static partial class Program
{
    static readonly List<string> checks = new();
    static readonly List<Probe> peers = new();
    static int port;
    static void Check(bool ok, string description)
    { if (!ok) throw new Exception(description); checks.Add(description); Console.WriteLine("PASS " + description); }
    static async Task Main(string[] args)
    {
        port = int.Parse(args[1]);
        try
        {
            if (args[0] == "create") await Create();
            else if (args[0] == "create-weapons") await CreateWeaponCombat();
            else if (args[0] == "exercise-weapons") await WeaponCombat();
            else if (args[0] == "create-buffs" || args[0] == "exercise-buffs") { await Buffs(args); return; }
            else if (args[0] == "create-instances" || args[0] == "exercise-instances") { await Instances(args); return; }
            else { await Exercise(); await Services(); await SocialAndMail(); await Travel(); await Recall(); await Sliders(); await PersonalVehicles(); }
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = true, checks, players = peers.Select(p => new { p.Name, p.Entry.PlayerId, p.Entry.CharacterUid, entities = p.World.Count }) }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(error); Environment.ExitCode = 1;
        }
        finally { foreach (var p in peers) p.Client.Dispose(); }
    }
    static async Task Create()
    {
        using (var admin = new FusionClient()) await admin.LoginAsync("127.0.0.1", port, "unityadmin", "test-only-password");
        foreach (string name in new[] { "Alice", "Bobby", "Trava", "Travb" })
        {
            using var client = new FusionClient(); await client.LoginAsync("127.0.0.1", port, "unity" + name.ToLowerInvariant(), "test-only-password");
            var character = await client.CreateCharacterAsync(new CharacterCreation { FirstName = name, LastName = "Unity", Slot = 1,
                UpperBodyId = 67, LowerBodyId = 47, FootId = 34, UpperBodyIndex = 2, LowerBodyIndex = 2, FootIndex = 2, FaceStyleIndex = 2, HairStyleIndex = 2 });
            Check(character.Style.AppearanceComplete, name + " normal account character created");
        }
    }
    static async Task Exercise()
    {
        var a = await Join("Alice"); var b = await Join("Bobby");
        await Until(() => a.World.TryGet(WorldEntityKind.Player, b.Entry.PlayerId, out _) && b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out _), "mutual player interest entry");
        Check(a.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc), "initial NPC prelude is decoded");
        int nx = a.Entry.X + 100;
        await a.Client.SendMovementAsync(nx, a.Entry.Y, a.Entry.Z, 90, 1, 0, 0, 100, 3, false);
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var e) && e.X == nx, "remote movement round trip");
        await a.Client.SendMovementAsync(nx, a.Entry.Y, a.Entry.Z, 90, 0, 0, 0, 0, 0, true);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolWorld.PlayerStop), "remote stop round trip");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.SendFreeChatAsync("Unity live integration");
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolChat.Success) && b.Frames.Any(f => f.PacketType == ProtocolChat.Success), "chat delivered to sender and peer");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 0, ItemContainer.Equipment, 1);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess) && b.Frames.Any(f => f.PacketType == ProtocolInventory.EquipmentChanged), "equipment move acknowledgment and peer appearance update");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.SendItemMoveAsync(ItemContainer.Equipment, 1, ItemContainer.Inventory, 0);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "equipment returned to inventory");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.ActivateNanoAsync(0);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess) && b.Frames.Any(f => f.PacketType == ProtocolGameplay.NanoActiveBroadcast), "Nano activation server acknowledgment and peer broadcast");
        var skillTarget = a.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1);
        await a.Client.UseNanoSkillAsync(new[] { skillTarget.Id }, skillTarget.Id);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolNanoSkill.Success) && b.Frames.Any(f => f.PacketType == ProtocolNanoSkill.Broadcast), "Nano skill acknowledged and broadcast by server");
        var skillFrame = a.Frames.Last(f => f.PacketType == ProtocolNanoSkill.Success);
        Check(ProtocolNanoSkill.TryDecode(skillFrame, out var used) && used.NanoId == 1 && used.Stamina < 150 && used.Effects.Any(e => e.Id == skillTarget.Id), "Nano skill consumes server stamina and identifies the effect target");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.ActivateNanoAsync(-1);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess && Protocol0104.Read16(f.Payload, 0) == -1), "Nano deactivation acknowledged");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.SendTradeOfferAsync(b.Entry.PlayerId);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolTrade.Offer), "trade invitation delivered");
        await b.Client.SendTradeAcceptAsync(a.Entry.PlayerId, b.Entry.PlayerId);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTrade.Accepted) && b.Frames.Any(f => f.PacketType == ProtocolTrade.Accepted), "trade accepted by both participants");
        await a.Client.SendTradeRegisterItemAsync(a.Entry.PlayerId, b.Entry.PlayerId, new TradeItem(1, 67, 1, 0, 0));
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTrade.ItemRegistered) && b.Frames.Any(f => f.PacketType == ProtocolTrade.ItemRegistered), "trade item registered by server");
        await a.Client.SendTradeRegisterCashAsync(a.Entry.PlayerId, b.Entry.PlayerId, 10);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTrade.CashRegistered), "trade currency registered by server");
        await a.Client.SendTradeConfirmAsync(a.Entry.PlayerId, b.Entry.PlayerId);
        await b.Client.SendTradeConfirmAsync(a.Entry.PlayerId, b.Entry.PlayerId);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTrade.Completed) && b.Frames.Any(f => f.PacketType == ProtocolTrade.Completed), "trade completed with authoritative receipts");
        var receipt = a.Frames.Last(f => f.PacketType == ProtocolTrade.Completed);
        Check(ProtocolTrade.TryDecode(receipt, out var trade) && trade.Taros == 990, "sender balance reflects server settlement");
        var target = a.World.Entities.FirstOrDefault(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1 && e.Hp > 0)
            ?? throw new Exception("Seeded combat mob not streamed.");
        // Default unarmed attacks use the accepted 200-unit melee range. Move
        // into reach and observe the actual server position before sending intent.
        int attackX = target.X - 100, attackY = target.Y, attackZ = target.Z;
        await a.Client.SendMovementAsync(attackX, attackY, attackZ, 0, 0, 0, 0, 0, 0, true);
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var attacker) &&
            attacker.X == attackX && attacker.Y == attackY && attacker.Z == attackZ, "basic attack enters the source melee range through real movement");
        a.Frames.Clear(); b.Frames.Clear();
        await a.Client.AttackNpcsAsync(new[] { target.Id });
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolGameplay.AttackSuccess), "basic attack result from real server");
        Check(a.Frames.Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => { ProtocolGameplay.TryCombat(f, out var c); return c.Hits; }).Any(h => h.Id == target.Id && h.Damage > 0), "combat reports actual damage and target HP");
        a.Frames.Clear();
        await a.Client.SendMovementAsync(target.X - 100, target.Y, target.Z, 0, 0, 0, 0, 0, 0, true);
        await Until(() => a.Frames.Any(f => ProtocolGameplay.TryCombat(f, out var c) && c.Hits.Any(h => h.Kind == WorldEntityKind.Player && h.Id == a.Entry.PlayerId && h.Damage > 0)),
            "server NPC attack damages the actual player", 30);
        await Until(() => a.Hp == 0,
            "server combat reaches player death", 60);
        await a.Client.ReviveAsync();
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolGameplay.RegenSuccess), "server revival acknowledges health and destination");
        var revive = a.Frames.Last(f => f.PacketType == ProtocolGameplay.RegenSuccess);
        Check(revive.Payload.Length == 48 && ProtocolGameplay.I(revive.Payload, 0) > 0 && ProtocolGameplay.I(revive.Payload, 4) == 0,
            "revival carries a living overworld post-state");
        await a.Client.LeaveWorldAsync();
        await Until(() => !b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out _), "peer actor removed after saved exit");
        await b.Client.LeaveWorldAsync();
        a.Client.Dispose(); b.Client.Dispose(); peers.Clear();
        var again = await Join("Alice");
        Check(Protocol0104.Read32(again.Entry.RawLoadData, 104) == 990, "trade currency persists after reconnect");
        Check(Protocol0104.Read16(again.Entry.RawLoadData, 244 + 2) == 0, "sent item remains removed after reconnect");
        var giver = again.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 3076);
        var terminator = again.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 3075);
        await again.Client.SendMovementAsync(giver.X, giver.Y, giver.Z, 0, 0, 0, 0, 0, 0, true);
        await again.Client.StartTaskAsync(5213, giver.Id);
        await Until(() => again.Frames.Any(f => f.PacketType == 0x3100001c && ProtocolGameplay.I(f.Payload, 0) == 5213), "authored talk task accepted for normal account");
        await again.Client.TrackMissionAsync(841);
        await Until(() => again.Frames.Any(f => f.PacketType == 0x310000ef && ProtocolGameplay.I(f.Payload, 0) == 841), "mission tracking acknowledged");
        await again.Client.CompleteTaskAsync(5213, terminator.Id);
        await Until(() => again.Frames.Any(f => f.PacketType == 0x3100001e && ProtocolGameplay.I(f.Payload, 0) == 5213), "talk objective completed at correct NPC");
        await again.Client.StartTaskAsync(5214, 0);
        await Until(() => again.Frames.Any(f => f.PacketType == 0x3100001c && ProtocolGameplay.I(f.Payload, 0) == 5214), "outgoing task starts after authoritative completion");
        await again.Client.LeaveWorldAsync();
        again.Client.Dispose(); peers.Clear();
        var resumed = await Join("Alice");
        Check(Enumerable.Range(0, 9).Any(i => ProtocolGameplay.I(resumed.Entry.RawLoadData, 2032 + i * 52) == 5214), "running mission persists after reconnect");
        await resumed.Client.StopTaskAsync(5214);
        await Task.Delay(500);
        while (resumed.Client.TryDequeueEvent(out var e)) if (e.IsShard && e.Frame != null) resumed.Accept(e.Frame);
        Check(!resumed.Frames.Any(f => f.PacketType == 0x3100002d && ProtocolGameplay.I(f.Payload, 0) == 5214), "Nano progression mission cannot be abandoned");
        await resumed.Client.LeaveWorldAsync();
        resumed.Client.Dispose(); peers.Clear();
        var retained = await Join("Alice");
        Check(Enumerable.Range(0, 9).Any(i => ProtocolGameplay.I(retained.Entry.RawLoadData, 2032 + i * 52) == 5214), "rejected mission cancellation preserves persisted task");
        await retained.Client.LeaveWorldAsync();
    }
    static async Task<Probe> Join(string name)
    {
        var client = new FusionClient();
        var roster = await client.LoginAsync("127.0.0.1", port, "unity" + name.ToLowerInvariant(), "test-only-password");
        var entry = await client.EnterWorldAsync(roster.Single().Uid); await client.CompleteLoadingAsync();
        var probe = new Probe { Name = name, Client = client, Entry = entry, World = new WorldEntitySession(entry.PlayerId), Hp = entry.Hp };
        probe.Taros = Protocol0104.Read32(entry.RawLoadData, 104);
        for (int i = 0; i < 50; i++) probe.Inventory[i] = ProtocolServices.Item(entry.RawLoadData, 244 + i * 12);
        peers.Add(probe);
        foreach (var f in entry.BootstrapPackets ?? Array.Empty<ProtocolFrame>()) probe.Accept(f);
        return probe;
    }
    static async Task<ServiceUpdate> Reply(Probe p, uint type, string description)
    {
        await Until(() => p.Frames.Any(f => f.PacketType == type), description);
        ProtocolServices.TryDecode(p.Frames.Last(f => f.PacketType == type), out var result); return result;
    }
    static async Task MoveTo(Probe p, WorldEntity npc)
    { await p.Client.SendMovementAsync(npc.X, npc.Y, npc.Z, 0, 0, 0, 0, 0, 0, true); p.Frames.Clear(); }
    static async Task Services()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var denied = await Join("Alice"); var gold = denied.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 2586);
        await MoveTo(denied, gold); await denied.Client.OpenBankAsync(gold.Id);
        var failure = await Reply(denied, ProtocolServices.BankOpened + 1, "membership bank rejects an account without its card");
        Check(failure.Error == 2, "membership rejection preserves the source access-required code");
        await denied.Client.LeaveWorldAsync(); denied.Client.Dispose(); peers.Clear();
        var p = await Join("Bobby"); var vendor = p.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 651);
        await MoveTo(p, vendor); await p.Client.StartVendorAsync(vendor.Id, vendor.NpcType);
        await Reply(p, ProtocolServices.Started, "vendor start acknowledges live NPC and table identities");
        p.Frames.Clear(); await p.Client.RequestVendorTableAsync(vendor.Id, vendor.NpcType);
        var table = await Reply(p, ProtocolServices.Listed, "vendor table comes from the real authored catalog");
        var row = table.Listings.First(); var item = row.Item; item.Option = 1;
        p.Frames.Clear(); await p.Client.BuyVendorItemAsync(vendor.Id, vendor.NpcType, row.SourceSlot, item, 40);
        var purchase = await Reply(p, ProtocolServices.Bought, "purchase returns authoritative currency and destination item");
        Check(purchase.Taros == 1010 - (int)row.Price && purchase.Item.ItemId == item.ItemId, "purchase charges the server catalog price");
        p.Frames.Clear(); await p.Client.BuyVendorItemAsync(vendor.Id, vendor.NpcType, row.SourceSlot, item, 40);
        await Reply(p, ProtocolServices.Bought + 1, "purchase cannot overwrite an occupied inventory slot");
        foreach (int quantity in new[] { 0, -1, 2 })
        {
            p.Frames.Clear(); await p.Client.SendServiceFixtureAsync(ProtocolServices.Sell, ProtocolServices.Pair(40, quantity));
            await Reply(p, ProtocolServices.Sold + 1, "invalid sale quantity " + quantity + " is rejected without mutation");
        }
        p.Frames.Clear(); await p.Client.SellVendorItemAsync(40, 1);
        var sale = await Reply(p, ProtocolServices.Sold, "sale returns the sold item and exact remaining slot");
        Check(sale.Remaining.ItemId == 0 && sale.Item.ItemId == item.ItemId && sale.Taros > purchase.Taros, "rejected sales preserved the original item and balance");
        p.Frames.Clear(); await p.Client.BuyVendorItemAsync(vendor.Id, vendor.NpcType, 1, sale.Item, 0, true);
        await Reply(p, ProtocolServices.Restored + 1, "buyback rejects an occupied destination without losing its receipt");
        p.Frames.Clear(); await p.Client.BuyVendorItemAsync(vendor.Id, vendor.NpcType, 1, sale.Item, 40, true);
        var restore = await Reply(p, ProtocolServices.Restored, "buyback succeeds after the rejected destination");
        Check(restore.Taros == purchase.Taros && restore.Item.Equals(purchase.Item), "buyback restores exact item metadata and charges the original sale price");
        p.Frames.Clear(); await p.Client.BuyBatteryAsync(vendor.Id, vendor.NpcType, false, 1);
        var battery = await Reply(p, ProtocolServices.BatteryBought, "battery purchase returns both battery counters");
        Check(battery.Taros == restore.Taros - 100 && battery.WeaponBattery == 200, "battery purchase consumes exactly one hundred Taros");
        var types = new[] { 686, 2586, 2507, 3124, 3129 };
        for (int bank = 0; bank < types.Length; bank++)
        {
            var npc = p.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == types[bank]);
            await MoveTo(p, npc); await p.Client.OpenBankAsync(npc.Id);
            var opened = await Reply(p, ProtocolServices.BankOpened, "bank " + bank + " opens with the accepted membership identity");
            Check(opened.BankNumber == bank && opened.Bank.Count == 200 && opened.Bank.All(x => x.ItemId == 0), "bank " + bank + " has an isolated 200-slot snapshot");
            p.Frames.Clear(); await p.Client.SendItemMoveAsync(ItemContainer.Inventory, 40 + bank, ItemContainer.Bank, 199 - bank);
            await Until(() => p.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "deposit reaches high slot in bank " + bank);
            p.Frames.Clear(); await p.Client.CloseBankAsync(); await Reply(p, ProtocolServices.BankClosed, "bank " + bank + " closes with acknowledgment");
        }
        await p.Client.LeaveWorldAsync(); p.Client.Dispose(); peers.Clear(); p = await Join("Bobby");
        Check(ProtocolGameplay.I(p.Entry.RawLoadData, 104) == battery.Taros, "vendor and battery currency persist after saved reconnect");
        for (int bank = 0; bank < types.Length; bank++)
        {
            var npc = p.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == types[bank]);
            await MoveTo(p, npc); await p.Client.OpenBankAsync(npc.Id);
            var opened = await Reply(p, ProtocolServices.BankOpened, "bank " + bank + " reloads after reconnect");
            Check(opened.BankNumber == bank && opened.Bank[199 - bank].ItemId == (bank == 0 ? item.ItemId : 67)
                && opened.Bank.Count(x => x.ItemId > 0) == 1, "bank " + bank + " persists its own high slot without cross-bank leakage");
            p.Frames.Clear(); await p.Client.CloseBankAsync(); await Reply(p, ProtocolServices.BankClosed, "persisted bank " + bank + " closes");
        }
        await p.Client.LeaveWorldAsync();
    }
    static async Task Until(Func<bool> predicate, string description, int seconds = 12)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            foreach (var p in peers) while (p.Client.TryDequeueEvent(out var e))
            { if (e.Kind == NetworkEventKind.Disconnected) throw new Exception(p.Name + " disconnected: " + e.Message); if (e.IsShard) p.Accept(e.Frame); }
            if (predicate()) { Check(true, description); return; }
            await Task.Delay(15);
        }
        throw new TimeoutException(description + "; received: " + string.Join("; ", peers.Select(p => p.Name + ":" + string.Join(",", p.Frames.Select(f => $"{f.PacketType:x8}/{f.Payload.Length}")))));
    }
    static async Task<MailUpdate> MailReply(Probe p, uint type, string description)
    {
        await Until(() => p.Frames.Any(f => f.PacketType == type), description);
        ProtocolMail.TryDecode(p.Frames.Last(f => f.PacketType == type), out var u); return u;
    }
    static async Task SocialAndMail()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var a = await Join("Alice"); var b = await Join("Bobby");
        await b.Client.SendMovementAsync(a.Entry.X + 100, a.Entry.Y, a.Entry.Z, 0, 0, 0, 0, 0, 0, true);
        await Until(() => a.World.TryGet(WorldEntityKind.Player, b.Entry.PlayerId, out _), "social peers share the same interest region");
        a.Frames.Clear(); await a.Client.RequestFriendByNameAsync("NoSuch", "Unity");
        await Until(() => a.Frames.Any(f => f.PacketType == 0x310000ff), "missing friend name returns a failure instead of silently hanging");
        a.Frames.Clear(); await a.Client.RequestFriendByNameAsync("Alice", "Unity");
        await Until(() => a.Frames.Any(f => f.PacketType == 0x310000ff), "self friendship by name is rejected");
        a.Frames.Clear(); await a.Client.RequestFriendAsync(a.Entry.PlayerId, a.Entry.CharacterUid);
        await Until(() => a.Frames.Any(f => f.PacketType == 0x31000066), "self friendship by ID is rejected");
        a.Frames.Clear();
        await a.Client.RequestFriendByNameAsync("Bobby", "Unity");
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.NamedRequest), "friend request by name reaches the ordinary peer");
        await b.Client.AcceptNamedFriendAsync(a.Entry.CharacterUid, "Alice", "Unity", true);
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid) && b.Social.Friends.Any(f => f?.Uid == a.Entry.CharacterUid), "friend acceptance updates both authoritative lists");
        a.Frames.Clear(); await a.Client.RefreshFriendsAsync();
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolSocial.States), "friend presence is returned by the real login/shard route");
        var friend = a.Social.Friends.First(f => f?.Uid == b.Entry.CharacterUid);
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid && f.State == 1 && f.PlayerId == b.Entry.PlayerId), "presence keeps online state separate from the shard-local player ID");
        friend = a.Social.Friends.First(f => f?.Uid == b.Entry.CharacterUid);
        a.Frames.Clear(); b.Frames.Clear(); await a.Client.SendFriendChatAsync(friend.Slot, friend.Uid, "Unity friend whisper");
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolSocial.WhisperOk) && b.Frames.Any(f => f.PacketType == ProtocolSocial.WhisperOk), "private friend chat reaches sender and recipient");
        a.Frames.Clear(); await a.Client.InviteGroupAsync(int.MaxValue);
        await Until(() => a.Frames.Any(f => f.PacketType == 0x31000087), "invalid group target returns a failure");
        await a.Client.InviteGroupAsync(b.Entry.PlayerId);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.Invite), "a rejected target does not leave the next invitation stuck");
        await b.Client.AnswerGroupAsync(a.Entry.PlayerId, false);
        await Until(() => a.Frames.Any(f => f.PacketType == 0x31000088), "group refusal reaches the inviter");
        b.Frames.Clear(); await a.Client.InviteGroupAsync(b.Entry.PlayerId);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.Invite), "group can be invited again after refusal");
        await b.Client.AnswerGroupAsync(a.Entry.PlayerId, true);
        await Until(() => a.Social.Members.Count == 2 && b.Social.Members.Count == 2, "counted group roster reaches both members");
        Check(a.Social.Members.Any(m => m.Id == a.Entry.PlayerId && m.Name == "Alice Unity" && m.MaxHp > 0), "group roster decodes names, identities and authoritative health");
        a.Frames.Clear(); b.Frames.Clear(); await a.Client.SendGroupChatAsync("Unity group chat");
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolSocial.GroupChatOk) && b.Frames.Any(f => f.PacketType == ProtocolSocial.GroupChatOk), "group freechat is delivered to both real members");
        await b.Client.LeaveGroupAsync();
        await Until(() => a.Social.Members.Count == 0 && b.Social.Members.Count == 0, "leaving the two-player group disbands both rosters");
        a.Frames.Clear(); b.Frames.Clear(); await a.Client.RemoveFriendAsync(friend.Slot, friend.Uid, true);
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid && f.Blocked) && !b.Social.Friends.Any(f => f?.Uid == a.Entry.CharacterUid), "blocking retains the local blocked entry and removes the reciprocal friend");
        await a.Client.LeaveWorldAsync(); a.Client.Dispose(); peers.Remove(a); a = await Join("Alice");
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid && f.Blocked), "blocked friend persists across saved reconnect");
        friend = a.Social.Friends.First(f => f?.Uid == b.Entry.CharacterUid);
        await a.Client.RemoveFriendAsync(friend.Slot, friend.Uid);
        await Until(() => !a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid), "unblock removes the confirmed blocked-list slot");
        await a.Client.RequestFriendAsync(b.Entry.PlayerId, b.Entry.CharacterUid);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.Request), "nearby friend invitation uses player ID and persistent UID");
        await b.Client.AcceptFriendAsync(a.Entry.PlayerId, true);
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid) && b.Social.Friends.Any(f => f?.Uid == a.Entry.CharacterUid), "friendship can be re-established after unblocking");
        var attachment = new MailAttachment(0, b.Inventory[0]); Check(attachment.Item.ItemId == 67, "mail uses the item actually received in the earlier trade");
        var invalid = ProtocolMail.Compose(a.Entry.CharacterUid, "Invalid duplicate", "must preserve inventory", 0, new[] { attachment });
        Array.Copy(invalid, 1096, invalid, 1112, 16);
        b.Frames.Clear(); await b.Client.SendServiceFixtureAsync(ProtocolMail.Send, invalid);
        await MailReply(b, ProtocolMail.SendOk + 1, "duplicate second attachment is rejected");
        // Saved reload proves the failed packet did not silently consume its first valid attachment.
        await b.Client.LeaveWorldAsync(); b.Client.Dispose(); peers.Remove(b); b = await Join("Bobby");
        Check(b.Inventory[0].ItemId == 67, "failed mail preserves the first attachment across saved reconnect");
        b.Frames.Clear(); await b.Client.SendMailAsync(long.MaxValue, "No recipient", "must preserve inventory", 0, new[] { attachment });
        await MailReply(b, ProtocolMail.SendOk + 1, "missing mail recipient rejects the whole send");
        b.Frames.Clear(); a.Frames.Clear(); int money = b.Taros;
        await b.Client.SendMailAsync(a.Entry.CharacterUid, "Unity письмо", "Привет из RustyFusion!\nAttachment test.", 20, new[] { attachment });
        var sent = await MailReply(b, ProtocolMail.SendOk, "mail send is acknowledged with the real postage balance");
        Check(sent.Taros == money - 90, "postage charges 50 plus 20 per item plus the attached 20 Taros");
        await Until(() => b.Inventory[0].ItemId == 0 && a.Frames.Any(f => f.PacketType == ProtocolMail.New), "sender slot post-state and recipient notification arrive separately");
        await a.Client.LeaveWorldAsync(); a.Client.Dispose(); peers.Remove(a); a = await Join("Alice");
        a.Frames.Clear(); await a.Client.ListMailAsync(1);
        var page = await MailReply(a, ProtocolMail.ListOk, "mailbox page reloads from the isolated SQLite database");
        var letter = page.Summaries.Single(); Check(letter.Subject == "Unity письмо" && !letter.Read && letter.HasAttachments && letter.SenderUid == b.Entry.CharacterUid, "mail summary preserves UTF-16, unread state and sender UID");
        a.Frames.Clear(); await a.Client.ReadMailAsync(letter.Index); var read = await MailReply(a, ProtocolMail.ReadOk, "read returns body and all four attachment slots");
        Check(read.Body.Contains("Привет из RustyFusion!") && read.Items[0].ItemId == 67 && read.Taros == 20, "mail content and attached item/currency survive reconnect");
        a.Frames.Clear(); await a.Client.TakeMailItemAsync(letter.Index, 1, 1);
        // Slot 1 is empty in this fixture; fill-check coverage uses an actually occupied equipped-creation inventory if present.
        await MailReply(a, ProtocolMail.ItemOk, "one-based attachment claim returns its exact inventory destination");
        await Until(() => a.Inventory[1].ItemId == 67, "give-item packet commits the claimed item to the client inventory");
        a.Frames.Clear(); await a.Client.TakeMailItemAsync(letter.Index, 2, 1);
        await MailReply(a, ProtocolMail.ItemOk + 1, "claiming the same attachment twice is rejected");
        a.Frames.Clear(); await a.Client.TakeMailTarosAsync(letter.Index);
        var cash = await MailReply(a, ProtocolMail.TarosOk, "mail currency claim returns authoritative wallet balance");
        Check(cash.Taros == 1010, "recipient receives the attached Taros exactly once");
        a.Frames.Clear(); await a.Client.TakeMailTarosAsync(letter.Index);
        await MailReply(a, ProtocolMail.TarosOk + 1, "repeated currency claim is rejected");
        a.Frames.Clear(); await a.Client.ReadMailAsync(letter.Index); read = await MailReply(a, ProtocolMail.ReadOk, "claimed email can be read again");
        Check(read.Items.All(i => i.ItemId == 0) && read.Taros == 0, "claimed attachments are absent from the server email post-state");
        a.Frames.Clear(); await a.Client.DeleteMailAsync(letter.Index); await MailReply(a, ProtocolMail.DeleteOk, "deletion is acknowledged by email index");
        a.Frames.Clear(); await a.Client.ListMailAsync(1); page = await MailReply(a, ProtocolMail.ListOk, "deleted email is absent from the inbox"); Check(page.Summaries.Count == 0, "deleted message leaves an empty page");
        await a.Client.LeaveWorldAsync(); await b.Client.LeaveWorldAsync(); a.Client.Dispose(); b.Client.Dispose(); peers.Clear();
        a = await Join("Alice"); b = await Join("Bobby");
        await Until(() => a.Social.Friends.Any(f => f?.Uid == b.Entry.CharacterUid) && b.Social.Friends.Any(f => f?.Uid == a.Entry.CharacterUid), "mutual friendship survives both saved reconnects");
        Check(a.Taros == 1010 && a.Inventory[1].ItemId == 67 && b.Inventory[0].ItemId == 0, "mail currency and item transfer persist without duplicating the sender item");
        await a.Client.LeaveWorldAsync(); await b.Client.LeaveWorldAsync();
    }
    static async Task<TravelUpdate> TravelReply(Probe p, uint type, string description, int seconds = 15)
    {
        await Until(() => p.Frames.Any(f => f.PacketType == type), description, seconds);
        ProtocolTravel.TryDecode(p.Frames.Last(f => f.PacketType == type), out var result); return result;
    }
    static async Task WarpLoad(Probe p, bool instance)
    {
        var f = p.Frames.Last(f => f.PacketType == ProtocolTravel.WarpOk); ProtocolTravel.TryDecode(f, out var dest);
        p.World.Clear(); p.Entry = await p.Client.CompleteWarpAsync(dest, instance);
        foreach (var frame in p.Entry.BootstrapPackets) p.Accept(frame);
    }
    static async Task Travel()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var a = await Join("Trava"); var b = await Join("Travb");
        WorldEntity Npc(int type) => a.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == type);
        a.Frames.Clear(); await a.Client.CombineItemsAsync(40, 41);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolCroc.Ok), "Croc Pot responds to a real normal-account combination");
        ProtocolCroc.TryDecode(a.Frames.Last(f => f.PacketType == ProtocolCroc.Ok), out var c);
        Check(c.Taros == 100000 - 237 - 742 * 3 && c.LooksSlot == 40 && c.StatsSlot == 41, "Croc Pot charges exact authored costs and echoes both source slots");
        if (!c.Combined) Check(c.Item.ItemId == 67, "failed Croc roll returns the appearance item and charges Taros");
        int balance = c.Taros;
        for (int i = 0; i < 10; i++)
        {
            a.Frames.Clear(); await a.Client.CombineItemsAsync(42, 43);
            await Until(() => a.Frames.Any(f => f.PacketType == ProtocolCroc.Ok), "Croc Pot same-level attempt acknowledged");
            ProtocolCroc.TryDecode(a.Frames.Last(f => f.PacketType == ProtocolCroc.Ok), out c); balance -= 948;
            Check(c.Taros == balance, "Croc Pot attempt debits once, including unsuccessful rolls");
            if (c.Combined) break;
        }
        Check(c.Combined && c.Item.ItemId == 67 && c.Item.Option == (67 << 16), "successful Croc Pot retains the appearance owner and server equipment option");
        a.Frames.Clear(); await a.Client.InviteNpcAsync(Npc(651).Id); await Until(() => a.Frames.Any(f => f.PacketType == 0x310000f0), "vendor cannot be recruited as an NPC companion");
        var from = Npc(967); var to = Npc(968);
        await MoveTo(a, from); await a.Client.TravelAsync(from.Id, 5); await TravelReply(a, ProtocolTravel.TripFail, "locked destination rejects the transportation trip");
        a.Frames.Clear(); await a.Client.RegisterTransportAsync(from.Id, 1, 4); var unlocked = await TravelReply(a, ProtocolTravel.RegisterOk, "SCAMPER source registers at the exact source NPC");
        Check((unlocked.WarpFlags & 8) != 0, "SCAMPER registration uses one-based source IDs in persisted flags");
        await MoveTo(a, to); await a.Client.RegisterTransportAsync(to.Id, 1, 5); await TravelReply(a, ProtocolTravel.RegisterOk, "SCAMPER destination registers at its own NPC");
        await MoveTo(a, from); await a.Client.TravelAsync(from.Id, 5); var trip = await TravelReply(a, ProtocolTravel.TripOk, "SCAMPER trip returns destination and charged balance");
        balance -= 93; Check(trip.Transport == 1 && trip.Taros == balance && trip.X != from.X, "SCAMPER uses authored destination and exact fare");
        // Move back to the fixture's nearby source NPCs, preserving production route points and speed.
        await a.Client.SendMovementAsync(632032,187177,-5500,0,0,0,0,0,0,true);
        var land = Npc(2591); await MoveTo(a, land); await a.Client.RegisterTransportAsync(land.Id, 2, 11); await TravelReply(a, ProtocolTravel.RegisterOk, "Skyway destination registers");
        await a.Client.SendMovementAsync(444813,346594,-5264,0,0,0,0,0,0,true);
        await Until(() => a.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 995), "transport interest refresh exposes the authored Skyway source");
        var sky = Npc(995); await MoveTo(a, sky); await a.Client.RegisterTransportAsync(sky.Id, 2, 33); await TravelReply(a, ProtocolTravel.RegisterOk, "Skyway source registers");
        await MoveTo(a, sky); await a.Client.TravelAsync(sky.Id, 78); await TravelReply(a, ProtocolTravel.TripOk, "Skyway fare and departure acknowledged");
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTravel.FlightMove), "Skyway sends the production route movement segments");
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolTravel.Riding && Protocol0104.Read32(f.Payload,4)==0), "Skyway completes its real authored route and dismounts", 60);
        balance -= 141;
        await a.Client.SendMovementAsync(632032,187177,-5500,0,0,0,0,0,0,true);
        await Until(() => a.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 680), "portal NPC returns to interest after landing");
        a.Frames.Clear(); b.Frames.Clear(); await a.Client.InviteGroupAsync(b.Entry.PlayerId);
        await Until(() => b.Frames.Any(f => f.PacketType == ProtocolSocial.Invite), "travel group invitation delivered"); await b.Client.AnswerGroupAsync(a.Entry.PlayerId,true);
        await Until(() => a.Social.Members.Count==2 && b.Social.Members.Count==2, "travel party formed on both clients");
        var companion = Npc(2567); await MoveTo(a,companion); a.Frames.Clear(); await a.Client.InviteNpcAsync(companion.Id);
        await Until(() => a.Social.Npcs.Count==1 && b.Social.Npcs.Count==1, "NPC member records reach both player rosters");
        Check(a.Social.Npcs[0].Type==2567 && a.Social.Npcs[0].Hp>0, "NPC roster preserves source type and live health");
        await a.Client.KickNpcAsync(companion.Id); await Until(() => a.Social.Npcs.Count==0 && b.Social.Npcs.Count==0, "NPC removal updates both rosters");
        var gate=Npc(680); await MoveTo(a,gate); await MoveTo(b,gate); a.Frames.Clear(); b.Frames.Clear(); await a.Client.WarpNpcAsync(gate.Id,4);
        await TravelReply(a, ProtocolTravel.WarpOk, "group leader receives instance warp"); await TravelReply(b,ProtocolTravel.WarpOk,"group passenger receives unsolicited instance warp");
        await Task.WhenAll(WarpLoad(a,true),WarpLoad(b,true));
        Check(a.Entry.MapNumber==75 && b.Entry.MapNumber==75, "both loading handshakes receive authoritative instance map 75");
        await Until(() => a.World.TryGet(WorldEntityKind.Player,b.Entry.PlayerId,out _) && b.World.TryGet(WorldEntityKind.Player,a.Entry.PlayerId,out _), "group passengers regain mutual interest inside the same instance");
        foreach(var p in new[]{a,b})
        {
            await Until(() => p.World.Entities.Any(e=>e.Kind==WorldEntityKind.Npc && e.NpcType==1735), "instance exposes its authored exit NPC");
            var exit=p.World.Entities.First(e=>e.Kind==WorldEntityKind.Npc && e.NpcType==1735); await MoveTo(p,exit); await p.Client.WarpNpcAsync(exit.Id,152);
            await TravelReply(p,ProtocolTravel.WarpOk,"authored exit portal acknowledges return"); await WarpLoad(p,false); Check(p.Entry.MapNumber==0,"exit handshake returns to the overworld");
        }
        await a.Client.LeaveWorldAsync(); await b.Client.LeaveWorldAsync(); a.Client.Dispose(); b.Client.Dispose(); peers.Clear();
        a=await Join("Trava"); Check(a.Taros==balance && a.Inventory[43].ItemId==0 && a.Inventory[42].Option==(67 << 16),"fare and Croc Pot inventory post-state persist after saved reconnect");
        Check((Protocol0104.Read32(a.Entry.RawLoadData,2504)&24)==24,"registered SCAMPER destinations persist across reconnect");
        await a.Client.LeaveWorldAsync();
    }
    sealed class Probe
    {
        public string Name; public FusionClient Client; public WorldEntry Entry; public WorldEntitySession World;
        public int Hp, Taros;
        public EquipmentItem[] Inventory = new EquipmentItem[50];
        public SocialSession Social = new();
        public List<ProtocolFrame> Frames = new();
        public void Accept(ProtocolFrame f)
        {
            Frames.Add(f); World.Apply(f);
            ProtocolServices.TryDecode(f, out _);
            if (ProtocolSocial.TryDecode(f, out var social)) Social.Apply(social);
            if (ProtocolMail.TryDecode(f, out var mail) && !mail.Failed && (mail.Type == ProtocolMail.SendOk || mail.Type == ProtocolMail.TarosOk)) Taros = mail.Taros;
            if (f.PacketType == 0x31000061 && Protocol0104.Read32(f.Payload, 0) == 1) Inventory[Protocol0104.Read32(f.Payload, 4)] = ProtocolServices.Item(f.Payload, 8);
            if (ProtocolGameplay.TryCombat(f, out var c)) foreach (var hit in c.Hits) { World.SetHealth(hit.Kind, hit.Id, hit.Hp); if (hit.Kind == WorldEntityKind.Player && hit.Id == Entry.PlayerId) Hp = hit.Hp; }
            if (f.PacketType == ProtocolGameplay.Tick) { Protocol0104.RequireSize(f.Payload, 32); Hp = ProtocolGameplay.I(f.Payload, 0); }
            if (ProtocolNpcSkill.TryDecode(f, out var npcEffects)) foreach (var effect in npcEffects)
                if (effect.Hp.HasValue) { World.SetHealth(effect.Kind, effect.Id, effect.Hp.Value); if (effect.Kind == WorldEntityKind.Player && effect.Id == Entry.PlayerId) Hp = effect.Hp.Value; }
            if (ProtocolNanoSkill.TryDecode(f, out var skill)) foreach (var effect in skill.Effects) if (effect.Hp.HasValue) World.SetHealth(effect.Kind, effect.Id, effect.Hp.Value);
        }
    }
}

namespace FFOne.Network
{
    // This test-only seam is compiled solely by the .NET harness, never into the Unity player.
    public sealed partial class FusionClient
    { public Task SendServiceFixtureAsync(uint type, byte[] payload) => SendGameplayAsync(type, payload, default); }
}
