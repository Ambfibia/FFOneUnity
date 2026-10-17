using System.Text.Json;
using FFOne.Network;

static partial class Program
{
    // Separate modes keep the instance matrix independent from the Alice/Bobby economy fixture.
    static async Task Instances(string[] args)
    {
        var plan = JsonSerializer.Deserialize<InstancePlan>(File.ReadAllText(args[3]), InstanceJson);
        var results = new List<object>();
        bool passed = true;
        try
        {
            if (args[0] == "create-instances")
            {
                // The server grants its first account administrative privileges.
                // Reserve that account before creating any matrix character.
                using var administrator = new FusionClient();
                await administrator.LoginAsync("127.0.0.1", port, "unityinstanceadmin", "test-only-password");
            }
            foreach (var item in plan.cases)
            {
                if (item.disposition != "eligible")
                { results.Add(new { item.map, item.cell, status = "blocked", item.reason, item.invalidDestinationWarps }); continue; }
                if (args[0] == "create-instances")
                {
                    using var client = new FusionClient();
                    var roster = await client.LoginAsync("127.0.0.1", port, item.account, "test-only-password");
                    var created = roster.Count == 0 ? await client.CreateCharacterAsync(new CharacterCreation { FirstName = item.name, LastName = "Instance", Slot = 1,
                        UpperBodyId = 67, LowerBodyId = 47, FootId = 34, UpperBodyIndex = 2, LowerBodyIndex = 2, FootIndex = 2, FaceStyleIndex = 2, HairStyleIndex = 2 }) : roster.Single();
                    Check(created.FirstName == item.name && created.Slot == 1, $"instance {item.map}: isolated character identity is exact");
                    Check(created.Style.AppearanceComplete, $"instance {item.map}: normal character created");
                    results.Add(new { item.map, status = "created", created.Uid });
                    continue;
                }
                int start = checks.Count;
                try
                {
                    await ExerciseInstance(item);
                    results.Add(new { item.map, item.cell, status = "passed", item.exitMode, checks = checks.Skip(start).ToArray(),
                        entranceWarps = item.entry.Select(e => e.warp), exitWarps = item.exit.Select(e => e.warp), item.level, item.tasks });
                }
                catch (Exception error)
                {
                    passed = false;
                    results.Add(new { item.map, item.cell, status = "failed", error = error.ToString(), checks = checks.Skip(start).ToArray() });
                    Console.Error.WriteLine($"INSTANCE {item.map}: {error}");
                }
                finally
                {
                    foreach (var peer in peers.ToArray())
                    {
                        try { if (peer.Client.IsInWorld) await peer.Client.LeaveWorldAsync(); } catch { }
                        peer.Client.Dispose();
                    }
                    peers.Clear();
                }
                // A completed row remains reviewable even if the next scene or server fails.
                WriteInstances();
            }
        }
        catch (Exception error) { passed = false; results.Add(new { status = "runner-failed", error = error.ToString() }); }
        WriteInstances();
        if (!passed) Environment.ExitCode = 1;

        void WriteInstances() => File.WriteAllText(args[2], JsonSerializer.Serialize(new {
            passed, coverageComplete = plan.cases.All(c => c.disposition == "eligible") && passed,
            source = plan.source, maps = plan.cases.Length, eligible = plan.cases.Count(c => c.disposition == "eligible"),
            blocked = plan.cases.Count(c => c.disposition != "eligible"), results
        }, InstanceJson));
    }

    static readonly JsonSerializerOptions InstanceJson = new() { IncludeFields = true, WriteIndented = true, PropertyNameCaseInsensitive = true };
    sealed class InstancePlan { public string source; public InstanceCase[] cases; }
    sealed class InstanceCase
    {
        public int map, level; public string cell, account, name, disposition, reason, exitMode;
        public int[] tasks; public InstanceEdge[] entry, exit; public InstancePoint reconnectPosition;
        public bool sequentialMission;
        public object[] invalidDestinationWarps;
    }
    sealed class InstancePoint { public int x, y, z; }
    sealed class InstanceEdge
    {
        public int fromMap, map, warp, npcType, npcX, npcY, npcZ, x, y, z, cost, task;
        public int requiredType, requiredItem, requiredSlot, consumedSlot;
        public bool instance;
        public InstanceMissionAction[] beforeMission, afterMission;
    }

    static async Task<Probe> JoinInstance(InstanceCase item)
    {
        var client = new FusionClient();
        var roster = await client.LoginAsync("127.0.0.1", port, item.account, "test-only-password");
        var entry = await client.EnterWorldAsync(roster.Single().Uid); await client.CompleteLoadingAsync();
        var probe = new Probe { Name = item.account, Client = client, Entry = entry, World = new WorldEntitySession(entry.PlayerId), Hp = entry.Hp,
            Taros = Protocol0104.Read32(entry.RawLoadData, 104) };
        peers.Add(probe);
        foreach (var frame in entry.BootstrapPackets ?? Array.Empty<ProtocolFrame>()) probe.Accept(frame);
        return probe;
    }

