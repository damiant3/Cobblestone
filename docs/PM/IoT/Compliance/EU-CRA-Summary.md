# EU Cyber Resilience Act (Regulation 2024/2847)

**Source**: EUR-Lex, EU Digital Strategy official page
**Status**: Law (entered into force 10 December 2024)

## Critical Dates

| Date | Obligation |
|---|---|
| 10 December 2024 | Entry into force |
| 11 September 2026 | Vulnerability reporting obligations begin |
| 11 December 2027 | Full obligations apply |

## Scope

Applies to all "products with digital elements" placed on the EU
market -- hardware and software with connectable digital components.
Covers baby monitors, smart watches, apps, firmware, operating
systems. Entire supply chain is in scope.

## Exclusions

Medical devices (2017/745, 2017/746), vehicles (2019/2144), aviation
(2018/1139), national security/defense products.

## Product Categories

- **Standard products**: Self-assessment (default)
- **Important Class I**: Smart home, baby monitors, some network
  devices -- third-party assessment available
- **Important Class II**: Firewalls, IDS, higher-risk products --
  third-party assessment required
- **Critical products**: Subject to mandatory European cybersecurity
  certification

## Manufacturer Obligations

- Cybersecurity risk assessment before market placement
- Security requirements across full lifecycle (planning, design,
  development, production, maintenance)
- CE marking required for compliant products
- Support period minimum 5 years (unless shorter product lifetime)
- Security updates without delay
- Automatic update mechanisms for consumer products
- Coordinated vulnerability disclosure policy

## Vulnerability Reporting (Article 14)

- **24 hours**: Early warning of actively exploited vulnerability
- **72 hours**: Full vulnerability notification
- Reporting to ENISA (EU Agency for Cybersecurity)

## Penalties (Article 64)

Non-compliance penalties up to EUR 15 million or 2.5% of worldwide
annual turnover (whichever is higher).

## Codex Compliance Mapping

| CRA Requirement | Codex Feature |
|---|---|
| Security by design | Linear types (no use-after-free), effect types (no unauthorized I/O) |
| Integrity verification | Signed CDX binaries (Ed25519 + SHA-256 content hash) |
| Capability scoping | CDX capabilities manifest -- binary declares what it can access |
| Vulnerability tracking | Fact store (content-addressed, immutable audit trail) |
| Secure updates | CDX verifier rejects unsigned or capability-escalating updates |
| No default passwords | Capability lease model -- identity IS the Ed25519 key |
| Lifecycle management | Compiler-generated compliance evidence as build artifact |

## Full Legal Text

Regulation (EU) 2024/2847, Official Journal L, published 20.11.2024.
EUR-Lex: https://eur-lex.europa.eu/eli/reg/2024/2847
