# Agent Reek Work Plan

**Date**: 2026-06-22
**Stream**: //Codex/MutableRecords
**Seed**: E625476A (CL 5704)
**Last CL**: 5724

---

## Priority 1: Fix Compiler Type Checker Bug — RESOLVED

Not a type checker bug. `build.ps1` was missing 5 Cvmm chapters
from its build list, causing `compile.ps1` to silently prepend
them (with their Data deps including RowSchema). The extra chapters
had genuine type errors. See `docs/Test/CVMM-TYPE-CHECKER-BUG.md`.

### Steps

1. **Add sort verification to env-finalize**
   - File: `codex/compiler/Types/TypeEnv.codex`
   - After `sort-by (env.bindings) compare-type-binding-names`,
     add a linear scan that verifies adjacent entries are in order:
     `text-compare (list-at sorted i).name (list-at sorted (i+1)).name <= 0`
   - If out of order, emit CDX diagnostic with the two names
   - This proves/disproves the sort hypothesis

2. **Investigate sort-by at 2929 elements**
   - File: `codex/foreword/core/Sort.codex`
   - The quicksort uses in-place `list-set-at` swaps
   - Test: write a standalone test that sorts 3000 text items and
     verifies the result is ordered
   - If sort is wrong, check `sort-partition` for off-by-one in
     the pivot placement

3. **Investigate bsearch-text-pos**
   - File: `codex/compiler/Core/Collections.codex`
   - The binary search uses `text-compare name mid-name <= 0`
   - Verify: does `text-compare` handle CCE hyphen (code 73)
     correctly in all comparison positions?
   - Test: binary search for names with hyphens in a sorted list
     of 3000 names

4. **Seed rebuild after fix**
   - Any change to TypeEnv.codex, Collections.codex, or Sort.codex
     is a codegen change requiring a two-pass seed rebuild
   - Follow the procedure in the Operator's Manual

5. **Rebuild CVMM app**
   - `apps/cvmm/build.ps1` should compile clean after the fix
   - Run in codex-vm with `-gop-width 1024` to verify

### Diagnostic Improvements Needed

These errors cascaded badly this session. Better compiler
diagnostics would have saved hours:

1. **CDX1021 "Expected 'then'"** — When the parser encounters `->` 
   inside what it thinks is an `if` expression, the diagnostic
   should also report WHERE the `if` started (the line that opened
   the `if` context), not just where the `->` was found.

2. **CDX3001 "Duplicate"** — Should report BOTH definition sites
   (the original and the duplicate), not just the duplicate.

3. **CDX2001 "Type mismatch"** — When the type name doesn't exist
   in the source (phantom like `RowSchema`), the diagnostic should
   note "type name not found in current compilation — possible
   type environment corruption" instead of reporting it as a
   normal mismatch.

4. **`fn` keyword** — The parser should recognize `fn` as a common
   mistake for `lambda` and emit: "CDX1050: Unknown keyword 'fn'.
   Did you mean 'lambda'?"

5. **Dangling field access** — When `.field-name` appears after a
   closing `)` without being inside parens, emit: "CDX1051:
   Ambiguous field access — did you mean to parenthesize the
   preceding expression?"

6. **Inline if in & chains** — When `if...then...else` appears
   inside a `&` concatenation and is followed by `&` on the next
   line, emit: "CDX1052: Inline if/then/else in text concatenation
   — use a let binding to avoid parse ambiguity."

---

## Priority 2: Run CVMM App at 1024 Width

After the type checker fix:

```powershell
Copy-Item seed/Codex.cdx build-output/bare-metal/Codex.cdx -Force
pwsh -NoProfile -File apps/cvmm/build.ps1
tools/codex-vm.exe -kernel apps/cvmm/build-output/cvmm-server.cdx ^
    -gop-width 1024 -gop-height 768 -mem 3072
```

### Features to verify

- Sidebar with 25+ nav entries (original 17 + 13 new settings)
- Settings views: Audio, Notifications, Screen Saver, Power,
  DateTime, Bluetooth, Privacy, Default Apps, Wallpaper, Keyboard
- Calendar: 7-column grid, today highlight
- Terminal: lolcat rainbow colors
- All new UI controls: Dropdown, TreeView, DataTable

### Features temporarily disabled

- Monitor view (parse errors in Monitor.codex pre-date this session)

---

## Priority 3: Copy-Up and Sync

After the app is running:

1. Copy-up all fixes to main
2. Merge-down from main (other agents' work)
3. Run test battery to verify no regressions

---

## Session Inventory (2026-06-22)

### New Foreword Modules (7)
codex/foreword/ui/: Dropdown, TreeView, DataTable, Markdown, Editor, Validation
codex/foreword/encode/: Jwt

### New OS Modules (20)
codex/os/dev/: Notification, AudioControl, MultiMonitor, ScreenSaver,
  Taskbar, AppLauncher, Wallpaper, WindowSnap, GlobalSearch,
  PowerManager, DateTimeSettings, Bluetooth, PrivacySettings,
  DefaultApps, BackupRestore, ScreenRecorder, NetworkDiag,
  VpnFirewall, PrintManager, GamepadManager, TouchpadSettings,
  KeyboardRgb, QmkProtocol
codex/os/kernel/: UsbHid
codex/os/net/: OAuthClient, OAuthProvider, ImapClient
codex/os/trust/: ExternalAuthBridge
codex/os/observe/: NotificationLog

### Modified Files
codex/foreword/game/Color.codex — lolcat palettes
codex/foreword/ui/Render.codex — shadows, gradients, accents, event-dot
codex/foreword/ui/Theme.codex — Shadow, Gradient, AccentBorder on WidgetStyle
codex/foreword/ui/Icon.codex — 38 new 8x8 icons (55 total)
apps/cvmm/Terminal.codex — lolcat rainbow
apps/cvmm/Calendar.codex — 7-column grid, week/day/agenda views
apps/cvmm/CvmmTheme.codex — enriched styles
apps/cvmm/CvmmShell.codex — settings view dispatch
apps/cvmm/CvmmState.codex — 12 new ViewIds
apps/cvmm/CvmmApp.codex — 13 new nav entries
apps/cvmm/CvmmRoutes.codex — monitor disabled, fleet JSON fix
apps/cvmm/CvmmDisplay.codex — event constructor renames (Disp* prefix)
apps/cvmm/ServiceManager.codex — fn→lambda fix, sm-list-has helper
+ 4 app themes updated for WidgetStyle fields

### Design Docs
docs/Designs/OS/Active/ExternalAuthBridge.md
docs/Designs/OS/Active/GuiOsRoadmap.md
docs/Designs/Hardware/Active/KeyboardRgb.md
docs/Test/CVMM-TYPE-CHECKER-BUG.md

### Seed
seed/Codex.cdx — rebuilt CL 5704 (SHA256 E625476A) for WidgetStyle expansion
