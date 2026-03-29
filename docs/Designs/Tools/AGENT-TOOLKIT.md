# Codex Agent Toolkit — Design Document

**Date**: 2026-03-20 (verified via system clock)
**Author**: Copilot (VS 2022, Windows)
**Status**: Draft — for review by linux agent

---

## The Problem

AI agents working on the Codex compiler repeatedly hit the same failure modes:

1. **Möbius loops** — The agent encounters a hard problem, works through it, runs
   out of context, circles back to the same spot, and doesn't know it's been there
   before. In the bootstrap sessions, this manifested as re-discovering the same
   parser bug from different angles across context resets.

2. **Tool-induced thrashing** — `edit_file` silently corrupts large files. The agent
   detects the corruption, fetches the backup, re-reads, retries, fails again. Each
   cycle burns context window. The worst cases burned 80% of a session on file I/O
   that should have been trivial.

3. **Cognitive overload** — The six hottest compiler files total ~116% of the agent's
   effective working memory (60K chars). When the agent tries to hold Parser +
   TypeChecker + Emitter simultaneously, reasoning quality collapses. The agent
   doesn't know it's overloaded until it starts making mistakes.

4. **Invisible progress** — During the type-debt reduction (90 → 7 → 0 `object` refs),
   the agent had no way to see trajectory. Is the number going up or down? Without
   trajectory, the agent can't distinguish "9 errors and falling" from "9 errors
   and stuck."

5. **Stale assumptions** — After 15 minutes of reading and planning without building,
   the agent accumulates assumptions that may be wrong. There's no signal to say
   "you haven't verified anything in a while."

These aren't hypothetical. They're documented in `Opus.md`, `Reflections2.md`,
`CognitiveMeterReport.md`, and the iteration handoffs. The existing dashboard
(`codex-dashboard.ps1` / `codexdashboard.sh`) was the first response — it works,
but it's passive (the agent must remember to run it) and it can't prevent problems,
only report them.

---

## The Vision

A unified **Codex Agent Toolkit** — a single .NET console application that replaces
all the shell scripts and the dashboard with something that:

- **Runs cross-platform** — one C# codebase, no ps1/sh duality
- **Knows the project** — understands the Codex pipeline, file roles, and hot paths
- **Tracks cognitive state** — monitors what the agent has loaded, how long since
  last build, how many edits are unverified
- **Detects loops** — records breadcrumbs so the agent can see "you were here before"
- **Provides safe file operations** — snapshot/diff/restore that never corrupts
- **Emits structured diagnostics** — JSON for programmatic consumption, human-readable
  for the terminal

### Name

`codex-agent` — invoked as `dotnet run --project tools/Codex.Agent -- <command>`

---

## Architecture

### One Project, Many Commands

```
tools/Codex.Agent/
├── Codex.Agent.csproj
├── Program.cs                    # Command dispatch
├── Commands/
│   ├── PeekCommand.cs            # Read file lines (replaces peek.ps1)
│   ├── StatCommand.cs            # File stats (replaces fstat.ps1)
│   ├── SnapCommand.cs            # Snapshot/diff/restore (replaces sdiff.ps1)
│   ├── StatusCommand.cs          # Git + project status (replaces gstat.ps1)
│   ├── TestCommand.cs            # Run tests with filtering (replaces trun.ps1)
│   ├── DashboardCommand.cs       # Cognitive dashboard (replaces codex-dashboard.ps1)
│   └── SessionCommand.cs         # NEW: session tracking & loop detection
├── Cognition/
│   ├── CognitiveState.cs         # The meter — tracks load, staleness, trajectory
│   ├── SessionLog.cs             # Breadcrumb trail for loop detection
│   ├── ThrashDetector.cs         # Identifies Möbius patterns
│   └── FileRiskMap.cs            # Knows which files cascade to which stages
└── Infrastructure/
    ├── ProjectModel.cs           # Understands the Codex pipeline structure
    ├── GitHelper.cs              # Git operations (read-only)
    └── Output.cs                 # JSON + human-readable formatting
```

### Why .NET, Not Shell Scripts

