# ETSI EN 303 645 -- Cyber Security for Consumer IoT

**Source**: ETSI, confirmed by Intertek, Bureau Veritas, TUV SUD
**Status**: Active standard; foundation for UK PSTI Act 2022

## Structure

- 13 cybersecurity provisions + 1 data protection provision
- 33 mandatory requirements + 35 recommendations
- Applicable to consumer IoT devices

## The 13 Cybersecurity Provisions

1. **No universal default passwords** (Provision 5.1)
   - 5.1-1: Unique per-device passwords (MANDATORY)
   - 5.1-2: No published default passwords
2. **Implement a means to manage reports of vulnerabilities**
3. **Keep software updated**
4. **Securely store sensitive security parameters**
5. **Communicate securely**
6. **Minimize exposed attack surfaces**
7. **Ensure software integrity**
8. **Ensure personal data is secure**
9. **Make systems resilient to outages**
10. **Examine system telemetry data**
11. **Make it easy for users to delete user data**
12. **Make installation and maintenance easy**
13. **Validate input data**

## UK PSTI Act Relationship

The UK Product Security and Telecommunications Infrastructure Act
2022 mandates the top 3 provisions from EN 303 645:
- Provision 5.1: No default passwords
- Provision 5.2: Vulnerability disclosure
- Provision 5.3: Minimum security update period

## EU CRA Relationship

EN 18031 series is the formal harmonised standard for CRA compliance.
However, ENISA confirms EN 303 645 has strong alignment with CRA
Annex I Part I and serves as a practical compliance stepping stone.

## Codex Compliance Mapping

| EN 303 645 Provision | Codex Feature |
|---|---|
| 5.1 No default passwords | Capability lease model -- identity is Ed25519 keypair |
| 5.3 Secure communication | TLS with effect-typed channels |
| 5.5 Ensure software integrity | CDX signed binaries, content-addressed fact store |
| 5.6 Minimize attack surface | No-OS bare-metal compilation |
| 5.7 Software integrity | CDX verifier checks signature before loading |
| 5.13 Validate input data | Bounded integers with compile-time range checking |
