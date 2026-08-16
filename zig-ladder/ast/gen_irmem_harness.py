#!/usr/bin/env python3
"""Generate IrmemHarness.codex: the IRTextParser memory probe. The plug
guest spends roughly 250 bytes of heap per byte of IR it ingests, and
this harness decomposes that number: it embeds a real rung IR (decoded
CCE to source text), runs each parser stage, and prints __heap-save
deltas -- exact bump-allocator truth, not RSS guesswork. One bare-metal
compile and run per iteration; a diet change re-banks new numbers and
the ten-rung sweep stays the correctness gate.

The sample is the smallest banked .ir whose decoded text is pure ASCII -- the loop
wants speed, and ratios do not need megabytes
(a text literal cannot carry multibyte CCE); which one won is printed
and embedded in the output header line."""
import pathlib
import sys

HERE = pathlib.Path(__file__).parent
sys.path.insert(0, str(HERE.parent))
from cce import decode

CANDIDATES = ['lex.ir', 'desugar.ir', 'scope.ir', 'check.ir']

sample_name, sample = None, None
for name in CANDIDATES:
    p = HERE / name
    if not p.exists():
        continue
    raw = p.read_bytes()
    if any(b >= 97 for b in raw):
        continue
    txt = decode(raw)
    if any(ord(c) > 127 for c in txt) or '\t' in txt or '\r' in txt:
        continue
    sample_name, sample = name, txt
    break
if sample is None:
    raise SystemExit('no banked .ir decodes to clean ASCII; run a truth arm first')

escaped = sample.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n')

out = f'''Chapter: IrmemHarness

Section: Sample

 The sample is {sample_name}, decoded from its banked CCE bytes at
 generation time. The parser sees byte for byte what the plug sees on
 the wire, minus the transport.

  sample-ir : Text
  sample-ir = "{escaped}"

Section: Diagnostics

  sx-kind : SExp -> Text
  sx-kind (e) =
   when e
    is SAtom (t) -> "atom:" & t
    is SStr (t) -> "str:" & t
    is SList (xs) -> "list/" & show (list-length xs)

  sx-head : SExp -> Text
  sx-head (e) =
   when e
    is SList (xs) ->
     if list-length xs == 0 then "list/0"
     else sx-kind (list-at xs 0)
    is otherwise -> sx-kind e

Section: Driver

  opening : [Console] Nothing = act
    let h0 = __heap-save
    in let toks = tokenize sample-ir
    in let h1 = __heap-save
    in let tree = build-tree toks
    in let h2 = __heap-save
    in act
      print-line-uni ("sample {sample_name} bytes " & show (text-length sample-ir))
      print-line-uni ("tokens " & show (list-length toks))
      print-line-uni ("heap-tokenize " & show (h1 - h0))
      print-line-uni ("heap-tree " & show (h2 - h1))
      print-line-uni ("tree " & sx-kind tree)
      print-line-uni ("tree-head " & sx-head tree)
      irmem-walk-stage sample-ir h2
    end
  end

  irmem-walk-stage : Text, Integer -> [Console] Nothing
  irmem-walk-stage (sample-ir) (h2) = act
    let parsed = parse-ir-chapter sample-ir
    in let h3 = __heap-save
    in act
      print-line-uni ("heap-full-parse " & show (h3 - h2))
      print-line-uni ("defs " & show (list-length ((parsed.chapter).defs)))
      print-line-uni ("type-defs " & show (list-length (parsed.type-defs)))
    end
  end
'''

dest = HERE / 'IrmemHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes, sample = {sample_name} ({len(sample)} chars)')
