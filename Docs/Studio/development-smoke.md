# Development Studio smoke: tutorial Timeline documentation review

Date: 2026-11-14. Mode: DEVELOP. Role: Technical Director, loaded from
`.codex/agents/technical_director.toml`. Workflow: `unity6-production`, with
`reconstruction-studio` routing. Integrator: parent Studio task. Sole writable
file in this review: this report. README and runtime remain unchanged.

This was a real delegated task using the host's native collaboration mechanism.
The host lacks a named-role selector, so the delegate read and applied the role
instructions directly. It does not demonstrate automatic named-role dispatch.
The same delegate participated in the earlier Unity audit; this is a fresh
source-verification pass, not a blinded independent evaluator. The evidence
below was reread directly rather than accepted from the previous report.

## Finding and minimal recommendation

README lines 37–39 describe `TutorialCinematics` handling the montage and say an
ordinary PlayableDirector cannot independently execute gameplay actions. The
second statement is defensible for an unbound director, but the paragraph
omits the current native graph ownership and can suggest a separate legacy
playback engine. Clarify both ownership and the requirement for project bindings.
No runtime or asset change is justified by this documentation finding.

Recommended minimal patch to `README.md`:

```diff
 `FFOne → Tutorial → Create Missing Editable Timelines` создаёт отсутствующие сцены
-и подключает их к контенту. Монтаж обрабатывает `TutorialCinematics`;
-обычный PlayableDirector не исполняет игровые действия самостоятельно.
+и подключает их к контенту. `TutorialCinematics` воспроизводит авторский Timeline
+через нативный `PlayableDirector`, настраивая привязки игровых дорожек и порядок
+команд. Для игровых действий нужны эти привязки; одного назначения Timeline
+произвольному директору недостаточно. Подробнее: [нативный Timeline](Docs/native-tutorial-timeline.md).
```

This patch is a recommendation, not an applied edit. It preserves the useful
warning against assigning the asset to an arbitrary director without runtime
bindings. The rest of the bounded README guidance inspected here is supported:
authored assets exist, import only creates missing scenes, clip order is retained,
and skip/loop definitions are separate scene data.

## Direct evidence

Paths below are relative to `D:/CodexProject/FusionFallProject/FFOneUnity`.

| Claim | Current source evidence |
| --- | --- |
| Authored Timeline is the native playable asset | `Assets/FFOne/Runtime/Gameplay/Tutorial/TutorialCinematics.cs:56`: creates private host and PlayableDirector, sets `definition.Timeline`, manual update mode and track bindings |
| Project owns ordered gameplay output and presentation gating | `TutorialCinematics.cs:68`: builds a script mixer, connects track playables, creates script output, starts director; `:90` gates graph evaluation |
| Clip behavior is native and state belongs to graph instances | `TutorialActionClip.cs:14`: `ScriptPlayable<TutorialActionBehaviour>.Create`; `TutorialActionTrack.cs:18`: configures behavior with runtime, clip and order |
| Graph callback drives gameplay evaluator | `TutorialScenePlayable.cs:12`: `ProcessFrame` calls `Runtime.EvaluateTimeline(playable.GetTime())`, routes failure to controller cleanup |
| An arbitrary unbound director is insufficient | `TutorialActionTrack.cs:21`: behavior configuration requires `TutorialTimelineBinding`; `TutorialTimelineBinding.cs:7` owns the runtime reference |
| Serialization remains ordinary authored data | `TutorialTimelineScene.cs:11`: ScriptableObject holds Timeline, Skip and Loops; `:21` guards `Definition()` exporter with `UNITY_EDITOR` |
| Ordering/skip remain explicit | `TutorialCinematics.cs:114`: start/order sorting and crossed command evaluation; `:145`: skip stops director and applies cleanup/loop/fade actions |
| Existing authored assets are not overwritten by missing-only publication | `Assets/FFOne/Editor/Importers/TutorialTimelineImport.cs:30`: load existing scene; create only if absent and throw on conflicting existing scene/timeline path |
| Focused documentation matches this architecture | `Docs/native-tutorial-timeline.md`: native graph time, state ownership, manual gate, script mixer, editor-only exporter |

