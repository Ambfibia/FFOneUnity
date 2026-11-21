# Evidence and preservation registers

The initial register is deliberately bounded. It is not a claim that every quest,
NPC, Nano, object, texture or mechanic has been inventoried. Expand it per production
task using existing FusionForge inventories and accepted recipes; do not duplicate
whole-build extraction. Missing rows mean unassessed, not absent.

`sources.json` is a coverage snapshot referencing the canonical registry
`../FusionForge/tools/legacy-sources/sources.json`. Source IDs match that registry.
Inventory hashes identify inventory files, not an independently rehashed whole
build. Do not use historical navigationProject/navigationWorkDir as required caches.

`provenance.json` records systems and selected objects: ID/namespace, sources,
exact identity where known, hashes with their scope, Unity and FFOneClient states,
RustyFusion dependency, differences, preferred decision and reason, status and
confidence. Exact object coverage is supplied by referenced recipes; unknown
hashes/identities remain null or explicit unknowns. Never invent them.

`preservation.json` records achievement obligations, categories and acceptance
gates. PRESERVE is a product requirement; it is not proof the Unity migration is
already complete. RECONSTRUCT/IMPROVE require a specific delta. CANDIDATE is not
permission to integrate. DEPRECATED requires a cited accepted decision; no new
feature is marked deprecated in this initial register.

Evidence stages are independent: present → wired → executed → tested → accepted.
A class/asset proves only presence; an executed path is not automatically correct.
Confidence values: observed (direct source/tool reading), derived (reasoned from
facts), historical (prior receipt), unknown. Retain date/artifact scope on results.

Conflicts cannot be closed by a majority vote among documents. Current code can
show effective behavior, but product identity changes require accepted intent.
Nano52 is unresolved between FFOneClient documentation and Forge recipes. Preserve
current saves/assets and inspect actual tables plus decision history before any
replacement. Kimchi's beta-20101123 atlas reference must not be relabeled as the
available beta-20100104 build.

Update JSON alongside the focused task and run `python Tools/Studio/studio.py
validate`. The checker validates identities/references and local evidence paths;
it does not confer parity or resolve decisions. Hash changed evidence specimens
before/after inspection when relevant. Research provenance stays outside shipped
runtime asset metadata unless a serialization identity is required by the game.
