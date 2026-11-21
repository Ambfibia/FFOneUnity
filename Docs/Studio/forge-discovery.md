# FusionForge discovery — 2026-11-14

This is an initial bounded audit, not complete content parity certification. All paths below resolve from `D:/CodexProject/FusionFallProject` unless stated otherwise. No source build, production asset, or gameplay code was changed during inspection.

## Repository and tools

FusionForge HEAD is `6da75fe1b44fa2adefbd8ec0923df372bdfb21fd` (2026-10-08). At discovery it had eight tracked modifications and three untracked entries, including changes to `inspect.rs`, `managed.rs`, CLI routing and publication. Preserve those changes. The Studio setup subsequently changes only its project research skill.

The four workspace crates are `fusionforge` (CLI, Unity/container/managed inspection), `ffone-asset-pipeline` (conversion/publication), `ffone-content` (schemas), and `ffone-reference-audit`. Existing `vendor/ffbuildtool` and `vendor/FFSpy` submodules remain authoritative implementations. `FusionForge/docs/source-map.md` maps owners. Workspace declares edition 2024 / rust-version 1.98; the CLI package itself declares edition 2021 / rust-version 1.98. Do not infer a missing toolchain or upgrade it from these declarations.

`target/debug/fusionforge.exe --help` executed successfully. Inspected executable SHA-256 was `013358c4b5264c459ca897ace41f53faad64dc4ca53091df01843f35c1489208`. This identifies the inspected binary; it does not prove a later rebuild is identical.

| Category | Existing surface | Effect boundary |
| --- | --- | --- |
| Inspect / enumerate | `inspect`, `list-contents`, `list-assetbundle`, `show-caching-manifest` | Bounded stdout inspection |
| Compare | `compare-inventories`, `compare-unity-json` | Existing inputs; stdout or explicit report |
| Validate | `validate-bundle-refs`, `validate-object-sizes`, `reference-audit` | Consult exact operation before running |
| Evidence export | `dump-object-evidence`, `export-managed-assembly-evidence` | Explicit final evidence/payload destinations |
| Convert / publish | `convert-native-*`, `convert-unity-ui`, `native`, `publish-*` | Writes final target assets; requires scoped plan |
| Legacy mutation | `replace-*`, `patch-*`, `repair-native`, bundle merge | Never part of routine read-only research |

The table classifies existing operations; only the tests listed below were exercised. Do not rename commands solely to match these categories.

## Source inventory

Canonical registry: `FusionForge/tools/legacy-sources/sources.json`. Relative build paths resolve from FusionForge. All nine requested directories exist. Existing TSV inventory counts are historical records, not a fresh whole-build hash audit.

| Build | Registry ID | Source role | Historical TSV rows |
| --- | --- | --- | ---: |
| retrobution-20260821 | primary | Primary historical parity authority | 361 |
| retrobution-20260613 | previous | Accepted selected appearances / historical comparison | 378 |
| OG-academy | alternate | Explicit donors only | 456 |
| beta-20100104 | beta-20100104 | Early historical orientation | 393 |
| retro-010920 | retro-010920 | Historical; accepted Unstable-power donor exception | 361 |
| retro-110219 | retro-110219 | Adoption purpose unresolved | 361 |
| retro-112219 | retro-112219 | Adoption purpose unresolved | 361 |
| retro-121719 | retro-121719 | Adoption purpose unresolved | 361 |
| reborn-cache-candidate-20260913 | reborn-cache | Unverified donor candidate | 979 |

Reborn documentation describes 978 cache payloads plus a historical runner, not an attributed complete/current build. No verified `main.unity3d` is established. `patched` is a separate historical overlay entry, never clean original evidence. Studio must reference this registry rather than establish competing source aliases.

## Accepted decisions and unresolved conflict

Read `FusionForge/docs/source-contract.md`, `recipes/native/identities/accepted-native-identities.json`, `recipes/native/retention/previous-build-retention.json`, and the exact domain recipe before selecting donors. Preserve separate Omniverse NPC variants 3464–3468; primary NPC 3430–3450 maps to native/server 3469–3489. Previous-build appearances include selected Kevin/Dee Dee models and UI/tutorial artwork; Fusion Dee Dee follows primary. Numeric equality across NPC, Nano, mesh, icon and voice namespaces is not identity.

**Open conflict: Nano 52 and Unstable tuning.** FusionForge's accepted identity recipe says Nano 52 is retired/empty and Nano 41 uses tunes 288–290, skill 122, icon 67. `FFOneClient/docs/nano-roster.md` says IDs 52 and 66 are separate accepted variants sharing the Van Kleiss model, and Nano 41 uses tunes 285–287 / skills 210–212. It also records Coop 67, Ben 68, Ghostfreak 69 and Upgrade 70. Neither a recipe filename nor historical date alone resolves this conflict. Preserve current working tables and ownership, inspect the actual Unity/client/server tables and relevant acceptance history, and record an explicit cross-project decision before any publication or saved-state migration. Do not mark 52 deprecated or delete it based on FusionForge alone.

Academy incubator Way Big 71 / Ultimate Big Chill 72 and semantic Finn/Jake portraits have existing recipes. Validate their current production consumers separately. Fusion Kimchi's accepted atlas references beta-20101123, outside the nine requested directories; its availability/provenance must remain a separate question, never be replaced with beta-20100104 by name similarity. Preserve EN/RU routing and approved replacement fonts.