    static async Task ExerciseInstance(InstanceCase item)
    {
        var p = await JoinInstance(item);
        Check(p.Entry.MapNumber == 0 && p.Entry.Level == item.level, $"instance {item.map}: normal fixture enters its authored overworld source with level {item.level}");
        foreach (int task in item.tasks)
            Check(Enumerable.Range(0, 9).Any(i => ProtocolGameplay.I(p.Entry.RawLoadData, 2032 + i * 52) == task), $"instance {item.map}: required task {task} survives normal journal loading");
        await InstanceRoute(p, item, item.entry);
        Check(p.Entry.MapNumber == item.map, $"instance {item.map}: authoritative loading handshake identifies the destination independently of shared geometry");
        if (item.sequentialMission)
            await InstanceRoute(p, item, item.exit);
        await p.Client.LeaveWorldAsync(); p.Client.Dispose(); peers.Remove(p);
        p = await JoinInstance(item);
        var finalExit = item.exit.LastOrDefault();
        var point = item.sequentialMission && finalExit != null
            ? new InstancePoint { x = finalExit.x, y = finalExit.y, z = finalExit.z } : item.reconnectPosition;
        Check(p.Entry.MapNumber == 0 && p.Entry.X == point.x && p.Entry.Y == point.y && p.Entry.Z == point.z,
            $"instance {item.map}: saved exit and reconnect restore the original overworld portal, including nested instances");
        if (item.exit.Length > 0)
        {
            await InstanceRoute(p, item, item.entry);
            await InstanceRoute(p, item, item.exit);
            Check(p.Entry.MapNumber == 0, $"instance {item.map}: actual authored exit route completes the overworld loading handshake");
            var last = item.exit.Last();
            await p.Client.LeaveWorldAsync(); p.Client.Dispose(); peers.Remove(p);
            p = await JoinInstance(item);
            Check(p.Entry.MapNumber == 0 && p.Entry.X == last.x && p.Entry.Y == last.y && p.Entry.Z == last.z,
                $"instance {item.map}: authored exit position persists across another saved reconnect");
        }
        await p.Client.LeaveWorldAsync();
    }

    static async Task InstanceRoute(Probe p, InstanceCase item, IEnumerable<InstanceEdge> route)
    {
        foreach (var edge in route)
        {
            Check(p.Entry.MapNumber == edge.fromMap, $"instance {item.map}: warp {edge.warp} starts on its original map {edge.fromMap}");
            await BeforeInstanceWarp(p, item, edge);
            await p.Client.SendMovementAsync(edge.npcX, edge.npcY, edge.npcZ, 0, 0, 0, 0, 0, 0, true);
            WorldEntity npc = null;
            await Until(() => (npc = p.World.Entities.FirstOrDefault(e => e.Kind == WorldEntityKind.Npc && e.NpcType == edge.npcType &&
                Math.Abs(e.X - edge.npcX) < 500 && Math.Abs(e.Y - edge.npcY) < 500)) != null,
                $"instance {item.map}: original portal NPC {edge.npcType} enters interest on map {edge.fromMap}");
            p.Frames.Clear();
            await p.Client.WarpNpcAsync(npc.Id, edge.warp, edge.requiredItem > 0 ? (edge.requiredType == 8 ? 2 : 1) : 4, edge.requiredSlot, edge.consumedSlot);
            await Until(() => p.Frames.Any(f => f.PacketType == ProtocolTravel.WarpOk || f.PacketType == ProtocolTravel.WarpFail),
                $"instance {item.map}: warp {edge.warp} returns an authoritative decision");
            Check(!p.Frames.Any(f => f.PacketType == ProtocolTravel.WarpFail), $"instance {item.map}: original prerequisites admit warp {edge.warp}");
            var reply = await TravelReply(p, ProtocolTravel.WarpOk, $"instance {item.map}: warp {edge.warp} acknowledges the destination");
            Check(reply.X == edge.x && reply.Y == edge.y && reply.Z == edge.z, $"instance {item.map}: warp {edge.warp} preserves exact authored destination coordinates");
            await WarpLoad(p, edge.instance);
            Check(p.Entry.MapNumber == edge.map && p.Client.IsWorldLoaded, $"instance {item.map}: warp {edge.warp} completes loading for map {edge.map}");
            await AfterInstanceWarp(p, item, edge);
        }
    }
}
