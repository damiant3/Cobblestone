#!/usr/bin/env python3
"""The back-end harness, shared by every rung that compiles a subject all the
way to a CDX binary.

The driver is the compiler's own entry point, x86-64-emit-cdx, and the output
is what it returns: the symbol map, then header-bytes, the content buffer and
tail-bytes, 32 to a line. Nothing here decides anything about emission -- the
whole point is that the rung runs the real thing, so a copied driver that
could drift is exactly what this is not.

Two rungs use it and they differ only in the subject they hand it:
gen_fibx_harness.py compiles eighteen lines of fib, gen_scale_harness.py
compiles a real compiler chapter. Same emitter surface, different size, which
is what makes the second one cheap.
"""


def codex_literal(s):
    """Escape a program so it can ride inside the harness as a Text literal."""
    for ch, name in {'\t': 'tab', '\r': 'carriage return'}.items():
        if ch in s:
            raise SystemExit(f'subject contains a {name}; CCE has no escape for it')
    if any(ord(c) > 127 for c in s):
        raise SystemExit('subject has non-ASCII chars; the zig runtime panics on multibyte CCE')
    return s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n')


def harness_source(chapter, prefix, subject_text, passes=False):
    """Render the harness chapter. `prefix` names the walkers so two harnesses
    can be bundled in one unit without colliding.

    `passes` inserts the IR pipeline between lower and emit, the way
    compile-frontend-passes does. It is off by default because the rungs that
    predate it banked truth without it -- and it is not cosmetic: IR emission
    prunes to what the opening reaches, so a harness that never calls
    run-ir-pipeline prunes Simplify, Occurrence and LambdaLifting straight
    back out of the unit however many chapters were bundled."""
    # The pipeline's infos are the only evidence it did anything. Without
    # them "the passes ran" is inferred from a byte count, and a pipeline
    # that silently did nothing would look exactly like one that ran.
    info = ('\n      print-line-uni ("pass-infos " & show (list-length (passed.infos)))'
            if passes else '')
    lower = f"""in let ir-raw = lower-chapter ch sorted cst (rr.ctor-names) [] skip-list-text-empty [] 0
    in let passed = run-ir-pipeline default-ir-pipeline ir-raw False
    in let ir = passed.chapter""" if passes else \
        "in let ir = lower-chapter ch sorted cst (rr.ctor-names) [] skip-list-text-empty [] 0"
    return f'''Chapter: {chapter}

Section: Subject
  subject-text : Text
  subject-text = "{codex_literal(subject_text)}"

Section: Byte Dump

  {prefix}-line : Integer, Integer, Integer, List Text -> Text
  {prefix}-line (buf) (i) (hi) (acc) =
   if i >= hi then text-concat-list acc
   else {prefix}-line buf (i + 1) hi (list-push acc (integer-to-text (peek-byte buf i) & " "))

  {prefix}-print-bytes : Integer, Integer, Integer -> [Console] Nothing
  {prefix}-print-bytes (buf) (i) (len) = act
    if i >= len then print-line-uni "."
    else act
      print-line-uni ({prefix}-line buf i (if i + 32 < len then i + 32 else len) [])
      {prefix}-print-bytes buf (i + 32) len
    end
  end

 header-bytes and tail-bytes arrive as lists rather than in the workspace,
 so they need their own walker; splitting on the source of the bytes rather
 than on what they mean keeps both walkers dumb.

  {prefix}-list-line : List Integer, Integer, Integer, List Text -> Text
  {prefix}-list-line (bs) (i) (hi) (acc) =
   if i >= hi then text-concat-list acc
   else {prefix}-list-line bs (i + 1) hi (list-push acc (integer-to-text (list-at bs i) & " "))

  {prefix}-print-list : List Integer, Integer -> [Console] Nothing
  {prefix}-print-list (bs) (i) = act
    if i >= list-length bs then print-line-uni "."
    else act
      print-line-uni ({prefix}-list-line bs i (if i + 32 < list-length bs then i + 32 else list-length bs) [])
      {prefix}-print-list bs (i + 32)
    end
  end

 The symbol map is what the real compiler writes beside a .cdx, and it is
 the only thing that says which bytes are which. It goes through the oracle
 too: a plug that emitted the code correctly but scrambled the map would
 still be wrong.

  {prefix}-print-lines : List Text, Integer -> [Console] Nothing
  {prefix}-print-lines (ls) (i) = act
    if i >= list-length ls then print-line-uni "."
    else act
      print-line-uni (list-at ls i)
      {prefix}-print-lines ls (i + 1)
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
    {lower}
    in let res = x86-64-emit-cdx ir sorted
    in act
      print-line-uni ("check-errors " & show ((cst.bag).error-count))
      print-line-uni ("ir-defs " & show (list-length (ir.defs))){info}
      print-line-uni ("emit-errors " & show ((res.bag).error-count))
      print-line-uni ("header-len " & show (list-length (res.header-bytes)))
      print-line-uni ("content-len " & show (res.content-len))
      print-line-uni ("tail-len " & show (list-length (res.tail-bytes)))
      print-line-uni "--- symbols ---"
      {prefix}-print-lines (res.symbol-map) 0
      print-line-uni "--- header ---"
      {prefix}-print-list (res.header-bytes) 0
      print-line-uni "--- content ---"
      {prefix}-print-bytes (res.content-buf) 0 (res.content-len)
      print-line-uni "--- tail ---"
      {prefix}-print-list (res.tail-bytes) 0
    end
  end
'''
