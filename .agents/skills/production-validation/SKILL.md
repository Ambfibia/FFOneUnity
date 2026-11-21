---
name: production-validation
description: Audit feature preservation and validate FFOneUnity changes with scoped smoke, unit, integration and visual evidence.
---

# production-validation

Input: diff/specification, baseline, preservation IDs, exact player/server artifacts. Read [validation](../../../Docs/Studio/validation.md) and [registries](../../../Docs/Studio/registries.md). Snapshot before changes with python Tools/Studio/studio.py snapshot --output Logs/Studio/<unique-baseline>.json. After infra-only work use compare; for gameplay inspect the expected changed files separately.

Select existing tests matching the risk. Distinguish present, wired, executed, tested, accepted; each assertion gets an evidence path, time and artifact/hash scope. Resource runner writes accounts: first verify isolated database/server pairing and explicit player path. Do not run it against a presumed disposable default. Validate new infra with python Tools/Studio/studio.py validate. For parity compare source evidence and actual product behavior; first frame, occupied portraits, creation and world entry are mandatory when character loading changes. Record fail/blocked/not-run honestly; do not promote historical receipts to a fresh full-suite pass.

