---
name: legacy-unity-research
description: Research historical FusionFall behavior and resources with existing FusionForge, REA and FFSpy; produce a bounded reconstruction specification.
---

# legacy-unity-research

Input: question, build/version, current consumer and expected result. Use the existing [Unity research skill](../../../../FusionForge/.agents/skills/reverse-engineer-unity-client/SKILL.md) and [research workflow](../../../Docs/Studio/research-workflow.md). Containers use FusionForge; standalone assemblies use REA; source-only development does not need decompilation.

Start with current product and accepted source/retention contracts. Inspect one relevant owner with explicit limits; capture hash and CAB/pathId or assembly/MVID/token. Report unsupported formats, truncation and conflicts without guessing. Never write into builds or require extraction caches. Prepare facts, unknowns, behavior states and minimum change; hand off to gameplay/content and independent parity QA. Done means a reproducible evidence-backed specification, not a claim of runtime parity from static code.

