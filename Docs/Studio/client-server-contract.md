# FFOneUnity / RustyFusion contract

Audit: 2026-11-14. This is a source-backed starting contract, not a claim of complete gameplay parity. Paths below are relative to `D:/CodexProject/FusionFallProject` unless they start with `Assets/` (FFOneUnity).

## Product boundary

FFOneUnity is the production Unity client. RustyFusion is the production server. FFOneClient is the previous Rust/Bevy client and a source of accepted identities, behavior, regression tests and engineering implementations. The local OpenFusion checkout is a legacy reference, not the destination for new server features.

Support one FFOne / Retrobution 0104 protocol and preserve local extensions. The production implementations live in RustyFusion and FFOneUnity. RustyFusion owns the server-side table validators in its checkout.

## Shared gameplay tables

RustyFusion pins the authoritative `tabledata` revision as a Git submodule. Live placements, drops and eggs come from the server. Unity ships an editable native publication of XDT for presentation and gameplay lookups. Its publication metadata has a separate lifecycle.

After changing the server's XDT, run `python Tools/Studio/check_tabledata.py` from FFOneUnity. The check compares the entire gameplay payload and reports the two SHA-256 hashes and any divergent sections. Inspect a disagreement before publishing. Use the existing `FFOne.Editor.InstanceRoutesPublication.Publish` editor command for a reviewed publication. It checks asset identity and preserves authored direct model routes. Retain the source, native document, receipt and asset GUID together. Use a reviewed overlay for any intended gameplay divergence and record its scope here before accepting it.

The 2026-11-21 check against tabledata `f3285f1` found the same gameplay values in server XDT, the Unity source JSON and the serialized Unity document. Unity carries its own model routes under `_ffone`. The older source hash in the publication receipt predates the current source and should be reconciled with the native document before the next XDT publication.

Unity's Nano landing publication already samples native collision and exports the result to RustyFusion's `tabledata/unstable_landings.json`. Review and publish that file with the server tabledata revision.

## Wire format

Source owners: `Assets/FFOne/Runtime/Network/Protocol0104.cs`, `FrameConnection.cs`; `RustyFusion/src/net/{mod,crypto,ffconnection,packet}.rs`; `FFOneClient/crates/ffone-protocol` and `ffone-net`.

| Property | Inspected contract |
| --- | --- |
| Transport | TCP; four-byte little-endian plaintext body length |
| Body | Encrypted four-byte packet ID followed by payload; 4..4096 bytes including ID |
| ID mask | `0xff000fff`; bits 12..23 carry a 12-bit Fletcher-style payload checksum |
| Client sequence | Outgoing sequence XORs checksum flags; preserve socket writer ordering |
| Initial key | `0x51217e5472243e62`; legacy XOR and byte-swap transform |
| Derived FE key | `0x3989a24fb8c56068` in accepted vectors |
| Login request | 468-byte payload; password and cookie formats have different field encodings |
| Roster character | 204-byte payload, account-owned 64-bit UID, signed coordinates, appearance and equipment |
| World entry | 2700-byte payload containing 2688-byte load data; selected UID must match |
| Names | UTF-16LE fixed fields; cookie fields use byte buffers |

Validation is asymmetric in the inspected code. Unity validates server frame checksum, size, truncation and trailing data. RustyFusion `FFConnection::read_next_packet` strips inbound checksum/sequence bits before decoding the ID; it does not validate their value in that path. Do not describe this as symmetric checksum validation or change it as part of infrastructure setup.

The historical generated wire mirror has 2552/2564-byte load/entry layouts. Active accepted codecs use 2688/2700. Generated declarations are evidence, not permission to replace active codecs. Preserve explicit field packing, counts and signedness; compare both active implementations before any wire change.

## Session and error ownership

`FusionClient.cs` owns login, shard and retiring-shard connections, world entry and return. `FrameConnection.cs` maintains exactly one reader and serialized writer per socket. One pending transaction owns its expected reply set. Timeout/cancellation closes the socket so a late response cannot complete a subsequent transaction. Heartbeats are handled during handshake and pumping. Shard generation filters discard stale ingress.

Character flow is authenticated login -> roster -> name check/save -> appearance create -> character/shard selection -> shard handshake -> world snapshot -> loading completion. Creation requires the account-owned UID from successful name save. An unfinished appearance entry is distinct from a completed character. The retained login connection supports return-to-roster. Test actual reconnection and rehydration; socket fields existing in code are not recovery acceptance.

Server-side state determines inventory, currency, rewards, Nano ownership/stamina/tuning and mission progress. UI submits typed intent and applies accepted post-state. Preserve failure replies, transaction correlation, timeouts and disconnect behavior; never infer success from a packet send.

