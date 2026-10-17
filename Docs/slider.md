# Slider: original bus and passenger path

Primary client: `builds/retrobution-20260821/main.unity3d`, SHA-256
`01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`.
The existing accepted `FFOneClient` transportation assets and original
`RustyFusion/src/entity/slider.rs` supply the content and server route. No bus
placement or movement path is invented by the Unity client.

## Presentation and support

The accepted model is
`characters/transportation/downtown_bus/DT_ETC_Downtownbus_A_00.glb`.
Its `stand1` animation and exact `collision` child are retained. The separate
accepted `.collision.glb` contains the original 134-vertex, 531-index collision
mesh. Unity mounts that mesh on the source child as a nonconvex MeshCollider;
the collision mesh's own renderers are hidden.

Primary `cnBusMoveController.ForceUpdate`, token `0x060001b3`, updates position
without rotating the bus toward the route segment. `NetworkWorldEntities`
therefore interpolates this transportation actor without the NPC heading path.
`SetModel`, token `0x060001ae`, explicitly loops and plays `stand1`. The accepted
prefab retains that loop. `SetupBus`'s state machine, token `0x06000cff`, assigns
table textures only to material names containing the case-sensitive `sub` or
`main` substring. The accepted bus's authored material names have neither, so
their imported atlases remain bound, as in the accepted native presentation.

Primary `EnvironmentCollision.HandleBusCollision`, token `0x06000786`, enters
the bus support path from CharacterController contact. `SliderGroundContact`
forwards collision callbacks from the actual player object to its world
controller. A walkable upward contact records the bus and its relative player
position. The platform's next displacement carries the player through the
normal CharacterController. Jump, relocation, bus removal or loss of support
clears the relationship. Peer players use their published relative coordinates
while their bus is present. All platform positions update before attached peers
are placed. The previous single pass depended on interest-arrival order and left
a peer one render frame behind a bus that entered after that player.

Focused static inspection can be replayed from `FusionForge`:

```powershell
.\target\debug\fusionforge.exe inspect '..\builds\retrobution-20260821\main.unity3d' --assembly 'Assembly - CSharp.dll' --class cnBusMoveController --method ForceUpdate --limit 1
.\target\debug\fusionforge.exe inspect '..\builds\retrobution-20260821\main.unity3d' --assembly 'Assembly - CSharp.dll' --class EnvironmentCollision --method HandleBusCollision --limit 1
```

## Protocol

`ProtocolSlider` uses Retrobution 0104 pack(4) layouts:

| Frame | ID | Body |
|---|---|---|
| Transportation enter / new | `0x3100009b` / `0x3100009e` | 24-byte appearance |
| Transportation exit | `0x3100009c` | 8 bytes |
| Transportation move | `0x3100009d` | 28 bytes |
| Around / leave around | `0x3100009f` / `0x310000a0` | count-prefixed pages |
| Passenger broadcast | `0x310000b4` | 72 bytes |
| Passenger request | `0x13000062` | 60 bytes |

Appearance contains transport kind, identity, model type and three coordinates.
The bus kind is 3 and original Slider model type is 1. Passenger frames carry
relative coordinates at 8/12/16, absolute coordinates at 20/24/28, bus identity
at 44, heading at 48 and speed at 56. Broadcasts append the player identity at
60 and server timestamp at 64. Full page validation precedes session mutation.

The local movement sender selects the passenger packet only while supported by
a Slider. Ordinary movement clears a peer's previous transportation relationship.
The declared transportation warp-recovery packet is not exercised by the current
RustyFusion Slider route, which publishes enter, exit and move frames.

## Verification

`SliderTests` covers lifecycle decoding, page atomicity, passenger fields and
preservation of transportation metadata during movement. Its same-frame peer
attachment regression checks both player-first and bus-first interest arrival,
including a server position correction, without loading presentation assets.
`Program.Slider.cs`
checks the real isolated server's bus lifecycle, passenger relay and departure.
`LiveGameplaySliderSmoke.cs` loads the published model and collision, finds a
walkable triangle, boards through actual CharacterController contact, verifies
server-driven displacement without relative drift, captures the scene and exits
the circuit. Final run identifiers and results belong to the acceptance report.

Paired runtime capture against the original client was not performed. Source
statements above are bounded static observations; Unity runtime behavior is
verified separately by the development player acceptance.

The [16 October acceptance receipt](../Tools/LiveGameplay/acceptance-20261016-slider-vehicles-instances-recall.json)
records the final DLL, 191 network checks, 182 native-player checks, 275 latest
Editor cases and the captured Slider ride.
