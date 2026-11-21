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
            bool Heard(string cue) => frontend.GetComponentsInChildren<AudioSource>(true).Any(source =>
                source.name=="Retrobution service voice" && source.clip &&
                source.clip.name.IndexOf(cue,System.StringComparison.OrdinalIgnoreCase)>=0);
            NetworkEntityView Npc(int type) => frontend.LiveWorld.Replicas.Actors.First(a => a.Entity.Kind == WorldEntityKind.Npc && a.Entity.NpcType == type);
            async Task Open(int type)
            {
                var npc = Npc(type); await frontend.LiveMoveTo(npc.Entity.X, npc.Entity.Y, npc.Entity.Z);
                await Wait(() => frontend.LiveWorld.IsReady, "travel NPC scenery is ready");
                Check(frontend.LiveUi.OpenNpcTravel(npc.Entity, npc.Definition), "native NPC travel window opens for type " + type);
                await Frames(4);
                var service=frontend.LiveUi.Windows.View?.Q<Button>("npc-use-service");
                if(service!=null){Submit(service);await Frames(4);}
            }
            await Open(640);
            int nano = frontend.LiveState.Snapshot.NanoSlots.First(id=>id>0);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("inventory-nano-"+nano));
            await Frames(4); Capture(uiTexture,"nano-station");
            Check(frontend.LiveUi.Windows.View.Q<Button>("station-slot-"+System.Array.IndexOf(frontend.LiveState.Snapshot.NanoSlots.ToArray(),nano))!=null,
                "equipped Nano card exposes its station action");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("close-nano-card"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("inventory-nano-2"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("tune-4"));Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(()=>frontend.LiveState.Snapshot.Nanos.Any(n=>n.Id==2 && n.SkillId==4),"station first tune installs the selected power");
            int fm=frontend.LiveState.Snapshot.FusionMatter;
            Submit(frontend.LiveUi.Windows.View.Q<Button>("tune-5"));Submit(frontend.Root.Q<Button>("prompt-confirm"));
            await Wait(()=>frontend.LiveState.Snapshot.Nanos.Any(n=>n.Id==2 && n.SkillId==5) && frontend.LiveState.Snapshot.FusionMatter<fm && frontend.LiveState.Snapshot.Inventory[46].IsEmpty,
                "station retune consumes the confirmed catalyst stack and level-based FM");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("station-slot-2"));
            await Wait(()=>frontend.LiveState.Snapshot.NanoSlots[2]==2,"station equips Nano in chosen slot");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("station-slot-2"));
            await Wait(()=>frontend.LiveState.Snapshot.NanoSlots[2]<=0,"station unequips selected Nano");
            frontend.LiveUi.Windows.Close();
            await Open(3133);
            await Wait(()=>Npc(3133).GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.enabled) ||
                Npc(3133).GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled), "Croc Pot NPC model is ready");
            await Frames(3);
            Capture((RenderTexture)frontend.LiveUi.Windows.View.Q<Image>("croc-npc").image, "croc-npc-portrait");
            void SelectCroc(int slot, string side)
            {
                var view = frontend.LiveUi.Windows.View;
                Submit(view.Q<Button>("inventory-slot-" + slot)); Submit(view.Q<Button>("croc-" + side));
            }
            SelectCroc(40, "looks"); SelectCroc(41, "stats"); await Frames(4);
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
                await Open(3133); SelectCroc(40, "looks"); SelectCroc(1, "stats");
                int before = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("croc-combine")); Submit(frontend.Root.Q<Button>("prompt-confirm"));
                await Frames(4);
                Capture((RenderTexture)frontend.LiveUi.Windows.View.Q<Image>("croc-waiting-npc").image, "croc-waiting-portrait");
                await Wait(() => !frontend.LiveUi.TravelBusy && frontend.LiveState.Snapshot.Taros == before - 948, "native Croc Pot confirms an attempt and applies exactly one charge");
                await Frames(4); Capture(uiTexture, "croc-result");
                if (!frontend.LiveState.Snapshot.Inventory[1].IsEmpty)
                    Submit(frontend.Root.Q<Button>("prompt-confirm"));
                frontend.LiveUi.Windows.Close();
            }
            Check(frontend.LiveState.Snapshot.Inventory[1].IsEmpty && frontend.LiveState.Snapshot.Inventory[40].Option == (67 << 16), "native Croc Pot success consumes only the stats slot and preserves exact appearance metadata");
            foreach (var pair in new[] { (967, 4), (968, 5) })
            {
                await Open(pair.Item1); Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-register-1-" + pair.Item2));
                await Wait(() => !frontend.LiveUi.TravelBusy, "native SCAMPER registration acknowledged"); frontend.LiveUi.Windows.Close();
            }
            await Open(967); await Frames(4); Capture(uiTexture, "transport");
            int wallet = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-trip-5"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-go"));
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
            int carriedSlot = System.Array.FindIndex(frontend.LiveState.Snapshot.NanoSlots.ToArray(), id =>
                frontend.LiveState.Snapshot.Nanos.Any(n => n.Id == id && n.Stamina > 0));
            Check(carriedSlot >= 0, "Skyway fixture has a Nano with stamina");
            await frontend.Session.ActivateNanoAsync(carriedSlot);
            await Wait(() => frontend.LiveState.Snapshot.ActiveNanoSlot == carriedSlot && frontend.LiveWorld.LocalNano?.Model,
                "Nano is visibly carried before boarding Skyway");
            Capture(worldTexture, "skyway-before-boarding");
            wallet = frontend.LiveState.Snapshot.Taros; Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-trip-78")); Submit(frontend.LiveUi.Windows.View.Q<Button>("transport-go"));
            await Wait(() => frontend.LiveWorld.Riding && frontend.LiveUi.TravelBusy, "native Skyway flight locks ground movement and financial actions");
            await Wait(() => frontend.LiveWorld.Riding && frontend.LiveState.Snapshot.ActiveNanoSlot < 0 &&
                frontend.LiveWorld.LocalNano?.NanoId == 0 && !frontend.LiveWorld.LocalNano.Model,
                "Skyway boarding recalls the visible Nano in the client and on the server");
            await Frames(2); Capture(worldTexture, "skyway-nano-recalled");
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
            string removeNpc = frontend.Language == "ru" ? "УБРАТЬ" : "REMOVE";
            Check(frontend.LiveUi.Windows.View.Q<Button>("npc-group-action").text == removeNpc, "NPC interaction switches to removal after the acknowledged invitation");
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
            async Task Locate(int type,int x,int y,int z)
            {
                await frontend.LiveMoveTo(x,y,z);
                await Wait(()=>frontend.LiveWorld.Replicas.Actors.Any(a=>a.Entity.NpcType==type),"race ECom appears in interest");
                await Open(type);
            }
            async Task WaitForRankingLayout()
            {
                var view = frontend.LiveUi.Windows.View;
                await Wait(() => Mathf.Approximately(view.Q("rank-left").resolvedStyle.left, 8) &&
                    Mathf.Approximately(view.Q("rank-right").resolvedStyle.left, 511),
                    "ranking panels settle at their authored positions");
            }
            await Locate(1750,129579,236387,-13902);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("race-rank"));
            await WaitForRankingLayout(); Submit(frontend.LiveUi.Windows.View.Q<Button>("rank-next"));
            Submit(frontend.LiveUi.Windows.View.Q<Button>("rank-period-3")); await Frames(4); Capture(uiTexture,"race-rankings");
            Submit(frontend.LiveUi.Windows.View.Q<Button>("rank-close")); await Open(1750);
            Submit(frontend.LiveUi.Windows.View.Q<Button>("race-start"));
            await Wait(()=>frontend.Root.Q<Label>("race-pods")!=null,"race start acknowledged on the original instance");
            await Wait(()=>Heard("clickstart"),"race starter plays a localized ClickStart take from its own voice bank");
            var pod=WorldBehaviour.RaceRings.OrderBy(r=>(r.Center-frontend.LiveWorld.Player.position).sqrMagnitude).First();
            var pick=WorldCoordinates.ToServer(pod.Center-Vector3.up*.7f);
            await frontend.LiveMoveTo(pick.x,pick.y,pick.z);
            await Wait(()=>frontend.Root.Q<Label>("race-pods")?.text.Contains("001")==true,"authored ring triggers an acknowledged pickup");
            var timer = frontend.Root.Q(className: "ff-race-hud");
            var objective = frontend.Root.Q("hud-current-objective");
            Check(timer.worldBound.width > 0 && objective.resolvedStyle.display == DisplayStyle.Flex &&
                !timer.worldBound.Overlaps(objective.worldBound), "race timer and tracked mission occupy separate HUD areas");
            await Frames(4); Capture(uiTexture,"race-hud");
            CaptureRacePod(frontend.Root.Q<Image>("race-pod"));
            await Locate(1751,129340,212962,-11797);
            await Wait(()=>frontend.LiveUi.Windows.View?.Q<Button>("race-accept")!=null,"race finish receives score and rewards");
            await Wait(()=>Heard("racefinished"),"finish agent plays its own localized RaceFinished voice");
            await Frames(4); Capture(uiTexture,"race-result"); Submit(frontend.LiveUi.Windows.View.Q<Button>("race-accept"));
            await Open(1751);Submit(frontend.LiveUi.Windows.View.Q<Button>("race-rank"));
            await WaitForRankingLayout();
            var rankings=frontend.LiveUi.Windows.View;
            for(int page=0;page<6 && rankings.Q<Button>("rank-location-19")==null;page++)
                Submit(rankings.Q<Button>("rank-next"));
            Submit(rankings.Q<Button>("rank-location-19"));
            await Wait(()=>rankings.Q<Label>("score-0-0-1")?.text==frontend.LiveState.Snapshot.DisplayName,"OFAPI personal ranking names the actual finisher");
            Submit(rankings.Q<Button>("rank-period-3")); await Frames(4);Capture(uiTexture,"race-personal-ranking");
            Submit(rankings.Q<Button>("rank-close"));
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
        void CaptureRacePod(Image icon)
        {
            var background = icon.resolvedStyle.backgroundImage;
            var art = background.texture ? background.texture : background.sprite ? background.sprite.texture : null;
            Check(art && art.width > 1 && art.height > 1, "race HUD binds the original pod artwork");
            var target = RenderTexture.GetTemporary(art.width, art.height, 0);
            try { Graphics.Blit(art, target); Capture(target, "race-hud-pod"); }
            finally { RenderTexture.ReleaseTemporary(target); }
        }
    }
}
#endif
