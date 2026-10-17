using FFOne.Network;

static partial class Program
{
    static async Task Recall()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var p = await Join("Trava");
        await p.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await Until(() => p.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 680), "Recall fixture reaches the original instance entrance");
        var gate = p.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 680);
        await MoveTo(p, gate); p.Frames.Clear(); await p.Client.WarpNpcAsync(gate.Id, 4);
        await TravelReply(p, ProtocolTravel.WarpOk, "Recall fixture enters instance 75 through the server portal");
        await WarpLoad(p, true); Check(p.Entry.MapNumber == 75, "Recall registration fixture uses authoritative map 75");
        int entranceX = p.Entry.X, entranceY = p.Entry.Y, entranceZ = p.Entry.Z;
        await p.Client.SendMovementAsync(130715, 223861, -16066, 0, 0, 0, 0, 0, 0, true);
        await Until(() => p.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1386 && e.X == 130715 && e.Y == 223861 && e.Z == -16066),
            "original Recall placement 569 reaches server interest without spawning a replacement");
        var point = p.World.Entities.Single(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1386 && e.X == 130715 && e.Y == 223861 && e.Z == -16066);
        async Task<RecallRegistration> Register(int id, bool failed, string description)
        {
            p.Frames.Clear(); await p.Client.RegisterRecallPointAsync(id);
            await Until(() => p.Frames.Any(f => f.PacketType == (failed ? ProtocolRecall.Fail : ProtocolRecall.Success)), description);
            var frame = p.Frames.Last(f => f.PacketType == (failed ? ProtocolRecall.Fail : ProtocolRecall.Success));
            Check(ProtocolRecall.TryDecode(frame, out var reply) && reply.Failed == failed, "Recall registration reply obeys the exact 0104 ABI");
            return reply;
        }
        var accepted = await Register(point.Id, false, "RustyFusion accepts registration at the real Recall Point");
        Check(accepted.NpcId == point.Id && accepted.X == point.X && accepted.Y == point.Y && accepted.Z == point.Z,
            "Recall ACK contains the network NPC identity and authored coordinates, independently of map 75 and type 1386");
        var repeated = await Register(point.Id, false, "RustyFusion acknowledges repeated Recall registration");
        Check(repeated.NpcId == accepted.NpcId, "repeat registration keeps the exact selected NPC identity");
        await Register(int.MaxValue, true, "unknown Recall NPC receives server failure");
        await p.Client.SendMovementAsync(point.X + 2000, point.Y, point.Z, 0, 0, 0, 0, 0, 0, true);
        await Register(point.Id, true, "Recall registration outside interaction range receives server failure");
        await MoveTo(p, point); await Register(point.Id, false, "Recall registration succeeds after returning to the original point");
        Check(Protocol0104.Read16(p.Entry.RawLoadData, 1668) == 9 && Protocol0104.Read16(p.Entry.RawLoadData, 1444 + 9 * 6 + 2) == 25,
            "Recall fixture owns original Nano 9 tuned to its accepted Recall skill 25 in carry slot 2");
        p.Frames.Clear(); await p.Client.ActivateNanoAsync(1);
        await Until(() => p.Frames.Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess && Protocol0104.Read16(f.Payload, 0) == 1),
            "real Recall Nano activates through the production server handler");
        await p.Client.SendMovementAsync(point.X + 400, point.Y, point.Z, 0, 0, 0, 0, 0, 0, true);
        p.Frames.Clear(); await p.Client.UseNanoSkillAsync(Array.Empty<int>());
        await Until(() => p.Frames.Any(f => f.PacketType == 0x31000031), "registered Recall Nano power returns the player through real GOTO_SUCC");
        var recalled = Protocol0104.DecodePlayerPosition(p.Frames.Last(f => f.PacketType == 0x31000031).Payload);
        Check(recalled.X == point.X && recalled.Y == point.Y && recalled.Z == point.Z,
            "Recall power returns to registered NPC coordinates rather than the pre-warp overworld entrance");
        Check(!p.Frames.Any(f => f.PacketType == ProtocolNanoSkill.Success), "self Recall preserves the original ABI with no invalid empty skill-success trailer");
        await p.Client.ActivateNanoAsync(-1);
        await Until(() => p.Frames.Any(f => f.PacketType == ProtocolGameplay.NanoActiveSuccess && Protocol0104.Read16(f.Payload, 0) == -1),
            "Recall Nano deactivates after the successful live return");
        await p.Client.SendMovementAsync(entranceX, entranceY, entranceZ, 0, 0, 0, 0, 0, 0, true);
        await Until(() => p.World.Entities.Any(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1735), "Recall fixture retains its authored exit");
        var exit = p.World.Entities.First(e => e.Kind == WorldEntityKind.Npc && e.NpcType == 1735);
        await MoveTo(p, exit);
        await Register(exit.Id, true, "nearby exit portal cannot be registered as a Recall Point");
        p.Frames.Clear(); await p.Client.WarpNpcAsync(exit.Id, 152);
        await TravelReply(p, ProtocolTravel.WarpOk, "registered player leaves the instance through its authored exit");
        await WarpLoad(p, false); Check(p.Entry.MapNumber == 0, "Recall fixture returns to the overworld");
        await Register(point.Id, true, "Recall NPC from the previous instance cannot be registered from the overworld");
        await p.Client.LeaveWorldAsync(); p.Client.Dispose(); peers.Clear();
    }
}
