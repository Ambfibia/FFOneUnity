# FFOneUnity discovery baseline

Audit date: 2026-11-14. This is a read-only inventory of the existing product,
not a claim that all gameplay is complete or a replacement for the existing
acceptance records. Paths in this document are relative to
`D:/CodexProject/FusionFallProject/FFOneUnity` unless stated otherwise.

## Environment and working tree

- Production client: this existing Unity project; no replacement project.
- `ProjectSettings/ProjectVersion.txt`: Unity `6000.4.0f1 (8cf496087c8f)`.
- `Packages/manifest.json`: URP `17.4.0`, Addressables `4.1.1`, Input System
  `1.19.0`, Timeline `1.8.11`, Test Framework `1.6.0`, Unity MCP `v10.3.0`.
- Observed Git branch `main`, HEAD `f5b40dbc990f4dd42066d0bf9e32400bccff8ce9`.
- Initial porcelain inventory: 1,878 modified, 28 deleted and 2,994 untracked
  entries. These predate Studio work and must not be reset, migrated or staged
  wholesale. Directory entries in porcelain are not individual asset counts.
- No new Unity compilation, player build, test run, reimport or Editor state
  change was performed for this inventory. Live connector availability is a
  separate Studio integration check.

## Existing production pipeline

`Assets/FFOne/Runtime` owns Characters, Content, Core, Gameplay, Network, UI and
World. `Assets/FFOne/Editor` owns importers, publication metadata, inspectors,
Asset Browser, World Editor and validation. `FFOne.Runtime.asmdef` references
Unity packages and Newtonsoft; it has no REA/FusionForge runtime dependency.

Observed on disk: 170 world `.unity` scenes, ten tutorial `.playable` assets and
68 Nano directories. Counts prove presence, not gameplay completeness.

`Runtime/World/WorldStreamer.cs` requires a scene address and loads authored
scenes additively through Addressables. It does not regenerate missing cells
from historical collision geometry. The authoritative collision rules are in
[native-collision-contract.md](../native-collision-contract.md): scenes own
their MeshCollider geometry, exact surfaces and bindings. The recorded migration
counts of 59,635 colliders and 6,444 embedded meshes were not recomputed here.

`Runtime/Gameplay/Tutorial/TutorialCinematics.cs` binds authored Timeline tracks
to a native PlayableDirector graph. The graph clock is controlled to preserve
the presentation gate. Read [native-tutorial-timeline.md](../native-tutorial-timeline.md)
for skip, restart and command semantics. The README paragraph saying ordinary
PlayableDirector does not execute gameplay actions is older than this native
implementation; prefer the focused contract and current code until reconciled.

Content uses stable loading addresses and asset GUIDs. Imported meshes,
materials, prefabs, textures and animation are separated by ownership; source
documents remain provenance. Importers and authoring commands are tools, not
runtime prerequisites. Existing authored assets must remain manually editable.
Do not repeat historical folder migrations or GUID-changing deduplication.

## Historical execution evidence verified against disk

The newest successful resource-loading receipt found was
`Logs/NativeTutorialOverlay/FinalClient/validation.json`, dated 2026-11-08.
It targets `Builds/NativeTutorialOverlay/Windows/FFOne.exe`, not the runner's
default `Builds/Windows/FFOne.exe` (observed executable date: 2026-10-24).

All four recorded hashes were recalculated on 2026-11-14 and matched:

| File under `Builds/NativeTutorialOverlay/Windows/FFOne_Data` | SHA-256 |
| --- | --- |
| `Managed/FFOne.Runtime.dll` | `536f90b88eeb2f883e250f5ff00a8525abc64aaa05b03a89b8af9476f406a5a5` |
| `StreamingAssets/aa/settings.json` | `0ee964617cdd8a5bd51353abc8e3c98eab6f5e89cca4816dca263ba0929b4f66` |
| `StreamingAssets/aa/catalog.bin` | `71bbe3456f6eb636c9f56822851e30b48a2948a882652d0a3d1de64e8479af87` |
| `StreamingAssets/aa/catalog.hash` | `9fda5aa78a9b9b5afdcaa010e343e29334b238d8b69da92b897dbc72345fd13e` |