| Concern | Shell scripts | .NET tool |
|---------|--------------|-----------|
| Cross-platform | Maintain 2 copies (ps1 + sh) | One codebase |
| String processing | Fragile regex/awk | Proper parsing |
| State persistence | Write temp files | In-memory + `.codex-agent/session.json` |
| Testable | Not easily | xUnit tests like everything else |
| Extensible | Copy-paste | New command class |
| IDE integration | None | Could be invoked by LSP, MCP, etc. |

---

## The Commands

### 1. `peek` — Read File Lines

Replaces `peek.ps1` / `peek.sh`. Fixes the `get_file` first-line-drop bug.

```
codex-agent peek <file> [start] [end]
codex-agent peek <file> 0 0              # entire file
codex-agent peek <file> 1 30             # lines 1-30
```

Output: numbered lines, always accurate. No dropped first line.

### 2. `stat` — File Statistics

Replaces `fstat.ps1` / `fstat.sh`.

```
codex-agent stat <file-or-glob>
codex-agent stat src/Codex.Types/*.cs
```

Output: line count, char count, byte count per file. Totals for globs.

### 3. `snap` — Snapshot / Diff / Restore

Replaces `sdiff.ps1` / `sdiff.sh`. The most critical tool — prevents the
file-corruption thrashing that destroyed entire sessions.

```
codex-agent snap save <file>             # snapshot before editing
codex-agent snap diff <file>             # show what changed
codex-agent snap restore <file>          # revert to snapshot
codex-agent snap clean                   # remove all snapshots
```

Snapshots stored in `.codex-agent/snapshots/`. Auto-cleanup on `session end`.

### 4. `status` — Git + Project Status

Replaces `gstat.ps1` / `gstat.sh`.

```
codex-agent status
```

Output: branch, commit, ahead/behind, dirty files, recent commits, feature branches.
Also shows time since last build (from session state).

### 5. `test` — Run Tests with Filtering

Replaces `trun.ps1` / `trun.sh`.

```
codex-agent test                         # all tests
codex-agent test --project Types         # scope to one project
codex-agent test --filter "Bootstrap"    # name filter
```

Captures full output. Filters to failures + summary. No truncation.
Records results in session state for trajectory tracking.

### 6. `dashboard` — Cognitive Load Dashboard

Replaces `codex-dashboard.ps1` / `codexdashboard.sh`. Same metrics, better
implementation, integrated with session state.

```
codex-agent dashboard
codex-agent dashboard --json
codex-agent dashboard --watch
```

Metrics (from CognitiveMeterReport.md + improvements):

| Metric | What it measures | Why it matters |
|--------|-----------------|----------------|
| Hot path ratio | % of context budget consumed by key files | >80% = can't hold the problem |
| Type debt | `object` refs + `_p0_` proxies in generated output | Bootstrap fidelity |
| Fixed-point status | Stage 1 = Stage 3? | Self-hosting invariant |
| Thrash risk score | Composite 0–6 | HIGH/CRITICAL = scope down |
| Cascade risk | Parser/Lexer bugs → all downstream | Prioritize upstream fixes |
| Dirty file count | Uncommitted changes | Accumulated assumptions |
| **Time since build** | Minutes since last successful build | >10 = verify before continuing |
| **Error trajectory** | Direction of error count over session | Rising vs falling |
| **Session depth** | How many edits/reads in this session | Deep sessions lose context |
| **Loop risk** | Have we visited this file+region before? | Möbius detection |

---

## The Cognitive Engine (the new part)

This is the core innovation. The existing dashboard reports *project* state.
The cognitive engine tracks *agent* state.

### Session Tracking

When an agent starts work, it begins a session:

```
codex-agent session start
```

This creates `.codex-agent/session.json`:

```json
{
  "id": "s-20260320-153042",
  "started": "2026-03-20T15:30:42Z",
  "agent": "Copilot (VS 2022)",
  "breadcrumbs": [],
  "builds": [],
  "edits": [],
  "errors_trajectory": [],
  "warnings": []
}
```

Every subsequent command automatically appends to the session:

