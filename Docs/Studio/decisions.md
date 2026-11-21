# Studio decisions — 2026-11-14

1. **Production owner:** FFOneUnity remains the Unity 6000.4.0f1 client and
   RustyFusion remains the server. FFOneClient is retained both as accepted product
   reference and as an actual shared Rust contract dependency.
2. **Project scope:** Keep roles, Skills, orchestration, checks and registers in
   FFOneUnity; adapt only relevant sibling instructions/skill. Avoid a new repo,
   daemon, Unity project, global instructions rewrite or competing content registry.
3. **Codex schema:** Installed desktop binary is 0.167.0-alpha.4.1; PATH CLI 0.92.0
   is not the compatibility target. Use standalone `.codex/agents/*.toml` with
   name/description/developer_instructions. Inherit model and permissions. Project
   Skills use `.agents/skills`. Read/config discovery was exercised by app-server.
4. **Working tree:** Thousands of existing Unity modifications and dirty sibling
   source files preclude checkout/stash or broad content operations. Keep current
   branch and create isolated infrastructure files; capture original heads/status
   plus production-file protection snapshot in ignored Logs/Studio. No automatic
   commits or mass migration. New worktree would omit current accepted dirty state.
5. **Evidence:** Reuse canonical Forge source registry and accepted recipes.
   Historical receipts remain dated; metadata/source presence does not prove a
   fresh compile or runtime parity. Native runtime assets never depend on Studio.
6. **Tool improvements:** First deliver regression coverage and exact command
   contracts for existing Forge. Its dirty inspect/managed implementation is not
   rewritten during setup. `inspect --help`, stale help fields and silently skipped
   fixtures are explicit next-step debt, not reported as fixed.
7. **Verification boundary:** No Editor is connected to Unity MCP at discovery.
   Existing full build commands alter scenes/catalogs. Record prior build/test
   hashes, run pure Rust/Forge tests and independent code review now; defer fresh
   Unity compilation/player acceptance to a dedicated isolated baseline task.
8. **Identity conflict:** Neither stale docs nor the latest build can silently
   retire/merge Nano52. Preserve runtime and saves; investigate decision history
   with both current tables before deciding.
