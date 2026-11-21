# Reconstruction research workflow

Use this workflow for a specific legacy behavior, asset or source comparison. FFOneUnity is the production Unity 6 client; RustyFusion is the production server. FFOneClient retains accepted product decisions and useful implementations. Historical primary evidence never automatically overrides current accepted improvements.

## Input and ownership

Start with one question, the current consumer, source candidates and expected output. Assign one integrator. Independent researchers can investigate separate questions; only one owner edits each scene, prefab, `.meta`, Addressables catalog or network contract. Identify user changes before edits. A research result may be a verified specification or an explicit unknown; an import is a separate scoped action.

Resolve build aliases through `../../../FusionForge/tools/legacy-sources/sources.json` relative to this document. Read the source contract and exact retention/identity recipe only when choosing a donor. Compare them with current client/server acceptance documents and actual tables. Conflicting claims stay open; for example Nano 52/tuning currently conflicts between FusionForge recipes and FFOneClient's Nano roster. Do not silently choose either source.

## Choose the existing tool

| Evidence needed | Tool / route |
| --- | --- |
| Unity 2.5 container, serialized owner, object or embedded assembly | FusionForge `inspect` with exact filters and a bound |
| Existing source code or ordinary development review | Repository tools; no REA readiness ritual |
| Standalone PE/CLI identity, metadata, CIL | REA `inspect_managed_artifact`, then focused managed queries |
| Focused C# recovery | Existing FFSpy CLI, exact type and dependencies; decompiled code is evidence |
| Native binary behavior/algorithm | REA native route / Ghidra only when the question needs it |
| Current Unity content or API state | Unity Editor / Unity MCP with the correct selected instance |
| Timing, rendering, interaction, parity | Controlled actual-client observation at matched conditions |

Use the installed `reverse-engineer-anything` skill for shipped binary research and FusionForge's project `reverse-engineer-unity-client` skill for legacy Unity work. Never install or upgrade tools merely because a named provider is unavailable. Record the observed blocker and continue supported independent work.

## Bounded read-only example

From FFOneUnity, this verified command inspects a real immutable container:

```powershell
& ..\FusionForge\target\debug\fusionforge.exe inspect ..\builds\retrobution-20260821\FutureNano.resourceFile --type GameObject --limit 2
```

Expected scope: two objects out of 171 matches, `truncated=true`; mappings remain unconfirmed. See [discovery](forge-discovery.md) for the exact container hash and object keys. Do not turn a bounded result into a whole-build claim. Root `fusionforge --help` works; `inspect --help` currently does not.

For an already researched managed example:

```powershell
& ..\FusionForge\target\debug\fusionforge.exe inspect ..\builds\retrobution-20260821\main.unity3d --assembly 'Assembly - CSharp.dll' --class cnQuit --method OnGUI --limit 1
```

Record separate raw IL and decoded-instruction truncation limits. Calls in static IL are not proof of runtime execution. Preserve exact historical tokens only together with their assembly/container identity.

## Evidence and handoff

For each claim record build/source role; relative container and SHA-256; serialized asset, object type/pathId/owner or exact assembly/member token; producer version/hash; replay command; observation versus inference; coverage/limits; current Unity and FFOneClient consumer; RustyFusion dependency; conflict and unknown state. The object identity key is `(build, relative container, serialized asset, type, pathId)`. For managed records bind tokens to assembly hash/MVID when available.

Read the owner's external-file table for PPtrs. `pathId=0` is null, `fileId=0` is local; names and matching low ID bits do not resolve external ownership. Academy packed references require the full 48-bit object ID. Treat unresolved or ambiguous targets as errors, never nearest-name fallback.

The development handoff includes observable requirements, accepted product differences, current implementation gap, unknowns, affected assets/contracts, minimal proposed change, and tests needed. Do not automatically translate decompiled names or infer missing mechanics. A provenance row may say present or statically wired; executed, tested and parity-confirmed require their respective evidence.

## Production result and verification

Publish approved work into normal editable Unity scenes, prefabs, native imported assets, serialized data and C# code. Keep source locators/evidence in Editor or documentation records, not runtime requirements. Preserve GUIDs, manual edits, accepted identities, EN/RU routing and authored MeshCollider surfaces. Reconstruction tooling must not be required to regenerate core gameplay at every launch.

Validate the affected consumer and both client/server sides when relevant. For character-loading changes, observe the first rendered frame, occupied roster portraits, creation and world entry. For static-only research, explicitly leave runtime/parity unchecked. Resource loading validators and builds may change Editor/runtime state: inspect their actual behavior before running and coordinate ownership of the active Editor.

For the focused Forge regression, first verify the real specimen exists, then from FusionForge run:

```powershell
cargo test --locked --offline -p fusionforge --test console focused_inspection_is_bounded_and_read_only
```

Missing input is blocked/skipped, not passed. This test uses a disposable working directory and asserts bounded JSON/no inspection output files. It does not establish all converters, gameplay or visual parity. Additional tests should follow the affected owner and failure mode.

Completion means the specific question has evidence or explicit unresolved gaps, source bytes are preserved, the handoff is usable, and any claimed execution/validation was actually observed. Reports are requested final artifacts, not mandatory extraction caches. Do not introduce whole-build indexing, staged JSON assembly or an alternate implementation of REA/FusionForge.