- `peek <file> <range>` → breadcrumb: `{ "action": "read", "file": "...", "lines": [1, 30], "time": "..." }`
- `snap save <file>` → breadcrumb: `{ "action": "edit-start", "file": "...", "time": "..." }`
- `snap diff <file>` → breadcrumb: `{ "action": "edit-verify", "file": "...", "time": "..." }`
- `test` → records pass/fail counts with timestamp
- `dashboard` → computes cognitive metrics from the session

### Loop Detection

The `ThrashDetector` analyzes breadcrumbs for patterns:

**Pattern 1: File revisit** — Reading the same file region more than twice
in one session without an intervening successful build.
```
⚠️ You've read TypeChecker.codex lines 150-200 three times this session
   without a successful build in between. Consider:
   - Writing a minimal repro in samples/
   - Building to verify your current understanding
   - Asking the user to clarify the requirement
```

**Pattern 2: Edit-restore cycle** — Snapping, editing, restoring the same
file more than once.
```
⚠️ You've restored CSharpEmitterExpressions.codex twice this session.
   The edit_file tool may be struggling with this file (433 lines).
   Consider: partial class strategy or write-full-file approach.
```

**Pattern 3: Error plateau** — Test error count hasn't changed across 3+
builds despite edits.
```
⚠️ Error count has been 12 across your last 3 builds.
   Your changes aren't reaching the failures. Check:
   - Are you editing the right file?
   - Is the build picking up your changes?
   - Would a different approach work better?
```

**Pattern 4: Context exhaustion** — Session has accumulated >20 file reads
across >6 distinct files without a build.
```
⚠️ You've read 8 files (22 reads) without building.
   Your accumulated assumptions may be stale.
   Build now to verify before continuing.
```

### Cognitive Load Estimation

The engine estimates current cognitive load based on:

```
load = Σ (file_chars × recency_weight × cascade_multiplier) / budget

where:
  file_chars     = characters of each file read in this session
  recency_weight = 1.0 for last 5 reads, 0.5 for older, 0.0 for >15 reads ago
  cascade_mult   = 2.0 for Parser/Lexer, 1.5 for TypeChecker, 1.0 for others
  budget         = 60,000 chars (empirically determined in CognitiveMeterReport.md)
```

Output levels:

| Load | Meaning | Guidance |
|------|---------|----------|
| 0-40% | GREEN — plenty of headroom | Proceed normally |
| 40-70% | YELLOW — moderate load | Finish current task before taking on more |
| 70-90% | ORANGE — high load | Build and verify before reading more files |
| 90%+ | RED — overloaded | Stop. Build. Test. Consider asking the user for help. |

### Proactive Warnings

When the agent runs any command, the engine checks for conditions and appends
warnings to the output:

```
$ codex-agent peek src/Codex.Types/TypeChecker.codex 1 50

  --- src/Codex.Types/TypeChecker.codex lines 1..50 of 338 ---
  [file content...]

  ⚡ COGNITIVE LOAD: 73% (ORANGE)
     You have Parser + TypeChecker + Emitter loaded.
     Budget: 60K chars. Loaded: ~44K chars.
     → Build and verify before reading more files.

  🔄 LOOP RISK: You read this file's lines 1-30 earlier this session.
     → Check session log: codex-agent session show
```

These warnings appear automatically — the agent doesn't need to remember to
check the dashboard.

### Session End

```
codex-agent session end
```

Cleans up snapshots. Writes a session summary to `.codex-agent/history/`.
The summary includes: files touched, builds run, error trajectory, total
edits, loop incidents, and a one-line cognitive-load profile
(e.g., "GREEN → YELLOW → ORANGE → GREEN after build").

Previous session summaries are available for the next session to read,
providing cross-session memory:

```
codex-agent session history
```

This is the anti-Möbius feature. The next agent session can see what the
previous session did, where it got stuck, and what files it touched.

---

## Migration Plan

### Phase 1: Create the project, port the basic tools

Port `peek`, `stat`, `snap`, `status`, `test` as direct translations from
the PowerShell scripts. Verify each one produces identical output.

