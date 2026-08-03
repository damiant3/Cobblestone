# Codex Services

A library of shared system services (no entry point of its own) providing wall-clock time management, trust-lattice revocation, and managed/parental accounts, consumed by the shell and all other apps.

## Modules

- **TimeService** -- Tri-source clock: CMOS RTC baseline + NTP sync + HPET tick interpolation. Wall clock + monotonic clock. Drift estimation. TOTP counter/remaining support.
- **Revocation** -- Replaces CRL/OCSP. Six revocation types: key compromise (with effective-since window), trust withdrawal, capability revocation, content retraction, team-member removal, all-capabilities. Ed25519-signed records content-addressed by SHA-256. Wire serialization.
- **ManagedAccounts** -- Guardian/managed account hierarchy. Three age-group presets (Child/Teen/Restricted). Per-account policy: max trust tier, allowed apps, blocked publishers, content filter, time limits. Enforcement functions for time, content, and app access.
- **ParentalUI** -- Full widget-tree UI: account selector, PIN entry, guardian panel, managed launcher, create account form, policy editor, time-remaining progress bar
- **ServicesPersist** -- DiskFacts serialization: revocation store as kind 26, accounts as kind 27

## Completeness

55% -- TimeService and Revocation are implementation-complete as libraries. ManagedAccounts model and ParentalUI rendering are complete. Key gaps: account persistence save/load not yet implemented (kind 27 declared but unused); no `opening` entry point (by design -- consumed as a library); NTP sync modeled but no network event loop connected; ParentalUI hardcodes current-hour; activity log view has no render implementation.

## Codex Conformance

Full -- Written entirely in Codex. No entry point by design; consumed as a library. All cryptographic dependencies cited from Foreword.
