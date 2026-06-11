# Codex Secrets Manager

Encrypted vault for passwords, API keys, tokens, certificates, and
secure notes. Personal and team vaults with AES-GCM authenticated
encryption, PBKDF2 key derivation, hash-chained audit log, and
trust lattice integration.

## Architecture

```
  Master Password
       │
       ▼ PBKDF2 (100K iterations)
  Master Key (32 bytes)
       │
       ▼ HKDF ("vault-encryption:" + vault-id)
  Vault Key (32 bytes)
       │
       ▼ HKDF ("secret:" + secret-id)
  Per-Secret Key (32 bytes)
       │
       ▼ AES-GCM (key, nonce, plaintext)
  Ciphertext + Tag
```

Team sharing uses Curve25519 (DiffieHellman) key exchange. Each
member's vault key is encrypted to their public key via the DH
shared secret, verified through the trust lattice.

## App Structure (7 modules, ~2100 lines)

| Module | Lines | Purpose |
|--------|-------|---------|
| SecretsApp.codex | ~500 | Main app: lock screen, vault UI, event loop, rendering |
| Vault.codex | ~250 | Vault model: CRUD, search, folders, favorites, filtering |
| VaultCrypto.codex | ~200 | Key hierarchy, AES-GCM encrypt/decrypt, team key sharing |
| SecretEntry.codex | ~200 | Entry types (10), payload, fields, search, sorting |
| SecretGenerator.codex | ~180 | Password/passphrase generation, strength assessment |
| AuditLog.codex | ~150 | Hash-chained append-only log, tamper detection |
| opening.codex | ~15 | Entry point |

## Security Model

- **At rest**: Every secret payload encrypted with AES-GCM using a
  per-secret key derived from the master key via HKDF. Nonces are
  derived deterministically from seed + secret ID (no random
  required at encryption time).

- **In transit**: Secrets never leave the device unencrypted. Team
  sharing encrypts the vault key to each member's public key.

- **Audit**: Every access (read, create, update, delete, share,
  login, logout) is logged in a hash-chained append-only log.
  Chain verification detects tampering.

- **Clipboard**: Copied passwords auto-clear after 30 seconds.

- **Key derivation**: PBKDF2 with 100,000 iterations makes
  brute-force expensive. Salt is 32 bytes.

## Secret Types

Password, API Key, Token, Certificate, Secure Note, SSH Key,
Database, Wi-Fi, Credit Card, Identity.

## Views

Lock Screen, Vault List, Secret List (with folder sidebar),
Secret Detail, Secret Edit, Secret New, Password Generator,
Audit Log, Settings.
