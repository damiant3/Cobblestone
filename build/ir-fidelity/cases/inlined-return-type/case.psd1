@{
    respect   = "the element type of an empty list a single-caller helper returns, which the checker resolves at the call site"
    a         = 'a.codex'
    b         = 'b.codex'
    knows     = 'knows.codex'
    knowsCode = 'CDX2001'
    # `make-empty : Integer -> List a` is inlined into `opening` by
    # inline-single-caller, so the literal sits in opening's body only when
    # the pipeline runs: this case reads with -Passes, and is UNSUPPORTED
    # under -Passes none (no list-expr in opening).
    path      = 'def:opening/body/find:list-expr/slot/2'
    passes    = $true
    # `a` lives in the helper's return type and in no parameter, so
    # parameter/argument matching never reaches it; once-apply-site pairs
    # the declared return type with the site's type. Without that pair the
    # cell carries the helper's own variable in both a and b.
    expect    = 'CARRIED'
}
