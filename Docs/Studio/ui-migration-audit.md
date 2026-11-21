# UI migration audit

The Unity baseline was FFOneUnity `f5b40dbc990f4dd42066d0bf9e32400bccff8ce9`
with existing local work, compared with FFOneClient
`fdee0fb830259faf0d73013f4f5b8edee98eca8e` and the Retrobution 2026-08-21
`main.unity3d` SHA-256
`01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`.
The [code style](ui-code-style.md) applies to subsequent UI changes.

| Area | Current change | Remaining verification or gap |
| --- | --- | --- |
| Login, roster and character creation | Restore roster keyboard navigation and preserve focus during server checks. Share roster slots, name wheels and swatches as UXML instances. | Full play flow with first visible avatar frame and an occupied roster. Older bindings and static styles in the creation controller still require conversion. |
| Tutorial and live HUD | Shared player/Nano HUD, target panel, combat frame, active Nano strip, minimap, NanoCom, chat and mission presentation. The tutorial binds local lesson state. | Live target acquisition uses the existing Unity aim query and keeps an eligible hostile in range. The reference has broader selection and a separate player target portrait. Overworld combat lock rules and Nano cooldown and delayed stamina presentation need comparison. |
| Mission journal and messages | Common offer, completion and active details, mission rows, tracked objective and NanoCom messages. The task shown by the overworld HUD follows the accepted CurrentMissionId. | Compare source NPC speech bubbles, voice timing, reward choices and mission progress text through the whole flow. |
| Settings, inventory and trade | Bind blocked players and unignore to the existing buddy protocol. Redeem through the RustyFusion command and its system response. Reuse chat rows in trade. | Run live unignore, redemption and trade. Several persisted display options still lack gameplay consumers, including nameplates, floating combat numbers, alternative chat layout and animated Nano selection. |
| 9-slice | Check source GUISkins, sprite import borders, exposed USS states and background types. Correct roster, scrollbar, journal, NPC/transport and social slices. | Unity UI Toolkit renders sampled views at multiple sizes. Other state, language and resolution combinations still need visual coverage. |

The primary UI sources include `CnGuiMonster_info::DoWindowNpc` (token
`0x06000372`), `::DoWindowMob` (`0x06000373`),
`cnGUINanocom::RenderMenu` (`0x060003a6`),
`CnGuiNano_info::DoWindow` (`0x060003bd`), `cnMissionJournal::DoWindowLeft`
(`0x06000452`) and `GameFrame::SendCombatMode` / `::EndCombatMode`
(`0x060007f4` / `0x060007f5`). FFOneClient
`crates/ffone-client/src/ui/gameplay/combat_target.rs` confirms the NPC portrait
table route. The current 0104 server message and buddy flows come from RustyFusion `718e92de33c2bde7f177f635a00f41737becdbb1`
`src/server/shard/{chat_redeem,helpers,buddy}.rs`.

Two source rectangles needed an explicit Unity UI Toolkit adjustment so their
center could stretch. The 64px NPC service texture uses L20/R43 instead of the
source L20/R45. The 31px custom name tab uses T20/B10 instead of T21/B10.
The original GUISkin evidence remains available for comparison. In Unity
SpriteImporter the border order is left, bottom, right, top.

Validation output for this work is in `Logs/Studio/UI`. The scope of the
static check includes 21 UXML files, 4,039 resolved asset references and 86
nonzero PNG Sprite import borders. FusionForge's source check validated 26
GUISkins, 584 classes and 422 output assets. The Editor checks render sampled
views in a UIDocument with actual UI Toolkit layout and event dispatch.


On 2026-11-14 the Windows player and Addressables build passed. Offline frontend
smoke passed at 1920 × 1080 and 1920 × 1200 with twelve captures each. The fresh
RustyFusion fixture used loopback ports 63500/63501 and a disposable SQLite
database. The creation/world phase passed in 264.424 s, including ten tutorial
cinematics, the tutorial HUD and first overworld frame. The cold occupied roster
passed in 11.928 s. The runtime SHA-256 on this build was
`d1127a57617d84209a3b891e86d91a07a090f208985b58f336d3c8307f1d50c9`.
See `Logs/Studio/UI/{frontend-captures,live-client}/validation.json` for receipts.
The initial cinematic smoke attempt froze tutorial input before stage 0/0
advanced and failed to see the Skip button. A repeat of the same player passed.
The smoke now waits for the opening timeline to own a frame before freezing
input. This change affects development validation only.

