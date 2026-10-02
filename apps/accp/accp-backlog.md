# ACCP -- open capabilities

App-domain register. Cross-lane priorities belong in `docs/PM/CurrentPlan.md`.

| Source | Gap |
|---|---|
| `AccpJson.codex:217-229` | `aj-quote` handles quotation marks, backslashes and LF but passes NUL raw. MCP/protocol fields and `AccpIr.ai-json-list` use this helper. Add NUL and non-ASCII round-trip arms before changing the serializer. Source-inspected 2026-10-01 at main 33668; experiment `ex-quote` is separate and already handles NUL. |
