# Studio validation contract

Use the existing client and server procedures. Do not equate the presence of a
class or asset with working gameplay. Report distinct states: present, wired,
executed, tested, and matched to an accepted specification. A failure or unknown
is an output, never a reason to manufacture a pass.

## Definition of Done

A task closes only when its applicable checks have evidence, scope and results:

1. Record repository revisions, initial dirty paths and ownership of edited
   files. Preserve unrelated user changes and accepted product variants.
2. State the target behavior, provenance, known unknowns and acceptance criteria.
   Historical identifiers required by serialization/network compatibility stay
   exact; production code uses clear domain names.
3. Compile affected C#/Rust code using the installed pinned toolchain. Separate
   pre-existing errors/warnings from regressions. Infrastructure-only documents
   do not require rebuilding the whole game; record that scope explicitly.
4. Run meaningful affected unit and contract tests. Record commands, exit codes,
   report locations and any skipped tests. A previous report is historical
   evidence, not a fresh test run.
5. For assets, verify references, GUIDs, saved/reopened serialization, ownership,
   load/unload and manual editability. Persistent content must survive without
   REA, FusionForge or runtime reconstruction scripts.
6. For network changes, examine both client and RustyFusion, packet structure,
   state transitions, server authority, rejection and recovery paths; run an
   isolated integration scenario. No unilateral incompatible protocol changes.
7. For character/content presentation, run the actual player and inspect the
   first rendered frame, occupied roster, character creation and world entry.
   Native screenshot review is separate from checking a capture filename.
8. For reconstruction, compare with authenticated source evidence and the
   accepted product specification. Record intentional divergence and unresolved
   parity; preserve accepted enhancements.
9. Confirm no new critical errors, unexpected exceptions or content failures.
   Record known diagnostics with their existing baseline rather than hiding
   them. Retain reproducible reports tied to binary/catalog/source fingerprints.
10. One Production Integrator reviews the final diff, checks preservation and
    records residual risk, rollback and remaining work. Remove only task-owned
    temporary processes; do not terminate an unknown user's editor/server.
