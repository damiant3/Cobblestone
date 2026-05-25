# Update — 2026-05-25

## Highlights

**10x compile speed restored.** The static bounds prover introduced in
the proof system work had O(n^2) complexity per function (deep IR walk
with linear environment scan). Replaced with O(1) shallow pattern
matching — same 245 bounds proven on the self-host, compile time back
to ~30s from ~320s.

**Project Robusto: compiler hardening.** Fuzzed the compiler with 44
adversarial inputs. Found and fixed 1 crash (Token.column u16 overflow
on 100KB lines). All 8 compiler phases now guard against deck overflow
(CDX9002 diagnostic instead of silent memory corruption).

**codex-vm stability.** Multiple vid.sys BSOD fixes for parallel VM
operation on Win11 26100:
- Shadow VGA buffers (0xD1 race between display thread and hypervisor)
- System-wide WHP mutex serializing partition create/destroy (0x13A cascade)
- Serial socket blocking mode fix (binary transfer truncation)

**Trace-alloc profiler.** New `-Trace` flag on compile.ps1 instruments
every `__alloc` call, recording (size, callsite RIP) pairs into a
ring buffer. At exit the boot stub dumps entries over serial as
`T:<addr>:<size>` lines. `resolve-trace.ps1` resolves RIPs against the
symbol map and reports per-function, per-phase allocation totals.
Completely gated — zero overhead when not enabled.

**Prologue yield-path argument clobber.** Fixed a size-dependent GPF
where the cooperative scheduler's yield path in every function prologue
clobbered RDI/RSI (the first two arguments) without saving them. Only
triggered on inputs >100KB when a timer interrupt set the yield flag.

## Seed

Rebuilt from CL 2378. Includes shallow bounds prover, trace-alloc
instrumentation, yield-path fix.

```
seed/Codex.cdx  2,194,745 bytes
SHA-256: 300190EDA5D89734DE5335C01367230BA4C27FFAF72834C9E705B9C596EA1509

seed/Codex.img  8,388,608 bytes
SHA-256: 3233F3F1B183F6D655121398C058B45CA7BCD51A54434F8F3C0DCD4E430C2484
```

## CLs since last push (2026-05-24 20:06)

| CL | Author | Summary |
|---|---|---|
| 2382 | reek | Trace-alloc serial dump + compile.ps1 -Trace flag |
| 2378 | fester | Seed rebuild: shallow prover, trace-alloc, yield fix |
| 2375 | gollum | Trace param fix in opening.codex |
| 2370 | reek | PerforceProcess.md: merge-down must be semantic |
| 2364 | gollum | Shallow bounds prover (10x perf) |
| 2359 | reek | Trace-alloc instrumentation, constants drift guard, lex survey 100x |
| 2355 | gollum | Robusto fuzz corpus (44 tests), -Fuzz flag, halted exit code fix |
| 2346 | gollum | Serial socket blocking fix (binary truncation) |
| 2341 | fester | Prologue yield-path argument clobber fix, triple-newline normalization |
| 2337 | gollum | Remove shutdown watchdog (caused truncation) |
| 2332 | gollum | WHP mutex: serialize partition create/destroy |
| 2328 | gollum | VM shutdown watchdog |
| 2323 | gollum | VM graceful shutdown, Close-Vm 5s wait |
| 2318 | reek | Plug emitters: consistent Go/lang output formatting |
| 2312 | gollum | Shadow VGA buffers (0xD1 BSOD fix) |
| 2309 | reek | README update, seed digests |
| 2308 | fester | Lex deck survey 12x→40x, game modules, seed rebuild |

## Test battery

210 total samples (was 160). 156 pass, 0 fail. New `-Fuzz` flag
includes the adversarial corpus without polluting the smoke test.
