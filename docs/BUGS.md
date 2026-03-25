# Codex Bug Tracker

Tracked issues for the Codex compiler and agent toolkit.
Format: `BUG-NNN | severity | component | status | summary`

---

## Open

(none)

---

## Details

### BUG-001: Recursive string-returning functions emit `return null`

**Found by:** Agent Linux (2026-03-25)
**Files:** `tools/codex-agent/codex-agent.cs` (format_roster_loop, stat_loop in fstat.cs)

The C# emitter generates recursive functions where the ternary expression is evaluated as a statement (for side effects) but the return value is discarded, followed by `return null`. Example from `format_roster_loop`:

```csharp
public static string format_roster_loop(List<string> files, long idx)
{
    ((idx >= ((long)files.Count)) ? "" : /* ...recursive computation... */);
    return null;  // ← BUG: discards the computed string
}
```

The expression-as-statement pattern works for `void`-returning functions (`do_greet`, `do_roster`) but silently breaks for functions that return a value. The IL emitter needs to detect non-void return types and emit `return <expr>` instead of `<expr>; return null;`.

**Impact:** `roster` command and `fstat` tool produce no output (null where formatted strings expected).

**Workaround:** None in Codex source — the emitter itself must be fixed.

### BUG-002: `do-greet` uses Unix-only `mkdir -p`

**Found by:** Agent Linux (2026-03-25)
**File:** `tools/codex-agent/codex-agent.codex`

The `do-greet` function creates the agents directory via `run-process "mkdir" ("-p " ++ agents-dir)`. This works on Linux/macOS/WSL but would fail on a bare Windows cmd.exe environment.

**Fix:** Add a `create-directory` builtin or use a platform-aware helper.

---

## Closed

| ID      | Severity | Component          | Status | Summary |
|---------|----------|--------------------|--------|---------|
| BUG-001 | medium   | Emit.CSharp        | fixed  | Effectful non-void functions emitted `return null` — fixed in `IsVoidLikeDefinition` |
| BUG-002 | low      | codex-agent        | fixed  | `mkdir -p` replaced with cross-platform `pwsh New-Item` |
