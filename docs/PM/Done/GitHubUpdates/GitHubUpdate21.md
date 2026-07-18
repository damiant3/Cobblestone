# GitHub Update 21 -- 2026-06-06

## x86-64 Codegen Optimization

- **Comparison folding.** `emit-if` fuses `cmp + jcc` for integer/bool/char comparisons in `if` expressions, eliminating the `setcc + movzx + test` materialization sequence. Saves 3 instructions per branch. Register-register and immediate-vs-register paths, with i32-signed range guard on immediates to prevent sign-extension truncation.
- **Preamble elision.** Function prologue reduced from 44 to 9 instructions. Inline scheduler yield removed (scheduler is invoked via timer IRQ). Stack HWM tracking made optional via `trace-alloc` flag (disabled by default, saving 6 instructions per call).
- **Store-load elimination.** When the right operand of a binary operation is `IrIntLit`, the store-local + load-local round-trip for the right side is skipped. The temp register from `emit-expr` is used directly. Saves 3-4 instructions per binary op with a literal operand.
- **Immediate add/sub.** `emit-binary-add-imm` and `emit-binary-sub-imm` use `add-ri`/`sub-ri` for literal operands instead of loading to a register first. Guarded against values outside i32-signed range.
- **Single-arg direct emit.** For single-argument function calls (the common case), emit `mov rdi, reg` instead of `push reg; pop rdi`, and skip `save-args-loop` (alloc-local + store-local + load-local). Saves 4-5 instructions per single-arg call.
- **Net result:** `fib(35)` cut from 107 to 53 instructions (50% reduction). Seed size reduced from 2,654,334 to 2,236,413 bytes (16% smaller).

## Bug Fix: i32 Sign-Extension Truncation

- **Root cause.** CL 3217's `emit-binary-sub-imm` passed values > INT32_MAX through `alu-ri`, which truncates to sign-extended i32. The expression `0 - 2147483648` compiled as `SUB reg, sign_ext(0x80000000)` = `reg + 2147483648` instead of `reg - 2147483648`. This broke `bound-fits-i32` (always returned False), forcing all bounds checks into a push/pop R9 codepath that produced `cmp r9, r9` (always equal) when the value register was also R9 — silently disabling `OvClamping` and `OvError` runtime bounds checks.
- **Fix.** Range-check immediates in `emit-binary` (add/sub) and `emit-if` (comparison folding) dispatchers; fall back to register-register path when outside `[-2^31, 2^31-1]`.
- **Symptom.** `Integer between 0 and 100 clamping` stored 150 unclamped. The `arithmetic` test was the sole failure (200/201).

## Compiler

- **Bulk text/bytes builtins.** `text-to-unicode-bytes` and `unicode-bytes-to-text` runtime helpers for bulk CCE-to-UTF8 conversion. HTTP parser throughput improved to ~529K req/s on the TechEmpower benchmark.
- **codex-vm crash diagnostics.** On any crash, codex-vm now dumps: all 16 GP registers with symbol resolution, x86-64 disassembly around RIP, RBP-chain backtrace, stack dump (24 slots), and memory at fault address. Built-in mini-disassembler, auto-loads `.map` from kernel path. Interactive debugger on crash when not headless.
- **Bench harness.** `bench/compare.ps1` — compile C (/Od + /O2) and Codex, extract CDX disassembly, produce comparison report. Four micro-benchmarks: fib, factorial, GCD, sum-to-N.
- **survey-check-mul.** Raised from 10 to 400 to handle type-dense plug source (PlugTypes.codex: ~40 types in 368 lines).

## Apps

- **WASM backend.** Phases 1-8 of the WASM plug: WASI runtime, strings, records, variants, lists, browser demo. Cranelift-targeted code generation.
- **Spark Studio / WebGPU 3D.** Full 3D creative suite compiled to WASM and rendered via WebGPU at 120fps. Phong shading, hardware depth buffer, cube/pyramid/sphere primitives. Studio features: gizmo transform, easing curves, particle systems, dynamic lighting, instancing, mirror/symmetry, align/distribute, scene tree, batch operations, grid/pivot snapping, export, persistent WASM allocator, circular undo.
- **CodexMagic web platform.** Admin dashboard, marketplace with store economy, card pool management, pack cracking, collection viewer, profile system. HTML plug widget renderers. TCP game server on port 9200.

## README

- Seed digest updated (`30253220...`, 2,236,413 bytes).
- New milestone row: x86-64 codegen optimization + WASM + Spark Studio.
- Module count updated (362 library modules).

## Stats

- Seed: 2,236,413 bytes (SHA256 `30253220ADAC1004BD118C013A8F5B1354FA4577FBBAB9DE857B2794FA5647EC`)
- CDX hard fixed point on bare metal (SUT === stage1, one pass); 211 total / 201 pass / 0 fail / 10 skip
- 238 foreword modules, 54 compiler files, 113 plug files, 330 app modules
- fib(35): 53 x86-64 instructions (was 107 at start of session, MSVC /O2 = 20)
