#!/usr/bin/env python3
"""Generate LexHarness.codex: an opening that tokenizes an embedded codex
snippet and dumps one line per token. show-kind's arms are derived
mechanically from Token.codex so the harness cannot drift from the
TokenKind variant."""
import re, sys, pathlib

REPO = pathlib.Path(__file__).resolve().parent.parent.parent
HERE = pathlib.Path(__file__).parent

token_src = (REPO / 'codex/compiler/Syntax/Token.codex').read_text()
# TokenKind is the first variant block in the file
block = token_src.split('TokenKind =', 1)[1]
kinds = []
for line in block.splitlines():
    m = re.match(r'\s+\| ([A-Za-z]+)\s*$', line)
    if m:
        kinds.append(m.group(1))
    elif kinds and not line.strip().startswith('|') and line.strip():
        break
assert len(kinds) > 80, f'expected ~87 kinds, got {len(kinds)}'

# The text the harness lexes. SUBJECT_FILE = None uses the built-in fib
# snippet; a path lexes real codex source instead, which is the point of the
# exercise -- fib exercises a dozen token kinds, the compiler's own source
# exercises strings, escapes, char literals, prose and the operator table.
# SUBJECT_LINES caps how much of that file is embedded (0 = the whole thing);
# bare-metal lexing is the slow arm, so raise it on measured evidence.
SUBJECT_FILE = REPO / 'codex/compiler/Syntax/Lexer.codex'
SUBJECT_LINES = 0

FIB = (
    'Chapter: Fib\n'
    '\n'
    '  fib : Integer -> Integer\n'
    '  fib (n) =\n'
    '   if n <= 1 then n\n'
    '   else fib (n - 1) + fib (n - 2)\n'
    '\n'
    '  opening : [Console] Nothing = act\n'
    '   print-line-uni (show (fib 20))\n'
    '  end\n'
)

def codex_literal(s):
    """Escape text for a codex text literal. The lexer decodes \\n, \\\\ and
    \\" (Lexer.codex decode-escapes); \\t and \\r are rejected outright, so
    a subject carrying either could not be embedded at all."""
    bad = {'\t': 'tab', '\r': 'carriage return'}
    for ch, name in bad.items():
        if ch in s:
            raise SystemExit(f'subject contains a {name}; CCE has no escape for it')
    non_ascii = [c for c in s if ord(c) > 127]
    if non_ascii:
        raise SystemExit(f'subject has non-ASCII chars {set(non_ascii)}; '
                         'the zig runtime panics on multibyte CCE')
    return (s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n'))

if SUBJECT_FILE is None:
    raw = FIB
else:
    lines = SUBJECT_FILE.read_text().splitlines(keepends=True)
    raw = ''.join(lines if SUBJECT_LINES == 0 else lines[:SUBJECT_LINES])
SUBJECT = codex_literal(raw)

arms = '\n'.join(f'    is {k} -> "{k}"' for k in kinds)

out = f'''Chapter: LexHarness

Section: Subject
  subject-text : Text
  subject-text = "{SUBJECT}"

Section: Show
  show-kind : TokenKind -> Text
  show-kind (k) =
   when k
{arms}

  show-token : Token -> Text
  show-token (t) =
   let base = show-kind (t.kind) & " " & show (t.offset) & "+" & show (t.length) & " L" & show (t.line) & "C" & show (t.column)
   in when t.kind
    is Newline -> base
    is Indent -> base
    is Dedent -> base
    is EndOfFile -> base
    is otherwise -> base & " |" & token-text t & "|"

Section: Driver
  print-tokens : List Token, Integer, Integer -> [Console] Nothing
  print-tokens (ts) (i) (len) = act
    if i >= len then print-line-uni "---"
    else act
      print-line-uni (show-token (list-at ts i))
      print-tokens ts (i + 1) len
    end
  end

  opening : [Console] Nothing = act
    let result = tokenize subject-text 1
    in act
      print-line-uni ("tokens " & show (list-length (result.tokens)))
      print-tokens (result.tokens) 0 (list-length (result.tokens))
      print-line-uni ("errors " & show (list-length (result.errors)))
    end
  end
'''

dest = HERE / 'LexHarness.codex'
dest.write_text(out)
what = 'fib' if SUBJECT_FILE is None else f'{SUBJECT_FILE.name} first {SUBJECT_LINES or "all"} lines'
print(f'{dest}: {len(kinds)} kinds, {len(out)} bytes, subject = {what} ({len(raw)} raw bytes)')
