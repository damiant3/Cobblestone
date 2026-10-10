# secrets -- backlog

| Source | Gap |
|---|---|
| `Vault.codex` `vault-set-password`, `VaultCrypto.codex` `generate-salt` | Every vault derives its master key under the same salt (`generate-salt 42`, a hash of the text "42"), so one dictionary precomputed against that salt tests the stored verifier (`vi-check`) of every vault at once. A per-vault salt needs an entropy source; the unlock path is pure. Source-inspected 2026-10-07. |
