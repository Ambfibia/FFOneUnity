# Verification — 2026-11-14

The safe first infrastructure iteration is implemented and exercised. This is not
full Unity/client production acceptance. All statements below identify the tested
surface and limits; missing checks are not silently converted into passes.

| Check | Result | Evidence / scope |
| --- | --- | --- |
| Native subagent delegation | PASS | Three independent discovery tasks, assigned doc writers, explicit-role reconstruction and development tasks, separate infrastructure reviewer; native collaboration spawn/message/followup/results used |
| Project Codex configuration | PASS | Desktop 0.167.0-alpha.4.1 `app-server config/read`; project layer active, agents enabled, max3 children; `Logs/Studio/codex-discovery.json` |
| Skill discovery | PASS | Native `skills/list` forceReload returned six enabled `repo` Skills and no errors; no model/chat was started by probe |
| Skill frontmatter | PASS | Bundled skill-creator `quick_validate.py`: six new Skills plus existing adapted Forge skill; existing cached PyYAML used, no installation |
| Custom role definitions | STATIC PASS / DISPATCH LIMIT | All13 TOML files parse with required fields; standalone schema follows current official docs. This host's collaboration API lacks a named-role selector. Two representative roles were explicitly read/applied by real subagents. Automatic selection of all13 by name is NOT verified |
| Studio config/registers | PASS | `python Tools/Studio/studio.py validate`: 13 roles,6 skills,9 sources; role/skill/source references, local evidence existence, statuses/confidence and category checks |
| Infrastructure regression tests | PASS | `python -m unittest discover -s Tools/Studio -p test_studio.py -v`:12/12; rejection of invalid refs/integrator/category/frontmatter, output preservation, source guard, discovery path containment, cleanup/privacy; `Logs/Studio/studio-tests.log` |
| RustyFusion protocol tests | PASS | `cargo test --locked --offline --lib net::`:6 passed,0 failed,228 filtered; `Logs/Studio/server-net-baseline.log`; no production DB/server |
| FusionForge real-container regression | PASS | `cargo test --locked --offline -p fusionforge --test console focused_inspection_is_bounded_and_read_only`:1 passed,0 failed,6 filtered; fixture existence preflight confirmed; `Logs/Studio/forge-regression.log` |
| Reconstruction process | PASS, static only | [Specimen handoff](reconstruction-smoke.md): primary FutureNano pathId481 → Transform362, input SHA stable; exact identity, limits/unknowns and consumer handoff; no invented Nano behavior |
| Development/review process | PASS, static only | [Timeline review](development-smoke.md): current graph/controller/track/clip chain, one authored GUID chain and minimal README clarification recommendation; no runtime edits |
| REA managed operation | PASS | Existing FFOne.Runtime.dll metadata inspected by actual MCP; complete metadata, hash/MVID recorded in [Forge audit](forge-discovery.md); not compilation or execution |
| FFSpy | HELP PASS | Existing CLI/version/options responded; target C# decompilation not exercised |
| Ghidra | AVAILABLE, NOT EXERCISED | REA reports configured12.1.4/JDK21; no native binary analysis in this iteration |
| Unity MCP | SERVER PASS / EDITOR BLOCKED | Resource discovery and `mcpforunity://instances` succeeded, `instance_count=0`; no live Editor operation or fresh Unity compilation claimed |
| Existing Unity baseline | HISTORICAL VERIFIED | Nov8 player receipt hashes match files;64/64,24/24 and66/66 separately scoped historical tests; see [Unity audit](unity-discovery.md) |
| Production file protection | PASS WITH SCOPE |557,354 FFOneUnity files in Assets/Packages/ProjectSettings: metadata compared; C#/JSON/asmdef/shader/HLSL and settings content hashed; no changed files, `Logs/Studio/protection-check.json` |
| Sibling changes | SCOPED STATUS REVIEW | Added/changed Git status entries relative to snapshot are within assigned instructions/skill paths. Not a full content hash of sibling dirty trees |

## Corrections found by real testing

Independent review initially produced8 passes,3 failures and1 error. Fixed:
substring path matching admitting similarly named sibling repos; expensive scan
before checking an existing snapshot output; unvalidated integration owner; and
unstructured failure on malformed skill frontmatter. All12 regression tests then
passed. Snapshot traversal was also changed to cached scandir metadata, streamed
hashing, progress and a300-second deadline; incomplete scans cannot publish a pass.
The original full snapshot took several minutes; the optimized full comparison
completed in about63 seconds. Both cover the same557,354 production files.

The reconstruction and development workflow evaluators had participated in the
initial audit; their second passes re-read actual inputs but were not blinded.
The infrastructure reviewer was a separate agent from the implementation author.
No result is described as independent behavioral parity solely on this basis.
The same independent reviewer reran all 12 utility tests and registry validation
after the fixes, reconciled pass counts against the logs, and found no remaining
material issue within that scope. Scoped Git whitespace checks passed in all four
affected repositories; live Markdown links resolve (the example README diff uses
paths relative to its intended destination).

## Boundaries and outstanding work

- No Unity Editor was connected. Fresh C# compilation, EditMode/PlayMode suites,
  asset-database loading and actual player/server/visual acceptance remain for
  the first production task. Historical builds do not establish current source
  readiness.
- `run_resource_loading_validation.py --help` succeeded and its implementation
  was audited; a fresh run was not performed. It creates accounts through the
  server and must use an isolated matching database/port and explicit player.
- Full build commands can change scenes, catalog groups and saved assets; they
  were deliberately excluded from this infrastructure iteration.
- Nano52/tuning conflict remains open. Do not retire/merge it automatically.
- Forge `inspect --help` and stale help fields remain debt; silent fixture skips
  require preflight. `binrw0.14.2` future-compat warning remains. Rust tests report
  existing `unused_braces` at item_move_tests.rs:114. No dependencies were updated.
- Registers are21 initial provenance rows and20 preservation obligations, not a
  full per-object/per-quest census. Missing rows mean unassessed.
- Agents are on-demand native tasks. This setup does not install an unattended
  service, promise work while Codex is closed, or force all13 roles on every task.

## Reproduction

From the FFOneUnity root:

```powershell
python Tools/Studio/studio.py validate
python -m unittest discover -s Tools/Studio -p test_studio.py -v
python Tools/Studio/probe_codex.py --codex 'C:\Users\ambfi\AppData\Local\OpenAI\Codex\bin\9e1da79431da526b\codex.exe' --output Logs/Studio/codex-discovery.json
python Tools/Studio/studio.py compare Logs/Studio/baseline.json
```

Resolve the current desktop executable after an app update; do not hardcode this
versioned path in game tooling. PATH `codex` is0.92.0 and was not used as the schema
authority. Local logs/snapshots are ignored and can be large (the initial snapshot
is about121MB); they are optional evidence, never runtime inputs.
