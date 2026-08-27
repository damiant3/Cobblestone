@{
    respect   = "a lambda parameter's true type, where the lambda is bound by a let"
    a         = 'a.codex'
    b         = 'b.codex'
    knows     = 'knows.codex'
    knowsCode = 'CDX2001'
    # Since main 20176 the -IrUni dump is lifted, so the lambda is the
    # top-level def __lam_0 rather than a (lambda ...) node inside opening.
    path      = 'def:__lam_0/param/0'
    # PR 93 (main 20184) fixed the let-bound carrier this case was written
    # to demonstrate: the param cell now reads text, not error.
    expect    = 'CARRIED'
}
