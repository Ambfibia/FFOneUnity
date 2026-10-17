# Recall Point 1386 / RXcom

NPC ID 1386 and mesh-row 260 remain unchanged. `WorldNpcCatalog` resolves the exact
`RXcom` mesh name to editable prefab `ffone/6949aa01f8c678a3f4f3390964a8aaf8` when
the accepted native character routes contain no RXcom entry. An existing canonical
route takes precedence. The prefab loads through `NativeModelAssets`; the normal
Addressables catalog rebuild registers its stable GUID address in the existing
`Characters-NPCs-rxcom` group.

The primary model is **renderless**. It has an actual Box MeshFilter and compressed
source geometry, but no MeshRenderer or SkinnedMeshRenderer. Publishing a visible
cube or substituting another NPC would change the original. The recovered asset
preserves all four named Transform owners, their local TRS/layer/activity, the
unrendered 24-vertex/12-triangle Box, both source clips, packed quaternion payloads,
position/scale keys and tangents, and the `stand1` `end` event at 0.7166666985 s.
The saved root name is `RXcom`; Unity's loaded main prefab object is named `model`
after `model.prefab`. The source `Box01` animation's packed quaternion
`0x7fdfed9f` decodes to `(0.702544031311, -0.002932551320, -0.000977517107,
-0.711633563481)`, rather than its static bind rotation. The two source keys are
identical; tests compare the full orientation with quaternion sign equivalence.
There are no source renderer materials to reconstruct. The existing accepted RXcom
texture is preserved independently.

Source: `builds/retrobution-20260821/Freearea_shared.resourceFile`, SHA-256
`748e1ede36aafe2586c8c3e8e970a738b874a56c17b2bb5af38fc8757055663f`.
Serialized asset: `CustomAssetBundle-74b5d4666b65a47518510d30c66283ee`.

| Original owner | Components / child |
| --- | --- |
| GameObject 4900 `RXcom` | Transform 3373; Animation 7018 |
| GameObject 4901 `Box01` | Transform 3374; child of 3373 |
| GameObject 4744 `Box01 NonAccum` | Transform 3375; child of 3374 |
| GameObject 4743 `Box` | Transform 3376; MeshFilter 6751 → Mesh 571; child of 3375 |
| Animation 7018 | default `nif-default` clip 1051; `stand1` clip 1052 |

Replay from the Unity project, with UnityPy and PyYAML installed:

```powershell
python Tools/recover_recall_point.py --check
python Tools/recover_recall_point.py
python Tools/recover_recall_point.py --check
```

The source container is immutable and decoded in memory. The importer writes only
the four final Unity assets and their metas, preserves equal existing files, and
rejects a different existing destination before any write. It does not weaken the
generic FusionForge `contains no publishable mesh` guard: that exporter follows
renderers and correctly finds none here.

The visible Recall marker comes from the original GameIcon particle effect.
`RecallPointPresentation` uses the already accepted ES446 closure while unselected
and ES811 while selected. Original managed evidence is in primary `main.unity3d`,
SHA-256 `01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`,
embedded `Assembly - CSharp.dll`:

- `NpcMoveController.MakeGameIcon()`, token `0x060009dd`, category 17 branch:
  compares player `iRXCom` with NPC `Status.iID`; uses table effect 446 when unequal,
  literal 811 when equal.
- `NpcMoveController/<GameIconCoroutine>d__61.MoveNext()`, token `0x06001073`:
  looks for `GameIcon`, then `Bip01 Head`, then uses the NPC root. RXcom has neither
  named attachment. The root fallback uses local position `(0, Height * .9, 0)` and
  local rotation `Euler(90, 0, 0)`. `NpcContainer.Add(sNPCAppearanceData)` at
  `IL_0245`–`IL_0251` sets `fHeight = table.m_iHeight * .01`; height 1.2 gives
  offset 1.08.
- `GameFrame.ReceivePacket`, offsets `IL_681f`–`IL_684e`, reads successful
  `REGIST_RXCOM` and assigns its first field, named `iMapNum`, directly to `iRXCom`.
  Despite that packet field name, primary uses it as the selected NPC instance ID.
  `WARP_USE_NPC_SUCC` resets `iRXCom` to zero at `IL_52aa`.
- ffproto 0104 revision `3cdf273` defines no Recall ID in the 2688-byte world-entry
  load block. Successful registration is packet `0x310000cd`, 16 bytes; failure is
  `0x310000ce`, 4 bytes. No bootstrap offset is invented.

The ownership and managed findings above are static evidence. Targeted Unity tests
exercise the actual model loader, source clip sampling, particle closure loading,
selected/unselected effects and attachment transform. Interactive rendering remains
a separate runtime check.

The network regression also passes real decoded NPC-interest frames through
`NetworkWorldEntities`: two NPC instances share type 1386, while a prelude
`REGIST_RXCOM` selects only instance 569. Repeated accepted registration preserves
the existing particle owner. A failure reply and malformed success replies preserve
the selected point. Switching registration selects instance 570; a validated
`WARP_USE_NPC_SUCC` clears both markers. Warp validation now precedes marker mutation,
including the normal decoder's wallet validation, so a malformed warp cannot clear
the accepted point before rejection. These are controlled protocol regression
checks, not evidence that the live server accepted a Recall registration.

