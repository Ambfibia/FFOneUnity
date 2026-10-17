# Personal vehicles — evidence and acceptance

Implemented for the accepted Retrobution/FFOne 0104 server contract. Equipment
slot 8, item type 10 is inventory ownership; a mount becomes active only after
the server's vehicle success or the local player's state bit 8. The V binding
and HUD button share the same request, modal gates and acknowledgement timeout.
Mounting is unavailable in tutorial, instances, Skyway or on a Slider.

## Evidence

* Primary artifact: `../builds/retrobution-20260821/main.unity3d`, SHA-256
  `01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`,
  assembly `Assembly - CSharp.dll`. Focused FusionForge `inspect` was used;
  these are static IL observations, not an observation of the original client running.
* `GameFrame/<LoadVehicle>d__115::MoveNext()`, token `0x06000f69`:
  IL_01cf selects the `vehicle` socket; IL_0270–02c9 instantiates at its world
  position with socket rotation multiplied by Euler(90,0,0), then reparents
  retaining world scale. The accepted `vehicles.json` supplies exact model and
  texture routes for each item. Weapon renderers are hidden while mounted.
  Equipment changes on the same rig retain the attachment, exhaust history and
  active jump/landing blend; newly equipped weapon renderers stay hidden. A new
  rig recreates the attachment on its own socket. This lifetime policy follows
  the server's `item.rs` contract: only equipment slot 8 changes force dismount.
* `cnAvatarAnimation.StrModifyForVehicle` family relation is retained by the
  accepted native FFOneClient `characters/avatar_action/types.rs`: equip type 1
  is board, 2/3 scooter. `vehicle_animations.json` owns fourteen additional base
  clips already present in both imported skeletons. PlayerAnimatorSetup appends
  these to the existing 127 states without changing the base contract.
  Their original GLB event metadata retains the source `end` boundaries:
  .683333337 jumpstart, .350000083 jumpend/landrun, .816666722 run/back,
  1.350000024 idle/jump. Every source curve key and its full length is preserved;
  jumpstart/jumpend sound events fire at .25 seconds.
* The callback distinguishes event presence from a transition. Primary
  `cnAvatarAnimation.MakeUperLayer` (`0x0600008e`, IL_05cb–05ef) assigns Loop
  to board/scooter stand1, run, runback and jump. `EndAnimation` (`0x06000088`,
  IL_04be–04de, IL_0576–05ba) takes its loop branch and immediately returns for
  jump; run/runback do not request a successor. These eight states per gender
  have no timed transition. Its stand1 branch (IL_05d9–0681) requests the same
  mounted stand1 with `CrossFade(name, .3)`. The wrapper (`0x06000080`,
  IL_0012–006f) updates previous/current names and delegates to Unity's legacy
  Animation.CrossFade without an explicit rewind or time assignment. The
  reconstruction keeps this active idle loop idempotent; it does not invent a
  forced phase reset. Legacy engine internals were not observed at runtime.
  `IsNextAnimationToNext` (`0x06000083`, IL_0003–006f) maps only jumpstart → jump,
  jumpend → stand1 and jumplandrun → run. The nonloop branch of EndAnimation
  (IL_0554–0569) crossfades to them over .3 seconds at the source end event.
  AvatarJump (`0x060000a0`, IL_00d1–00d9), Landing (`0x06000091`,
  IL_0039–0041) and LandRun (`0x06000092`, IL_003f–0047) enter over .15 seconds.
  Grounded input preserves the complete landing return blend before selecting
  ordinary mounted locomotion. Primary ExecuteJump uses the shared jump attribute and excludes
  variable-jump boost for mounted players, so the existing 5.68 launch / 10 gravity
  baseline also applies to personal vehicles.
