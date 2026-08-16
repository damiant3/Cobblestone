#!/usr/bin/env python3
"""Generate ScopeHarness.codex: an opening that parses an embedded codex
chapter, desugars it, scopes and resolves it, and dumps the result. The dump
is deliberately structural rather than pretty -- it exists to be diffed
between the bare-metal and zig arms, so every line must be derivable from
the AChapter alone.

The parse half of the dump is kept verbatim from the parse harness. It is
not redundant: if a desugar diff appears, the first question is whether
the CST going in was already different, and having both halves in one run
answers that without a second experiment."""
import pathlib
import re

REPO = pathlib.Path(__file__).resolve().parent.parent.parent
HERE = pathlib.Path(__file__).parent

# The chapter the harness parses and desugars. SUBJECT_FILE = REPO / 'codex/compiler/Semantics/NameResolver.codex' uses the
# built-in fib snippet, which is where the milestone started: a subject that
# isolates emitter gaps from subject breadth is worth more than coverage
# until both arms agree at all.
#
# Broaden from here the way desugar did, ending self-referential:
#   ChapterScoper.codex  616 lines
#   NameResolver.codex   485 lines
#
# One chapter per run is a hard limit, not a preference: the harness embeds
# a single text literal and parse-document parses one chapter, so subjects
# cannot be concatenated.
SUBJECT_FILE = REPO / 'codex/compiler/Semantics/NameResolver.codex'
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

out = f'''Chapter: ScopeHarness

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

Section: Driver
  opening : [Console] Nothing = act
    let toks = tokenize subject-text 1
    in let doc = parse-document (make-parse-state (toks.tokens) subject-text) 0
    in let dr = desugar-document subject-text doc (doc.chapter-title) 0
    in let ch0 = dr.dr-chapter
    in let ch = scope-achapter ch0 skip-list-text-empty [] 0
    in let rr = resolve-chapter ch skip-list-text-empty [] 0
    in act
      print-line-uni ("lex-tokens " & show (list-length (toks.tokens)))
      print-line-uni ("lex-errors " & show (list-length (toks.errors)))
      print-line-uni ("chapter |" & doc.chapter-title & "|")
      print-line-uni ("defs " & show (list-length (doc.defs)))
      show-defs (doc.defs) 0 (list-length (doc.defs))
      print-line-uni ("parse-errors " & show ((doc.parse-bag).error-count))
      print-line-uni "--- desugar ---"
      print-line-uni ("dr-sat " & show (dr.dr-sat))
      print-line-uni ("a-name |" & (ch.name).value & "|")
      print-line-uni ("a-chapter-title |" & ch.chapter-title & "|")
      print-line-uni ("a-prose-len " & show (text-length (ch.prose)))
      print-line-uni ("a-defs " & show (list-length (ch.defs)))
      show-adefs (ch.defs) 0 (list-length (ch.defs))
      print-line-uni ("a-type-defs " & show (list-length (ch.type-defs)))
      print-line-uni ("a-effect-defs " & show (list-length (ch.effect-defs)))
      print-line-uni ("a-class-defs " & show (list-length (ch.class-defs)))
      print-line-uni ("a-instance-defs " & show (list-length (ch.instance-defs)))
      print-line-uni ("a-citations " & show (list-length (ch.citations)))
      print-line-uni ("a-ground-effects " & show (list-length (ch.ground-effects)))
      print-line-uni ("a-prose-blocks " & show (list-length (ch.prose-blocks)))
      print-line-uni ("a-annotations " & show (list-length (ch.annotations)))
      print-line-uni ("a-sections " & show (list-length (ch.section-titles)))
      show-texts (ch.section-titles) 0 (list-length (ch.section-titles)) "a-section"
      print-line-uni ("a-rt-names " & show (list-length (ch.rt-names)))
      print-line-uni ("a-conversions " & show (list-length (ch.conversions)))
      print-line-uni "--- scope ---"
      print-line-uni ("resolve-errors " & show ((rr.bag).error-count))
      print-line-uni ("top-level-names " & show (skip-list-text-size (rr.top-level-names)))
      print-line-uni ("type-names " & show (skip-list-text-size (rr.type-names)))
      print-line-uni ("ctor-names " & show (list-length (rr.ctor-names)))
      show-texts (rr.ctor-names) 0 (list-length (rr.ctor-names)) "ctor"
      show-texts (skip-list-text-to-list (rr.top-level-names)) 0 (skip-list-text-size (rr.top-level-names)) "top"
      print-line-uni "---"
    end
  end
'''

dest = HERE / 'ScopeHarness.codex'
dest.write_text(out)
what = 'fib' if SUBJECT_FILE is None else f'{SUBJECT_FILE.name} first {SUBJECT_LINES or "all"} lines'
print(f'{dest}: {len(out)} bytes, subject = {what} ({len(raw)} raw bytes)')

# NameResolver needs the list of builtin NAMES so it does not report every
# builtin as an undefined name. It gets them via `builtins`, whose real
# table (Types/Builtins.codex) carries a bs-emit field typed over
# CodegenState and EmitResult -- so bundling it would drag in the whole
# x86-64 code generator, 104 emit functions deep, to read 260 strings.
#
# So the stub declares a BuiltinSpec with only the field the resolver
# reads, and the names are extracted from the real table here rather than
# retyped, which is what keeps them from drifting.
BUILTINS = REPO / 'codex/compiler/Types/Builtins.codex'
names = re.findall(r'bs-name = "([^"]*)"', BUILTINS.read_text())
assert len(names) > 200, f'expected the full builtin table, found {len(names)}'
specs = ",\n   ".join('BuiltinSpec { bs-name = "%s" }' % n for n in names)
stub = f"""Chapter: ScopeStubs

 The builtin name table, reduced to the one field the name resolver reads.
 Generated from Types/Builtins.codex by gen_scope_harness.py -- {len(names)}
 names -- so it cannot drift from the real table. The real BuiltinSpec also
 carries bs-type and bs-emit; bs-emit is typed over CodegenState and
 EmitResult and would pull the whole code generator in behind it.

Section: Builtin Names

  BuiltinSpec = record {{
   bs-name : Text
  }}

  builtins : List BuiltinSpec
  builtins =
   [{specs}]

  builtin-names-from : List BuiltinSpec, Integer, Integer, List Text -> List Text
  builtin-names-from (bs) (i) (len) (acc) =
   if i >= len then acc
   else builtin-names-from bs (i + 1) len (list-push acc ((list-at bs i).bs-name))
"""
(HERE / 'ScopeStubs.codex').write_text(stub)
print(f'{HERE / "ScopeStubs.codex"}: {len(names)} builtin names')
