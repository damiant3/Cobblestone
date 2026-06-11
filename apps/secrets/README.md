# Codex Secrets

A bare-metal encrypted password manager. Stores passwords, API keys, tokens, certificates, SSH keys, and other secrets in AES-GCM encrypted vaults unlocked by a PBKDF2-derived master key, with a hash-chained tamper-evident audit log.

## Modules

- **SecretsApp** — Main UI: lock screen, vault list, secret list with folder sidebar, detail/edit views, generator view, audit log view, clipboard auto-clear (30s)
- **Vault** — Vault model: CRUD for entries, folder management, search, filter by type/folder/favorites, statistics
- **VaultCrypto** — Full key hierarchy: PBKDF2 (100K iterations) -> master key, HKDF -> vault key, HKDF -> per-secret key, AES-GCM encrypt/decrypt; team vault sharing via Curve25519 DH
- **SecretEntry** — 10 secret types (Password, API Key, Token, Certificate, Note, SSH Key, Database, Wi-Fi, Credit Card, Identity), TOTP secret, attachment refs
- **SecretGenerator** — Configurable password generation, diceware-style passphrase generation, entropy estimation and strength assessment
- **AuditLog** — Append-only hash-chained log with 10 action types, SHA-256 chain with genesis anchor, verify-chain integrity check
- **SecretsPersist** — DiskFacts serialization (vault index as kind 23, audit count as kind 24)
- **opening** — Disk load, vault/audit restore, persistent event loop with change-detection before save

## Completeness

65% — Data model, crypto pipeline, rendering tree, event handling, and persistence are all fully implemented. Gaps: secret editing view renders as list fallback; settings view has no render function; vault creation UI absent (only hardcoded "My Vault"); encryption modeled but payloads stored cleartext in the add/retrieve path; TOTP code generation typed but not implemented; audit serialization stores only count, not actual entries.

## Codex Conformance

Full — Written entirely in Codex. All crypto primitives cited from Foreword. No foreign dependencies.
