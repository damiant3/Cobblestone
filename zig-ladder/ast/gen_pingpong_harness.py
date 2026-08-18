#!/usr/bin/env python3
"""Generate PingpongHarness.codex: stage 2 of the fixed point.

Identical to the text harness except that its subject is stage 1's OUTPUT.
If codex-emit-text-chapter is a faithful serializer, compiling its own
product must reproduce it exactly: text1 == text2.

Original docstring follows.

Generate TextHarness.codex: the lower harness with its dump replaced by
the compiler's own codex-text emitter.

The output is the emitted codex source and nothing else, so stage 1's
output is directly usable as stage 2's input -- which is the whole point of
the milestone. Every earlier rung printed a dump I designed; this one
prints what Damian's CodexEmitter produces, which is a far more demanding
artifact than anything I would have thought to check. The dump
is deliberately structural rather than pretty -- it exists to be diffed
between the bare-metal and zig arms, so every line must be derivable from
the AChapter alone.

The parse half of the dump is kept verbatim from the parse harness. It is
not redundant: if a desugar diff appears, the first question is whether
the CST going in was already different, and having both halves in one run
answers that without a second experiment."""
import pathlib
import re

from emit_harness import DECK_PROLOGUE, RESOLVED_TABLES

REPO = pathlib.Path(__file__).resolve().parent.parent.parent
HERE = pathlib.Path(__file__).parent

# The chapter the harness parses and desugars. SUBJECT_FILE = None uses the
# built-in fib snippet, which is where the milestone started: a subject that
# isolates emitter gaps from subject breadth is worth more than coverage
# until both arms agree at all.
#
# Broaden from here the way the earlier milestones did, ending
# self-referential -- the type checker's own source is the densest
# type-level code in the compiler:
#   TypeEnv.codex               181 lines
#   Unifier.codex             1,416 lines
#   TypeCheckerInference.codex 1,810 lines
#   TypeChecker.codex         3,435 lines
#
# One chapter per run is a hard limit, not a preference: the harness embeds
# a single text literal and parse-document parses one chapter, so subjects
# cannot be concatenated.
SUBJECT_FILE = HERE / 'text.truth'
SUBJECT_LINES = 0

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
    """Escape text for a codex text literal. The lexer decodes \\n, \\\\ and
    \\" (Lexer.codex decode-escapes); \\t and \\r are rejected outright, so
    a subject carrying either could not be embedded at all."""
    for ch, name in {'\t': 'tab', '\r': 'carriage return'}.items():
        if ch in s:
            raise SystemExit(f'subject contains a {name}; CCE has no escape for it')
    non_ascii = [c for c in s if ord(c) > 127]
    if non_ascii:
        raise SystemExit(f'subject has non-ASCII chars {set(non_ascii)}; '
                         'the zig runtime panics on multibyte CCE')
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n')


if SUBJECT_FILE is None:
    raw = FIB
else:
    lines = SUBJECT_FILE.read_text().splitlines(keepends=True)
    raw = ''.join(lines if SUBJECT_LINES == 0 else lines[:SUBJECT_LINES])
SUBJECT = codex_literal(raw)

