# ModBuilder compiler workspace

The player workspace offers Valheim C# source export for a user's own build
and loader workflow, and an embedded IL emitter for local mod DLL builds
without a player SDK. Generic C# lacks the Valheim entry and adapter
installation; that redundant pill is hidden in the ModBuilder profile.
Unrelated Prism binary-target help is removed from the player view.

Compiler and adapter source uses a file list beside escaped, syntax-coloured
code. Preview rendering is bounded by the page's character and line limits.
Copy full source and Save source ZIP preserve complete files. The layout
stacks on narrow screens.

`apps/modbuilder/website.md` owns the current page and publication contract.
`apps/modbuilder/native/README.md` owns helper setup, folder selection,
default-copy discovery and deployment. The app backlog owns remaining game
acceptance; this completed UI unit adds no gameplay acceptance claim.

Verification uses `test-managed-build.mjs`, `test-site.mjs --file` and
`test-deploy.mjs` under `apps/modbuilder/`. The focused checks cover source
selection and escaping, bounded previews, edited DLL identity, stale builds,
source/DLL explanations, file mode and deployment. Independent reader checks
validated the player-facing output and folder-flow explanations.
