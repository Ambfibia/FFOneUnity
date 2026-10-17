#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task Travel()
        {
            NetworkEntityView Npc(int type) => frontend.LiveWorld.Replicas.Actors.First(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == type);
            async Task Open(int type)
            {
                var npc = Npc(type); await frontend.LiveMoveTo(npc.Entity.X, npc.Entity.Y, npc.Entity.Z);
                await Wait(() => frontend.LiveWorld.IsReady, "travel NPC scenery is ready");
                Check(frontend.LiveUi.OpenNpcTravel(npc.Entity, npc.Definition), "native NPC travel window opens for type " + type);
                await Frames(4);
            }
            frontend.LiveUi.OpenCroc(); await Frames(4);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-looks-40")); Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-stats-41")); await Frames(4);
            Check(frontend.LiveUi.Windows.View.Q<Button>("croc-combine") is { enabledSelf: false }, "native Croc Pot shows the exact price and blocks insufficient Taros");
            Capture(uiTexture, "croc-pot"); frontend.LiveUi.Windows.Close();
            // Fund a successful UI attempt through normal player mail. This leaves the
            // preceding low-wallet check meaningful and exercises the same production path.
            using (var donor = new FusionClient())
            {
                var endpoint = LoginServer.Parse(System.Environment.GetEnvironmentVariable(FFOne.Integration.DevelopmentConnection.ServerVariable)).Endpoint;
                var roster = await donor.LoginAsync("127.0.0.1", endpoint.Port, "unitytrava", "test-only-password");
                await donor.EnterWorldAsync(roster.Single().Uid); await donor.CompleteLoadingAsync();
                await donor.SendMailAsync(frontend.LiveState.Snapshot.CharacterUid, "Croc Pot", "Acceptance Taros", 15000, System.Array.Empty<MailAttachment>());
                await Wait(() => frontend.LiveUi.UnreadMail > 0, "normal donor sends Croc Pot funds through production mail");
                frontend.LiveUi.OpenMail(); await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveUi.Windows.View.Query<Button>().ToList().Any(b => b.name.StartsWith("mail-row-")), "Croc Pot funding letter appears in the native inbox");
                Submit(frontend.LiveUi.Windows.View.Query<Button>().ToList().Single(b => b.name.StartsWith("mail-row-")));
                await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveUi.Windows.View.Q<Button>("mail-take-taros").enabledSelf, "native funding letter opens");
                int before = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-take-taros"));
                await Wait(() => !frontend.LiveUi.MailBusy && frontend.LiveState.Snapshot.Taros == before + 15000, "native wallet claims the real Croc Pot funding");
                Submit(frontend.LiveUi.Windows.View.Q<Button>("mail-delete")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
                await Wait(() => !frontend.LiveUi.MailBusy && !frontend.LiveUi.Windows.View.Query<Button>().ToList().Any(b => b.name.StartsWith("mail-row-")), "funding message is deleted after the claim");
                frontend.LiveUi.Windows.Close(); await donor.LeaveWorldAsync();
            }
            for (int attempt = 0; attempt < 10 && !frontend.LiveState.Snapshot.Inventory[1].IsEmpty; attempt++)
            {
                frontend.LiveUi.OpenCroc(); Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-looks-40")); Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-stats-1"));
                int before = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-combine")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
                await Wait(() => !frontend.LiveUi.TravelBusy && frontend.LiveState.Snapshot.Taros == before - 948, "native Croc Pot confirms an attempt and applies exactly one charge");
                await Frames(4); Capture(uiTexture, "croc-result"); frontend.LiveUi.Windows.Close();
            }
            Check(frontend.LiveState.Snapshot.Inventory[1].IsEmpty && frontend.LiveState.Snapshot.Inventory[40].Option == (67 << 16), "native Croc Pot success consumes only the stats slot and preserves exact appearance metadata");
            foreach (var pair in new[] { (967, 4), (968, 5) })
            {
                await Open(pair.Item1); Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-register-1-" + pair.Item2));
                await Wait(() => !frontend.LiveUi.TravelBusy, "native SCAMPER registration acknowledged"); frontend.LiveUi.Windows.Close();
            }
            await Open(967); await Frames(4); Capture(uiTexture, "transport");
            int wallet = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-trip-5"));
            Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveState.Snapshot.Taros == wallet - 93 && !frontend.LiveUi.Windows.IsOpen, "native SCAMPER trip applies its fare and closes the route window");
            var stop = WorldTravelCatalog.Load().Locations.Single(l => l.Type == 1 && l.Id == 5);
            Check(Vector3.Distance(frontend.LiveWorld.Player.position, WorldCoordinates.FromServer(stop.X, stop.Y, stop.Z)) < .2f, "native route reaches its exact authored destination");
            await frontend.LiveMoveTo(632032, 187177, -5500); await Wait(() => frontend.LiveWorld.IsReady, "overworld returns to the acceptance NPC area");
            await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.NpcType == 2591), "Skyway destination registrar enters native interest");
            await Open(2591); Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-register-2-11"));
            await Wait(() => !frontend.LiveUi.TravelBusy, "native Skyway destination registers"); frontend.LiveUi.Windows.Close();
            await frontend.LiveMoveTo(444813, 346594, -5264);
            await Wait(() => frontend.LiveWorld.IsReady && frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.NpcType == 995), "native world admits the actual Skyway source cell");
            await Open(995); Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-register-2-33"));
            await Wait(() => !frontend.LiveUi.TravelBusy, "native Skyway source registers");
            wallet = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-trip-78")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveWorld.Riding && frontend.LiveUi.TravelBusy, "native Skyway flight locks ground movement and financial actions");
            await Wait(() => !frontend.LiveWorld.Riding && !frontend.LiveUi.TravelBusy && frontend.LiveWorld.IsReady, "native Skyway finishes its unchanged authored route and restores the world motor");
            Check(frontend.LiveState.Snapshot.Taros == wallet - 141, "native Skyway applies its exact fare");
            await Frames(8); Capture(worldTexture, "skyway-arrival");
            await frontend.LiveMoveTo(632032, 187177, -5500); await Wait(() => frontend.LiveWorld.IsReady, "native world returns from the Skyway route");
            await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.NpcType == 680), "portal appears in the refreshed native interest set");
            // Build a two-player party through the real social action and server acknowledgement.
            peerFrames.Clear(); await frontend.Session.InviteGroupAsync(peer.World.PlayerId);
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolSocial.Invite), "native travel invitation reaches the peer"); await peer.AnswerGroupAsync(frontend.LiveState.Snapshot.PlayerId, true);
            await Wait(() => frontend.LiveUi.Social.Members.Count == 2, "native travel party is authoritative");
            await Open(2567);
            await Wait(() => Npc(2567).GetComponentsInChildren<Renderer>().Length > 0, "native NPC companion renders its accepted Ice King model through the source alias");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("npc-group-action"));
            await Wait(() => frontend.LiveUi.Social.Npcs.Count == 1, "native NPC group action applies its counted roster");
            Check(frontend.LiveUi.Windows.View.Q<Button>("npc-group-action").text == "УБРАТЬ", "NPC interaction switches to removal after the acknowledged invitation");
            frontend.LiveUi.Windows.Close(); frontend.LiveUi.OpenSocial(); await Frames(4); Capture(uiTexture, "group-npc");
            var companion = frontend.LiveUi.Social.Npcs.Single(); Submit(frontend.LiveUi.Windows.View.Q<Button>("group-npc-kick-" + companion.Id));
            await Wait(() => frontend.LiveUi.Social.Npcs.Count == 0, "native group roster removes the selected NPC"); frontend.LiveUi.Windows.Close();
            var gate = Npc(680).Entity; await peer.SendMovementAsync(gate.X, gate.Y, gate.Z, 0, 0, 0, 0, 0, 0, true);
            await Open(680); peerFrames.Clear(); Submit(frontend.LiveUi.Windows.View.Q<Button>("npc-warp-4")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolTravel.WarpOk), "group warp reaches the native client's passenger");
            ProtocolTravel.TryDecode(peerFrames.Last(f => f.PacketType == ProtocolTravel.WarpOk), out var destination);
            await peer.CompleteWarpAsync(destination, true);
            await Wait(() => frontend.LiveWorld.MapNumber == 75 && frontend.LiveWorld.IsReady && frontend.LiveWorld.MovementEnabled, "native instance cell and colliders become ready after the loading handshake");
            await Wait(() => frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peer.World.PlayerId), "native instance renders the group passenger in the same copy");
            Check(frontend.LiveWorld.Stream.LoadedSceneTileIds.SequenceEqual(new[] { "map_02_04" }) && frontend.LiveWorld.Stream.ColliderCount > 0, "instance 75 uses its exact published scene with authored colliders");
            await Frames(12); Capture(worldTexture, "instance-75"); Capture(uiTexture, "instance-hud");
            await RecallPoint();
            var exit = Npc(1735).Entity; await Open(1735); Submit(frontend.LiveUi.Windows.View.Q<Button>("npc-warp-152")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(() => frontend.LiveWorld.MapNumber == 0 && frontend.LiveWorld.IsReady && frontend.LiveWorld.MovementEnabled, "native authored exit returns to the overworld");
            Check(frontend.LiveWorld.Replicas.SelectedRecallPointId == 0, "successful authored exit clears the native Recall registration");
            peerFrames.Clear(); await peer.SendMovementAsync(exit.X, exit.Y, exit.Z, 0, 0, 0, 0, 0, 0, true); await peer.WarpNpcAsync(exit.Id, 152);
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolTravel.WarpOk), "passenger independently uses the authored exit");
            ProtocolTravel.TryDecode(peerFrames.Last(f => f.PacketType == ProtocolTravel.WarpOk), out destination); await peer.CompleteWarpAsync(destination, false);
            await frontend.Session.LeaveGroupAsync(); await Wait(() => frontend.LiveUi.Social.Members.Count == 0, "travel party disbands after the return");
            await frontend.LiveMoveTo(632032, 187177, -5500); await peer.SendMovementAsync(632132, 187177, -5500, 0, 0, 0, 0, 0, 0, true);
            await Wait(() => frontend.LiveWorld.IsReady && frontend.LiveWorld.Replicas.Actors.Any(a => a.Entity.NpcType == 1), "world and combat interest recover after the round trip");
        }
    }
}
#endif
