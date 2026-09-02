@{
    respect   = "an empty list literal's element type, which the checker resolves from a later use"
    a         = 'a.codex'
    b         = 'b.codex'
    knows     = 'knows.codex'
    knowsCode = 'CDX2001'
    path      = 'def:opening/body/find:list-expr/slot/2'
    # Re-baselined at the Update 54 release (2026-09-01, seed FCBABF07): the
    # -Grade run reported this row moved DROPPED to CARRIED. The fix is
    # COMPILER-30's witness (Steve Howell's PR 101, main 20944), which lets
    # the checker's element type reach the empty literal; banked DROPPED
    # since 2026-08-27.
    expect    = 'CARRIED'
}
