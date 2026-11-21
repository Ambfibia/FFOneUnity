---
name: asset-provenance
description: Compare FusionFall build donors and accepted NPC/Nano identities, update provenance and preservation without silently replacing variants.
---

# asset-provenance

Input: affected asset/system ID, namespace, current references and donor options. Read [registry rules](../../../Docs/Studio/registries.md), [source contract](../../../../FusionForge/docs/source-contract.md), and only the relevant accepted identity/retention recipe. Inspect both FFOneClient and Unity consumer; use FusionForge for bounded source evidence.

Record exact source version, IDs, hash scope, Unity/FFOneClient state, RustyFusion dependency, differences, preferred decision with reason, confidence and unknowns. IDs from Nano/NPC/tune/icon namespaces are distinct. Existing conflicts (including Nano52) block replacement, not unrelated work. Update provenance/preservation JSON and run python Tools/Studio/studio.py validate. Do not label a feature deprecated without an accepted decision. Completion requires traceable evidence and explicit unresolved coverage.