Static recheck on 2026-10-16 used the exact embedded DLL, SHA-256
`0f2513c2303cffc88c541a90b1b8fad1b73f60d5218830be72d0502533bf5792`,
without loading or executing its code. Focused FusionForge inspection reproduces
the GameIcon branch, root attachment and NPC height conversion:

```powershell
../FusionForge/target/debug/fusionforge.exe inspect ../builds/retrobution-20260821/main.unity3d --assembly "Assembly - CSharp.dll" --class NpcMoveController --method MakeGameIcon --limit 1
../FusionForge/target/debug/fusionforge.exe inspect ../builds/retrobution-20260821/main.unity3d --assembly "Assembly - CSharp.dll" --class '<GameIconCoroutine>d__61' --method MoveNext --limit 1
../FusionForge/target/debug/fusionforge.exe inspect ../builds/retrobution-20260821/main.unity3d --assembly "Assembly - CSharp.dll" --class NpcContainer --method Add --limit 4
```

`GameFrame.ReceivePacket(cnEvent)`, token `0x060007fb`, was additionally read as
metadata/CIL with Unity's installed Mono.Cecil library, bounded to the offsets
listed above; it reconfirmed the registration assignment and warp reset. REA's
whole-assembly member inventory was unavailable at its 64 MiB metadata
representation limit (`ev_7282b063dc6076dc2b8cc469612100ea93d2d1d4446fa00cb59d3d7d02c3287a`);
that result supplies no positive code evidence. The focused raw-container and
metadata reads supply the static findings. No client execution is inferred.

RustyFusion now handles the exact four-byte `REQ_REGIST_RXCOM` request in
`server/shard/nano.rs`. The transaction requires a living player in an instance,
a living, visible and interactable category-17 NPC in the same channel/map/copy,
and the normal interaction distance. Rejected requests send the four-byte failure
reply without replacing the last valid registration. Successful replies carry the
NPC network identity and its authoritative XYZ, matching the original client's
`iRXCom` comparison. This deliberately corrects the legacy OpenFusion handler's
use of a map number in that first word; no packet layout or native IDs change.

The registered destination is a volatile `Player::RecallPoint`, independent of
the pre-warp entrance used for respawn/lair return. Recall skills use this exact
point and reject a missing registration or one from another instance. Committed
NPC portal warps clear registrations for both leaders and group passengers;
instance changes through `set_instance_id` and GM portal transfers clear them as
well. A rejected warp preserves the point. No world-entry field or database
column is invented. Four focused server regressions cover exact ACK identity,
idempotence/replacement, eleven rejection cases, actual Recall skill coordinates
expiry on instance change, and the real empty-target Nano request using original
Nano 9 and tune 25 with its exact cost and cooldown.

Single Recall rows 25/66/75/214 have NPC target type 1, but self effect target 2;
row 236 also uses self effect target 2 with target type 2. Production targeting
resolves single Recall to the caster before ordinary target parsing, as the
local OpenFusion `Abilities::matchTargets` does. Unity derives the skill type
from the accepted table and sends the original empty self-target request.
Successful self Recall retains `GOTO_SUCC` without an invalid empty skill-result
trailer, while charging its authored stamina cost and cooldown exactly once.

Unity exposes the native Recall Point interaction through a `recall-register`
button. It sends `FusionClient.RegisterRecallPointAsync`, waits for the exact
success/failure ABI, and disables the button only after the accepted identity is
received. `NetworkWorldEntities` selects ES811 only from a validated decoded
server success, preserves ES811 on failure, and clears it on a validated NPC warp.
Protocol regressions reject zero identities, truncated and trailing replies.

The native player smoke checks the existing server placement 569 in instance 75
at `(130715, 223861, -16066)`, between the normal instance entry and exit checks.
It moves through `FrontendController.LiveMoveTo`, waits for the actual NPC type
1386 in server interest, checks the renderless body and original geometry, and
requires both ES446 particle surfaces to generate real mesh data with supported
materials. It captures `recall-point-446.png`, submits the actual registration
button and requires a RustyFusion success, the exact selected NPC identity,
rendered ES811, and the disabled acknowledged action. It captures
`recall-point-811-registered.png` and `recall-registration.png`, then checks a
repeat server registration preserves the particle owner and an unknown NPC
receives a real failure without changing selection. It returns the player to the
pre-check position, then requires the authored exit to clear the registration.
No synthetic server frame, direct `SetSelected` call or replacement NPC is used.
`Program.Recall.cs` adds an independent network check through the same original
entrance, registration point and exit, including unknown, far, wrong-category
and previous-instance rejection.

The earlier [16 October acceptance receipt](../Tools/LiveGameplay/acceptance-20261016-slider-vehicles-instances-recall.json)
retains the historical presentation-only capture and does not prove live
registration. The revised network and player smoke require the server transaction
above. The [final instance/Recall acceptance receipt](../Tools/LiveGameplay/acceptance-20261016-instance-routes-recall.json)
records the successful final run: 220 network checks and 196 native-player checks,
including 27 Recall-related network checks and 25 Recall-related player checks.
Registration, repeat ACK, rejected-registration preservation, original Nano 9's
real return and clearing on the authored exit passed on the same final DLL and
Addressables catalog as the fresh 151-map matrix. The three actual Recall
captures and their hashes are included in that receipt.
