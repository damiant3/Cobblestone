#!/usr/bin/env python3
"""Generate FibxHarness.codex: the fib ladder through the whole x86-64 back
end, ending in a CDX binary. The harness body is emit_harness.py's; the only
thing this rung owns is the eighteen lines it compiles.

fib is chosen for what it does NOT need. Integers are machine words, so its
arithmetic is a bare `add`; its two self-calls are its only fixup surface;
and it touches no rodata, no runtime helper and no absolute address. That is
what lets f3_run.zig carve it out of the emitted buffer and call it. double
rides along as a five-byte frameless leaf.

LowerStubs is NOT bundled for this milestone -- the real Types/Builtins
rides along because the whole x86-64 code generator does, so bs-emit's
referents finally exist. gen_lower_harness.py is still invoked for its
LowerHarness (the fib rung's sibling), not for the stub."""
import pathlib

from emit_harness import harness_source

HERE = pathlib.Path(__file__).parent

FIB = (
    'Chapter: Fib\n'
    '\n'
    'Section: Math\n'
    '  fib : Integer -> Integer\n'
    '  fib (n) =\n'
    '   if n <= 1 then n\n'
    '   else fib (n - 1) + fib (n - 2)\n'
    '\n'
    '  double : Integer -> Integer\n'
    '  double (n) = n + n\n'
    '\n'
    'Section: Main\n'
    '  opening : [Console] Nothing = act\n'
    '   print-line-uni (show (fib 20))\n'
    '  end\n'
)

out = harness_source('FibxHarness', 'fibx', FIB)
dest = HERE / 'FibxHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
