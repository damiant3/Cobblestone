# Chat -- open capabilities

App-domain register. Cross-lane priorities belong in `docs/PM/CurrentPlan.md`.

| Source | Gap |
|---|---|
| `ChatServer.codex:115` | `json-error` accepts arbitrary Text but quotes the message raw. This is a latent helper-contract gap: the current caller at line 25 supplies a fixed error, and name responses at 50/80 use the fixed sample name from line 12. Add escaping before accepting dynamic text; no request-driven malformed response is claimed. Source-inspected 2026-10-01 at main 33668. |
