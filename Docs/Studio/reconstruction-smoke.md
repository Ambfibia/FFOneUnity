# Reconstruction workflow forward test — 2026-11-14

Result: **PASS for bounded research and specification handoff**. Runtime behavior,
current Unity mapping and visual parity are **not tested**. No content, runtime,
source build or configuration was changed by this forward test; this report is its
only authored file.

## Assignment and workflow exercised

Question: what does primary `FutureNano.resourceFile` GameObject pathId 481 in
`CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06` represent, and what cannot be
inferred about gameplay from this evidence?

The actual native subagent read `.codex/agents/legacy_unity_analyst.toml` and followed
its `developer_instructions`, plus the project AGENTS, Studio README,
`.agents/skills/legacy-unity-research/SKILL.md`, the linked existing FusionForge skill
and research workflow. The parent remains integrator. The tool interface does not
select a custom role by name; this exercises the documented explicit-role fallback,
not automatic named-role dispatch.

This is RECONSTRUCT mode with one bounded analyst. Current consumer is the existing
FFOneUnity character/content pipeline; an exact production asset/character mapping
was not supplied or established. The result is a specification, not an import.

## Evidence identity and replay

- Build/source: `retrobution-20260821`, alias `primary`, historical parity source.
- Input: `D:/CodexProject/FusionFallProject/builds/retrobution-20260821/FutureNano.resourceFile`.
- SHA-256 before and after both inspections:
  `ec9427c7037308a35f4d7fcb3162ef6be793a501019d8e7cf06afa8d4b3a25a1`.
- Producer: current rebuilt `FusionForge/target/debug/fusionforge.exe`;
  SHA-256 `013358c4b5264c459ca897ace41f53faad64dc4ca53091df01843f35c1489208`.
- Owner: `CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06`.
- Full selected object key: `(retrobution-20260821, FutureNano.resourceFile,
  CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06, GameObject, 481)`.

Run from FFOneUnity:

```powershell
$source = (Resolve-Path ..\builds\retrobution-20260821\FutureNano.resourceFile).Path
Get-FileHash -LiteralPath $source -Algorithm SHA256
& ..\FusionForge\target\debug\fusionforge.exe inspect $source --asset CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06 --type GameObject --path-id 481 --limit 1
& ..\FusionForge\target\debug\fusionforge.exe inspect $source --asset CustomAssetBundle-50513e574023b4e259c6fd245ae5bc06 --path-id 362 --limit 1
Get-FileHash -LiteralPath $source -Algorithm SHA256
```

Both inspections completed successfully and each returned exactly one match,
`ambiguousSelection=false`, `truncated=false`, `dependenciesTruncated=false`.
Scope is those selected objects and reported references, not all 171 GameObjects
or a complete dependency closure.

## Observations, inference and unknowns

| Claim | Evidence and confidence |
| --- | --- |
| Object 481 is named `Bip01 R Forearm` and typed GameObject | Direct serialized-object observation |
| Its component reference is local pathId 362 | `/m_Component/0/second`, `fileId=0`, `status=resolved-local`, same owner |
| Object 362 is a Transform linked back to GameObject 481 | Direct type and `/m_GameObject` reference observation |
| Transform 362 has parent 361 and child 363 | `/m_Father` and `/m_Children/0`, both local and resolved in this owner |
| This is consistent with a right-forearm rig hierarchy node | Inference from the name and Transform relationship; skinning role not independently verified |
| It belongs to a particular gameplay Nano ID or accepted variant | Unknown; both inspections return `mappingStatus=unconfirmed`, `nativeMapping=null` |

The container name does not prove a Nano identity, summon behavior, availability or
server ownership. A `Bip01` bone name does not establish animation binding, mesh
weights, full skeleton root, rest pose or current production equivalence. The local
references prove serialized structure; they do not show an instantiated/active
runtime object, rendering, collision, animation playback or reachability. No
managed behavior, progression/tuning table or RustyFusion message was examined.

The reported four external-file entries were not followed: all references needed
for this question were local. Their presence does not prove that those external
dependencies are available or loaded at runtime. No cross-build ID/name substitution
was made. The separate Nano 52/tuning contract conflict remains unresolved and is
not answered by this bone inspection.

## Minimal specification and acceptance

For future inspection/recovery of this exact object, preserve its source-scoped
identity, GameObject-to-Transform association and local hierarchy references.
Keep semantic rig interpretation labeled as inference until renderer/bone binding
evidence corroborates it. A source record must not assign a native Nano ID merely
from this pathId, container name or bone name. Do not embed source locators as a
runtime dependency.

| State | Result |
| --- | --- |
| Present in original serialized container | Confirmed for GameObject 481 / Transform 362 |
| Serialized structure wired | Confirmed only for the reported local references |
| Present/wired in FFOneUnity | Unknown; exact consumer mapping not established |
| Original/client execution | Not observed |
| Inspector and input preservation | Tested successfully; before/after SHA equal |
| Visual/gameplay parity | Not tested |

Minimum production change justified by this evidence: **none**. The requested
research deliverable is complete without asset conversion or runtime edits.

## Consumer handoff

Unity Content Engineer / Asset Recovery Engineer receives the object key and
relationships above. If a concrete production task needs this node, first trace
the source renderer/skin/animation owner and locate the existing Unity prefab,
model import or rig definition through accepted publication metadata. Compare
existing content before proposing any change. Gameplay/Network engineers should
only become involved after evidence links the asset to a specific gameplay
identity or contract.

Parity QA receives a scoped requirement to verify the selected appearance/rig in
the actual client if content is changed, including first rendered frame and the
relevant roster/creation/world-entry scenarios. No parity pass can be inferred
from this report. The parent integrator should review the specification and retain
unknowns rather than accept an automatic import.

## Skill usability assessment

The new skill routed this container task correctly to existing FusionForge and
provided usable bounded selection, identity, preservation and handoff guidance.
Both relative skill links were resolved and exist:

- `../../../../FusionForge/.agents/skills/reverse-engineer-unity-client/SKILL.md`
- `../../../Docs/Studio/research-workflow.md`

No missing reference blocked this task. The documented `inspect --help` limitation
was avoidable using the tested invocation. No REA/Ghidra/FFSpy call was needed:
adding decompilation to a purely serialized-ownership question would not improve
the evidence. This test establishes usable instructions and actual native
delegation with explicit role loading; it does not prove unattended persistence,
automatic role selection or the entire reconstruction toolchain.