The broad Editor pass reported 115/115. The follow-up regression suite
reported 36/36 after the chat sizing assertion moved from inline C# styles
to resolved UI Toolkit geometry. Review of the overworld capture then
identified a duplicated active chat caption. The new USS channel states
passed the final focused Editor run (16/16). The Windows receipts predate
that final chat styling adjustment.

## Continuation on 2026-11-14

| Area | Implemented | Remaining comparison |
| --- | --- | --- |
| Frontend | Templates own the login, server browser, saved accounts, loading, roster, character editor and confirmation art. Character editor scene dependencies are serialized. | Check populated server/account lists and both languages on the final player. |
| Tutorial and world | Tutorial and overworld share the player/Nano HUD, target HUD, chat and mission journal. Subtitles, pause controls and the Dexter ship view use authored templates. World settings drive names, combat numbers and chat, speech bubbles and animated Nano portraits. | Target acquisition still uses Unity aim/proximity. Compare cooldown, exact low-stamina portrait cropping and presentation of mission reward choices. |
| Windows | Authored templates cover settings, prompts, NPC service menus, transport routes, mail, NanoCom invitations and Game Guide controls. A fixed layer order covers both tutorial and ordinary gameplay. | The completion pass below records the inventory, contacts, C.R.O.C., mentor, revival and map migration. |
| Settings | UI scale and tutorial hints are applied to the live views. The social blocked list uses the real unignore action. Redemption uses the server response and authoritative inventory. | Overworld hint production remains unresolved. |
| Fonts and nine-slice | The accepted RussianRail and Manrope TextCore assets preserve title and body roles. Local spacing and alignment were adjusted for journal level, selected settings tabs, roster captions, character creation and Game Guide. Mission track and journal state art use corrected slices. | Geometric validation checks declared borders and stretch centers. Verify remaining language, viewport and hover/pressed combinations visually before accepting pixel parity. |

The first continuation player passed offline captures at 1920 × 1080 and
1920 × 1200, each with 12 captures. Its resource-loading runs passed first
character creation, all ten tutorial cinematics, first overworld render and the
cold occupied roster against a disposable RustyFusion database. The separate
transport suite passed against disposable normal accounts. The first full live
Unity smoke reached group invitations and stopped at a stale selector in the
test. The test now waits for the visible NanoCom action using its semantic
class. These receipts are under `Logs/Studio/UIContinuation`.

Nameplates use a 30 m presentation radius derived from the existing speech
bubble distance. RussianRail and Manrope have similar capital heights at the
same nominal size and different ascent/descent. The affected controls use local
adjustments to their line geometry and padding.

The tutorial now renders the shared minimap and empty Nano belt while the
normal HUD is visible. The Nano slot still requires the local equip step. The
lesson controls when NanoCom can open, and zoom uses the same controls and range
as the overworld. The new-player loading path preloads the shared map artwork
and initial terrain tile. The recovery smoke checks the visible map and empty
slot during early combat.

The expanded Unity Editor run reported 115/115, including rendered Mail and
UI panel picking. Static validation parsed 32 UXML files and resolved 4,208
references. It checked 86 nonzero sprite borders and 52 authored slice rules
with explicit artwork, with no empty stretch centers. The GUISkin report
contains 33 unused legacy definitions with overflowing borders.

The Windows player with the early tutorial HUD passed all ten cinematics and
the explicit minimap/Nano-belt assertion at 1920 × 1080. The separate live
gameplay player recorded 165 checks against a copy of the successfully
exercised disposable RustyFusion fixture. These included the NanoCom invite,
Settings unignore, mail send/read/delete, code redemption and inventory award,
and transport. That longer run stopped at the existing ES446 particle check at
the Recall Point. See `tutorial-hud-resource/validation.json` and
`verified-fixture-live/validation.json` under `Logs/Studio/UIContinuation`.

The acceptance build imported the follow-up mail contrast and transport
caption spacing changes and rebuilt Windows and Addressables. Its standalone
UI smoke reached the journal after producing Guide, Nano belt, inventory and
mail captures. It stopped when the test addressed the Ben banner by its old
name. The test now addresses the banner by its semantic USS class and compiles.
The full standalone UI smoke has not yet passed after that change.

