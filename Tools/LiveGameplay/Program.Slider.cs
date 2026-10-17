using FFOne.Network;

static partial class Program
{
    static async Task Sliders()
    {
        foreach (var old in peers) old.Client.Dispose(); peers.Clear();
        var a = await Join("Alice"); var b = await Join("Bobby");
        foreach (var p in new[] { a, b }) await p.Client.SendMovementAsync(220450, 162484, -3502, 0, 0, 0, 0, 0, 0, true);
        await Until(() => a.World.Entities.Any(e => e.Kind == WorldEntityKind.Transportation), "Slider enters the production transportation interest set", 60);
        var slider = a.World.Entities.First(e => e.Kind == WorldEntityKind.Transportation);
        int id = slider.Id, beforeX = slider.X, beforeY = slider.Y;
        await Until(() => a.World.TryGet(WorldEntityKind.Transportation, id, out var s) && (s.X != beforeX || s.Y != beforeY), "Slider follows original server circuit without synthesized path", 30);
        a.World.TryGet(WorldEntityKind.Transportation, id, out slider);
        await a.Client.SendSliderMovementAsync(id, 0, 0, 200, slider.X, slider.Y, slider.Z + 200, 0, 0, 0, 0, 1200, 0);
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var rider) && rider.TransportId == id && rider.LocalZ == 200,
            "Slider passenger relative coordinates broadcast through the real server");
        b.Frames.Clear();
        await a.Client.SendMovementAsync(slider.X + 500, slider.Y, slider.Z, 0, 0, 0, 0, 0, 0, true);
        await Until(() => b.World.TryGet(WorldEntityKind.Player, a.Entry.PlayerId, out var rider) && rider.TransportId == 0,
            "ordinary movement releases the peer passenger attachment");
        foreach (var p in new[] { a, b }) await p.Client.SendMovementAsync(632032, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
        await Until(() => !a.World.Entities.Any(e => e.Kind == WorldEntityKind.Transportation), "Slider exits the interest set when player leaves the circuit");
        foreach (var p in new[] { a, b }) await p.Client.LeaveWorldAsync();
    }
}
