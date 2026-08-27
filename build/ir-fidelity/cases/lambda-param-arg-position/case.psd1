@{
    respect   = "a lambda parameter's true type, where the lambda is an ARGUMENT rather than let-bound"
    a         = 'a.codex'
    b         = 'b.codex'
    knows     = 'knows.codex'
    knowsCode = 'CDX2001'
    # Since main 20176 the -IrUni dump is lifted: the lambda is def __lam_0.
    path      = 'def:__lam_0/param/0'
    expect    = 'CARRIED'
}
