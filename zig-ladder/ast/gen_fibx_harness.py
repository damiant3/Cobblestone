#!/usr/bin/env python3
"""Generate FibxHarness.codex: F2 of the fib ladder -- the chain continues
through the TREE emitter and the output is fib's machine code. The state
preparation is x86-64-emit-cdx-with-exit-mode's own sequence, copied
verbatim with its default arguments and stopped just before finalize, so
the harness makes no emission decisions of its own; it prints code-len
and the workspace bytes, 32 to a line, and the byte stream is the oracle
output.

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

Section: Emission

 The body below is x86-64-emit-cdx-with-exit-mode line for line, with the
 x86-64-emit-cdx defaults substituted and finalize dropped: the oracle
 wants the emitted bytes, and finalize's serialization belongs to a later
 rung. A copied driver cannot drift into choices of its own.

  fibx-emit : IRChapter, List TypeBinding -> CodegenState
  fibx-emit (m) (tdefs) =
   let emit-deck = emit-build (list-length (m.defs) * 65536 + 25165824)
   in let de = __deck-enter
   in let tramp = bare-metal-trampoline
   in let st0 = x86-64-init-codegen-sorted tdefs
   in let st-mode = __record-set st0 "exit-mode" Exit
   in let st-poison = __record-set st-mode "poison-alloc" False
   in let st-trace = __record-set st-poison "trace-alloc" False
   in let st-wd = __record-set st-trace "watchdog-mode" WatchdogProgress
   in let st-eo2 = __record-set st-wd "effect-op-addrs" (assign-effect-op-addrs (m.effect-op-names) 0 (list-length (m.effect-op-names)) [])
   in let defs-lifted = m.defs
   in let ret-ty = opening-bare-print-type defs-lifted
   in let st-ret = __record-set st-eo2 "opening-ret-type" ret-ty
   in let arities = build-x86-arities defs-lifted 0 (list-length defs-lifted) []
   in let st-ar = __record-set st-ret "user-arities" arities
   in let pa-slug = def-chapter-slug defs-lifted "init-phase-allocator" 0 (list-length defs-lifted)
   in let dr-slug = def-chapter-slug defs-lifted "deck-record" 0 (list-length defs-lifted)
   in let st-dri = __record-set st-ar "deck-record-intrinsic" (pa-slug /= "" & pa-slug == dr-slug)
   in let st-wc = emit-wcet-unchecked st-dri [] 0
   in let st0a = emit-runtime-helpers st-wc
   in let dx = __deck-exit
   in emit-all-defs st0a defs-lifted 0

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
    in let res = fibx-emit ir sorted
    in act
      print-line-uni ("check-errors " & show ((cst.bag).error-count))
      print-line-uni ("ir-defs " & show (list-length (ir.defs)))
      print-line-uni ("fo-names " & show (list-length (res.fo-names)))
      print-line-uni ("code-len " & show (res.code-len))
      print-line-uni "--- code ---"
      fibx-print-bytes (res.workspace.code-buffer) 0 (res.code-len)
    end
  end
'''

dest = HERE / 'FibxHarness.codex'
dest.write_text(out)
print(f'{dest}: {len(out)} bytes')