11. For UI fixes and reconstruction, complete the
    [interactive UI review](#interactive-ui-review) for each affected window
    and share the screenshots with the user.

## Interactive UI review

For each affected window, list every applicable action and distinct visible
outcome using the current implementation and the accepted reference for
reconstructed behavior. Run the affected build and perform the interactions
through its normal controls with usable test data. Use an isolated server and
disposable database for actions that write to the server.

- Open, close and reopen the window. Exercise every available control and
  supported input, including tabs, menus, selection, scrolling, tooltips and
  keyboard actions where applicable. Populate the window and use its actual
  add, edit and remove actions where available. Include empty and populated
  states, confirmation and cancellation, and reachable disabled and error states.
- If the window supports drag-and-drop or the reference shows it, exercise
  each applicable source and destination. Capture the initial state,
  the drag over the target and the result after release. Check valid drops,
  invalid targets and cancellation where applicable.
- Capture the initial window and the result of each action. For a multistep
  action, include the intermediate frames that show what changes. For parity
  review, pair client and reference screenshots for the same steps when the
  reference can be exercised.
- Record links or paths to the screenshots in the Studio card and show the
  sequence in the review response as images or links. Label each step with the
  starting state, action and visible result so the user can identify exactly
  where behavior differs.
  Record the build tested and any unavailable steps or reference gaps on the
  card. Use `review` when user feedback is pending and record unverified
  scenarios as such.

## Evidence types

| Check | What it proves | What it does not prove |
| --- | --- | --- |
| Static audit | Files, references and code paths exist | Execution or behavior |
| Compilation/build | Selected code/assets can produce a build | Gameplay or visual parity |
| Unit/EditMode | Specific invariants under test conditions | Complete live networking or first-frame presentation |
| Smoke | A bounded boot/load/interaction route completes | Exhaustive mechanics, performance or parity |
| Integration | Actual client/server transaction and shared state | All content combinations or historical accuracy |
| Visual review | Observed frames match specified presentation | Hidden state, packet or timing correctness |
| Parity test | A specified claim matches authenticated reference | Unmeasured original behavior or whole-game completion |

Existing tests are under `Assets/FFOne/Tests/Editor`. Relevant examples include
`NativeAuthoredCollisionTests`, `TutorialNativeTimelineTests`,
`NativeDataAsyncTests`, `CharacterLifecycleTests`, `NetworkProtocolTests`,
`NetworkWorldContractTests` and gameplay protocol suites. Some Editor tests open
scenes and create temporary objects; inspect side effects and save user work
before selecting a suite. Never run the whole suite in an unknown live session.

## Non-destructive infrastructure checks

Read AGENTS inheritance and Git status. Parse configuration/skills and check
paths. Invoke a delegated bounded research or review task and verify its actual
result. Query tool availability without launching or mutating Unity. Recompute
retained receipt hashes where appropriate. Inspect existing validation scripts
before running them. Do not call build/import/rebuild menu items as a health check.

The audited baseline and limitations are in [unity-discovery.md](unity-discovery.md).
The preserved latest successful resource receipt is
`Logs/NativeTutorialOverlay/FinalClient/validation.json`; its four retained
binary/catalog hashes matched at the 2026-11-14 audit. This does not substitute
for running the current source tree.

## Actual-client resource loading runner

Working directory:
`D:/CodexProject/FusionFallProject/FFOneUnity`.

`Tools/run_resource_loading_validation.py` was inspected. It launches the real
player twice, first creation/world then cold occupied roster. It checks exit
codes, content errors, blocking appearance loads, requested cinematic/input
markers, and DLL/settings/catalog hashes before/after. It creates a disposable
`Recovery...` account through the server. SQLite inspection is read-only, but
the server transactions are writes. It also writes logs, captures, receipt and
isolated development profiles. It does not itself start or isolate the server.

Before execution:

- Confirm a task-owned RustyFusion instance and disposable database; verify
  the selected login port really belongs to that instance and that its config
  references the exact database supplied to the runner.
- Do not use an existing player account or production database. A matching
  account prefix alone does not establish isolation.
- Select an explicit player path and fresh output directory. Do not overwrite
  existing evidence. Record the server revision/config and database identity.
- Retain initial build hashes, source state, test profile and complete logs.
- Allow up to 600 seconds per player phase. `--native-screen` deliberately opens
  a player window for framebuffer captures. `--audio` enables the real device.
- `--input-actions` requires both `--overworld` and `--native-screen`.

Safe syntax inspection, with no player/database writes:

```powershell
python Tools/run_resource_loading_validation.py --help
```

Execution template after the prerequisites above are established (replace
bracketed values with the verified isolated instance; do not run them literally):

```powershell
python Tools/run_resource_loading_validation.py `
  --player Builds/NativeTutorialOverlay/Windows/FFOne.exe `
  --output Logs/ReconstructionStudio/<unique-run> `
  --database <absolute-path-to-isolated-database.db> `
  --port <verified-isolated-login-port> `
  --tutorial-cinematics --overworld --native-screen --input-actions
```

The historical player path above is explicit because `Builds/Windows/FFOne.exe`
was older than the November baseline. For a new implementation, build and use
the new explicitly identified player; do not pass a stale binary as current.
The runner defaults to port `24300` and database
`D:/CodexProject/FusionFallProject/RustyFusion/target/ffone-unity/database.db`.
Defaults are not proof of isolation. The runner reads the database and connects
to the socket independently, so a mismatched pair must be rejected in preflight.

Review `validation.json`, both `player.log` files and named screenshots.
Creation/world and cold occupied roster must both pass. Optional fields being
false/empty do not prove a failure if the associated option was not requested.
Frame stalls and synchronous waits require separate performance assessment.
Preserve the generated account in the disposable DB as evidence or remove the
entire task-owned fixture according to its lifecycle; never clean shared data
by account-name pattern.

## Build commands are mutations

`FFOne/Validation/Build Optimized Content And Client` calls
`ResourceLoadingValidation.Build` and `NativeUiValidationBuild.BuildContent`.
It rebuilds content layout/catalog, switches scenes, builds bundles and player,
and writes status to `Logs/ResourceLoading/build.txt`.

Even `NativeUiValidationBuild.BuildWithCurrentContentAt` temporarily changes an
Addressables setting, performs a player build and calls `AssetDatabase.SaveAssets`.
The outer wrapper rejects a dirty active scene, but this is not protection for
all unsaved editor state or all dirty assets. Do not call these for read-only
discovery or as a connector health probe. Plan target output and preserve the
editor session before an authorized build. Never mass reimport to solve an
unexplained compiler or connector issue.

## Follow-up production acceptance

First establish a current isolated frontend/creation/tutorial/world/roster
baseline with one binary/catalog/server combination and inspect first frames.
Then choose one measured resource-loading stall for a bounded change. Re-run
affected unit tests and the actual-client path; compare timings, content errors
and appearance without weakening existing acceptance thresholds. Preserve the
native collision contract and accepted character variants throughout.