The source expressions above establish current wiring, not a fresh runtime pass.
No unfamiliar API was introduced; live reflection or a new external API lookup
was unnecessary for this source-only review.

## Independent checks within this pass

1. Read the actual role, skill, AGENTS, orchestration, validation and focused
   contract. Skill-relative references resolved to the intended project docs.
2. Checked scoped Git state: README and TutorialCinematics were already modified;
   native Timeline source files and focused contract were untracked. Preserved
   all of these user changes and wrote only this new report.
3. Followed native call chain across controller, clip, track, binding and mixer.
   Confirmed runtime uses serialized action nodes and editor-only export is
   guarded by `UNITY_EDITOR`, rather than inferring ownership from class names.
4. Traced one real authored reference chain: `Worlds/Tutorial/Content/Content.asset`
   line 19 references BasicMove scene GUID `1006b700cdd3d7845b9e842d59801705`;
   `Cinematics/BasicMove.asset.meta` matches it. `BasicMove.asset:925` references
   Timeline GUID `cf03f52377506f04fa33d1348dcb99b0`, matching
   `BasicMove.playable.meta`. Skip and Loops reference local scene subassets.
   This is one sampled serialized chain, not a full asset-database validation.
5. Inspected `TutorialNativeTimelineTests.cs`: assertions cover graph ownership,
   clip evaluation, ordered long-frame events, muted tracks/groups, skip/restart
   and serialized node reading. Read historical `tests-duration-fix.xml`:
   Passed, 66/66. Tests were not rerun in this review.

Reference fingerprints captured in this pass (SHA-256):

| Input | Hash |
| --- | --- |
| `README.md` | `0aa358aad5541e9e5ee5f3f34d5340921289bc343c7aacb86760862a36ca9a03` |
| `Docs/native-tutorial-timeline.md` | `0d115cb9e10eb7d6f847968f01879a410c5294e0fe8616c80419c18c5cb4b6bd` |
| `TutorialCinematics.cs` | `d0bb75c69974a7c8b097c5d1ff82b01bd95111476dce19e94ff907f5ae2e35ab` |
| `TutorialActionClip.cs` | `4f42ed644016f9f6d123286becb590a601fc2b8e4962648427ea806aa4f0bf4c` |
| `TutorialActionTrack.cs` | `c911efb47f344b695dddebd986c0434c88444df07340499c66c7edec16feedf9` |
| `TutorialTimelineScene.cs` | `1679fc2baa3a81e15f94cc3cdebedbbc86270cd7b6ae9f98aa1b13b9edfcb3da` |
| `TutorialScenePlayable.cs` | `23e7572ad4da080372cfd492ef65c9b944a5a6b808c12a5fef31e9f4c90c568d` |
| `TutorialTimelineImport.cs` | `93078ec7da2b73322f7e6d347c1ad31722c05439aff3956a58fe8adbf013390e` |

## Skill evaluation and handoff

The role and skill were usable: they selected existing ownership, focused
contracts, minimal scope and preservation. Inputs and required output were
clear, links resolved, and no contradictory instruction required a runtime
change or broad rebuild. The role's read-only review rule and explicit sole
report-write assignment were compatible: production sources stayed read-only.
No skill correction is needed based on this exercise.

| Gate | Result |
| --- | --- |
| Role/skill instructions read and applied by real delegate | PASS, explicit host fallback |
| Current source and authored reference evidence | PASS, bounded sample |
| Actionable minimal document recommendation | PASS, not yet applied |
| New compile/EditMode/player/visual tests | NOT RUN, source/document review only |
| Automatic named-role selection | NOT VERIFIED, host selector unavailable |
| Blinded independent assessment | NOT CLAIMED, delegate had earlier context |

Production Integrator may apply the small README patch after checking its
current paragraph is unchanged and preserving all surrounding dirty edits.
Validate the relative link and review the resulting diff. No Unity build,
asset save or player run is needed for that wording-only change. If the scope
expands to graph behavior, require focused EditMode tests and actual-client
first-frame, ten-scene, skip/restart and occupied-roster acceptance separately.