## Subsystem owners and integration gates

| Area | RustyFusion owner | FFOneUnity owner | Required behavioral gate |
| --- | --- | --- | --- |
| Characters | `src/server/login/login.rs`, `src/state/login.rs` | `FusionClient.Characters.cs`, `LoginServer.cs` | Name-save/create/delete/select, unfinished roster, account UID authorization |
| NPC/services | `src/server/shard/npc.rs`, `src/barber.rs`, `src/tabledata.rs` | `ProtocolWorld.cs`, `FusionClient.Services.cs`, `WorldEntitySession.cs` | Accepted type and placement identity; service outcome and prerequisite checks |
| Nano | `src/server/shard/nano.rs`, `src/nano.rs` | `ProtocolGameplay.cs`, `ProtocolNanoSkill.cs` | Ownership, active state, tuning row/skill identity, stamina and cost |
| Progression/quests | `src/mission.rs`, `src/mission_stage.rs`, `src/server/shard/mission.rs` | `FusionClient.Missions.cs` and gameplay state | Task transitions, prerequisites, costs, rewards, exactly-once completion |
| Inventory/vendor/bank | `src/item.rs`, `src/entity/player/inventory_actions.rs`, `src/server/shard/item.rs` | `ProtocolInventory.cs`, `FusionClient.Inventory.cs` | Equipment 9/inventory 50/bank 200 slot boundaries; authoritative post-state |
| Trade/mail/social | `src/server/shard/{trade,email,buddy,group}.rs`, `src/trade.rs` | `FusionClient.Trade.cs`, `FusionClient.Social.cs`, family codecs | Both-party confirmation, replay/cancellation, malformed bodies |
| Travel/instances | `src/server/shard/transport.rs`, `npc.rs`, `src/instance_route_tests.rs` | `ProtocolTravel.cs`, world session and scene loading | Destination identity, cost/item/task gates, loaded-scene admission |
| Persistence | `src/database/{mod,sqlite,postgresql}.rs` | Session rehydration and visible UI state | Save/reopen isolated DB, retained identities, rejoin and authoritative snapshot |

Nano acquisition is not synonymous with level advancement. Growth missions may change level and charge Fusion Matter; ordinary quest/item/GM awards may require a level while preserving it. Never infer level from Nano ID, globally suppress Nano level-ups or rewrite saved ownership to match a different model.

## Changes spanning both sides

The Technical Director assigns one integration owner. Unity Gameplay Engineer and Network & RustyFusion Engineer inspect both active codecs, dispatch and state reducers before proposing a changed packet or behavior. Keep a reviewable impact plan with existing behavior, desired behavior, retained identities, fixtures and rollback. No two agents edit the same protocol contract concurrently.

Use isolated accounts, distinct local ports and a disposable database under ignored `target/` for mutating tests. The existing `RustyFusion/src/database/native_stage_persistence_tests.rs` uses UUID-named SQLite files under `target/mission-stage-tests`. Do not launch ordinary `cargo dev` against the user's current configuration as a smoke test: it may open the live database.

## Reproducible verification

From `RustyFusion`:

```powershell
cargo test --locked --offline --lib net::
```

Executed 2026-11-14: exit 0, **6 passed, 0 failed, 228 filtered out**. Covers aligned/misaligned buffers, packet roundtrip, encryption roundtrip and delayed service heartbeat using the negotiated key. One pre-existing-style `unused_braces` warning at `src/server/shard/item_move_tests.rs:114` was emitted; no automatic fix applied. Full log: `FFOneUnity/Logs/Studio/server-net-baseline.log`. This is a focused server network unit baseline, not a full server build matrix or Unity/server round trip.

Existing Unity EditMode `FFOne.Tests.NetworkProtocolTests` contains independent accepted vectors, malformed-frame cases, login-cookie bounds, roster fields, world entry UID checks and movement layouts. Other owners include `NetworkSessionTests`, `NetworkShardSessionTests`, `NetworkWorldContractTests`. Their presence is inspected here; execution results belong in the Studio verification report.

Useful existing encrypted character-select vectors for packet `0x12000005`, payload `08 07 06 05 04 03 02 01`, initial key:

- Sequence 0: `0C000000737EE6605C792754663D2667`.
- Sequence 7: `0C000000730EE6605C792754663D2667`.

Reuse these actual codec fixtures; do not substitute tests that only match source strings. Baseline pure unit success does not prove NPC interaction, quest completion, persistence, visual correctness or client/server interoperability. For avatar-related changes verify the actual client's first visible frame, occupied roster portraits, creation and world entry.
