---
name: unity6-production
description: Develop or review gameplay, UI and editable content in existing FFOneUnity while preserving assets and server contracts.
---

# unity6-production

Input: requested behavior, affected files/asset owners, current baseline. Read [Unity discovery](../../../Docs/Studio/unity-discovery.md), relevant focused Docs contract and [validation](../../../Docs/Studio/validation.md). Use source tools; query live Unity MCP only when a matching Editor exists. Verify unfamiliar Unity APIs against live reflection or official versioned docs.

Keep scene/prefab/ScriptableObject assets editable, GUIDs stable, authored collider geometry intact and no reconstruction runtime dependency. Allocate one writer per scene/prefab/meta/Addressables catalog. Plan behavioral or server changes with impact before implementation. Avoid broad rebuild/reimport as a check. Review affected first-frame/loading/UI paths in the real client when changed. On missing runtime retain the work and mark execution blocked; source review/compilation do not replace visual acceptance. Output minimal diff, focused checks, remaining risks and preservation evidence.

