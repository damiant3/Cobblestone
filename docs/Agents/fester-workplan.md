# Agent Fester Workplan

**Stream:** `//Codex/RESTRUCTURE`
**Updated:** 2026-06-27
**Latest CL:** 6177 (merge-down), optimization CLs 6141-6173

## ARM64 Codegen Optimization Campaign

### Score: 54/54 cross-tests pass (100%)

### Benchmark Results (CL 6173)

| Bench | Start | Codex ARM64 | GCC -O0 | GCC -Os | GCC -O2 |
|-------|------:|------------:|--------:|--------:|--------:|
| fib   | 36    | **21**      | 20      | 16      | 237*    |
| fact  | 26    | **13**      | 17      | 9       | 15      |
| gcd   | 31    | **23**      | 21      | 7       | 8       |
| sum   | 28    | **17**      | 20      | 9       | 13      |
| Total | 121   | **74**      | 78      | 41      | 273     |

*GCC -O2 fib = 237: aggressive unrolling inflated code size.

**Beats GCC -O0 total (74 vs 78).** fact beats ALL GCC levels
except -Os. sum and fact beat GCC -O0. fib is 1 instruction away.

### Optimization CLs (2026-06-26/27)

| CL | Phase | What | Impact |
|----|-------|------|--------|
| 6141 | 2 | Destination-driven emission | fib 33->28, fact 22->19 |
| 6148 | 2b | Direct arg emission | fib 28->26, fact 19->18 |
| 6150 | 3 | Compact prologue + local counting | fib 26->24, fact 18->15, sum 24->21 |
| 6157 | 4 | TCO skip-save for stable args | gcd 27->25 |
| 6158 | 5 | CMP-immediate for comparisons | fib 24->23 |
| 6162 | 6 | Peephole MOV eliminator | Code quality (fewer data hazards) |
| 6170 | 7 | NOP compaction pass | sum 21->19 (removes peephole NOPs) |
| 6173 | 9 | STP-pre/LDP-post frame merge | fib 23->21, fact 15->13, sum 19->17 |

### Next Targets

- **Near-leaf frame elision:** Skip full prologue for functions
  without calls or with only tail-calls. Biggest remaining win
  for closing the gap with GCC -Os.
- **Prologue STP compaction:** Blocked by `peak-local`
  underestimate for handler functions. Needs investigation into
  why effect handler dispatch uses registers not tracked by
  `a64-alloc-local`.
- **TCO direct arg shuffle:** Emit args directly to param
  registers (like x86-64 CL 3649) instead of through temps.

### Remaining Test Issues

- `vec-select` — pre-existing ARM64 failure (vector comparison
  mask wrong). Not related to optimization work.
- `tls-test`, `ui-orchestrator-test` — marked slow, not in
  default battery.
