#!/usr/bin/env python3
"""Generate ParseHarness.codex: an opening that parses an embedded codex
chapter and dumps the resulting Document. The dump is deliberately
structural rather than pretty -- it exists to be diffed between the
bare-metal and zig arms, so every line must be derivable from the CST
alone."""
import pathlib
from roots import CODEX

REPO = CODEX
HERE = pathlib.Path(__file__).parent

# The chapter the harness parses. SUBJECT_FILE = None uses the built-in fib
# snippet, which is what got both arms agreeing at all -- it isolated emitter
# gaps from parser breadth while the emitter still had gaps. Both arms now
# agree, so the subject is the parser's own source, the same way the lex
# harness lexes the lexer.
#
# ParserCore is 576 lines against the lexer's 578, so this is the like-for-
# like step. Parser.codex is 2,059 and is the one after it; raise this on
# evidence rather than optimism, because the truth arm runs on bare metal
# with no GC.
SUBJECT_FILE = REPO / 'codex/compiler/Syntax/Parser.codex'
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

out = f'''Chapter: ParseHarness

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

  show-type-defs : List TypeDef, Integer, Integer -> [Console] Nothing
  show-type-defs (ts) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let t = list-at ts i
      in print-line-uni ("type-def " & token-text (t.name))
      show-type-defs ts (i + 1) len
    end
  end

  show-diags : List Diagnostic, Integer, Integer -> [Console] Nothing
  show-diags (gs) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      let g = list-at gs i
      in print-line-uni ("diag " & show (g.code) & " sev " & show (g.severity)
           & " |" & g.message & "|")
      show-diags gs (i + 1) len
    end
  end

Section: Driver
  opening : [Console] Nothing = act
    let toks = tokenize subject-text 1
    in let doc = parse-document (make-parse-state (toks.tokens) subject-text) 0
    in act
      print-line-uni ("lex-tokens " & show (list-length (toks.tokens)))
      print-line-uni ("lex-errors " & show (list-length (toks.errors)))
      print-line-uni ("chapter |" & doc.chapter-title & "|")
      print-line-uni ("prose-len " & show (text-length (doc.prose)))
      print-line-uni ("defs " & show (list-length (doc.defs)))
      show-defs (doc.defs) 0 (list-length (doc.defs))
      print-line-uni ("type-defs " & show (list-length (doc.type-defs)))
      show-type-defs (doc.type-defs) 0 (list-length (doc.type-defs))
      print-line-uni ("effect-defs " & show (list-length (doc.effect-defs)))
      print-line-uni ("class-defs " & show (list-length (doc.class-defs)))
      print-line-uni ("instance-defs " & show (list-length (doc.instance-defs)))
      print-line-uni ("citations " & show (list-length (doc.citations)))
      print-line-uni ("quotations " & show (list-length (doc.quotations)))
      print-line-uni ("ground-effects " & show (list-length (doc.ground-effects)))
      print-line-uni ("sections " & show (list-length (doc.section-titles)))
      show-texts (doc.section-titles) 0 (list-length (doc.section-titles)) "section"
      print-line-uni ("prose-blocks " & show (list-length (doc.prose-blocks)))
      print-line-uni ("annotations " & show (list-length (doc.annotations)))
      print-line-uni ("parse-errors " & show ((doc.parse-bag).error-count))
      show-diags ((doc.parse-bag).diagnostics) 0 (list-length ((doc.parse-bag).diagnostics))
      print-line-uni "---"
    end
  end
'''

dest = HERE / 'ParseHarness.codex'
dest.write_text(out)
what = 'fib' if SUBJECT_FILE is None else f'{SUBJECT_FILE.name} first {SUBJECT_LINES or "all"} lines'
print(f'{dest}: {len(out)} bytes, subject = {what} ({len(raw)} raw bytes)')
