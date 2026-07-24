# CodexMagic mobile -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this application's own behaviour lives here instead, so the platform
register stays readable.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/codexmagic-mobile/Design.md`.

| # | Capability | State of the gap |
|---|---|---|
| MOB-1 | **Phase 2 economy pages** | Not built: no pack list, no mana coin purchase, no card browser. The files that once wore the planned names are now `CreationsPage.codex` / `GalleryPage.codex`; grep for those. Only `MobileApp.codex` defines `opening` today. |
| MOB-2 | **`MobileApp` compiles** | It carries a citation cycle: `MobileApp` cites its five page chapters and each cites `MobileApp` back, so the entry chapter enters the unit twice and raises CDX3001. **The cycle is LOAD-BEARING and must not simply be cut:** `navigate-to-home` calls `home-load` and `navigate-to-page` calls `creations-load` / `gallery-load`, which live in the pages, so breaking it moves when data loads and no runtime test exists to catch the difference. The documented fix is the `ErpServer` / `ErpServerMain` split -- move the entry point to a sibling chapter. |
| MOB-3 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Duplicated between this app's own chapters** (prefix one, or extract a shared chapter): `bn` (MobileTheme vs WebApp--WebTheme, 1u); `bsn` (MobileTheme vs WebApp--WebTheme, 1u); `ez` (MobileTheme vs WebApp--WebTheme, 1u); `logout-cb` (HomePage vs MobileApp--ProfilePage, 1u); `logout-cb` (ProfilePage vs MobileApp--HomePage, 1u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all. |
