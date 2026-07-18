# GitHub Update 33 -- 2026-07-07

Covers main CLs 7187-7226 (since Update 32 covered through 7186,
2026-07-06). One cycle, one headline, and it is the headline Update 32
told you had *not* happened: demand paging shipped. The effort the last
update recorded as "the effort that did not ship" -- a week burned, zero
of five stages landed -- landed in a day, byte-identical, with the
survey system deleted outright. Around it: a review-driven hardening
series, a sampling profiler that finally measured where the self-compile
actually spends its time, and a small design note.

## The headline: demand paging shipped

Update 32 was honest about the failure and drew the wrong lesson from
it. The retrospective concluded the model was weak at convergent
debugging under uncertainty. True, but beside the point. The real defect
was a **confound**: every experiment for a week had compared a
first-generation binary against a second-generation binary and called
the difference "demand paging," when *which compiler compiled the
binary* changed in lockstep with *which boot the binary had*. Nobody saw
the second axis because nobody believed it was moving.

Damian's move broke it: stop debugging the configuration where the bug
manifests. Take the demand patch back through history and bisect on
*time*, holding the trigger fixed. Eleven probes across six weeks of
history -- CLs 2500 through 7127, **green every one**, demand verified
armed at each. Head: red. The only compiler-source difference between
the last green and the red was a single constant, `check-mul 200 -> 40`
(CL 7162, a survey multiplier). A bisect that converges on a *constant*
is telling you the bug is not where you think.

The reveal, by arithmetic rather than debugger: the "corrupted
diagnostic string" contained operand pointers and length words --
*structure*, not damage. The binary had not been corrupted at runtime;
it had been **compiled wrong**. The demand patch's only sin was adding
~3 KB of source, pushing the self-compile over `check-mul 40`'s silent
under-reservation cliff. The overflow detector never fired. Demand
paging -- the mechanism, the handler, the faults -- had been innocent
from the first session. It was the messenger, shot six times.

The survey multipliers had been chosen non-monotonically: 20 worked, 25
did not, 40 worked. The correct response to a dial nobody understands is
not to find a lucky setting -- it is to remove the dial. So the survey
system is deleted. The compiler demand-pages its own heap: a
not-present-PDE trick plus a compact `#PF` handler, commit-on-touch. It
is the first compiler in this project's history to compile itself *while
demand-paging its own heap*, byte-identical, one pass. The full battery
scores **319 total, 304 pass, 0 fail, 15 skip** -- the same number the
flat arena scored, where the demand world had scored 188/115 the week
before. Damian's acceptance test -- *we can't break it* -- was run
literally: 86 KB of ballast appended to the compiler source, the exact
growth that used to miscompile silently at +3 KB, and the pingpong
stayed byte-identical. The old killer is now a regression test
(`build/test-growth.ps1`). (blu, main CL 7202; the story is
`docs/Designs/Done/Compiler/DemandPagingVictory.md`.)

## Hardening the demand world (review-driven)

A shipped mechanism is not a validated one. An adversarial review of the
demand arena found eight edges the gates never touched, and a series
closed them (val, main CL 7216, series CLs 7207-7215):

- **The `#PF` handler now grows only not-present faults.** A protection
  or reserved-bit fault (error code P=1) dumps `!EXC` instead of being
  silently remapped; demand-mapped pages keep the NX bit they would have
  had. A touched-page counter is the honest physical-consumption metric.
- **Double faults are loud.** A 64-bit TSS with IST1 gives the CPU a
  known-good emergency stack, so a stack-overflow-into-not-present-page
  becomes a standard register dump instead of a silent triple-fault.
- **The demand top is derived from actual RAM** (`[0xFE8] >> 21`), so
  any `-mem` from ~128 MB up boots; the top-of-RAM stack region stays
  present. codex-vm's default `-mem` moved 2048 -> 3072 to match.
- **AP idle stacks** moved out of the demand range into always-present
  low memory, and spawn stack pre-touch became an unrolled loop that
  cannot skip a middle page.

The same series repaired **37 plug `run.ps1` scripts** damaged by the
CL 4990 mass-edit, making the plug test matrix runnable again.

## Measuring what the self-compile actually costs

The demand work exposed that codex-vm never delivered timer interrupts
to a compute-bound guest -- the watchdog, preemption, and any profiler
were blind during compilation. A 55 ms VP-cancel kicker fixed that, and
with it came a sampling profiler (val, main CL 7218): a bias-free host
sampler (`CODEX_VM_PROFILE`) plus guest `prof-start`/`prof-dump`.

