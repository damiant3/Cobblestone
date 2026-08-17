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


def frontend_source(src, passes, scan=True):
    """The compiler's own sequence from source text to a lowered IRChapter,
    bound as `ir`. Every program built here runs exactly this -- the oracle
    harnesses and the hosted compiler alike -- so it is written once. `src` is
    whatever expression yields the source Text.

    `passes` runs the IR pipeline between lower and emit, where
    compile-frontend-passes runs it. Not cosmetic: IR emission prunes to what
    the opening reaches, so a program that never calls run-ir-pipeline prunes
    Simplify, Occurrence and LambdaLifting straight back out of the unit
    however many chapters were bundled.

    `scan=False` restores the empty renames/colliding/assignments this used to
    pass, and exists only to bisect a failure against the scan. It is wrong on
    purpose -- the driver computes those values -- so nothing should ship with
    it.
    """
    # opening.codex runs a RESOLVE phase between the IR pipeline and emission
    # (lines 633-634 and 823-825). Skipping it was the defect behind finding 11:
    # without build-type-def-map the emitter's st.type-defs holds no entry for a
    # record declared in the subject, so resolve-constructed-ty fails and
    # emit-field-access refuses with a ud2; and without rewrite-ir-defs the IR
    # still carries unresolved ConstructedTy annotations into emission.
    #
    # resolve-all-bindings lives in opening.codex and cannot be cited from here,
    # so its body -- a map applying deep-resolve to each binding -- is written as
    # the comprehension it is, rather than copied as a function.
    # The checker records a type for every expression, not only for bindings,
    # and the driver resolves that table too: opening.codex:635 runs
    # resolve-all-expr-types and line 692 rebuilds the UnificationState around
    # the result, so what reaches lowering is `expr-types = sorted-et`.
    #
    # Passing cr.state raw was the second half of clamp. arith's gauge is
    # `(Gauge { g = n }).g` -- the receiver is a record LITERAL, so its type
    # comes from expr-types rather than from any binding, and no amount of
    # resolving bindings reaches it. It arrived at emission unresolved,
    # emit-field-access refused with a ud2 exactly as it should, and the rung
    # compared error messages instead of a binary.
    #
    # resolve-all-expr-types lives in opening.codex and cannot be cited from
    # here, so its body -- a map applying deep-resolve to each entry -- is
    # written as the comprehension it is, the same way resolved-env is.
    EXPR_TYPES = """in let resolved-et = for e in (sort-expr-types ((cr.state).expr-types)) -> ExprTypeEntry { key = e.key, ty = deep-resolve (cr.state) (e.ty) }
    in let cst = __record-set (cr.state) "expr-types" resolved-et"""

    RESOLVE = """in let resolved-env = for b in ((cr.env).bindings) -> TypeBinding { name = b.name, bound-type = deep-resolve cst (b.bound-type) }
    in let all-bindings = for b in (sort-bindings (cr.types & resolved-env)) -> TypeBinding { name = b.name, bound-type = deep-resolve cst (b.bound-type) }
    in let type-map = build-type-def-map (ch.type-defs) 0 (list-length (ch.type-defs)) []
    in let sorted = sort-bindings (type-map & all-bindings)
    in let ir = __record-set ir0 "defs" (rewrite-ir-defs sorted (ir0.defs) 0)"""

    if scan:
        head = f"""let toks = tokenize {src} 1
    in let scan = scan-document (make-parse-state (toks.tokens) {src})
    in let assignments = build-all-assignments {src} (scan.def-headers) 0 []
    in let colliding = find-colliding-names assignments
    in let renames = build-global-rename-table assignments colliding"""
    else:
        head = f"""let toks = tokenize {src} 1
    in let assignments = []
    in let colliding = skip-list-text-empty
    in let renames = []"""
    lower = ("""in let ir-raw = lower-chapter ch bound cst (rr.ctor-names) renames colliding assignments 0
    in let passed = run-ir-pipeline default-ir-pipeline ir-raw False
    in let ir0 = passed.chapter""" if passes else
        "in let ir0 = lower-chapter ch bound cst (rr.ctor-names) renames colliding assignments 0")
    # opening.codex:442 opens with `let mountain-base = init-phase-allocator`,
    # and this is not only about setting the deck cell. X86_64Chapter.codex:1147
    # decides whether deck-record is the intrinsic or an ordinary function:
    #
    #   pa-slug = def-chapter-slug defs-lifted "init-phase-allocator" ...
    #   dr-slug = def-chapter-slug defs-lifted "deck-record" ...
    #   deck-record-intrinsic = (pa-slug /= "" & pa-slug == dr-slug)
    #
    # A harness that never calls init-phase-allocator leaves it unreachable, IR
    # emission prunes it, pa-slug comes back "", and the flag is False. Then
    # deck-record is emitted as what it looks like -- `mov rax,rdi ; ret`, four
    # bytes -- and every value the compiler wraps in it to survive
    # emit-all-defs's per-function __heap-restore is freed at that boundary
    # instead. bag-add wraps both its cons cell and its record, so the second
    # diagnostic a compile records walks a dangling spine: that is clamp's
    # RDI=0 into __list_snoc.
    #
    # Naming it is the whole job -- the marker has to be in the unit. It is
    # __heap-save + __deck-set, so the zig arm emits it without trouble.
    #
    # The reservation has to follow immediately, and cover the whole run rather
    # than sit in front of emission. init-phase-allocator points the deck cell
    # at the current heap top and bivy carries on from the same address, so
    # with the intrinsic live the first deck-record extent would allocate on
    # top of the parse and check data still in use. opening.codex never leaves
    # that window open: `build lex-deck-height` is line 444, two lines after
    # the init at 442. Ours is one region for every phase instead of the
    # driver's thirteen, which is the part of its shape a harness can skip.
    #
    # build's body is inlined rather than cited because build also reaches
    # deck-reservation-guard -> poke-byte, and poke-byte has no ZigEmitter
    # entry: citing it would make the builtin reachable and break the zig arm.
    # The guard pokes the boot stack's guard page, which a reservation this
    # size does not come near.
    #
    # demand-lift-floor is 104 MB against the 1 GB the harness runs in. It is a
    # first number, not a measured one: until now the intrinsic was off and no
    # rung allocated on the deck at all, so there is nothing to size against.
    # Too small shows up as CDX9002 or a fault, which is the honest direction
    # to be wrong in.
    return ("let mountain-base = init-phase-allocator\n"
            "    in let deck-base = __heap-save\n"
            "    in let deck-set = __deck-set deck-base\n"
            "    in let deck-adv = __heap-advance demand-lift-floor\n"
            "    in ") + head + f"""
    in let doc = parse-document (make-parse-state (toks.tokens) {src}) 0
    in let dr = desugar-document {src} doc (doc.chapter-title) 0
    in let ch0 = dr.dr-chapter
    in let ch = scope-achapter ch0 colliding assignments 0
    in let rr = resolve-chapter ch colliding assignments 0
    in let cr = check-chapter ch renames colliding assignments 0
    {EXPR_TYPES}
    in let bound = sort-bindings (cr.types)
    {lower}
    {RESOLVE}"""


