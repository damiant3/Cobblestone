# ACCP -- open capabilities

App-domain register. Cross-lane priorities belong in `docs/PM/CurrentPlan.md`.

| Source | Gap |
|---|---|
| `AccpJson.codex:217-229` | `aj-quote` handles quotation marks, backslashes and LF but passes NUL raw. MCP/protocol fields and `AccpIr.ai-json-list` use this helper. Add NUL and non-ASCII round-trip arms before changing the serializer. Source-inspected 2026-10-01 at main 33668; experiment `ex-quote` is separate and already handles NUL. |
| `docs/Reference/Nectry.md` section 5, item 1 | No information-flow property. Rows bound which effects and scopes a program uses, not which data reaches which output: a program granted `[Console, Network "api.example"]` can send everything it read to that host. Nectry's NectryCore and Chlipala's UrFlow (OSDI'10) decide this property; ACCP states none. |
| `docs/Reference/Nectry.md` section 5, item 2 | No human-approval grant kind. An action that requires a human, provably unbypassable, has no row label or lease form. Home: the `ProseGrant` and lease machinery in `codex/os/trust/`. |
| `docs/Reference/Nectry.md` section 5, item 3 | Policy is not a decidable sublanguage. `PolicyProse.codex` lowers grants into combinators, but nothing answers questions about a policy itself ("can any lease ever reach `/`?") as a decision. A deliberately non-Turing-complete policy language would. |
| `docs/Reference/Nectry.md` section 5, item 4 | The English-to-grant step is unchecked. `ProseGrant` text is the seam where an ambiguous request becomes the property everything downstream proves. Candidate: require grant text in Codex Prose Language (no implicit referent, quantity or order) and refuse the rest. |
