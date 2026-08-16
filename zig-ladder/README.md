# The zig plug's phase-oracle ladder

Working tooling for the zig transpiler plug, developed on Linux under
QEMU. Each rung bundles real compiler chapters into one unit with a
generated harness, compiles that subject two ways -- seed on bare metal
as truth, and through the zig plug to a zig program -- and requires
byte-identical output. Nine rungs: lex, parse, desugar, scope, check,
lower, text, pingpong (the source fixed point), lir (machine-code bytes
from the instruction selector).

Everything generated is ignored by `.gitignore` and regenerates from a
script beside it; the scripts are the record.

- `cycle.sh` -- rebundle the zig plug, ring-compile it, run the warmup
  oracles (hello, recurse, fib) against banked truth.
- `ast/allcycles.sh` -- rebuild the plug once, sweep every rung. The
  guard against fixing one rung and breaking four.
- `ast/truthcycle_<m>.sh` / `ast/<m>cycle.sh` -- one rung's truth arm /
  zig arm. `ast/plugcycle.sh <m>` rebuilds and runs one, reporting
  markers grepped from the emitted zig (zig stops at the first
  @compileError, so error counts under-report).
- `ring_compile.py` -- compile through the seed under QEMU via the
  codex-vm ring contract; blobs larger than the 1 MB ring stream
  through it (the host refills behind the guest's read cursor over the
  gdbstub, and kicks until consumption completes -- a reader that finds
  the ring dry parks in hlt with no wake source on this path).
  `ring_refill_test.sh` is that path's oracle.
- `plug_run_checked.py` -- two plug transfers at different chunk sizes
  must agree byte-for-byte before an output is trusted.
- `codex_vm.py` -- launch/READY/run helpers shared by the above.

Requires: qemu-system-x86_64, pwsh, python3, zig 0.16. Paths are
derived from script locations; nothing here assumes a particular
checkout directory.
