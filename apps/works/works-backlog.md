# Works -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this application's own behaviour lives here instead, so the platform
register stays readable.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| WORKS-1 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Private copies of a platform name** (cite it instead of defining it): `byte-to-hex` (KeyManager vs Foreword--Sha256, 1u); `text-trim` (DevConsoleBoot vs Foreword--TextSearch, 1u); `text-trim` (CodeBrowser vs Foreword--TextSearch, 1u); `hex-byte` (HexFormat vs Encode--Hex, 1u); `gpt-is-esp` (GopFat16 vs Foreword--Gpt, 1u); `count-occurrences` (ConsoleEditor vs Foreword--TextSearch, 1u); `hex-nibble` (KeyManager vs Encode--Hex, 1u). **Duplicated between this app's own chapters** (prefix one, or extract a shared chapter): `text-trim-left` (DevConsoleBoot vs Works--CodeBrowser, 1u); `text-trim-left` (CodeBrowser vs Works--DevConsoleBoot, 1u); `rtc-read` (GopRtc vs Works--GopDesk, 1u); `rtc-read` (GopDesk vs Works--GopRtc, 1u); `role-label` (AgentRuntime vs Works--AgentCoordinator, 1u); `role-label` (AgentCoordinator vs Works--AgentRuntime, 1u); `http-html` (Http vs Market--MarketWeb, 1u); `text-trim-right` (CodeBrowser vs Works--DevConsoleBoot, 1u); `html-page` (Http vs Market--MarketHtml, 1u); `dispatch-sweep-menu` (DevConsoleBoot vs Works--DevConsole, 1u); `dispatch-sweep-menu` (DevConsole vs Works--DevConsoleBoot, 1u); `dispatch-debug-menu` (DevConsoleBoot vs Works--DevConsole, 1u); `dispatch-debug-menu` (DevConsole vs Works--DevConsoleBoot, 1u); `dispatch-compile-menu` (DevConsoleBoot vs Works--DevConsole, 1u); `dispatch-compile-menu` (DevConsole vs Works--DevConsoleBoot, 1u); `text-trim-right` (DevConsoleBoot vs Works--CodeBrowser, 1u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all.  |
