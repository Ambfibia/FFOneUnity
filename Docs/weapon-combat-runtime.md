# Weapon combat runtime, 2026-10-17

This implementation uses the accepted Retrobution 0104 weapon table and the local OpenFusion combat contract. The inventory contains 806 positive weapon rows: 202 melee, 178 pistol, 226 shotgun, 73 sniper, 74 rocket and 53 grenade. None of their item IDs, attack powers, Nano tuning IDs or account privileges were changed.

The ordinary frontend attack path now selects the equipped weapon's range, number of targets, angle, delay and animation. Rocket and grenade input sends native FIRE requests, waits for the server's allocated bullet slot, renders that accepted bullet and sends HIT only when its motor impacts or expires. Authoritative damage updates both the local health state and interest replicas. Fire acknowledgements update the HUD battery counter. World exit and committed instance changes clear owned projectiles on both ends.

## Native evidence

FusionForge inspected the embedded `Assembly - CSharp.dll` in `builds/retrobution-20260821/main.unity3d`. `cnWarHead.InitInfo` (method token `0x060006f0`) converts weapon duration from tenths of a second, uses server-to-client attack range as flight speed, selects Effect1/Effect2 from the charged flag, then resolves that ID through BulletContainer. A grenade's initial vertical speed is half its DeliverTime. `cnWarHead.Update` (`0x060006f6`) distinguishes rocket movement toward its endpoint from grenade gravity and a 0.8 bounce response. Bounded JSON inspection is preserved in `Logs/CombatEvidence/`.

All 127 projectile weapons reference exactly 11 published bullet rows: 68, 69, 72, 135, 149, 150, 160, 161, 162, 163 and 164. The runtime resolves their original particle/model closures, charged effect choice, mesh and particle scales, impact scripts and available original impact sound cues. Bullet 162 has an authored quote-only cue and remains silent. Native inverse-alpha additive particle blends in grenade impacts use the existing World/Particle shader with their exact blend factors. The tutorial's existing shader defaults are preserved.

Local OpenFusion `Combat.cpp::pcAttackChars` allows only account levels at most GM (30). Its request targets are eight-byte `(ID, character type)` pairs, where PC=1 and mob=4. Unity now supports that preserved privileged attack path and the server validates it. Normal level-99 accounts cannot damage other players. The native player-versus-player damage calculation has no Nano RPS or NPC level difficulty term. This work does not invent a normal-account PvP entitlement, team mode or duel protocol.

Native `sPCBullet.iID` is a grenade marker, rather than its equipped item ID: local OpenFusion `Combat.cpp::addBullet` and `grenadeFire` explicitly send `1` for grenades and the actual weapon item for rockets. All 53 accepted grenade rows have the same original visual and flight parameters: Effect1/Effect2 72/163, attack range 800, effect area 250, duration 25 and deliver time 12. Item 22 therefore keeps its original equipment identity and stats while the wire marker resolves the exact same authored grenade visual/flight route as item 1. The live acceptance checks those identities separately and verifies parameter equality; the server's native marker is preserved.

## Server validation

Hitscan and projectile FIRE requests validate their complete frame, equipped weapon, alive/unmounted state, crowd control, cooldown and allowed targets before charging battery. Target identity includes its namespace; targets must be distinct, living, hostile where applicable, visible, in the same exact instance and within weapon range. Projectile damage snapshots its launch power, battery charge state, blast radius, target limit and instance. HIT requires the still-owned unexpired slot, exact native eight-byte target tail, a plausible flight envelope and blast targets inside the accepted effect area. Rejected HITs preserve ownership; a valid HIT consumes it once. Capacity rejection does not charge battery. The established RustyFusion battery formula remains unchanged.

## Validation and scope

`weapon_combat_tests` has seven passing server tests. They exercise every accepted hitscan and projectile weapon row through production handlers, then check malformed frames, wrong target namespace, normal-account denial, cooldown, range, hostile/dead/instance checks, projectile replay, expiry, capacity and warp cleanup. The focused result is `RustyFusion/target/weapon-combat-tests.log`.

`WeaponCombatProtocolTests` covers native packet offsets and target namespaces, malformed frame rejection, all six family counts, all 11 bullet references, original effect instantiation and original sound publication. It also verifies the native grenade marker, all 53 grenade rows' equal visual/flight/target parameters, strict FIRE-family markers, and shared native HIT packets carrying either a rocket item ID or grenade marker 1. The full Rust suite passed 234 tests; the gameplay, presentation, weapon and buff Editor regression suite passed 328 tests in `Logs/CombatBuffEditorTests-20261017.xml`. The final acceptance helper also compiled with zero errors in the isolated runtime MSBuild check.

`Program.WeaponCombat.cs`, `weapon_combat.py` and `Tools/run_weapon_validation.py` provide an independent isolated acceptance fixture. Two accounts keep normal permission 99; one explicitly disposable account alone receives source GM 30 in the runner-owned `RustyFusion/target/unity-weapons-*` SQLite database. An unused bootstrap account absorbs the server's first-account Master privilege. The accepted XDT is copied unchanged, and six extra type 59 mob placements keep their original live statistics. The network pass exercises all six families, authoritative damage and observer results, one-use projectile slots, normal-account PvP denial, and source-permitted GM PvP. On 17 October 2026 it passed all 42 checks in the final receipt `Logs/WeaponValidationFinal.json`.

