#!/usr/bin/env python3
"""Generate ClampHarness.codex: the whole compiler, compiling plug-oracle-arith.

This exists to attribute one failure. The hosted compiler (zigc) compiles
codex/test/plug-oracle-arith.codex into a binary that agrees for seventeen
values and then faults with !EXC=06 -- an invalid opcode -- where the
seed-compiled binary prints 100 / -100 / 42. Those three come from `gauge`,
whose record field is `Integer between -100 and 100 clamping`.

Two things could produce that, and they need separating:

  (a) the zig plug mis-transpiled the clamping path of the x86 emitter, so
      the transpiled compiler emits different x86 than bare metal does;
  (b) the transpilation is faithful and the harness is at fault -- it is not
      opening.codex and skips proof pruning and dropped-def handling -- or
      current source differs from the frozen seed here.

This rung is the same harness on both arms. If the arms agree, the
transpilation is faithful and (b) holds; if they differ, (a) does, and the
diff names the instruction.
"""
import pathlib

from emit_harness import harness_source
from roots import CODEX

HERE = pathlib.Path(__file__).parent
SOURCE = CODEX / 'codex' / 'test' / 'plug-oracle-arith.codex'

out = harness_source('ClampHarness', 'clamp', SOURCE.read_text(), passes=True)
dest = HERE / 'ClampHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
