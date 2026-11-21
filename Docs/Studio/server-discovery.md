# Server and previous-client discovery

Read-only discovery performed 2026-11-14, followed by the focused existing network tests described below. No game code, server configuration, database or live server process was changed. Repository counts are snapshots; concurrent user work may change them.

## Repository inventory

All paths are under `D:/CodexProject/FusionFallProject`.

| Repository | HEAD at audit | Dirty porcelain entries | Purpose |
| --- | --- | ---: | --- |
| RustyFusion | `718e92d` | 73 | Primary Rust/Tokio server; SQLite default, optional PostgreSQL, Luau AI |
| FFOneClient | `fdee0fb83` | 406 | Previous Rust/Bevy client; accepted content identities and behavior |
| OpenFusion | `26aeefe` | 0 | Local legacy server and accepted extensions reference |
| FFOneWeb | `fe114d4` | 0 | Web project |
| OpenFusionLauncher | `1d4975f` | 1 | Launcher; `resources/ffrunner` modified |

Sibling Cargo manifests identify FFOneClient and its crates, RustyFusion, FusionForge, ofapi and the launcher. No distinct additional historical Rust gameplay client was found within that bounded inventory. This does not prove none exists elsewhere. Treat FFOneClient as the located previous Rust client; keep the additional-client question open.

Read instructions: `RustyFusion/AGENTS.md`, `FFOneClient/AGENTS.md`. RustyFusion's existing instructions preserve the single FFOne/Retrobution protocol, accepted identifiers, Nano acquisition versus progression, local OpenFusion evidence and isolated DB testing. FFOneClient's instructions preserve variants, EN/RU, replacement fonts and native editable assets. Do not recreate deleted historical bug cards or reuse their IDs.

At the time of this audit RustyFusion used `FFOneClient/crates/ffone-runtime-contracts`. Since 2026-11-21, the server owns its required parsers in `src/tabledata_contracts` and builds independently. The archived `FFOneClient/docs/source-map.md` locates historical implementations.

## Preservation inventory seeds

These rows are evidence-backed preservation requirements. They do not independently certify Unity migration or present runtime correctness. The main Studio register should track presence, wiring, execution, testing and parity separately.

| Feature | Class | Identifiers / source evidence | Dependency and unresolved verification |
| --- | --- | --- | --- |
| Separate Omniverse NPC variants | PRESERVE | 3464 Ben, 3465 Gwen, 3466 Kevin, 3467 Albedo, 3468 Max; `FFOneClient/docs/npc-variants.md` | Document says types exist but are not placed in world; preserve originals, distinct models/portraits and EN/RU |
| Additional Nano identities | PRESERVE | 37 Flapjack, 38 Johnny Bravo, 41 Unstable, 67 Coop, 68 Ben, 69 Ghostfreak, 70 Upgrade; `docs/nano-roster.md` | Keep tuning rows and skill IDs distinct; verify current client/server tables and ownership |
| Nano41 quest/save identity | PRESERVE | Mission841, tasks5213–5215; same document | Existing Nano41 ownership stays Unstable; no save rewrite to Coop67 |
| Nano52 retention | PRESERVE / unresolved conflict | FFOneClient `docs/nano-roster.md` retains52/66 as distinct Van Kleiss-model variants; older server migration audit calls52 retired; `FusionForge/recipes/native/identities/accepted-native-identities.json` has `unstableNano.retiredAlias: 52` and `retiredSlot: "empty"` | Do not retire, renumber or merge automatically. Record contradictory sources and resolve against owner decision/current effective data |
| Owner-selected Nano powers | PRESERVE | Ben/Ghostfreak/Upgrade and Coop effect choices in `docs/nano-roster.md` | Shared effects do not merge ownership or visual identity |
| Nano acquisition versus growth | PRESERVE | `RustyFusion/src/server/shard/nano_acquisition_tests.rs`, `src/mission.rs` | Growth FM/level effects, ordinary level requirements, repeated completion and preowned Nano paths |
| EN/RU text and independent voice locale | PRESERVE | `FFOneClient/docs/localization-and-voice.md` | Semantic keys, placeholders, approved replacement fonts, geometry, variant-before-locale voice fallback |
| Protocol and session improvements | PRESERVE | Active codecs and `docs/server-gameplay.md`; see [contract](client-server-contract.md) | Expanded palettes, transaction ownership, malformed-frame handling, active2700/2688 layout |
| Server extensions and identifiers | PRESERVE | Current server dispatch, local OpenFusion and migration audit | Barber, bank/storage, tuning/NPC IDs, Luau behaviors; inspect effective tables per feature |
| Authored instance-route repairs | PRESERVE | `RustyFusion/docs/instance-route-repairs.md`; placement40000–40020, warps321–323, maps142–144/149/151 | Accepted authored extensions coexist with source-derived fixes; not historical placement/parity claims |
| Mission842 continuation | PRESERVE | Same route document, maps146–148, escort3338/hostile3337 | Preserve task graph, failure transitions, authored spawn and cost/prerequisite rules |
| Rust client implementations | PRESERVE | `FFOneClient/docs/architecture.md`, `docs/source-map.md` | Protocol golden tests, transport, tutorial core, schema and editor experience are migration sources |
| Additional Academy quests/progression | CANDIDATE | User source policy; no new quest extraction performed in this audit | Do not infer approval to replace progression wholesale |
| Reborn features | CANDIDATE | User source policy | Need bounded evidence and explicit product choice before integration |

