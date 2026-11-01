# secrets -- backlog

| Source | Gap |
|---|---|
| `Vault.codex` `vault-set-password`, `VaultCrypto.codex` `generate-salt` | Every vault derives its master key under the same salt (`generate-salt 42`, a hash of the text "42"), so one dictionary precomputed against that salt tests the stored verifier (`vi-check`) of every vault at once. A per-vault salt needs an entropy source; the unlock path is pure. Source-inspected 2026-10-07. |

## Test coverage

1 of 6 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/secrets -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
