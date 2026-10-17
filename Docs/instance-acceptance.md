# Independent instance acceptance

The production catalog preserves 151 nonzero map identities and 51 published
geometry cells. A shared scene never merges server map identities. The matrix
uses ordinary accounts with permission level 99, actual portal NPCs, XDT access
conditions, the loading handshake and exact authoritative destinations. Character
levels follow the route prerequisites. The selected routes use explicit NPC exits
for 140 maps and the saved pre-warp overworld fallback for the other 11, followed
by reconnect checks. The Windows player submits the production portal UI and checks
scene admission, colliders, rendered pixels, full unload and a fresh entry.

## Production route completion, 16 October 2026

The former 16 scene-only cases now have executable production routes. Thirteen
use reviewed table repairs and explicit additional routes; maps 146/147/148
exercise the sequential Nano mission 842. The server tables contain the actors
for normal clients. The acceptance runner does not invent portal placements.

See [the production route repairs](../../RustyFusion/docs/instance-route-repairs.md)
for the exact distinction between source-supported corrections and authored
native extensions:

- Maps 142/143/144 declare cell `map_07_05`, matching their original warp XYZ.
- Map 151 declares `map_15_09`, matching warp 319 and the original exit actors.
- Warp 317 and tasks 5236/5237 use existing map 149; dangling map-150 references
  are reconciled without adding or merging a map identity.
- Missing original portal types have persistent, explicitly authored placements
  anchored to paired warp endpoints.
- New choices 321/322/323 reach maps 4/136/152 through existing portal types,
  preserving their donor level, task, item, consumption, price and group gates.
- Unrelated invalid alternate warps 282/286 remain rejected by the geometry
  coverage guard; map 135 retains valid warp 284.

Unity publishes the reviewed server XDT into its existing NativeDataDocument
without changing the GUID. The publication preflight rejects unreviewed authored
source/document drift, checks the unchanged map identity set and updates the
source and provenance together. Addressables must then be rebuilt.

## Sequential Nano mission 842

Only saved initial task 5222 is seeded. Ordinary start/end requests reach later
stages. Warps 312/313/314 commit the original InstanceLeft=12 failure edges to
5227/5233/5231; the production frontend consumes the same branch and waits for
scene/loading readiness before requesting its successor. Other task rejections
preserve the running journal. Task 5233's outgoing 5229 prevents premature
mission completion despite its position as the last serialized XDT row.

Stage 5229 uses the real escort 3338 and hostile 3337. The fixture supplies a
normal accepted level-36 melee weapon 65 in inventory slot 49; both harnesses
equip it through the normal item-move protocol. Combat retains the enemy's
original stats, corruption immunity periods, actual damage, zero HP, server
kill credit and living escort checks. No later quest counters are seeded.
The full authored exit is exercised before reconnect, so later stages do not
become stranded outside their required instance. The route is then repeated.

## Final acceptance, 16 October 2026

The two fresh disjoint partitions completed successfully on the same final
Windows player: **151/151 server routes and 151/151 native-player routes across
51 published cells**, with zero blocked or scene-only cases. They recorded
5,896 network checks, 5,949 player checks and one named rendered capture for
each map. All 140 NPC exits and 11 saved pre-warp fallbacks passed, including
unload, reconnect and fresh entry. There are no unresolved map references,
NPC content errors, unexpected C# exceptions, server panics or AI script errors.
Both owned instance servers stopped normally.

The separate final gameplay run passed **220 network and 196 player checks**,
including actual Recall registration, ACK-driven ES811, a rejected registration
that preserves selection, the real Nano return and clearing on the authored
exit. Its owned server also stopped. All **284 selected Unity Editor tests**,
the complete **210-test RustyFusion library suite**, and six Python planner and
acceptance-invariant tests passed.

