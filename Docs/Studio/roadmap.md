# Production roadmap

Order follows dependency/risk rather than speculative delivery dates. Each task
needs one owner, bounded diff and focused acceptance; do not implement the whole
game as part of Studio installation.

| Priority / stream | Next bounded result | Gate |
| --- | --- | --- |
| P0 / production baseline | Identify exact current Unity player + isolated RustyFusion profile; compile and run frontend → creation → first frames → tutorial → overworld → occupied roster → reconnect | Record artifact hashes, isolated DB/ports, focused EditMode tests, runner receipt and visual review; no default stale build |
| P0 / accepted content | Resolve Nano52 documentation/recipe/current table conflict and Kimchi atlas source attribution | Preserve saves and current IDs until supported decision; no automatic remap |
| P1 / FFOneClient migration | Expand preservation rows per accepted NPC/Nano, UI localization/font and gameplay extension | Compare Unity with retained reference behavior and verify the active consumer |
| P1 / FusionForge | Fix `inspect --help` without breaking callers; document fields/instruction limits; make fixture absence an explicit test outcome | Dirty-diff review, CLI compatibility tests, immutable real-container regression, malformed/truncated/unsupported input no-write tests |
| P1 / Unity architecture | Investigate one measured loading stall/resource lifetime issue from baseline | First-frame/roster/world regressions and performance comparison on exact build |
| P1 / RustyFusion | Audit one current missing handler or recovery flow against both clients and accepted extensions | Golden wire/state tests, isolated persistence/reconnect, no protocol variant |
| P2 / missing mechanics | Resolve documented grenade/BulletTable 170–174 or one Nano/buff consumer gap | Bounded primary evidence → specification → minimal implementation → parity plus preservation |
| P2 / remaining content | Inventory one Academy quest/NPC/Nano dependency closure | Separate donor content from Academy progression; explicit decision before integration |
| P2 / tools and authoring | Improve one Editor asset workflow or typed gameplay definition owner | Normal editable assets, stable references, no runtime generator/cache dependency |
| P3 / future candidates | Research one Reborn or historical feature | Provenance/attribution first; no assumed complete main bundle or automatic adoption |
| P3 / release readiness | Repeatable build, platform performance, persistence/migration, long-session and recovery tests | Evidence across full product matrix, not a single smoke launch |

## First production task brief

**Outcome:** establish a fresh, reproducible character-entry baseline of the current
working product with no intended gameplay changes. Integrator: production_integrator.
Implementation owners: build_qa_engineer and network_rustyfusion_engineer; independent
visual acceptance by parity_qa/UI owner. Begin by identifying an Editor 6000.4.0f1
session and documenting exact build command side effects. Preserve dirty assets and
save/restore scene context. Prepare isolated DB and unused ports, then run existing
protocol tests and `run_resource_loading_validation.py` with explicit player path.

Acceptance: first visible character frame, occupied portraits, creation, ten tutorial
scenes, world admission, return-to-roster/reconnect; exact artifact hashes; current
errors separated from previous ones; no identity/collider/GUID regressions. Record
unsupported runner assertions as separate manual/integration checks, not inferred
passes. Only after the baseline select one demonstrated stall or behavior failure
for a separately scoped fix.