## Verified bounded specimen

From FFOneUnity:

```powershell
& ..\FusionForge\target\debug\fusionforge.exe inspect ..\builds\retrobution-20260821\FutureNano.resourceFile --type GameObject --limit 2
```

Exit 0; 171 matches; two returned objects; `truncated=true`. Container SHA-256 before and after was `ec9427c7037308a35f4d7fcb3162ef6be793a501019d8e7cf06afa8d4b3a25a1`. Serialized owner is `CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06`. GameObject 481 is `Bip01 R Forearm`, with resolved local component 362; 482 is `Bip01 R Foot`, with local component 450. Both native mappings remain unconfirmed. These objects are an inspection regression specimen, not approved Nano identity mappings.

`inspect <same-container> --limit 0` rejected the invalid bound with exit 1 and `limit must be 1..=1000`.

```powershell
& ..\FusionForge\target\debug\fusionforge.exe inspect ..\builds\retrobution-20260821\main.unity3d --assembly 'Assembly - CSharp.dll' --class cnQuit --method OnGUI --limit 1
```

This succeeded with container SHA-256 `01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef`, method `cnQuit::OnGUI()`, token `0x06000585`, 544 IL bytes. Raw IL hex is truncated at its 512-byte cap; decoded instructions include calls to `GoToCharacter`, `OnQuit`, and `Exit`. This does not prove runtime reachability or UI parity.

## REA, Ghidra and FFSpy

REA MCP was called successfully, not merely discovered. Live server is `rea-agents` 6.8.0; expected REA skill version 49; catalog reports 229 tools. `binary_session` reported no open target / cleanup pending. Ghidra 12.1.4 with JDK 21.0.9 is configured available at `D:/CodexProject/SuperKTK/KTK/artifacts/rea-install-20261012/tools/ghidra_12.1.4_PUBLIC`. Native analysis was not run; this is availability evidence only. The Windows native route has admission limits and is not the managed-assembly route.

Actual `inspect_managed_artifact` with `detail="summary"` succeeded on `FFOneUnity/Library/ScriptAssemblies/FFOne.Runtime.dll`: SHA-256 `26510a343cfebe7ddaa614227533a7bd7e4be6b4e65dde761c1e0f5e98ec38e7`, MVID `91213c40-242e-4839-95da-c756b6768da4`, complete metadata/no issues, Unity Mono CIL / AnyCPU. Parent Evidence: `ev_8493b1af1ff92e21bccee49655f48a6f9179144e12f10921dadd154991ebbd37`; summary: `ev_6c7c8ac7a887e45a3356715847530916ee09844897c8f462cb4804c4556c4839`. These IDs are session evidence references, not portable files; replay against the recorded hash. The assembly was not executed and this does not prove current source compilation.

Existing FFSpy CLI `FusionForge/vendor/FFSpy/ICSharpCode.Decompiler.Console/bin/Debug/net6.0/ilspycmd.exe --help` succeeded: ilspycmd 7.2.0.0 / decompiler 7.2.0.6843. Exact type selection, reference paths and `--preserve-iterator-state-machines` are advertised. Target decompilation was not tested. No installation or provider upgrade is needed for the tested surfaces.

## Debt and scoped next checks

- `fusionforge inspect --help` fails because `--help` is parsed as a filename. Use root `--help` and the owned parser until a focused fix is reviewed.
- Root help omits supported `--fields` and `--instruction-limit`; the relevant source is already dirty. Add help/parser consistency tests in a separate scoped change.
- `sources.json` retains historical `work/...` navigation paths; these are not mandatory current workflow.
- `repair-native` and older publication APIs retain staged behavior; `docs/native-cli.md` explicitly records this debt. Keep them outside ordinary inspection.
- Historical `unstable-power-icon` operation describes icon 68; source-contract says 67, and current client tables may differ further. Resolve the actual product contract before using a repair.
- Existing real-container console tests return early when fixtures are absent. Studio must preflight specimen existence and distinguish skipped from passed.

Focused robustness follow-up: assert malformed/truncated container rejection without output, negative/overflow object-ID rejection, unknown format/option rejection, ambiguity behavior, bounded instruction truncation metadata and source-hash stability. Reuse existing tests; never fuzz/mutate immutable originals. Use disposable copies or in-memory fixtures. Do not refactor dirty parser source during Studio setup.

Focused Cargo regression command and its actual output are recorded in `FFOneUnity/Logs/Studio/forge-regression.log`. The real fixture preflight succeeded; `cargo test --locked --offline -p fusionforge --test console focused_inspection_is_bounded_and_read_only` rebuilt FusionForge and passed: 1 passed, 0 failed, 6 filtered out, exit 0. Cargo reported a pre-existing future-incompatibility warning for `binrw v0.14.2`; no dependency was changed. This covers bounded inspection only, not complete FusionForge correctness or native parity.

The existing project research skill was updated with routing, the reproducible example,
production handoff, error handling and the Nano conflict. `git diff --check` passed for
that edit. The default Python initially lacked `yaml`. The integrator subsequently
ran the bundled skill-creator validator using an already installed PyYAML from the
local uv cache via process-local PYTHONPATH: all six Studio Skills and this Forge
skill passed. No package was installed or global environment changed.
