# secrets -- backlog

## Team sharing does not derive a shared key

`VaultCrypto.codex:dh-shared-secret` hashes `private & public` with SHA-256.
`create-team-share` and `decrypt-team-share` therefore derive different keys
for the two participants. Reproduced on 2026-09-17 with two valid X25519
keypairs: the primitive returned matching 32-byte secrets, the vault helper
disagreed, and the recipient could not decrypt the share.

Evidence: red's `build-output/crypto-audit-20260917/vault-sharing-final/`
and `vault-sharing-probe.codex`; output is `x25519-control=True`,
`vault-agreement=False`, `vault-decrypt=no`. The run is an observed failure,
not an exact-output PASS. The app-level sharing workflow remains outside the
completed primitive repair set.

The repair must define the sharing key type, use actual key agreement,
propagate low-order/invalid-key refusal and supply a fresh AEAD nonce.
The current share nonce is derived solely from the recipient public key.
Acceptance: independent participant derivation and authenticated decryption,
wrong-recipient refusal, and distinct nonces for repeated shares under one key.