The result overturned the obvious guess. A naive guest sampler blamed
`__alloc` at 57% -- but that is injection skew (the timer lands on the
tight `rep stosb`), and deleting the zero-fill entirely moved the
self-compile median by 0.1 s of 21.9 s, inside noise. The bias-free host
sampler shows the truth: **~78% `__write_binary`** -- serial output of
the 2 MB CDX one byte at a time through a port -- with `__alloc` at ~5%.
The lever is the output path, not the allocator; the optimization
backlog is re-ranked accordingly. Four latent profiler bugs (buffer
inside the page tables, a missing `is-builtin` entry, a value-vs-function
type, a control-channel hang) were fixed in passing.

### Wall-clock, this seed (917711F3, 20-core host, quiescent)

Concrete timings, measured clean (steady state, no background load):

| Operation | Wall-clock |
|-----------|-----------:|
| Self-compile (one CDX self-compile, incl. VM boot + serial feed) | **~22.5 s** (22.3-22.7 median; 38.7 s cold VM/disk) |
| Full gate build (`build/build.ps1`) | **170 s** (2.8 min) |
| Full battery (`build/test.ps1 -Jobs 4`, 304 tests) | **294 s** (4.9 min) |

The gate build's phase breakdown makes the profiler finding tangible.
The pure CDX compile is ~22 s, but the three *text*-emitting phases
dominate the wall-clock: `text-stage1` 31.4 s, `text-stage2` 30.1 s,
`sem-equiv` 25.4 s -- because text emission rides the *same*
byte-at-a-time serial output path the profiler flagged as ~78% of a
CDX compile (`__write_binary`). Batching that path would cut the pure
compile and every text stage at once.

```
clean 1.0   concat 0.9   cdx-build 22.4   sign 2.0   canary 1.5
text-stage1 31.4   sem-equiv 25.4   text-stage2 30.1   text-fp 0.2
cdx-stage1 21.8   cdx-fp 0.0   test-bvt 9.3   plug-binary 23.8   = 170 s
```

## Odds and ends

- **A design note (fester, CLs 7222/7226).**
  `docs/Designs/Compiler/Active/PhysicalCostCodegen.md` applies Adam
  Chlipala's "Why Your CPU Works So Hard" to Codex: the CPU's hidden
  reconstruction work (register renaming, alias disambiguation,
  speculation, coherence) is exactly the information Codex already proves
  in its types. It reads the demand-paging victory as the same move on
  the residency axis -- replace a predictive abstraction with a measured
  one -- and lists three codegen/layout work items, the first being to
  feed the linear-types alias proof into the emitter.

fester's WGSL plug and the ~40-demo gpushow WebGPU showcase were already
covered in Update 32; they remain on the fester stream pending review and
are not part of this push.

## By the numbers

| Metric | Update 32 | Update 33 | Delta |
|--------|----------:|----------:|------:|
| Copy-ups | ~18 | ~6 | -- |
| Plug roster | 53 | 53 | 0 |
| DemandPaging: survey system | in place | **deleted** | -- |
| Full battery (pass / fail / skip) | ~294 / 0 / 15 | 304 / 0 / 15 | -- |
| Self-compile hot spot (measured) | -- | ~78% `__write_binary` | new |
| Seed size | 2,106,070 B | 2,112,715 B | +6,645 B |

The survey system deletion is the structural change: deck sizing no
longer scales with input by formula, so the class of silent
grow-the-source miscompile that consumed a week is not fixed but
extinct. Deck allocation and `phase-compact` remain; only the prediction
is gone.

Seed at push time: `seed/Codex.cdx`, 2,112,715 bytes (~2.01 MB), SHA-256
`917711F305BC4E864CA90BA7BD79AD134F3A207C3F1323A0D0EA6457A7FB7342`,
content hash
`558A357B892C27444FEC01CF27218EDB09403693BEBC4B636A00EC355E1A3512`.

## What's next

The demand arena's optimization backlog is now evidence-ranked: batch
the binary output path (the measured ~78%), then the frame pool +
decommit stage that returns physical RAM at `phase-compact`. Per-core
TSS/IST and an AP-executes-code battery test close the SMP coverage the
review flagged. And the `PhysicalCostCodegen` work items open a separate
thread: turn the type system's alias and effect proofs into codegen the
emitter can act on.
