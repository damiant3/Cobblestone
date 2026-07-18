# CodexMagic Mobile

A .NET MAUI companion app for the CodexMagic collectible card game, targeting Android and iOS. Provides on-the-go account management, card collection browsing, store purchases, clan interaction, and community gallery access as a thin client over the Explorer/Magic server API.

## Modules

- **MobileApp** — Entry point, session resume, page routing (6 pages), bottom nav bar, event dispatch
- **LoginPage** — Login and registration forms with two-mode toggle, posts to `/api/auth/*`, stores token in local storage
- **HomePage** — Dashboard: player identity, creations count, quick-action buttons, logout
- **CreationsPage** — Lists user's saved creations with delete and refresh
- **GalleryPage** — Public community gallery with type filter and Remix action
- **ClanPage** — Placeholder page: static "Coming Soon" feature list
- **ProfilePage** — Profile display, export-my-creations action, app info, sign-out
- **MobileTheme** — Dark-gold mobile theme with larger touch targets, complete StateStyles, runtime stubs
- **build.ps1** — Bundles chapters and feeds to the MAUI plug pipeline

## Completeness

55% — Phase 1 (auth + navigation skeleton) is complete and functional. Phase 2 (economy) partially done — store and collection pages repurposed from the explorer portal, not the pack-store and token-collection pages described in Design.md. Phase 3 (social/clan) is a stub. Phase 4 (polish) is absent. MAUI plug pipeline referenced but not present in this tree.

## Codex Conformance

Partial — All application logic is written in Codex. Build pipeline intended to compile through the MAUI plug, which is the correct "emit through plugs" pattern. Runtime stubs in MobileTheme are placeholders the MAUI plug is expected to replace with real C# implementations. The plug itself is not confirmed to exist in this workspace.
