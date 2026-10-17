using FFOne.Network;

static partial class Program
{
    static async Task PersonalVehicles()
    {
        foreach (var p in peers) p.Client.Dispose(); peers.Clear();
        var a = await Join("Alice"); var b = await Join("Bobby");
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out _), "vehicle peers share authoritative interest");
        a.Frames.Clear(); await a.Client.SetVehicleAsync(true);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OnFailure), "personal mount without equipment receives the real server failure");
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 47, ItemContainer.Equipment, 8);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "board equips in source slot eight");
        a.Frames.Clear(); b.Frames.Clear(); await a.Client.SetVehicleAsync(true);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OnSuccess) && b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var actor) && (actor.StateFlags & 8) != 0,
            "one-byte vehicle mount is acknowledged and peer mounted flag replicates");
        var x = a.Entry.X + 250;
        await a.Client.SendMovementAsync(x, a.Entry.Y, a.Entry.Z, 90, 7.5f, 0, 0, 750, 3, false);
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var actor) && actor.X == x && actor.Speed == 750 && (actor.StateFlags & 8) != 0,
            "personal vehicle movement retains authoritative mounted state and table speed");
        a.Frames.Clear(); await a.Client.SetVehicleAsync(false);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OffSuccess) && b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var actor) && (actor.StateFlags & 8) == 0,
            "one-byte dismount clears peer mounted state");
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 48, ItemContainer.Equipment, 8);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "jetbike swaps with the board using authoritative item movement");
        a.Frames.Clear(); await a.Client.SetVehicleAsync(true);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OnSuccess), "scooter family mount is server-confirmed");
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Equipment, 8, ItemContainer.Inventory, 47);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OffSuccess) && a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess) &&
            b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var actor) && (actor.StateFlags & 8) == 0,
            "moving the equipped vehicle forces a server dismount before its item receipt");
        // Restore both fixture slots for the Unity player acceptance run.
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 47, ItemContainer.Inventory, 48);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "vehicle acceptance restores board and jetbike fixture identities");
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Inventory, 47, ItemContainer.Equipment, 8);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "instance mount probe retains equipped board identity");
        await Until(() => a.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 680), "instance vehicle probe resolves the authored entrance");
        var gate = a.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 680); await MoveTo(a, gate);
        a.Frames.Clear(); await a.Client.WarpNpcAsync(gate.Id, 4); await TravelReply(a, ProtocolTravel.WarpOk, "vehicle probe enters an authored instance");
        await WarpLoad(a, true);
        a.Frames.Clear(); await a.Client.SetVehicleAsync(true);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolVehicle.OnFailure), "equipped personal vehicle is rejected by the server inside an instance");
        await Until(() => a.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1735), "vehicle instance probe resolves its authored exit");
        var exit = a.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1735); await MoveTo(a, exit);
        a.Frames.Clear(); await a.Client.WarpNpcAsync(exit.Id, 152); await TravelReply(a, ProtocolTravel.WarpOk, "vehicle instance probe returns through the normal exit");
        await WarpLoad(a, false);
        a.Frames.Clear(); await a.Client.SendItemMoveAsync(ItemContainer.Equipment, 8, ItemContainer.Inventory, 47);
        await Until(() => a.Frames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "instance rejection leaves vehicle ownership intact");
        await a.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await a.Client.LeaveWorldAsync(); await b.Client.LeaveWorldAsync();
    }
}
