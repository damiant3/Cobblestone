#!/usr/bin/env python3
"""Generate WholeHarness.codex: the whole compiler, minus its driver.

This is the dump-harness form of the DDC witness. The C# arm pushes the whole
compiler through its plug and stops at "the emitted C# compiles"; this pushes
the whole compiler through the zig plug and compares the binary it emits,
byte for byte, against bare metal's.

It stands in for codex/compiler/opening.codex, which cannot be bundled
alongside it -- two chapters cannot both define `opening`. Standing in for the
driver is also what decides the unit's contents, because IR emission prunes to
what the opening reaches: the harness calls run-ir-pipeline exactly where
compile-frontend-passes does, and that call is the only reason Simplify,
Occurrence, LambdaLifting, Passes and IRCheck end up in the IR at all.

The subject is chosen to make the middle end DO something. Transpiling the
pass chapters and running them are two different claims, and fib only tested
the first: with fib the pipeline is a no-op -- double is dead, fib is
recursive so nothing inlines, nothing folds -- so whole.truth came out
byte-identical to fibx.truth and the passes could have been broken without
the oracle noticing. This subject gives each of default-ir-pipeline's three
passes something to bite on:

  fold-constants        folded, and the 7 * 6 in the driver
  inline-leaf-calls     double is a leaf and is actually called
  inline-single-caller  scale-by-four has exactly one call site

fib stays for the recursion, which nothing inlines and which keeps a real
call in the emitted code. Everything is integer arithmetic with one printed
answer, so the subject stays small: the work under test is the compiler, not
the program it compiles -- that is what the scale rung is for.
"""
import pathlib

from emit_harness import harness_source

HERE = pathlib.Path(__file__).parent

SUBJECT = (
    'Chapter: Mid\n'
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
    '  scale-by-four : Integer -> Integer\n'
    '  scale-by-four (n) = double (double n)\n'
    '\n'
    '  folded : Integer = 2 + 3 * 4\n'
    '\n'
    'Section: Main\n'
    '  opening : [Console] Nothing = act\n'
    '   print-line-uni (show (scale-by-four (fib 10) + folded + 7 * 6))\n'
    '  end\n'
)

out = harness_source('WholeHarness', 'whole', SUBJECT, passes=True)
dest = HERE / 'WholeHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
