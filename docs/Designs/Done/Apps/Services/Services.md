# Codex System Services

## TimeService

Wall-clock time combining three sources: CMOS RTC (boot baseline),
NTP (network sync via existing `Ntp.codex` module), and HPET tick
counter (monotonic interpolation). Exposes wall clock (real time)
and monotonic clock (never goes backward). TOTP support for the
secrets manager. Drift estimation between syncs.

Key: `time-now`, `time-now-millis`, `time-now-datetime`,
`time-monotonic`, `totp-current-counter`, `totp-seconds-remaining`.

## Revocation

Replaces the legacy web's CRL/OCSP with signed revocation records
in the trust lattice. Six revocation types: key compromise, trust
withdrawal, capability revocation, content retraction, team member
removal, and all-capabilities revocation.

Key properties:
- Records are signed by the revoking authority (Ed25519)
- Content-addressed (SHA-256 hash is the record ID)
- Verified on add -- unsigned/invalid records are rejected
- Key compromise records specify an effective-since date,
  allowing signatures made before compromise to remain valid
- Wire format for transmission over data channels or fact store

Checks: `is-identity-revoked`, `is-content-revoked`,
`is-capability-revoked`, `is-vault-access-revoked`,
`is-signature-trustworthy` (respects compromise window).
