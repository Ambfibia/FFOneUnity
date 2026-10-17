#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using System.Threading.Tasks;
using FFOne.Content;
using FFOne.Network;
using UnityEngine;
using UnityEngine.UIElements;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task PersonalVehicles()
        {
            var state = frontend.LiveState;
            var avatar = frontend.LiveWorld.Player.GetComponent<NativeAvatarPreview>();
            Check(!state.Snapshot.VehicleMounted && !frontend.LiveWorld.PersonalVehicleMounted, "equipped inventory never invents a personal mount");
            foreach (var item in new[] { (47, 1, "board_"), (48, 4, "scooter_") })
            {
                await frontend.LiveUi.RequestItemMove(ItemContainer.Inventory, item.Item1, ItemContainer.Equipment, 8);
                await Wait(() => state.Snapshot.Equipment[8].ItemId == item.Item2, "vehicle equips through the native item movement path");
                await Frames(3);
                Submit(frontend.Root.Q<Button>("vehicle-toggle"));
                await Wait(() => state.Snapshot.VehicleMounted && frontend.LiveWorld.PersonalVehicleMounted && !frontend.LiveUi.VehicleBusy,
                    "vehicle button waits for the production mount acknowledgement");
                Check(avatar.PersonalVehicleId == item.Item2 && avatar.PersonalVehicleObject && avatar.PersonalVehicleObject.GetComponentsInChildren<Renderer>().Any(),
                    "accepted vehicle model and texture attach to the animated foot socket");
                Check(avatar.PersonalVehicleObject.GetComponent<NativeVehicleExhaust>()?.TrailCount > 0,
                    "accepted persistent vehicle exhaust attaches to the original model tags");
                Check(avatar.LocomotionClip("stand1") == item.Item3 + "stand1" && avatar.PlayerAnimation.Supports(item.Item3 + "run"),
                    "mounted family selects its imported idle and movement animations");
                Check(frontend.LiveWorld.PersonalVehicleSpeed == GameplayUiCatalog.Load().Item(10, item.Item2).VehicleSpeed / 100f,
                    "local motor uses the exact equipped vehicle XDT speed");
                var before = frontend.LiveWorld.Player.position; Vector3 moving = Vector3.zero;
                for (int frame = 0; frame < 30; frame++) moving = frontend.LiveWorld.AcceptVehicleMotorStep(0, 1, 1f / 60);
                Check(Vector3.Distance(before, frontend.LiveWorld.Player.position) > .3f && moving.magnitude > 1,
                    "production vehicle acceleration moves the CharacterController through authored world collision");
                var coast = frontend.LiveWorld.AcceptVehicleMotorStep(0, 0, 1f / 60);
                Check(coast.magnitude < moving.magnitude && coast.magnitude > 0,
                    "releasing vehicle movement retains decaying native momentum");
                var sound = frontend.LiveWorld.Player.GetComponents<AudioSource>().FirstOrDefault(s => s.loop);
                Check(sound && sound.clip && sound.clip.name.ToLowerInvariant().Contains("vehicle_"), "mounted vehicle plays its accepted table engine loop");
                await Frames(10); Capture(worldTexture, item.Item3 + "vehicle");
                Submit(frontend.Root.Q<Button>("vehicle-toggle"));
                await Wait(() => !state.Snapshot.VehicleMounted && !frontend.LiveWorld.PersonalVehicleMounted && !frontend.LiveUi.VehicleBusy,
                    "vehicle button dismount restores the ordinary avatar motor");
                var start = FFOne.World.WorldCoordinates.ToServer(before); await frontend.LiveMoveTo(start.x, start.y, start.z);
                await frontend.LiveUi.RequestItemMove(ItemContainer.Equipment, 8, ItemContainer.Inventory, item.Item1);
                await Wait(() => state.Snapshot.Equipment[8].IsEmpty && state.Snapshot.Inventory[item.Item1].ItemId == item.Item2, "vehicle returns to its exact source inventory slot");
            }
            Check(!frontend.Root.Q<Button>("vehicle-toggle").enabledSelf, "vehicle button blocks a mount without equipped transport");
            peerFrames.Clear(); await peer.SendItemMoveAsync(ItemContainer.Inventory, 47, ItemContainer.Equipment, 8);
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolInventory.MoveSuccess), "peer equips its vehicle through the production server");
            await peer.SetVehicleAsync(true);
            NativeAvatarPreview remoteVehicle = null;
            await Wait(() => (remoteVehicle = frontend.LiveWorld.Replicas.Actors.FirstOrDefault(a => a.Entity.Kind == WorldEntityKind.Player && a.Entity.Id == peer.World.PlayerId)?.Avatar) &&
                remoteVehicle.PersonalVehicleId == 1 && remoteVehicle.PersonalVehicleObject, "native interest renderer attaches a peer's confirmed vehicle");
            Check(remoteVehicle.LocomotionClip("run") == "board_run", "peer locomotion uses the mounted animation family");
            peerFrames.Clear(); await peer.SendItemMoveAsync(ItemContainer.Equipment, 8, ItemContainer.Inventory, 47);
            await Wait(() => peerFrames.Any(f => f.PacketType == ProtocolVehicle.OffSuccess) && remoteVehicle && remoteVehicle.PersonalVehicleId == 0,
                "peer equipment replacement removes its vehicle after the server forced dismount");
        }
    }
}
#endif