`LiveGameplayWeaponSmoke` (`--live-weapon-smoke`) uses the actual frontend attack path, renderer and server transactions for all six families, including automatically reported rocket/grenade impacts. It additionally checks normal 99 frontend/server PvP denial, real GM 30 frontend PvP damage and observer health, authoritative HUD cost, and exit while an owned rocket is flying. FIRE packet IDs identify the family; shared native HIT packets use grenade marker 1 or the rocket item ID. The final player run passed all 220 checks. Its `weapons-result.json` includes raw incoming projectile and PvP receipts, real target movement and health, original flight geometry/materials, named screenshots and shown/hidden pixel comparisons.

A moving target can legitimately evade a native grenade. The helper permits at most three normal projectile attempts, each with a new aim, the original cooldown, real FIRE acknowledgement and observer trajectory, and its actual battery cost. Only a completed empty-target impact can be retried. RustyFusion consumes that empty HIT without an acknowledgement or broadcast; a nonempty impact must receive matching-slot owner/observer HIT receipts and matching target Damage/HP, or acceptance fails. HP baselines are refreshed before each attempt; each family's HUD records battery before the first attempt and the actual final total. Incoming wire receipts are labelled separately from `expectedHitPayloadFromImpact`, which is reconstructed from the production impact event rather than captured from the outgoing transport. Both projectile families succeeded on their first attempt in the final run.

After the actual gameplay transactions, a separately labelled visual matrix instantiates both original flight and impact closures for all 11 distinct accepted bullet rows. Its 22 captures use the same production effect loader and exact authored scales; moving flight effects use the accepted reference weapon's range as native flight speed. Every entry requires supported material shaders and more than 32 changed pixels compared with the same empty inspection camera. `projectileClosureVisuals` records bullet/effect/weapon IDs, scale, geometry, original culling, camera and changed pixel count separately from the real server transactions. Shown/hidden PNGs and metadata are saved before the pixel assertion. For coplanar, consistently wound meshes with authored back-face culling, the inspection camera selects their front side from the actual transformed triangles. ES793's two planes retain their original disabled billboard components, culling and scale; their final Bullet 162 flight proof contributes 57,590 pixels.

## Final player evidence, 17 October 2026

The complete command exited 0:

```text
python Tools/run_weapon_validation.py --skip-build --player Builds/Windows/FFOne.exe --result-path Logs/WeaponValidationFinal.json
```

The final owned fixture is `RustyFusion/target/unity-weapons-20261017-111715-648493/`. Its `result.json` and `player-profile/weapons-result.json` agree with the final receipt. The owned servers and player have stopped.

| Evidence | Final result |
| --- | --- |
| Network acceptance | 42/42 checks |
| Actual Unity player | 220 passing checks; all six weapon families |
| Native rocket flight | 96,767 visible pixels; `player-profile/weapon-5-flight.png` and matching `-motor-hidden.png` |
| Native grenade flight | 123 visible pixels; `player-profile/weapon-6-flight.png` and matching `-motor-hidden.png` |
| Independent original effect matrix | 22/22 flight/impact closures; minimum 2,097 changed pixels; `player-profile/bullet-<id>-flight.png` / `-impact.png` with matching `-effect-hidden.png` |
| Actual battery HUD | Seven shown/hidden label proofs, 214–255 changed text pixels |
| GM30 PvP | Native target ID 2, Damage 12, HP 1375→1363; owner ACK, observer broadcast, canonical owner HP and rendered target HP agree exactly; battery 1000→988 |
| Ordinary99 PvP | Frontend refusal and independent server denial; no target damage or success |
| Cleanup | Acknowledged exit removes in-flight projectile motors and world ownership |
| Content/runtime/server errors | Empty content errors, player exceptions, AI script errors and server panics |

The seven actual HUD values are `0992`, `0984`, `0976`, `0968`, `0961`, `0949`, and GM PvP `0988`; their baseline-to-final costs are 8, 8, 8, 8, 7, 12 and 12. The PvP evidence retains the native 32-byte owner ACK `0x31000102` and observer broadcast `0x31000103`, including target namespace, ID, Damage and HP, for independent decoding.

Player runtime assembly SHA256: `22e28718f0ab2935acdc94b4f2574c8bc611362912cb747b81822646f570239a`. Addressables `catalog.bin` SHA256: `778aebd6faa8572476b60d64fafda124a9b12c373a90d8af413d4b97ef9fe676`. The runner confirms identical before/after hashes for the assembly, all three player content metadata files, server and harness binaries, and 66 source inputs. Source and copied fixture XDT SHA256 both remain `191a9d17f4d233270cceb574a9087990e1526ae697174655203fbb1c6116955c`.

The projectile renderer follows the observed movement and accepted assets, but grenade collision resolution is a current Unity raycast reconstruction; exact original physics timing has not been proven. This change does not claim full hitscan effect parity: five positive weapon effect references, bullet IDs 170–174, have no row in the primary Retrobution BulletTable itself. Direct primary `Effects.resourceFile` inspection of BulletTable path ID 14153 finds 170 rows indexed 0–169, proving that these are absent from the source rather than missed publication. Existing accepted animation and server damage are used, with no substituted effect for those missing routes.
