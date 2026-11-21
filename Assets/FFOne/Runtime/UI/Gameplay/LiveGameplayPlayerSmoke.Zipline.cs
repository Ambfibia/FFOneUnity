#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Linq;
using System.Threading.Tasks;
using FFOne.Network;
using FFOne.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FFOne.UI
{
    public sealed partial class LiveGameplayPlayerSmoke
    {
        async Task Zipline(int port)
        {
            // map_11_01 trigger BuildPlayer-Map_11_01#16489, center in 0104 centimetres.
            const int x = 596390, y = 92021, z = -1184;
            var world = frontend.LiveWorld;
            var state = frontend.LiveState;
            Check(Protocol0104.Read16(frontend.Session.World.RawLoadData, 0) == 99, "zipline fixture uses ordinary account privileges");
            Check(WorldWeaponCatalog.Load().Find(state.Snapshot.Equipment[0].ItemId)?.Projectile == true, "fixture equips a real projectile weapon");
            peer = new FusionClient();
            var roster = await peer.LoginAsync("127.0.0.1", port, "unitybobby", "test-only-password");
            await peer.EnterWorldAsync(roster.Single().Uid);
            await peer.CompleteLoadingAsync();
            await peer.SendMovementAsync(x, y + 100, z, 0, 0, 0, 0, 0, 0, true);
            await frontend.Session.ActivateNanoAsync(0);
            await Wait(() => state.Snapshot.ActiveNanoSlot == 0 && world.LocalNano.Model, "Nano is visibly carried before entering the zipline");
            await frontend.LiveMoveTo(x, y, z);
            await Wait(() => Application.isFocused && world.IsReady && world.MovementEnabled && !world.InputBlocked &&
                WorldZipline.Find(world.View, world.Player.position) != null, "authored overworld zipline trigger is under the focused gameplay player");

            worldTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            worldTexture.Create();
            world.View.targetTexture = worldTexture;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                await Frames(3);
                Capture(worldTexture, "zipline-before");
                world.PlayAttack();
                var animator = world.Player.GetComponentInChildren<FFOne.Content.NativePlayerAnimator>().Animator;
                Check(animator.GetLayerWeight(1) > .9f, "active weapon attack is visible on the upper layer before boarding");
                await PressZiplineInput(keyboard, Key.Z);
                Check(world.Ziplining, "normal Fire1 interaction starts the authored ride");
                bool UpperEmpty() => animator.GetLayerWeight(1) == 0 &&
                    animator.GetCurrentAnimatorStateInfo(1).IsName(FFOne.Content.NativePlayerAnimator.UpperLayer + ".Empty");
                Check(UpperEmpty(), "boarding clears the in-progress weapon attack from the upper layer");
                var boardPosition = world.Player.position;
                Renderer Trolley() => world.Player.GetComponentsInChildren<Renderer>()
                    .FirstOrDefault(r => r.enabled && FindTrolleyAncestor(r.transform, world.Player));
                await Wait(() => world.Ziplining && Trolley() && state.Snapshot.ActiveNanoSlot == -1 &&
                    !world.LocalNano.Model && Vector3.Distance(boardPosition, world.Player.position) > 1,
                    "rider advances, published trolley renders and the server recalls the carried Nano");
                Capture(worldTexture, "zipline-riding");

                int firstBroadcast = peerFrames.Count;
                received.Remove(ProtocolProjectiles.GrenadeSuccess);
                received.Remove(ProtocolProjectiles.RocketSuccess);
                await PressZiplineInput(keyboard, Key.Z, Key.Digit1);
                await PressZiplineInput(keyboard, Key.Z);
                Check(world.Ziplining && state.Snapshot.ActiveNanoSlot == -1, "attack and summon controls do not interrupt the current ride");
                await PressZiplineInput(keyboard, Key.Space);
                await Wait(() => !world.Ziplining && !Trolley() && peerFrames.Skip(firstBroadcast).Any(f =>
                    f.PacketType == ProtocolZipline.Broadcast && Protocol0104.Read32(f.Payload, 56) == 1),
                    "Jump detaches, removes the trolley and sends the accepted detach packet to the observer");
                Check(state.Snapshot.ActiveNanoSlot == -1 && !received.Contains(ProtocolProjectiles.GrenadeSuccess) &&
                    !received.Contains(ProtocolProjectiles.RocketSuccess) && !peerFrames.Skip(firstBroadcast).Any(f =>
                        f.PacketType == ProtocolProjectiles.GrenadePeer || f.PacketType == ProtocolProjectiles.RocketPeer),
                    "the attempted ride inputs produced neither a projectile nor a Nano summon");
                Capture(worldTexture, "zipline-detached");
                Check(UpperEmpty(), "dismount does not resume the interrupted attack");

                await frontend.LiveMoveTo(x, y, z);
                await Wait(() => world.IsReady && WorldZipline.Find(world.View, world.Player.position) != null,
                    "the same authored zipline remains usable after a detach");
                await PressZiplineInput(keyboard, Key.Z);
                Check(world.Ziplining, "authoritative player is riding before the gamepad jump");
                firstBroadcast = peerFrames.Count;
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                await Frames(2);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                await Wait(() => !world.Ziplining && peerFrames.Skip(firstBroadcast).Any(f =>
                    f.PacketType == ProtocolZipline.Broadcast && Protocol0104.Read32(f.Payload, 56) == 1),
                    "gamepad A detaches the rider and the observer receives the detach packet");
                Capture(worldTexture, "zipline-gamepad-detached");
                await frontend.LiveMoveTo(x, y, z);
                await Wait(() => world.IsReady && WorldZipline.Find(world.View, world.Player.position) != null,
                    "the trigger remains usable after the gamepad detach");
                firstBroadcast = peerFrames.Count;
                await PressZiplineInput(keyboard, Key.Z);
                Check(world.Ziplining, "second interaction starts the same ride again");
                await Wait(() => !world.Ziplining && !Trolley() && peerFrames.Skip(firstBroadcast).Any(f =>
                    f.PacketType == ProtocolZipline.Broadcast && Protocol0104.Read32(f.Payload, 56) == 0 &&
                    Protocol0104.Read32(f.Payload, 20) == Protocol0104.Read32(f.Payload, 24)),
                    "natural endpoint removes the trolley and publishes the total route distance");
                Capture(worldTexture, "zipline-finished");
            }
            finally { InputSystem.RemoveDevice(pad); InputSystem.RemoveDevice(keyboard); }
            await peer.LeaveWorldAsync();
        }

        static bool FindTrolleyAncestor(Transform transform, Transform player)
        {
            for (var node = transform; node && node != player; node = node.parent)
                if (node.name == "Zipline trolley") return true;
            return false;
        }

        static async Task PressZiplineInput(Keyboard keyboard, params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            await Frames(2);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames(2);
        }
    }
}
#endif