## Completion pass on 2026-11-14

Inventory and Nano slots, contacts and party rows, C.R.O.C. controls, mentor
cards, resurrection controls, and map markers and filters now use authored
UXML and USS. The inventory cabinet is shared with mail, trade and vendor
windows. The resurrection overlay shares the common prompt frame. Runtime
bindings use semantic classes. Animated portraits, projected map coordinates,
timers and authoritative action states remain runtime data.

The obsolete OLD CHAT row and its restart message were removed. Existing
profiles load their other preferences and drop the obsolete key when saved.

The transport harness passed 222 checks on a fresh disposable RustyFusion
database. The Recall smoke previously looked for mesh filters and mesh
renderers, although the accepted effect uses Unity's native particle system.
The updated smoke requires two active particle systems, supported materials,
and nonempty geometry baked by the actual renderer. The protocol and effect
playback code are untouched by that test correction.

The first Windows build in this pass completed the live Unity scenario with
240 checks and 38 captures. Its disposable RustyFusion shard confirmed real
social operations, mail, inventory, C.R.O.C. settlement, both Recall particle
variants, revival and the final world save. The shard and the profile used
fresh copies isolated from development data. The editor retest passed all
72 selected tests, including pointer picking across overlapping layers.

The final source audit parsed 41 UXML layouts and resolved 4,339 authored
asset references. All 86 nonzero sprite import borders and 70 explicit
sprite/USS combinations retained a positive stretch center. This result
covers border geometry. Render captures and the panel tests cover the
specific states exercised in those runs.

The final Windows player completed the standalone gameplay UI smoke with 29
captures. It verified the four mentor portraits, the authored Ben confirmation,
cancel and commit after a synthetic mentor acknowledgement, manual Phoenix and
automatic revival, C.R.O.C. recipe selection and confirmation, and control
picking under modal overlays. See `Logs/Studio/UICompletion/ui-player-final.log`
and `Builds/StudioUiCompletionFinal/Windows/GameplayUiValidation`.

The final Windows player also passed all 240 live checks with no content
errors. The first attempt lost the isolated shard's registration after a
heartbeat timeout. The successful retry used fresh ports and a fresh database
copy, with a 120-second heartbeat interval for that test process. The runner
stopped the shard after the player's acknowledged world save. The runtime
SHA-256 was
`c24e12a98649235900650613dbc5a02a3a256a8b86f951043079c02537145229`.
See `Logs/Studio/UICompletion/verified-fixture-live-final-retry/validation.json`.

The Editor rerun on the final source passed 76 of 76 selected tests after
including the gameplay HUD binding suite. See
`Logs/Studio/UICompletion/editor-final.xml`.

The final player's resource smoke passed character creation, ten tutorial
cinematics, the shared HUD in the early combat segment, and the first
overworld frame. The cold launch with the occupied roster also passed. There
were no content errors in either phase. The isolated shard used a fresh
database and the same 120-second test heartbeat interval. See
`Logs/Studio/UICompletion/resource-final-retry/validation.json`.

## Visual matrix on 2026-11-15

The matrix uses the Windows player and the production UI Toolkit views.
It sets the locale from a disposable profile and verifies the locale and
render target dimensions before collecting frames. Frontend and gameplay
fixtures exercise both locales at 1280 × 720, 1920 × 1080, 1920 × 1200
and 2560 × 1080. Live checks use isolated RustyFusion profiles for screens
that require authoritative NPC, account or world state.

The frontend changes preserve the loading bar's shaped end at its smallest
width. Settings dropdowns retain the arrow and panel corners as their rectangles
change size. The graphics captions clear the slider tracks and the round
close icon uses its full artwork. The mentor confirmation buttons have extra
space for Russian captions and the selected hologram draws behind card copy.
Gameplay changes address collision between the race timer and the tracked
objective, contrast in active controls, and overlapping inventory, journal,
map and C.R.O.C. copy.

The static scan resolved 4,345 authored references across 41 UXML layouts.
It checked 86 sprite import borders, 77 explicit texture and slice pairs, and
226 state definitions with nonzero borders from the generated shared stylesheet.
The scan found no missing references or empty stretch centers. The initial
Windows player with the matrix harness and pre-fix assets completed all 16
frontend/gameplay runs with 440 named frames. See
`Logs/Studio/UIVisualMatrix/baseline-validation.json`.

