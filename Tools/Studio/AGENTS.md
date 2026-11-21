# Studio infrastructure

Keep these utilities dependency-light and independent of game runtime. Read-only
commands must not launch Unity/player/server, publish assets, mutate source builds,
or call chat/model creation endpoints. App-server discovery may read skills/config;
never persist credentials, complete user config or MCP command arguments.

Write test artifacts under ignored Logs/Studio or temporary directories. Treat
incomplete snapshots, missing fixtures, discovery errors and unsupported schemas
as failed/blocked checks rather than success. Use focused tests for behavior such
as rejecting a broken reference or detecting a protected file change. Do not test
Markdown wording. Maintain exact hash scope and distinguish metadata guards from
backups/full asset content verification.