- Creation/world phase: passed, exit 0, 245.302 seconds; ten cinematics,
  HUD, bridge, movement and environment checks passed.
- Cold occupied roster: passed, exit 0, 11.593 seconds.
- Both phases: no reported exceptions, missing animations or blocking
  appearance waits. Creation/world still records other synchronous content
  waits and frames up to 2.939 seconds. Smoke success is not a performance pass.
- Native input and audible SFX are not established by this receipt: those
  profile fields are false/empty. Do not interpret unused optional profiles as
  either a demonstrated regression or successful coverage.
- `Logs/NativeTutorialOverlay/build.log` records
  `NATIVE UI VALIDATION BUILD SUCCEEDED`.

Historical scoped EditMode results, inspected as XML:

| Evidence | Result | UTC execution date |
| --- | --- | --- |
| `Logs/NativeTutorialOverlay/tests.xml` | 64/64, no failed/skipped | 2026-11-08 |
| `Logs/NativeGameplayInput/tests-verified.xml` | 24/24 | 2026-11-08 |
| `Logs/NativeTutorialTimeline/tests-duration-fix.xml` | 66/66 | 2026-11-08 |

These are separate selected suites, not one full current suite. Hash equality
authenticates the retained build against its receipt; it does not establish
that the dirty current source tree produced that binary.

## Preservation and coverage inventory

| Feature | Decision | Evidence level and next boundary |
| --- | --- | --- |
| FFOneClient appearance identities, including distinct Ben variants | PRESERVE | Required by AGENTS and FusionForge source contract; expand per-object provenance separately |
| Additional Nano/NPC and distinct Eddy legacy identity | PRESERVE | 68 Nano directories present; identity policy documented, all abilities not proven |
| Authored scenes and collision surfaces | PRESERVE | Wired streaming plus historical actual-client routes; no exhaustive new scene scan |
| Ten native tutorial Timelines, skip and cleanup | PRESERVE | Wired graph, scoped tests and recorded player execution |
| Character first frame, creation, world entry, occupied roster | PRESERVE | Actual-player receipt with verified retained hashes |
| Stable content addresses, GUIDs and authoring ownership | PRESERVE | Existing migration receipts; avoid mass regeneration |
| Inventory, trade, shop/bank, mail, friends/groups, travel/instances | PRESERVE | Network implementations plus October scoped acceptance, not newly executed |
| Weapons, PvP privileges, buffs, Recall, Slider and vehicles | PRESERVE | Focused Docs and `Tools/LiveGameplay/acceptance-20261017-combat-buffs.json` |
| Synchronous content waits and session-long resource caches | IMPROVE | Observed waits; lifetime/mobile work remains |
| Remaining NPC/Nano animation behavior and trigger semantics | RECONSTRUCT | README records gaps; verify current implementation before selecting a slice |
| Exact grenade physics and missing BulletTable rows 170–174 | RECONSTRUCT | Explicit unresolved historical evidence, not permission to invent values |
| Dash, BuffHeal, ExtraBank, some item consumers and group-NPC aura | RECONSTRUCT | Explicit coverage gaps in combat acceptance; re-audit both client/server |
| Typed gameplay definitions and AudioMixer/events | IMPROVE | Older README backlog; confirm against later audio work before implementing |

The principal historical gameplay receipts live in `Tools/LiveGameplay`,
including commerce/mail/travel on October 15, instance/Recall on October 16,
and combat/buffs on October 17. Their different binary hashes and coverage must
remain separate; do not combine their pass counts into current production readiness.

## First production task candidate

Establish a current, isolated acceptance baseline for frontend, character
creation, first rendered frames of all ten tutorial scenes, overworld entry and
cold occupied roster on one identified player/catalog/server combination.
Use [validation.md](validation.md), then select one measured resource-loading
stall for a bounded improvement. Preserve identities and collision surfaces.
Acceptance must include capture review and hashes, not only compile success.

A safe documentation-only Development Studio exercise is to reconcile the
README Timeline paragraph with the current native graph contract and obtain
independent review. A safe Reconstruction exercise is to reproduce bounded
evidence for the recorded jump contract without republishing assets.