* Primary `cnAvatarThirdPersonMove.UpdateVehicleAcceleration`,
  `UpdateVehicleCoasting` and `UpdateVehicleMovement`: forward/diagonal/back/
  strafe speed caps are 1.35/1.215/0.54/0.9 times `m_iUp_runSpeed`; acceleration
  uses the original grounded/airborne Lerp rates, then an alignment Lerp.
  Coasting multiplies momentum by .98 grounded/.997 airborne per Update until
  its magnitude falls below .1. A short forward obstacle probe applies a 7.5
  reverse impulse, followed by the current input's speed cap. Primary
  `UpdateVehicleAcceleration` (`0x0600013d`, IL_0259–0279, IL_02ed–0384)
  performs that cap after the response: reverse and strafe at the 7.5 base
  vehicle speed therefore bounce at 4.05 and 6.75 respectively. Unity now
  retains the directional cap through `Bounce`, including its subsequent coast.
  `PersonalVehicleMotion` expresses this horizontal state independently of the
  CharacterController. Ground/vertical collision continues through Unity's motor.
* The engine comes from XDT `m_iSound1` → `m_pItemSoundData` and published
  `audio/sfx/vehicles` assets. Pitch is max(1, horizontal speed /
  (vehicle speed × 1.5) × 2); Time Squad retains pitch 1. The accepted native
  FFOneClient correction gives board engines gain .5 and scooter engines 1
  before the user's effects gain. That native correction is an accepted extension.
* Primary LoadVehicle chooses effect 835 for exact mesh `vehicle_hoverboard`,
  836 for `vehicle_cloud`/`vehicle_hoverboard2`, and 15 for other meshes, then
  attaches persistent exhaust to tag01–tag04 and sets TrailTimes to 1.5. The
  already accepted `effects/vehicles/catalog` publishes these standard/board/
  cloud variants: sixteen history points, heights .30/.15/.15, original additive
  textures and a mesh tip only for the standard variant. NativeVehicleExhaust
  reuses the existing source-derived trail mesh playback and owns every created
  material/mesh through dismount or interest removal.
* Server authority: `RustyFusion/src/server/shard/pc.rs::pc_vehicle_on/off`,
  `item.rs` slot movement dismount, `state/shard.rs::check_for_expired_vehicles`,
  `helpers.rs::broadcast_state`. Requests and success receipts have one unused
  byte; failures have four bytes. State change has i32 player ID and i8 state
  followed by alignment padding. Item expiry is a counted list of container/slot
  pairs and is validated completely before applying any deletion.

Replay a focused primary inspection from FusionForge, changing only the class
and method for the other motion entries:

```powershell
.\target\debug\fusionforge.exe inspect '..\builds\retrobution-20260821\main.unity3d' --assembly 'Assembly - CSharp.dll' --class cnAvatarThirdPersonMove --method UpdateVehicleAcceleration --limit 1
```

## Verification boundary

`GameplayVehicleTests` checks packet sizes/padding, authoritative state,
foreign-player isolation, atomic expiry, peer state through movement,
the real peer appearance/equipment/state path retaining a mounted jump until
a grounded movement packet,
directional acceleration/coasting and capped obstacle response, both model families, socket placement and
equipment refresh, jump/landing entry blends and protection of the return blend.
`PlayerAnimatorTests` verifies all 141 reachable states, source-length vehicle
loops through their end events, idempotent mounted idle, and the six clamp
returns per gender at their event boundaries with .3 second blends.
The live .NET harness exercises actual mount/off/rejection, peer replication,
movement, forced dismount on item move and instance rejection/normal return.
The development player acceptance exercises both HUD actions, accepted models,
animations, engine loops, production momentum and CharacterController movement,
coasting, and the peer attachment lifecycle. Its explicit motor driver bypasses
OS focus while retaining authored collision and normal network movement events.

Live passage of the server's minute expiry timer is not asserted by this run;
the exact expiry packet and reducer are covered by deterministic tests. Paired
runtime capture against the original client was not performed; primary timing,
attachment, sound, trail and movement statements above are static-source
observations corroborated by the accepted native consumer and new Unity checks.

The [16 October acceptance receipt](../Tools/LiveGameplay/acceptance-20261016-slider-vehicles-instances-recall.json)
records the final DLL, passing network/player regressions, both captured vehicle
families and the latest Editor outcomes.