The delivered `Builds/Windows/FFOne.exe` uses `FFOne.Runtime.dll` SHA-256
`82994d820b34e9b7818a8b6755e76866e9b63b1bc1b5cc68d20540ec69171037`.
Both full partitions, the separate Recall run and the delivered files have
identical DLL and built Addressables catalog hashes. The final publisher also
checked current server/harness/executable hashes, source fingerprints and the
server/client XDT publication. No focused recovery replaces a failed row in
this final matrix; earlier interrupted attempts remain diagnostic evidence.

The [final acceptance receipt](../Tools/LiveGameplay/acceptance-20261016-instance-routes-recall.json)
records `passed=true` and `fullMatrixRerunOnFinalBinary=true`, all 151 map results,
the full reports and test-log hashes, source fingerprints and 177 capture hashes.

## Reproduction

With the Unity editor closed:

```powershell
python Tools/run_gameplay_validation.py route-data
python Tools/run_gameplay_validation.py tests --test-results Logs/InstanceRoutesEditorTests.xml
python Tools/run_gameplay_validation.py content-build
python -m unittest discover -s Tools/LiveGameplay -p test_instance_plans.py
$first = (1..76) -join ','
$second = (77..152 | Where-Object { $_ -ne 150 }) -join ','
python Tools/run_instance_validation.py --maps $first --player Builds/Windows/FFOne.exe --noncombat --require-routes --result-path Logs/InstanceRoutes151Part1.json
python Tools/run_instance_validation.py --maps $second --player Builds/Windows/FFOne.exe --noncombat --require-routes --skip-build --result-path Logs/InstanceRoutes151Part2.json
python Tools/summarize_instance_acceptance.py Logs/InstanceRoutes151Part1.json Logs/InstanceRoutes151Part2.json --output Logs/InstanceRoutes151Final.json
python Tools/run_live_gameplay.py --player Builds/Windows/FFOne.exe --skip-build --result-path Logs/InstanceRoutesRecallGameplayFinal.json
python Tools/LiveGameplay/write_instance_recall_acceptance.py --matrix Logs/InstanceRoutes151Final.json --gameplay Logs/InstanceRoutesRecallGameplayFinal.json --editor-tests Logs/InstanceRoutesEditorTests.xml
```

`--require-routes` rejects any scene-only plan and requires both real network
and native-player route coverage. Each row must pass without NPC content errors.
The runner records exact table/source, server, harness and player hashes, verifies
that the player DLL and built Addressables catalog did not change during the run,
and stops only its own server. The two disjoint partitions can run concurrently
after a single server/harness build, using `--skip-build` for both. Their merger
requires identical binaries, catalogs and sources, and exactly one row per map.
All mutable fixture data remains under an owned ignored
`RustyFusion/target/unity-gameplay-instances-*` directory.

`--noncombat` removes unrelated hostile spawns from the isolated copy while
retaining mandatory mission hostile 3337. It records the original and effective
mob hashes. Production portal placements, XDT conditions, normal privileges and
mission encounter stats remain intact. This isolates route acceptance from
unrelated overworld PvE. A focused `--maps` rerun does not replace a full matrix.

The retained scene-only diagnostic mode remains available for investigation,
but cannot satisfy `--require-routes` or the final acceptance publisher.
Full scene/route coverage does not establish every mission, room, enemy script
or original-client visual/timing parity. The matrix exercises one selected route
per map identity, rather than every alternative warp. It checks scene and collider
admission; its controlled positioning before each portal does not establish
continuous walking access through every room or corridor.

Plan paths are independently owned by each case. The planner regressions cover
the shared prefix of maps 145/146/147/148: mission actions and the equipped weapon
must never leak from a sequential case into map 145 or another case. The complete
matrix is regenerated after a planner change; earlier partial runs are retained
as interrupted diagnostics rather than merged into final acceptance.

## Earlier evidence

`acceptance-20261016-slider-vehicles-instances-recall.json` retains the earlier
135-route/16-scene-only baseline and separate final HNPC-646 repair. It is
historical evidence, not the full rerun requested here. The HNPC backpack repair
and all 195 accepted nonempty appearances remain covered by Editor regressions.
