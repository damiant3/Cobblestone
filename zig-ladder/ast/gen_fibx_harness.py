#!/usr/bin/env python3
"""Generate FibxHarness.codex: the fib ladder through the whole x86-64 back
end, ending in a CDX binary.

The driver calls x86-64-emit-cdx -- the compiler's own entry point -- and
prints what it returns: the symbol map, then header-bytes, the content
buffer and tail-bytes, 32 to a line. Earlier this harness carried a copy of
x86-64-emit-cdx-with-exit-mode's body stopped just before finalize, because
finalize's serialization belonged to a later rung. This is that rung, and
the copy is gone: a copied driver is a driver that can drift.

Restoring finalize also patches the code in place, so the offsets no longer
need a call table beside them -- the symbol map (which is what the real
compiler writes to the .cdx.map) is enough to find a function and call it.

LowerStubs is NOT bundled for this milestone -- the real Types/Builtins
rides along because the whole x86-64 code generator does, so bs-emit's
referents finally exist. gen_lower_harness.py is still invoked for its
LowerHarness (the fib rung's sibling), not for the stub."""
import pathlib

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


def codex_literal(s):
    for ch, name in {'\t': 'tab', '\r': 'carriage return'}.items():
        if ch in s:
            raise SystemExit(f'subject contains a {name}; CCE has no escape for it')
    if any(ord(c) > 127 for c in s):
        raise SystemExit('subject has non-ASCII chars; the zig runtime panics on multibyte CCE')
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n')


SUBJECT = codex_literal(FIB)

out = f'''Chapter: FibxHarness

Section: Subject
  subject-text : Text
  subject-text = "{SUBJECT}"

Section: Byte Dump

  fibx-line : Integer, Integer, Integer, List Text -> Text
  fibx-line (buf) (i) (hi) (acc) =
   if i >= hi then text-concat-list acc
   else fibx-line buf (i + 1) hi (list-push acc (integer-to-text (peek-byte buf i) & " "))

  fibx-print-bytes : Integer, Integer, Integer -> [Console] Nothing
  fibx-print-bytes (buf) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      print-line-uni (fibx-line buf i (if i + 32 < len then i + 32 else len) [])
      fibx-print-bytes buf (i + 32) len
    end
  end

 header-bytes and tail-bytes arrive as lists rather than in the workspace,
 so they need their own walker; splitting on the source of the bytes rather
 than on what they mean keeps both walkers dumb.

  fibx-list-line : List Integer, Integer, Integer, List Text -> Text
  fibx-list-line (bs) (i) (hi) (acc) =
   if i >= hi then text-concat-list acc
   else fibx-list-line bs (i + 1) hi (list-push acc (integer-to-text (list-at bs i) & " "))

  fibx-print-list : List Integer, Integer -> [Console] Nothing
  fibx-print-list (bs) (i) = act
    if i >= list-length bs then print-line-uni "."
    else act
      print-line-uni (fibx-list-line bs i (if i + 32 < list-length bs then i + 32 else list-length bs) [])
      fibx-print-list bs (i + 32)
    end
  end

 The symbol map is what the real compiler writes beside a .cdx, and it is
 the only thing that says which bytes are which. It goes through the oracle
 too: a plug that emitted the code correctly but scrambled the map would
 still be wrong.

  fibx-print-lines : List Text, Integer -> [Console] Nothing
  fibx-print-lines (ls) (i) = act
    if i >= list-length ls then print-line-uni "."
    else act
      print-line-uni (list-at ls i)
      fibx-print-lines ls (i + 1)
    end
  end

Section: Driver

  opening : [Console] Nothing = act
    let toks = tokenize subject-text 1
    in let doc = parse-document (make-parse-state (toks.tokens) subject-text) 0
    in let dr = desugar-document subject-text doc (doc.chapter-title) 0
    in let ch0 = dr.dr-chapter
    in let ch = scope-achapter ch0 skip-list-text-empty [] 0
    in let rr = resolve-chapter ch skip-list-text-empty [] 0
    in let cr = check-chapter ch [] skip-list-text-empty [] 0
    in let cst = cr.state
    in let sorted = sort-bindings (cr.types)
    in let ir = lower-chapter ch sorted cst (rr.ctor-names) [] skip-list-text-empty [] 0
    in let res = x86-64-emit-cdx ir sorted
    in act
      print-line-uni ("check-errors " & show ((cst.bag).error-count))
      print-line-uni ("ir-defs " & show (list-length (ir.defs)))
      print-line-uni ("emit-errors " & show ((res.bag).error-count))
      print-line-uni ("header-len " & show (list-length (res.header-bytes)))
      print-line-uni ("content-len " & show (res.content-len))
      print-line-uni ("tail-len " & show (list-length (res.tail-bytes)))
      print-line-uni "--- symbols ---"
      fibx-print-lines (res.symbol-map) 0
      print-line-uni "--- header ---"
      fibx-print-list (res.header-bytes) 0
      print-line-uni "--- content ---"
      fibx-print-bytes (res.content-buf) 0 (res.content-len)
      print-line-uni "--- tail ---"
      fibx-print-list (res.tail-bytes) 0
    end
  end
'''

dest = HERE / 'FibxHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
