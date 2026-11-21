# FFOneUnity / Codex Reconstruction Studio

FFOneUnity is the production Unity 6 client and `../RustyFusion` is its production
server. `../FFOneClient` is the archived Rust/Bevy client and an implementation
reference. Production builds must work independently of it. Do not start a replacement game or discard
working improvements merely because they differ from historical builds.

For reconstruction, development or mixed tasks, use `.agents/skills/reconstruction-studio`
and `Docs/Studio/README.md`. Infer RECONSTRUCT / DEVELOP / HYBRID from the task.
Delegate independent research or implementation/review to the minimum useful
native subagent team; roles are in `.codex/agents`. Small edits need no team.
Assign one integrator and one writer per scene, prefab, meta, catalog or wire
contract. The current host may require passing role instructions in the task
instead of selecting a named role; report that limitation rather than faking it.

Check Git state first and preserve unfinished user changes. Never reset/stash
their work, upgrade dependencies, mass-reimport assets or regenerate GUIDs as an
incidental step. Keep native scenes/prefabs/data editable; reconstruction tools
must not become runtime dependencies. Use an isolated server/database for tests.
Inspect both sides before changing the single FFOne/Retrobution 0104 contract.
Use the RustyFusion `tabledata` revision as the input for shared gameplay
tables. Check the Unity publication with `python Tools/Studio/check_tabledata.py`.

Use evidence with source/hash scope and distinguish present, wired, executed,
tested and accepted. Record unknowns/conflicts in `Docs/Studio`, preserving current
product behavior until resolved. Existing reports are historical evidence, not
a fresh pass. Apply `Docs/Studio/validation.md` proportionally to the change.

For the initial Studio setup, only infrastructure, instructions, documentation
and non-destructive tests are in scope. Gameplay changes, mass content import and
wire changes require a separate impact plan; future explicit user tasks define
their own authorized scope. Do not modify global user Codex instructions.

Unity migration of FFOneClient. The primary behavior and presentation reference is
`D:/CodexProject/FusionFallProject/builds/retrobution-20260821`.
Use `../FusionForge` for bounded inspection of that immutable build.

`D:/CodexProject/FusionFallProject/FFOneClient` is the existing Rust implementation
and the source of accepted project-specific appearance and identity decisions.
Its differences from Retrobution are intentional: selected older appearances and
separate character variants (including Omniverse Ben and related variants) survive
the engine migration. Do not replace them wholesale with the primary build table.
Consult `../FusionForge/docs/source-contract.md` and the accepted native identity
and retention recipes when selecting models, textures, icons or voices.

Verify asynchronous character loading in the actual client, including the first
rendered frame, occupied roster portraits, character creation and world entry.

World collision history and the native Unity implementation contract are recorded
in `Docs/native-collision-contract.md`. Authored scenes own their MeshCollider
geometry; preserve original surfaces and bindings rather than restoring separate
legacy collision assets as active scene physics.


## Unity UI code style

Use [the project UI code style](Docs/Studio/ui-code-style.md) for authored UI.
UXML and USS own static presentation and interaction states. Bind game data
and events from C#, use serialized Unity references for authored dependencies,
and avoid UXML element-name lookups in new or migrated bindings.

For UI fixes and reconstruction, follow the
[interactive window review](Docs/Studio/validation.md#interactive-ui-review).
Exercise every applicable action in the running client and show the resulting
screenshot sequence for user review, including drag-and-drop where supported
or evidenced by the reference.

## Project dashboard

Track every project task in FFOne Studio as described in `Docs/Studio/dashboard.md`.
Use the card ID supplied by the chat. For tasks started elsewhere, find the existing
card or create one, then mark it active. The integrator updates that card before
the final response with the outcome, actual checks, evidence and remaining work.
Use review, clarify or blocked when appropriate. Subagents report to the integrator
unless they own a separate card. Use bug_triage_analyst for batches of observations.
Use the dashboard CLI even when the local web server is stopped.

## Generated files

Use one ignored output directory per task for local builds, test profiles and
server fixtures. Reuse it for retries where the tool supports it. Before closing
the task, keep the final report, linked captures and any build needed to reproduce
the result. Remove disposable retries, copied dependencies and test databases
once their processes exit. Keep local build and test output out of Git.

Before workspace cleanup, check tracked paths, running processes and references
from Studio cards and reports. Preserve active saves, authored assets, donor
builds and unique evidence. Share a short record of removed directories.
