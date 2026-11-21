---
name: rustyfusion-integration
description: Develop and verify FFOneUnity and RustyFusion together using the accepted 0104 wire, authority, Nano and persistence contracts.
---

# rustyfusion-integration

Input: use case, messages/state transitions, both source owners and isolated test profile. Read [contract](../../../Docs/Studio/client-server-contract.md) and sibling RustyFusion/AGENTS.md. Inspect active encoders/dispatchers, not generated historical mirrors. Preserve 2700/2688 layouts, transaction ownership and independent Nano acquisition/progression rules.

Assign gameplay and network owners for coupled changes. Reuse protocol golden vectors and pure Rust tests first; DB tests require disposable paths/ports. Never use default production DB for integration. Record baseline vs new failures, reconnect behavior and authoritative state. Missing dispatch or asymmetric validation is a known limitation, not permission to invent messages. Done requires both sides checked, rollback/migration implications and isolated live acceptance when behavior changes.

