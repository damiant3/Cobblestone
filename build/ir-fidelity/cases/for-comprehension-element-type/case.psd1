@{
    respect   = "the element type of a for-comprehension's lambda parameter"
    a         = 'a.codex'
    b         = 'b.codex'
    knows     = 'knows.codex'
    knowsCode = 'CDX2001'
    # The comprehension desugars to map-list (lambda) list, and the lambda is
    # lifted to the top-level def __lam_0, as in lambda-param-type.
    path      = 'def:__lam_0/param/0'
    # DROPPED until 2026-09-07: Desugarer.codex:79 built the comprehension's
    # lambda with synthetic-span while var-tok was in scope, so the param cell
    # read (tvar 23) -- map-list's own forall variable, free in the lambda's
    # def -- while the body of that same def read int-default. One def
    # disagreeing with itself. It carries no error and no noexpect, so the
    # ErrorTy census could never see it: this is COMPILER-30's half two, the
    # IR dropping a type the checker resolved.
    expect    = 'CARRIED'
}
