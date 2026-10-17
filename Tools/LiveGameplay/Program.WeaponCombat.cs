using FFOne.Network;

static partial class Program
{
    static async Task CreateWeaponCombat()
    {
        // A throwaway bootstrap account absorbs the server's first-account
        // Master privilege; all three fixture characters start ordinary.
        using (var bootstrap = new FusionClient()) await bootstrap.LoginAsync("127.0.0.1", port, "unityweaponmaster", "test-only-password");
        foreach (string name in new[] { "Weapa", "Weapb", "Weapgm" })
        {
            using var client = new FusionClient(); await client.LoginAsync("127.0.0.1", port, "unity" + name.ToLowerInvariant(), "test-only-password");
            await client.CreateCharacterAsync(new CharacterCreation { FirstName = name, LastName = "Unity", Slot = 1,
                UpperBodyId = 67, LowerBodyId = 47, FootId = 34, UpperBodyIndex = 2, LowerBodyIndex = 2, FootIndex = 2, FaceStyleIndex = 2, HairStyleIndex = 2 });
            Check(true, name + " disposable weapon fixture created");
        }
    }
    static async Task WeaponCombat()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var a = await Join("Weapa"); var b = await Join("Weapb");
        await Until(() => a.World.TryGet(WorldEntityKind.Player, b.Entry.PlayerId, out _), "weapon peers enter the same live instance");
        Check(Protocol0104.Read16(a.Entry.RawLoadData, 0) == 99 && Protocol0104.Read16(b.Entry.RawLoadData, 0) == 99, "weapon clients retain native normal-account privilege 99");
        int initialHp = b.Hp; a.Frames.Clear(); b.Frames.Clear();
        await a.Client.AttackCharactersAsync(new[] { (WorldEntityKind.Player, b.Entry.PlayerId) }); await PumpWeaponFrames(500);
        Check(b.Hp == initialHp && !a.Frames.Any(f => f.PacketType == 0x31000102) && !b.Frames.Any(f => f.PacketType == 0x31000103), "ordinary account character attack is denied without player damage or success");
        var weapons = new[] { (mode: 1, id: 706), (mode: 2, id: 621), (mode: 3, id: 612), (mode: 4, id: 625), (mode: 5, id: 781), (mode: 6, id: 22) };
        int casterX = a.Entry.X, casterY = a.Entry.Y, casterZ = a.Entry.Z;
        for (int i = 0; i < weapons.Length; i++)
        {
            await MoveWeaponPeers(a, b, casterX, casterY, casterZ, "weapon family " + weapons[i].mode + " restores mutual observer interest before equip");
            var weapon = weapons[i]; a.Frames.Clear(); b.Frames.Clear();
            await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 20 + i, ItemContainer.Equipment, 0);
            await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess) && b.Frames.Any(f => f.PacketType == ProtocolInventory.EquipmentChanged), "weapon family " + weapon.mode + " equips through server inventory and peer appearance");
            await PumpWeaponFrames(2100);
            var target = a.World.Entities.Where(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 59 && e.Hp > 0).OrderBy(e => e.X).First();
            casterX = target.X - 100; casterY = target.Y; casterZ = target.Z;
            await MoveWeaponPeers(a, b, casterX, casterY, casterZ, "weapon family " + weapon.mode + " moves caster and real observer into mutual attack interest");
            a.Frames.Clear(); b.Frames.Clear(); int beforeHp = target.Hp;
            if (weapon.mode < 5)
            {
                await a.Client.AttackNpcsAsync(new[] { target.Id });
                await Until(() => a.Frames.Any(f => f.PacketType == ProtocolGameplay.AttackSuccess) && b.Frames.Any(f => f.PacketType == ProtocolGameplay.AttackBroadcast), "weapon family " + weapon.mode + " damage ACK and observer result");
            }
            else
            {
                int fromX = target.X - 100; await a.Client.FireProjectileAsync(weapon.mode, fromX, target.Y, target.Z, target.X, target.Y, target.Z);
                uint success = weapon.mode == 5 ? ProtocolProjectiles.RocketSuccess : ProtocolProjectiles.GrenadeSuccess;
                uint broadcast = weapon.mode == 5 ? ProtocolProjectiles.RocketPeer : ProtocolProjectiles.GrenadePeer;
                await Until(() => a.Frames.Any(f => f.PacketType == success) && b.Frames.Any(f => f.PacketType == broadcast), "projectile family " + weapon.mode + " fire ACK and observer trajectory");
                ProtocolProjectiles.TryDecode(a.Frames.Last(f => f.PacketType == success), out var fired);
                Check(fired.Mode == weapon.mode && fired.WeaponId == (weapon.mode == 6 ? 1 : weapon.id) && fired.Slot >= 0 && fired.Slot < 3 && fired.WeaponBattery.HasValue,
                    "projectile family " + weapon.mode + " native bullet identity slot and post-cost battery");
                await a.Client.HitProjectileAsync(weapon.mode, fired.Slot, target.X, target.Y, target.Z, new[] { target.Id });
                // The accepted server follows OpenFusion's GRENADE_HIT
                // broadcast for both original projectile styles.
                uint detonate = ProtocolProjectiles.GrenadeDetonate;
                await Until(() => a.Frames.Any(f => f.PacketType == detonate) && b.Frames.Any(f => f.PacketType == detonate), "projectile family " + weapon.mode + " authoritative blast reaches owner and observer");
                int count = a.Frames.Count(f => f.PacketType == detonate);
                await a.Client.HitProjectileAsync(weapon.mode, fired.Slot, target.X, target.Y, target.Z, new[] { target.Id }); await PumpWeaponFrames(150);
                Check(a.Frames.Count(f => f.PacketType == detonate) == count, "projectile family " + weapon.mode + " consumed slot cannot damage twice");
            }
            Check(a.Frames.Where(f => ProtocolGameplay.TryCombat(f, out _)).SelectMany(f => { ProtocolGameplay.TryCombat(f, out var result); return result.Hits; }).Any(hit => hit.Id == target.Id && hit.Damage > 0 && hit.Hp < beforeHp), "weapon family " + weapon.mode + " reports real target damage and health");
        }
        await b.Client.SendMovementAsync(b.Entry.X, b.Entry.Y, b.Entry.Z, 0, 0, 0, 0, 0, 0, true);
        await PumpWeaponFrames(100);
        await a.Client.LeaveWorldAsync(); a.Client.Dispose(); peers.Remove(a);
        var gm = await Join("Weapgm");
        await Until(() => gm.World.TryGet(WorldEntityKind.Player, b.Entry.PlayerId, out _), "disposable GM and target share interest");
        Check(Protocol0104.Read16(gm.Entry.RawLoadData, 0) == 30, "only dedicated disposable GM fixture has source permission 30");
        await gm.Client.SendMovementAsync(b.Entry.X, b.Entry.Y, b.Entry.Z, 0, 0, 0, 0, 0, 0, true);
        gm.Frames.Clear(); b.Frames.Clear(); int pvpHp = b.Hp;
        await gm.Client.AttackCharactersAsync(new[] { (WorldEntityKind.Player, b.Entry.PlayerId) });
        await Until(() => gm.Frames.Any(f => f.PacketType == 0x31000102) && b.Frames.Any(f => f.PacketType == 0x31000103) && b.Hp < pvpHp,
            "source-permitted GM character attack damages real target and broadcasts native post-state");
        await gm.Client.LeaveWorldAsync(); await b.Client.LeaveWorldAsync(); gm.Client.Dispose(); b.Client.Dispose(); peers.Clear();
    }
    static async Task MoveWeaponPeers(Probe caster, Probe observer, int x, int y, int z, string description)
    {
        // The real observer follows each combat location, outside the original
        // mob's sight radius. It must witness movement before equip/fire checks.
        await caster.Client.SendMovementAsync(x, y, z, 0, 0, 0, 0, 0, 0, true);
        await observer.Client.SendMovementAsync(x, y + 2000, z, 0, 0, 0, 0, 0, 0, true);
        await Until(() => caster.World.TryGet(WorldEntityKind.Player, observer.Entry.PlayerId, out var peer) && peer.X == x && peer.Y == y + 2000 && peer.Z == z &&
            observer.World.TryGet(WorldEntityKind.Player, caster.Entry.PlayerId, out var owner) && owner.X == x && owner.Y == y && owner.Z == z, description);
    }
    static async Task PumpWeaponFrames(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until) { foreach (var p in peers) while (p.Client.TryDequeueEvent(out var e)) if (e.IsShard && e.Frame != null) p.Accept(e.Frame); await Task.Delay(10); }
    }
}
