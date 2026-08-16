#!/usr/bin/env python3
"""Generate ScaleHarness.codex: the same back end, a real chapter as subject.

fibx compiles eighteen lines. This compiles codex/foreword/core/CCE.codex --
526 lines of the compiler's own character-encoding chapter, verbatim, with a
driver appended. No new emitter surface is expected: the point is capacity,
which is the one thing a tiny subject cannot test. Accumulators, deck sizing,
the WCET walk and the code buffer all scale with the subject, and every cap
in that path was set by subjects small enough never to reach it.

CCE is the chapter for it because it cites nothing. The subject compiler
compiles one chapter of text with no bundler in the loop, so anything with a
cite would resolve to nothing. It is also honest work rather than a
benchmark: a variant type, lookup tables, classification, encode and decode.

The driver appended below stays deliberately dull -- integers in, integers
out -- because a rung that also exercised new print paths would stop being a
capacity test and start being two experiments at once.
"""
import pathlib

from emit_harness import harness_source

HERE = pathlib.Path(__file__).parent
REPO = HERE.parent.parent
SOURCE = REPO / 'codex' / 'foreword' / 'core' / 'CCE.codex'

DRIVER = '''
Section: Scale Driver

  scale-total : Integer, Integer -> Integer
  scale-total (i) (acc) =
   if i > 4000 then acc
   else scale-total (i + 7) (acc + cce-encode-length i + cce-decode-length (int-mod i 256))

  opening : [Console] Nothing = act
    print-line-uni (show (scale-total 1 0))
    print-line-uni (show (list-length (cce-encode 5000)))
    print-line-uni (show cce-tier1-block-count)
  end
'''

chapter = SOURCE.read_text()
if 'cites' in chapter:
    raise SystemExit(f'{SOURCE} has a cite; the subject compiler resolves none')
subject = chapter.rstrip('\n') + '\n' + DRIVER

out = harness_source('ScaleHarness', 'scale', subject)
dest = HERE / 'ScaleHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes ({subject.count(chr(10))} lines of subject)')