def pipeline_source(src, passes, scan=True):
    """frontend_source plus the x86 emission, bound as `res`. The rungs that
    dump a CDX want this; the one that emits IR wants the frontend only --
    calling x86-64-emit-cdx there would put the whole back end in the IR's
    reachable set for nothing.

    The deck reservation this used to make here moved into frontend_source: it
    has to be open before the first deck-record extent anywhere, not before
    emission, and by emission the frontend has been allocating for a while.
    """
    return frontend_source(src, passes, scan) + """
    in let res = x86-64-emit-cdx ir sorted"""


def harness_source(chapter, prefix, subject_text, passes=False, scan=True):
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

 A count says a compile went wrong; the messages say what. This prints the
 bag itself so a rung that fails is a rung that explains itself, which is
 the difference between "emit-errors 72" and a diagnosis.

 emit-diags is the length of the same list, printed as a number, because a
 walker that emits nothing and a list that holds nothing look identical and
 the first reading of this had no control to tell them apart.

 The bag gates the byte sections, the way emit-binary-tail gates them on
 bag-has-errors. A compile that recorded an error has ALREADY refused: when
 emit-field-access cannot resolve a record type it emits ud2 where the load
 belonged, deliberately. Printing those bytes anyway produced a binary that
 traps at runtime and a rung that compared two crash dumps -- the harness
 shipping something the compiler had rejected.

  {prefix}-print-diags : List Diagnostic, Integer -> [Console] Nothing
  {prefix}-print-diags (ds) (i) = act
    if i >= list-length ds then print-line-uni "."
    else act
      print-line-uni ("  diag " & show ((list-at ds i).code) & " sev " & show ((list-at ds i).severity) & " " & (list-at ds i).message)
      {prefix}-print-diags ds (i + 1)
    end
  end

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
    {pipeline_source("subject-text", passes, scan)}
    in act
      print-line-uni ("check-errors " & show ((cst.bag).error-count))
      print-line-uni ("ir-defs " & show (list-length (ir.defs))){info}
      print-line-uni ("emit-errors " & show ((res.bag).error-count))
      print-line-uni ("emit-diags " & show (list-length ((res.bag).diagnostics)))
      {prefix}-print-diags ((res.bag).diagnostics) 0
      if bag-has-errors (res.bag) then print-line-uni "CODEGEN-HALTED: errors in bag; no binary printed"
      else act
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
  end
'''
