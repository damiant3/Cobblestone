#!/usr/bin/env python3
"""Generate DesugarHarness.codex: an opening that parses an embedded codex
chapter, desugars the resulting Document, and dumps the AChapter. The dump
is deliberately structural rather than pretty -- it exists to be diffed
between the bare-metal and zig arms, so every line must be derivable from
the AChapter alone.

The parse half of the dump is kept verbatim from the parse harness. It is
not redundant: if a desugar diff appears, the first question is whether
the CST going in was already different, and having both halves in one run
answers that without a second experiment."""
import pathlib
from roots import CODEX

REPO = CODEX
HERE = pathlib.Path(__file__).parent

# The chapter the harness parses and desugars. SUBJECT_FILE = None uses the
# built-in fib snippet, which is where the milestone started: a subject that
# isolates emitter gaps from subject breadth is worth more than coverage
# until both arms agree at all.
#
# Subjects cleared so far, each on the first attempt once fib was green:
#   ParserCore.codex     576 lines    3,306 tokens
#   Parser.codex       2,059 lines   18,812 tokens
#   Desugarer.codex    1,474 lines   16,389 tokens   (desugar on its own source)
#   AstNodes.codex       510 lines    4,867 tokens   (declaration-dense)
#
# One chapter per run is a hard limit, not a preference: the harness embeds
# a single text literal and parse-document parses one chapter, so subjects
# cannot be concatenated.
SUBJECT_FILE = REPO / 'codex/compiler/Ast/AstNodes.codex'
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

out = f'''Chapter: DesugarHarness

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
    in let ch = dr.dr-chapter
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
      print-line-uni "---"
    end
  end
'''

dest = HERE / 'DesugarHarness.codex'
dest.write_text(out)
what = 'fib' if SUBJECT_FILE is None else f'{SUBJECT_FILE.name} first {SUBJECT_LINES or "all"} lines'
print(f'{dest}: {len(out)} bytes, subject = {what} ({len(raw)} raw bytes)')