No new DEPRECATED decision was established. Presence of a different historical implementation does not justify retiring an accepted product feature.

## Documentation conflicts and remaining handler questions

`RustyFusion/docs/openfusion-migration-audit.md` records September evidence. It is not a current blanket failure list. Current inspected dispatch handles `P_CL2FE_DOT_DAMAGE_ONOFF` and `P_CL2FE_REQ_REGIST_RXCOM`; associated infection and recall test modules exist. These supersede the historical assertion that those dispatch arms were missing, without proving full end-to-end behavior.

Current inspected login dispatch still lacks `P_CL2LS_REQ_CHANGE_CHAR_NAME`. Search did not find registration of `P_CL2FE_REQ_WARP_USE_RECALL`, trade-offer-abort, GM warp-to-PC or GM give-Nano-skill. Street-stall cancel is registered; a full store is not established by that handler. These are research/integration gaps, not permission to add historical messages automatically. Recall registration alone does not prove recall travel compatibility.

The older `FFOneClient/docs/protocol-0104-inventory.md` is pinned to an OpenFusion snapshot with116 shard registrations, while later server audits describe133. Preserve both evidence dates and inspect current dispatch for the specific request; do not treat the counts as competing present-day truths.

## Baseline and limits

Fresh focused command from RustyFusion:

```powershell
cargo test --locked --offline --lib net::
```

Result 2026-11-14: exit0, **6 passed / 0 failed / 228 filtered out**; test run0.29s after compilation. Log: `FFOneUnity/Logs/Studio/server-net-baseline.log`. One `unused_braces` warning from `src/server/shard/item_move_tests.rs:114`; no runtime/database process started. The heartbeat fixture uses an isolated local network test, not the configured production server. Build artifacts are under existing RustyFusion `target/`.

Historical October16 logs were inspected but are not fresh acceptance:

- `RustyFusion/target/buff-item-check.log`: successful cargo check, one warning.
- `target/instance-recall-tests.log`:228 passed.
- `target/buff-integration-lib-tests.log`:231 passed, one AI pack-following failure.
- `target/buff-ai-follower-recheck.log`: that single test later passed; this does not replace a complete rerun.

This audit did not run the full RustyFusion suite, PostgreSQL matrix, release packaging, live DB migration, Unity runtime, multiplayer or visual tests. Server README completeness/performance statements were not independently verified. The fresh six-test result proves only its scoped network invariants.

## Recommended production follow-up

Verify one complete character-entry flow with existing golden codec tests and isolated accounts: login, occupied roster portrait, name save/create, first rendered avatar, shard/world admission, return-to-roster and reconnect. Cross-check accepted identities, saved UID and generation/timeout behavior. Record first-frame and world evidence separately from compilation. If a gap requires runtime/content or protocol changes, prepare the bounded impact plan before implementation.