The 0104 mentor scenario enters through the authored Guide Changer. The
isolated L4 account changed from Dexter to Ben after one acknowledged request.
The selected guide and 2,000 Fusion Matter survived a fresh login. RustyFusion
currently reconstructs the guide count as 1 on login, even when the last
accepted count was 2. The L3 account received the real rejection and retained
its previous guide and balance. The English and Russian UI use level-specific
copy for that rejection and generic copy when the server supplies no
actionable cause.

The first complete post-fix package completed 16 of 16 standalone runs and
produced 464 named frames. The first 720p English launch reached its startup
deadline under concurrent validation and passed a retry with identical
package hashes. See
`Logs/Studio/UIVisualMatrix/final-standalone-combined.json`.
The selected Unity Editor suite passed all 69 tests, including resolved 9-slice,
slider clearance, and Settings popup picking. See
`Logs/Studio/UIVisualMatrix/editor-final.xml`.

The follow-up visual review corrected the clipped journal row summary,
English mentor confirmation copy, the Russian label next to the Phoenix
icon, mail input frames, and light-background captions in the race rankings.

`Builds/StudioUiVisualRelease/Windows/FFOne.exe` completed 16 standalone
runs with 464 named captures. An English 1920 × 1080 launch hit a Windows
`File.Replace` error when closing Settings. The same binary passed in a
fresh profile. The full receipt verifies identical player and catalog hashes.
The network supplement passed 243 assertions in English at 1280 × 720
and 243 in Russian at 1920 × 1200, with 38 capture pairs and independent
RustyFusion databases. Both shards were stopped after the acknowledged save.

See `Logs/Studio/UIVisualMatrix/release-standalone-combined.json`,
`release-review-all`, `release-late-review` and `live-release` under the same
directory. The release mentor check used the same runtime SHA-256
`a10046ccb7b8da6a34e56fb139ec775481e5ea48cde11263086c0ebe5426bd22`.
Its 18 checks and full-width confirmation frame are in
`Logs/Studio/UIMentorLive/en-1280x720-20261115-101736-38fc7/validation.json`
and `Logs/Studio/UIMentorLive/mentor-confirmation-spacing.png`.

After the Windows package was built, the enlarged Russian journal crop
revealed clipping at the edges of the inactive Active tab. The source
stylesheet expands the label while keeping its bounds separate from the
Completed label. The runtime-panel Editor test confirmed measured text fit,
separate bounds and pointer picking at 1264 × 681, 1280 × 720 and 1920 × 1080.
See `Logs/Studio/UIVisualMatrix/editor-journal-tab-retest.xml` and
`Logs/GameplayUiPreview/journal-ru-completed-tab.png`.

## Mentor persistence and Windows journal rebuild on 2026-11-15

RustyFusion now saves the active mentor and count together. The additive
migration keeps the previously displayed count of 1 for existing chosen
mentors and 0 for Computress. The old schema stored only the mentor identity,
so it provides no history from which to recover earlier counts. The 0104
world-entry and mentor-confirmation fields keep their existing representation.

SQLite passed 16 database tests, including a legacy schema migration,
save, reopen, and load-count assertion. The PostgreSQL library and binaries
compiled, and the migration passed SQL parser validation. The live checks
used SQLite.

The first isolated Unity run started from an unmodified legacy fixture
schema. The database was copied before startup and migrated in place.
It started with Dexter, count 1, and 2,000 Fusion Matter. An acknowledged
UI change and world save produced Ben, count 2, and the same balance in
SQLite. A new login and world entry returned Ben and count 2 to Unity.
See `Logs/Studio/MentorPersistence/ru-1280x720-20261115-164142-cae2a/validation.json`.

The rebuilt Windows client at `Builds/StudioMentorPersistence/Windows/FFOne.exe`
includes the expanded Active-tab label. Both Russian viewport runs passed,
with 36 captures each at 1280 × 720 and 1920 × 1200. The new client also
passed the isolated mentor migration and relog scenario with
`accepted=2, relog=2`. See `Logs/Studio/MentorPersistence/journal-player/validation.json`,
`journal-tabs-windows.png` and `ru-1280x720-20261115-165324-4aad6/validation.json`
under that evidence directory.
