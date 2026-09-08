# The shape a plug emitted for `choose 0 2 3` in test-input/overapply.codex.
#
# Dot-sourced by BOTH test-plugs.ps1 (which grades on it) and
# classify-overapply.ps1 (which tabulates it). One copy: a second set of these
# patterns would drift, and the whole point of the row is that the harness and
# the reading must agree.
#
# `choose` takes ONE parameter and RETURNS a two-parameter function. The wire
# delivers application curried (DevelopersRulebook.md:260), so a plug must emit
# the extra arguments applied ONE AT A TIME unless it knows the callee's arity.

# Languages whose ordinary call syntax IS curried application, so the flat
# spelling `choose 0 2 3` already means choose(0)(2)(3). Clojure and Scheme are
# NOT here: both define `choose` at arity 1 and `(choose 0 2 3)` is an arity
# error, which is why spelling alone cannot decide this.
$script:OverapplyNativeCurried = @('haskell', 'ocaml')

# Lenses this subject is not aimed at, and which are therefore not GRADED on
# it: ptx, wgsl and spirv are GPU kernel targets and a scalar program is not
# their subject (plugs-backlog 2.29), and evidence, recheck and t3isa answer a
# different question than "transpile this program". Grading them here would
# park two permanent reds that nobody intends to fix.
$script:OverapplyNotSubject = @('ptx', 'wgsl', 'spirv', 'evidence', 'recheck', 't3isa')

# Targets with no first-class functions at all. ada emits the literal `null`
# for a lambda and cobol emits nothing, so neither can express a definition
# that returns a function: the repair is LIFTING, not a branch at the apply
# site, and it is a design rather than a fix (plugs 1.59). They are named
# here rather than detected from their output, because "the surplus arguments
# were dropped" is not decidable from the emitted text (see Get-OverapplyShape).
$script:OverapplyNoClosures = @('ada', 'cobol', 'pascal', 'babbage')

function Get-OverapplyShape {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Plug
    )

    # DECIDED STRUCTURALLY, NOT BY SPELLING, and this is the third version of
    # this rule. The first two enumerated the shapes that happened to exist,
    # and each time an emitter was REPAIRED it produced a correct shape the
    # rule did not know, and was graded as a defect (L-INSTRUMENT). Repairing
    # seven emitters at once produced seven new correct spellings in one run:
    #   (((choose 0) 2) 3)                            clojure, scheme
    #   choose.(0).(2).(3)                            elixir
    #   Function.apply(Function.apply(...))           flutter
    #   choose(0)->(2)->(3)                           perl
    #   ((choose(0)).asInstanceOf[Any => Any](2))...  scala
    #   ((id(^)(id))(...(choose(@(0))))(@(2)))(@(3))  objc
    # No enumeration survives that. So only FLAT is matched positively and the
    # rest is decided by whether the surplus arguments were applied AT ALL.

    # Native curried syntax is checked BEFORE flat, because `choose 0 2 3` is
    # the flat spelling AND the correct one, and only the target decides which.
    if (($Plug -in $script:OverapplyNativeCurried) -and [regex]::IsMatch($Text, 'choose\s+[01]\s+2\s+3')) {
        return 'native'
    }

    # A flat call carries 0/1, 2 and 3 in ONE argument list. The gap between
    # `choose` and that list tolerates a cast or a type assertion but must not
    # cross another call, or the `add3(1, 2, 3)` on the subject's first line
    # is matched instead and a correct emission is scored flat.
    $arg = { param($lit) '(?:@\(\s*' + $lit + '\w{0,4}\s*\)|' + $lit + '\w{0,4})' }
    $flatArgs = '\(\s*' + (& $arg '[01]') + '\s*,\s*' + (& $arg '2') + '\s*,\s*' + (& $arg '3') + '\s*\)'
    $flat = [regex]::IsMatch($Text, '(?s)choose(?:(?!add3|choose).){0,60}?' + $flatArgs) -or
            [regex]::IsMatch($Text, '\(\s*choose\s+[01]\s+2\s+3\s*\)') -or
            [regex]::IsMatch($Text, 'Function\.apply\s*\(\s*choose\s*,\s*\[\s*[01]\s*,\s*2\s*,\s*3\s*\]')
    if ($flat) { return 'flat' }

    # Anything else is applied one argument at a time.
    #
    # NOT distinguished here: an emission that DROPS the surplus arguments.
    # It was attempted and withdrawn, because no text rule separates it from a
    # correct chain across these targets. The measurement that settled it: go's
    # correct chain carries its second surplus argument 110 characters after
    # `choose`, while ada's nearest contaminating `C(2)(3)` from the NEXT
    # statement sits at 130, so every window wide enough to accept go also
    # accepts ada, and every window narrow enough to reject ada rejects go.
    # A shorter window is not a stricter rule, it is a different wrong answer.
    #
    # The targets that drop are the ones with no closures at all, and they are
    # named in $OverapplyNoClosures rather than guessed at from their output.
    return 'stepwise'
    return 'dropped'
}

# The subject has TWO defects in it, and the over-application above is only
# one. `choose`'s body is `add3 1`, a PARTIAL application of a three-parameter
# definition, which a target without partial application must eta-wrap:
# `(_p0_) => (_p1_) => add3(1, _p0_, _p1_)`. Emitting `add3(1)` is a call of a
# three-parameter function with one argument.
#
# Graded separately because a plug can be fixed on one half and not the other,
# and without this a half-repair reads GREEN: the over-application shape would
# be correct while the function it applies is still uncallable.
function Get-OverapplyPartialShape {
    param([Parameter(Mandatory)][string]$Text)
    # add3 applied to exactly one argument: the closing paren follows the first
    # argument with no comma. `add3(1, 2, 3)` therefore does not match.
    # The single argument carries no whitespace, which keeps a DEFINITION out
    # of the match: wasm writes `(func $add3 (param $a i64) ...)` and an
    # argument pattern that allowed spaces read that as a one-argument call.
    if ([regex]::IsMatch($Text, '(?i)\badd3\s*\(\s*[^,()\s]{1,12}\s*\)')) { return 'flat-partial' }
    return 'eta'
}

function Get-OverapplyShapeNote {
    param([Parameter(Mandatory)][string]$Shape)
    switch ($Shape) {
        'stepwise' { 'applied one argument at a time' }
        'native'   { 'curried (native call syntax)' }
        'flat'     { 'FLAT 3-arg call of a 1-parameter definition' }
        'dropped'  { 'EXTRA ARGUMENTS DROPPED' }
        default    { $Shape }
    }
}
