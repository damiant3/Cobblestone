# Vision

Organizational intelligence platform that replaces status meetings with structured signal flow. Ideas enter as weighted Signals (Musing / Suggestion / Directive / Mandate), cascade down the org hierarchy tier-by-tier, and structured Responses flow back up -- all asynchronously, with full accountability visibility.

## Modules

- **VisionTypes** -- Core types: Signal, Hold, Response, Person, Domain, OpenSlot, AuditEntry, portfolio types, nudge types
- **VisionSignal** -- Signal lifecycle state machine (new, picked-up, pushed, responded, closed/expired); SignalStore with filter/sort/query
- **VisionCascade** -- Cascade engine: hold creation, push-down, response submission, overdue/approaching nudges, cascade health metrics, audit trail
- **VisionOrg** -- Org graph with reporting chains, domain ownership, keyword-based signal routing, skip-level prevention
- **VisionPortfolio** -- Projects, milestones, risks, dependencies; layered health scoring and downstream impact queries
- **VisionStore** -- Full app state, JSON serialization, DiskFacts persistence (kinds 42/43/44)
- **VisionConfig** -- Per-org configuration: response windows, nudge thresholds, notification rules, scoring weights
- **VisionTheme** -- Signal weight colors, state colors, role colors, risk heat colors, layout constants
- **SignalPage** -- Signal feed with weight-sorted cards; signal detail view with full cascade chain
- **PortfolioPage** -- Executive portfolio dashboard: project health scores, milestone progress, risk heat map
- **TimelinePage** -- Gantt-like timeline with milestone markers and dependency overlay
- **OrgPage** -- Org hierarchy list with domain ownership and keyword annotations
- **opening** -- Entry point: loads persisted signals, builds sample org, runs keyboard-driven event loop

## Completeness

65% -- All core data types match the design document. Signal state machine, weight semantics, response windows, cascade engine, org graph routing, and portfolio layer are complete. All four UI pages are implemented. Persistence is functional. Missing: VisionGraph (dependency graph, critical path analysis) and VisionServer (JSON HTTP API) are listed in the design but absent. Org data is hardcoded in opening. Skip-level prevention is implemented but not enforced. Volunteer board has types but no UI page.

## Codex Conformance

Full -- Written entirely in Codex. UI through widget foreword. Persistence through DiskFacts/AppPersist. When a browser client is needed, it will be emitted through plugs.
