#!/usr/bin/env python3
"""Generate IrmemHarness.codex: the IRTextParser memory probe. The plug
guest spends roughly 250 bytes of heap per byte of IR it ingests, and
this harness decomposes that number: it embeds a real rung IR (decoded
CCE to source text), runs each parser stage and each transport-shaped
stage (frame accumulation, payload slicing, flat-buffer reassembly,
bytes-to-text), and prints __heap-save deltas -- exact bump-allocator
truth, not RSS guesswork. One bare-metal
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

Section: Transport Stages

 The receive path, one measured stage per shape it takes on the way in.
 A frame carries 1460 payload bytes (the negotiated MSS); each simulated
 frame is accumulated byte by byte the way ne2k-read-from-buf reads the
 NIC buffer, then sliced three more times the way eth-payload,
 ip-payload, and tcp-parse each materialize the payload, then written
 into the flat reassembly buffer the way transport-feed-raw does.
 Nothing here is restored between frames, because the real receive loop
 restores only on the empty-poll branch.

  irmem-text-bytes : Text, Integer, Integer, List Integer -> List Integer
  irmem-text-bytes (s) (i) (len) (acc) =
   if i >= len then acc
   else irmem-text-bytes s (i + 1) len (list-push acc (char-code (char-at s i)))

  irmem-slice : List Integer, Integer, Integer, List Integer -> List Integer
  irmem-slice (xs) (i) (stop) (acc) =
   if i >= stop then acc
   else irmem-slice xs (i + 1) stop (list-push acc (list-at xs i))

  irmem-frame-sim : List Integer, Integer, Integer, Integer -> Integer
  irmem-frame-sim (bs) (off) (n) (base) =
   if off >= n then 0
   else let stop = if off + 1460 > n then n else off + 1460
   in let frame = irmem-slice bs off stop []
   in let clen = list-length frame
   in let s1 = irmem-slice frame 0 clen []
   in let s2 = irmem-slice s1 0 clen []
   in let s3 = irmem-slice s2 0 clen []
   in let w = __buf-write-bytes base off s3
   in irmem-frame-sim bs stop n base

  irmem-transport-stage : Text -> [Console] Nothing
  irmem-transport-stage (sample) = act
    let n = text-length sample
    in let h0 = __heap-save
    in let bs = irmem-text-bytes sample 0 n []
    in let h1 = __heap-save
    in let base = __heap-save
    in let adv = __heap-advance n
    in let h2 = __heap-save
    in let fs = irmem-frame-sim bs 0 n base
    in let h3 = __heap-save
    in let body = __buf-read-bytes base 0 n
    in let h4 = __heap-save
    in let txt = bytes-to-text body 0 n
    in let h5 = __heap-save
    in act
      print-line-uni ("heap-bytes-build " & show (h1 - h0))
      print-line-uni ("heap-frame-sim " & show (h3 - h2))
      print-line-uni ("heap-buf-read " & show (h4 - h3))
      print-line-uni ("heap-bytes-to-text " & show (h5 - h4))
      print-line-uni ("transport-text-len " & show (text-length txt))
    end
  end

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
      irmem-transport-stage sample-ir
    end
  end
'''

dest = HERE / 'IrmemHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes, sample = {sample_name} ({len(sample)} chars)')