# This harness predates emit_harness.py and writes its own pipeline, because
# its bundle carries only the front end and being cheap is what earns this rung
# its place low on the ladder. It takes the two pieces of the driver's shape
# that cost no extra chapter -- see emit_harness.py for why each one is there:
#
#   DECK_PROLOGUE    names init-phase-allocator, which is what turns
#                    deck-record-intrinsic on. Without it deck-record compiles
#                    to `mov rax,rdi ; ret` and the deck discipline is off for
#                    the whole unit, which is the condition that faulted clamp.
#   RESOLVED_TABLES  cst and bound, the tables opening.codex hands lowering.
#                    `sort-bindings (cr.types)` alone omits every type the
#                    subject DECLARES, since register-type-defs puts those in
#                    the env rather than in .types.
#
# What it deliberately does not take is the RESOLVE step: rewrite-ir-defs lives
# in ResolveTypes.codex, which this bundle does not carry, so the IR dumped here
# keeps unresolved ConstructedTy annotations. fibx and whole prove that path.
out = f'''Chapter: PingpongHarness

Section: Subject
  subject-text : Text
  subject-text = "{SUBJECT}"

Section: Show Lists
  show-texts : List Text, Integer, Integer, Text -> [Console] Nothing
  show-texts (xs) (i) (len) (label) = act
    if i >= len then print-line-uni "."
    else act
      print-line-uni (label & " " & list-at xs i)
      show-texts xs (i + 1) len label
    end
  end

  show-defs : List Def, Integer, Integer -> [Console] Nothing
  show-defs (ds) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let d = list-at ds i
      in print-line-uni ("def " & token-text (d.name)
           & " params " & show (list-length (d.params))
           & " anns " & show (list-length (d.ann))
           & " L" & show ((d.name).line) & "C" & show ((d.name).column)
           & " slug " & d.chapter-slug)
      show-defs ds (i + 1) len
    end
  end

  show-adefs : List ADef, Integer, Integer -> [Console] Nothing
  show-adefs (ds) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let d = list-at ds i
      in print-line-uni ("adef " & (d.name).value
           & " params " & show (list-length (d.params))
           & " dtype " & show (list-length (d.declared-type))
           & " L" & show (((d.span).start).line) & "C" & show (((d.span).start).column)
           & " slug " & d.chapter-slug)
      show-adefs ds (i + 1) len
    end
  end

  type-kind : CodexType -> Text
  type-kind (t) =
   when t
    is IntegerTy (lo) (hi) (m) -> "int"
    is TextTy -> "text"
    is BooleanTy -> "bool"
    is CharTy -> "char"
    is ListTy (e) -> "list"
    is FunTy (p) (r) (q) -> "fn"
    is EffectfulTy (e) (s) (i) -> "eff"
    is ForAllTy (i) (b) -> "forall"
    is ForAllEff (c) (b) -> "foralleff"
    is TypeVar (i) -> "tvar"
    is SumTy (n) (a) (c) -> "sum:" & n.value
    is RecordTy (n) (a) (f) -> "rec:" & n.value
    is ConstructedTy (n) (a) -> "con:" & n.value
    is TypeCon (n) -> "tycon:" & n.value
    is otherwise -> "other"

  show-bindings : List TypeBinding, Integer, Integer -> [Console] Nothing
  show-bindings (bs) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let b = list-at bs i
      in print-line-uni ("tb " & b.name & " " & type-kind (b.bound-type))
      show-bindings bs (i + 1) len
    end
  end

  expr-kind : IRExpr -> Text
  expr-kind (e) =
   when e
    is IrIntLit (v) (s) -> "int"
    is IrNumLit (v) (s) -> "num"
    is IrTextLit (v) (s) -> "text"
    is IrBoolLit (v) (s) -> "bool"
    is IrCharLit (v) (s) -> "char"
    is IrName (n) (t) (s) -> "name:" & n
    is IrBinary (op) (l) (r) (t) (s) -> "binary"
    is IrNegate (x) (t) (s) -> "negate"
    is IrIf (c) (th) (el) (t) (s) -> "if"
    is IrLet (n) (t) (v) (b) (s) -> "let:" & n
    is IrApply (f) (a) (t) (s) -> "apply"
    is IrLambda (ps) (b) (t) (s) -> "lambda"
    is IrList (es) (t) (s) -> "list"
    is IrMatch (sc) (bs) (t) (s) -> "match"
    is IrAct (ss) (t) (s) -> "act"
    is IrRecord (n) (fs) (t) (s) -> "record:" & n
    is IrFieldAccess (r) (f) (t) (s) -> "field:" & f
    is IrFieldStore (r) (f) (v) (t) (s) -> "store:" & f
    is IrError (m) (t) (s) -> "error"
    is otherwise -> "other"

 The walk is pre-order and descends only into the arms that carry an
 IRExpr, so the printed shape is the expression tree and nothing else.
 Kinds that hold statement lists print their kind and stop: the point is
 to diff two arms against each other, and a node the walk does not enter
 is still a node both arms must agree on.

  show-ir-expr : IRExpr, Integer -> [Console] Nothing
  show-ir-expr (e) (d) = act
    print-line-uni ("e" & show d & " " & expr-kind e)
    when e
     is IrBinary (op) (l) (r) (t) (s) -> act
       show-ir-expr l (d + 1)
       show-ir-expr r (d + 1)
     end
     is IrNegate (x) (t) (s) -> show-ir-expr x (d + 1)
     is IrIf (c) (th) (el) (t) (s) -> act
       show-ir-expr c (d + 1)
       show-ir-expr th (d + 1)
       show-ir-expr el (d + 1)
     end
     is IrLet (n) (t) (v) (b) (s) -> act
       show-ir-expr v (d + 1)
       show-ir-expr b (d + 1)
     end
     is IrApply (f) (a) (t) (s) -> act
       show-ir-expr f (d + 1)
       show-ir-expr a (d + 1)
     end
     is IrLambda (ps) (b) (t) (s) -> show-ir-expr b (d + 1)
     is IrFieldAccess (r) (f) (t) (s) -> show-ir-expr r (d + 1)
     is otherwise -> print-text ""
  end

  show-irdefs : List IRDef, Integer, Integer -> [Console] Nothing
  show-irdefs (ds) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let d = list-at ds i
      in act
        print-line-uni ("irdef " & d.name
          & " params " & show (list-length (d.params))
          & " slug " & d.chapter-slug
          & " punctual " & (if d.is-punctual then "1" else "0")
          & " uparams " & show (list-length (d.unique-params))
          & " ty " & type-kind (d.type-val))
        show-ir-expr (d.body) 0
      end
      show-irdefs ds (i + 1) len
    end
  end

Section: Driver
  opening : [Console] Nothing = act
    {DECK_PROLOGUE}let toks = tokenize subject-text 1
    in let doc = parse-document (make-parse-state (toks.tokens) subject-text) 0
    in let dr = desugar-document subject-text doc (doc.chapter-title) 0
    in let ch0 = dr.dr-chapter
    in let ch = scope-achapter ch0 skip-list-text-empty [] 0
    in let rr = resolve-chapter ch skip-list-text-empty [] 0
    in let cr = check-chapter ch [] skip-list-text-empty [] 0
    {RESOLVED_TABLES}
    in let ir = lower-chapter ch bound cst (rr.ctor-names) [] skip-list-text-empty [] 0
    in let tm = IRTextMeta {{
     chapter-title = ch.chapter-title,
     prose = ch.prose,
     section-titles = ch.section-titles,
     ctor-names = rr.ctor-names,
     prose-blocks = ch.prose-blocks,
     annotations = ch.annotations,
     ground-effects = ch.ground-effects
    }}
    in act
      print-line-uni (codex-emit-text-chapter ir tm (ch.type-defs) [])
    end
  end
'''