| Old | New | Effort |
|-----|-----|--------|
| `peek.ps1` / `peek.sh` | `codex-agent peek` | Small |
| `fstat.ps1` / `fstat.sh` | `codex-agent stat` | Small |
| `sdiff.ps1` / `sdiff.sh` | `codex-agent snap` | Medium |
| `gstat.ps1` / `gstat.sh` | `codex-agent status` | Small |
| `trun.ps1` / `trun.sh` | `codex-agent test` | Medium |

### Phase 2: Port the dashboard

Move `codex-dashboard.ps1` / `codexdashboard.sh` into `DashboardCommand.cs`.
Same metrics, same output format, but now computed in C# with proper data
structures instead of string scraping.

### Phase 3: Build the cognitive engine

Add session tracking, loop detection, thrash detection, and proactive warnings.
This is the new capability that doesn't exist today.

### Phase 4: Update agent rules

Update `.github/copilot-instructions.md` to reference `codex-agent` instead of
the individual scripts. The old scripts can stay for backward compatibility but
are deprecated.

### Phase 5: Delete the shell scripts

Once both agents have been using `codex-agent` successfully for a few sessions,
remove the PowerShell and Bash scripts.

---

## What This Doesn't Do

- **It doesn't replace `edit_file` or `get_file`.** Those are IDE tools. This
  toolkit works alongside them, providing safety nets (snap/restore) and
  diagnostics (cognitive load).

- **It doesn't automatically fix code.** The agent still makes all decisions.
  The toolkit provides information and warnings, not automation.

- **It doesn't require network access.** Everything is local filesystem + git.

- **It doesn't persist across git clones.** The `.codex-agent/` directory is
  gitignored. Session state is ephemeral by design — what matters is the
  session history summaries, which are structured enough to be useful but
  small enough to not be noise.

---

## Connection to the Project's Principles

From `docs/10-PRINCIPLES.md`:

> **Self-awareness is a feature.** A system that knows its own limits
> is more useful than one that doesn't.

The cognitive engine makes the agent self-aware. It knows when it's
overloaded, when it's looping, when it's been too long since verification.
The agent that runs `codex-agent dashboard` before touching Parser.codex
is the agent that doesn't corrupt it.

From `Opus.md`:

> **Small repro files are the most powerful debugging tool in existence.**

The thrash detector's primary recommendation when it detects a loop is:
"write a minimal repro." The toolkit encodes the hard-won lesson from
the bootstrap into a persistent, automated signal.

From `CognitiveMeterReport.md`:

> **The best debugging session this entire project had was mini-bootstrap.codex.**
> 40 lines, 8 errors, binary search by simplification. Every dashboard metric
> exists to push the agent toward that strategy and away from "let me read
> 4 files and think really hard."

This is the north star for the cognitive engine. Every warning, every metric,
every loop detection is designed to push the agent toward small experiments
and away from big thinks.

---

## Open Questions

1. **Should session state include file content hashes?** This would let the
   engine detect when a file has changed since the agent last read it (stale
   reads). Cost: slightly more I/O per command. Benefit: catches "you're
   reasoning about a version of this file that no longer exists."
Damian: sounds like a great idea.  running a hash on the file is cheap compared to the cognitive cost of stale assumptions.
2. **Should the dashboard auto-run on every command?** Currently proposed as
   opt-in (warnings appended to output). Could be always-on with a `--quiet`
   flag to suppress. The risk is noise fatigue — if every command has a
   warning footer, agents learn to ignore them.
Damian: Yes. The proactive warnings are the whole point. If the agent learns to ignore them, that's a failure mode we want to detect and address (e.g., by tuning thresholds or improving messaging). The goal is to make the warnings salient and actionable enough that the agent pays attention.
3. **Should session history be committed?** Currently proposed as gitignored.
   But the handoff docs serve a similar purpose. Could session summaries
   auto-generate handoff docs. Or could be overkill.
Damian: Depends on mode.  Researchers will (should?) love it.  but it would get in our way moving forward.  put it in opt-in.
4. **Should the toolkit be usable outside the Codex project?** The cognitive
   engine is project-specific (it knows about Parser, TypeChecker, etc.).
   But the session tracking and loop detection are generic. Could be split
   into a general-purpose library + Codex-specific configuration.
Damian: Of course.
