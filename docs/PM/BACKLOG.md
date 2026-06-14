# Backlog — Outstanding Work

**Updated**: 2026-06-13

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs physical USB stick test on Asus + Dell. **New since CL 3742: the 3GB seed needs 3GB of contiguous RAM below the PCI MMIO hole — confirm on both machines.** |

### Memory

| # | Item | Notes |
|---|------|-------|
| 3 | **Non-contiguous physical memory (the real 8GB+)** | `bare-metal-ram-size` is now 3GB (CLs 3736/3742), the hard maximum for the contiguous design: the top 1GB of 32-bit space is PCI MMIO, and RAM above 4GB relocates to addresses the kernel never maps. Going past 3GB needs: boot path reads the firmware memory map instead of a baked constant; page tables skip the MMIO hole and map above 4GB; allocator and stack-top handle a non-contiguous arena. This is the item that kills the memory ceiling for good. Design home: `docs/Designs/Memory/Active/`. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 2 | **Phase discipline — remaining items** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. Per-phase build/measure/compact and the RESOLVE/LIFT split are done. Open: deck-record toggle ratchet, escape-invariant enforcement, TCO-reset removal, per-phase survey tightening (lex 40x done CL 2306). |
| 3 | ~~**Keyword as pattern variable**~~ | **DONE** CL 4018: `is-keyword` check in `parse-pattern` emits CDX1060 at parse time. |
| 4 | ~~**Act-block statement merge**~~ | **NOT REPRO** 2026-06-13: `is-app-start` excludes `IfKeyword` and the app loop does not skip newlines at paren-depth 0, so the described scenario cannot occur with the current parser. Likely fixed by intervening parser changes since CL 3849. |

### Apps — Never-Compiled Code Inventory (2026-06-11 sweep)

Found while root-causing the crypto vector failures: large bodies of app
code that have never compiled, written against APIs or syntax that do
not exist. They pass no gate because nothing collects them. Each class
needs either a compiler feature, a mechanical rewrite, or deletion.

| # | Item | Notes |
|---|------|-------|
| 1 | **Hex sites need rewriting to `#` notation** | Language decision made 2026-06-12: `#RRGGBB`-style hash literals (CL 3837), not C's `0x`. The 178 app sites (browser 47, diagram 44, vision 32, collab 23, globe 15, fileshare 11, secrets 5, explorer 1) still spell `0x...` and need a mechanical `0x` -> `#` pass once CL 3837 lands. |
| 2 | **`is Nothing ->` patterns** | DONE CL 3838: 205 pattern sites + 68 expression-position sites rewritten to `None` across 45 app files. |
| 3 | **Bare `list-map` callers** | Not in the foreword; per-file sweep overcounts because cross-chapter defs resolve via cites — needs a compile-based count, not grep. |
| 4 | **Nonexistent text API calls** | `text-slice`, `text-insert-at`, `text-remove-at` used in cvmm and others (cvmm fixed in CL 3832). Rewrite as `text-take`/`text-drop` composites or add the helpers to StringUtils. |
| 5 | **test/lib triage queue — round 2: six real library bugs** | Round 1 (CL 3869) fixed the 6 compile fails (let-in-act statement disease + a wrong-quire cite) and hkdf's stale expected (prk-len=8 predates CL 3622). Six remain `.skip`'d, each a REAL library bug found by diffing captured actuals: **decimal** (multiply gives 0.01 for 5.25*7.74, parse ignores the point); **toml + yaml** (parsers return defaults — title=?, port=0); **numeric** (bisect/newton/trap all orders-of-magnitude off — scale confusion); **path** (path-join emits double slash, basename returns full path); **number-theory** (GPF at primes — runtime crash, needs the debugger). |

### Encoding

| # | Item | Notes |
|---|------|-------|
| 1 | **CCE multilingual coverage (tiers beyond 1)** | Tier 1 is 128 codes with 16 accented + 15 Cyrillic slots — a simplifying assumption, not a writing system. Getting the rest of the world's languages in means the compiler accepting CCE tiers greater than 1 (wider code space), not remapping tier-1 slots. Touches lexer, Text representation, the I/O boundary converters, and `cce-to-unicode-table` (foreword/core/CCE.codex). |

### Tooling — Host Stability

| # | Item | Notes |
|---|------|-------|
| 1 | **codex-vm whp_lock fail-open** | `tools/codex-vm.c:37` — `WaitForSingleObject(whp_mutex, 30000)` ignores the result; on timeout the process proceeds into the WHP calls concurrently, the exact vid.sys kernel-heap-corruption scenario the mutex guards (file comment, line 31). Implicated in the 2026-06-11 double CLOCK_WATCHDOG_TIMEOUT host crashes. Fix: INFINITE wait, treat WAIT_ABANDONED as acquired, die on WAIT_FAILED/NULL mutex. Prompt handed to an agent 6/11; **verify landed before resuming parallel/multi-agent batteries.** |
| 2 | **build.ps1 hardcodes -mem 4096** | Lines 31/65. Current seed maps 2GB; since CL 3732 the clamp is real, so every VM commits 4GB of host RAM. Drop to 2048 (or derive from the seed) to halve commit pressure under parallel batteries on the 32GB host. |