dest = HERE / 'PingpongHarness.codex'
dest.write_text(out)
what = 'fib' if SUBJECT_FILE is None else f'{SUBJECT_FILE.name} first {SUBJECT_LINES or "all"} lines'
print(f'{dest}: {len(out)} bytes, subject = {what} ({len(raw)} raw bytes)')

# The type checker reads bs-type off every builtin, so unlike the scope
# stub this cannot be names alone. Rather than rebuild the table -- which
# lost the 22 helper definitions the chapter also carries, and cost a
# truth-arm run to discover -- this TRANSFORMS the real chapter: every line
# verbatim except the bs-emit field, which is typed over CodegenState and
# EmitResult and is the only reason Types/Builtins.codex cannot simply be
# bundled. Nothing this side of the emitter reads it.
BUILTINS = REPO / 'codex/compiler/Types/Builtins.codex'
out_lines, stripped = [], 0
for line in BUILTINS.read_text().splitlines():
    if line.strip().startswith('bs-emit :'):
        # drop the field from the record, and the comma the line before it
        while out_lines and out_lines[-1].rstrip().endswith(','):
            out_lines[-1] = out_lines[-1].rstrip()[:-1]
            break
        stripped += 1
        continue
    if ', bs-emit = ' in line:
        head, _, tail = line.partition(', bs-emit = ')
        line = head + (' }' + tail[tail.rindex('}') + 1:] if '}' in tail else ' }')
        stripped += 1
    out_lines.append(line)
assert stripped > 200, f'expected to strip the whole emit column, stripped {stripped}'
text = "\n".join(out_lines) + "\n"
text = text.replace('Chapter: Builtins', 'Chapter: Builtins', 1)
assert 'bs-emit' not in text, 'a bs-emit reference survived'
(HERE / 'PingpongStubs.codex').write_text(text)
print(f'{HERE / "PingpongStubs.codex"}: real chapter minus {stripped} bs-emit references')
