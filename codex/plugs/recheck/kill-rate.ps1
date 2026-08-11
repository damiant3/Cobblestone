# The rechecker's kill-rate. This is the deliverable, not the rechecker.
#
# A rechecker that agrees with the compiler on every input in the tree is
# indistinguishable from a program that returns AGREE unconditionally.
# Agreement is not evidence; sensitivity is. So every mutation below is a
# single targeted corruption of VALID IR that the rechecker MUST report as
# DISAGREE, and the run reports caught over planted, per class.
#
# The mutations are applied to the IR TEXT rather than to the source, because
# the compiler would reject the source. That is also what makes the corpus
# independently useful: a mutation the COMPILER accepts is a soundness bug
# found directly, with the rechecker agreeing or disagreeing about nothing.
#
# THE CONTROL IS THE FIRST ROW AND IT IS NOT A FORMALITY. The unmutated IR
# must come back with zero DISAGREE. Without it a rechecker that answered
# DISAGREE unconditionally would score a perfect kill-rate.
#
#   pwsh codex/plugs/recheck/kill-rate.ps1
[CmdletBinding()]
param([string]$Subject = '', [string]$Kernel = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot))) 'build\plug-ports.ps1')
$RecheckPort = Get-PlugPort 'recheck'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$PlugCdx = Join-Path $PlugDir 'build-output\recheck-plug.cdx'
$WorkDir = Join-Path $PlugDir 'build-output\kill-rate'
if (-not $Subject) { $Subject = Join-Path $PlugDir 'corpus\subject.codex' }
# The effect row and the linear fact only appear on the wire from the compiler
# that publishes them. Until that seed is installed the corpus must be built
# with the SUT; afterwards the default is correct.
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run codex/plugs/recheck/build.ps1 first")
    exit 2
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# -- Produce the valid IR once ----------------------------------------
$baseIr  = Join-Path $WorkDir 'base.ir'
$baseLog = Join-Path $WorkDir 'base.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $baseIr `
    -Log $baseLog -IrCce -Passes none -Kernel $Kernel 2>&1 | Out-Null
if (Select-String -Path $baseLog -Pattern 'error CDX' -Quiet) {
    [Console]::Error.WriteLine("FAIL: subject did not compile; see $baseLog")
    exit 4
}
$baseBytes = [System.IO.File]::ReadAllBytes($baseIr)

# The IR wire is CCE, not ASCII, so an anchor written in ASCII finds nothing.
# CCE shares no boundary with ASCII: '(' is 74 and 'a' is 15. This map is the
# compiler's own answer, produced by printing `from-unicode u` for u in
# 32..126 against the depot seed and pasted here.
#
# It is not trusted on its own: every anchor below must encode and then be
# found EXACTLY ONCE in the base IR. A wrong map finds nothing and the run
# reports NOT-APPLICABLE rather than quietly planting a mutation that was
# never planted.
$script:AsciiToCce = @{}
foreach ($p in ('32=2;33=67;34=72;35=83;36=95;37=96;38=84;39=71;40=74;41=75;42=78;43=76;44=66;45=73;46=65;47=81;' +
                '48=3;49=4;50=5;51=6;52=7;53=8;54=9;55=10;56=11;57=12;58=69;59=70;60=79;61=77;62=80;63=68;64=82;' +
                '65=41;66=58;67=50;68=48;69=39;70=54;71=55;72=46;73=43;74=61;75=60;76=49;77=52;78=44;79=42;80=57;' +
                '81=63;82=47;83=45;84=40;85=51;86=59;87=53;88=62;89=56;90=64;91=88;92=86;93=89;94=94;95=85;96=93;' +
                '97=15;98=32;99=24;100=22;101=13;102=28;103=29;104=20;105=17;106=35;107=34;108=23;109=26;110=18;' +
                '111=16;112=31;113=37;114=21;115=19;116=14;117=25;118=33;119=27;120=36;121=30;122=38;123=90;' +
                '124=87;125=91;126=92') -split ';') {
    $kv = $p -split '='
    $script:AsciiToCce[[int]$kv[0]] = [byte][int]$kv[1]
}

function ConvertTo-Cce([string]$s) {
    $out = New-Object byte[] $s.Length
    for ($i = 0; $i -lt $s.Length; $i++) {
        $c = [int][char]$s[$i]
        if (-not $script:AsciiToCce.ContainsKey($c)) { return $null }
        $out[$i] = $script:AsciiToCce[$c]
    }
    return ,$out
}

function Find-Bytes([byte[]]$hay, [byte[]]$needle, [int]$start) {
    $limit = $hay.Length - $needle.Length
    for ($i = $start; $i -le $limit; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($hay[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { return $i }
    }
    return -1
}

function Count-Bytes([byte[]]$hay, [byte[]]$needle) {
    $n = 0; $at = 0
    while ($true) {
        $at = Find-Bytes $hay $needle $at
        if ($at -lt 0) { break }
        $n++; $at++
    }
    return $n
}

# -- The mutations ----------------------------------------------------
# Each is one targeted edit with an anchor that must occur in the base IR.
# A mutation whose anchor is missing is reported as NOT-APPLICABLE rather
# than silently counting as caught, because a corpus that quietly plants
# nothing scores 0 of 0 and reads as success.
$mutations = @(
    @{ Class = 'unbound-name'
       From  = '(name "label" (fn int-default text))'
       To    = '(name "no-such-name-anywhere" (fn int-default text))' },

    @{ Class = 'apply-arg-type'
       From  = '(apply (name "show" (fn int-default text)) (name "n" int-default) text)'
       To    = '(apply (name "show" (fn int-default text)) (name "n" text) text)' },

    @{ Class = 'apply-result-type'
       From  = '(apply (name "show" (fn int-default text)) (name "n" int-default) text)'
       To    = '(apply (name "show" (fn int-default text)) (name "n" int-default) boolean)' },

    @{ Class = 'apply-non-function'
       From  = '(apply (name "label" (fn int-default text))'
       To    = '(apply (name "label" text)' },

    # The five literal forms carry no type on the wire, so every literal
    # argument used to reach rc-apply-arg-verdict as TyUnknown and be swallowed
    # into silence, which the tally reads as AGREE. That was 33,922 of the
    # 34,338 undecided argument comparisons over codex/test. rc-expr-ty now
    # synthesizes a literal's type from what the guide publishes, and this arm
    # is what says the synthesis DECIDES rather than merely stops abstaining:
    # a text literal replaced by an integer one, into Bright's declared Text
    # parameter. It scores nothing about bounds; bounds-exceeded-arg covers
    # that through the other checker.
    @{ Class = 'apply-arg-literal-sort'; Kind = 'apply-arg-type'
       From  = '(text-lit "hi")'
       To    = '(int-lit 7)' },

    @{ Class = 'ctor-pat-field-type'
       From  = '(ctor-pat "Dim" (subs (var-pat "k" int-default))'
       To    = '(ctor-pat "Dim" (subs (var-pat "k" text))' },

    # Dim is NOT parametric, so the mutation above walks the plain path and
    # says nothing about a sum with type arguments. Until 2026-08-07 the
    # corpus had no parametric type at all, so every nominal-argument
    # comparison in the checker was unpoliced -- including the arm that
    # decides whether Box Integer may stand where Box Text is declared.
    @{ Class = 'ctor-pat-nominal-arg'; Kind = 'ctor-pat-field-type'
       From  = '(ctor-pat "Wrap" (subs (var-pat "k" int-default)) (ctd "Box" (args int-default))'
       To    = '(ctor-pat "Wrap" (subs (var-pat "k" int-default)) (ctd "Box" (args text))' },

    @{ Class = 'ctor-pat-unknown'
       From  = '(ctor-pat "Dim" (subs'
       To    = '(ctor-pat "NotAConstructor" (subs' },

    @{ Class = 'ctor-pat-arity'
       From  = '(ctor-pat "Bright" (subs (var-pat "t" text))'
       To    = '(ctor-pat "Bright" (subs (var-pat "t" text) (var-pat "zz" text))' },

    @{ Class = 'ctor-ref-payload-type'
       From  = '(name "Bright" (fn text (sum "Shade"'
       To    = '(name "Bright" (fn boolean (sum "Shade"' },

    # Fair is a real constructor and resolves as a name, but it belongs to
    # Weather, not Shade. Naming something that binds nowhere would be caught
    # by the unbound-name arm instead and leave this one unexercised, which is
    # exactly what the first version of this corpus did.
    @{ Class = 'ctor-ref-unknown'
       From  = '(name "Dim" (fn int-default (sum "Shade"'
       To    = '(name "Fair" (fn int-default (sum "Shade"' },

    # The variance arm. `boxed` is an ordinary function, not a constructor, so
    # this cannot be caught by the ctor-ref arms the way a mutation on Wrap
    # would be -- that was the first version of this row and it scored under
    # ctor-ref-payload-type before the checker under test had run at all.
    #
    # Only the CALLEE's declared return moves; the apply node's own recorded
    # type stays `(ctd "Box" (args int-default))`, so exactly one comparison
    # changes and the outer unbox application is untouched.
    #
    # The two candidate readings genuinely disagree, which is what makes this
    # a mutation rather than decoration: 0..10 FITS inside int-default, so
    # under covariance of a type argument this is well typed and silent, and
    # only invariance rejects it. It was confirmed MISSED before the rule was
    # published and CAUGHT after.
    @{ Class = 'variance-widened-type-arg'; Kind = 'apply-result-type'
       From  = '(name "boxed" (fn int-default (ctd "Box" (args int-default))))'
       To    = '(name "boxed" (fn int-default (ctd "Box" (args (int 0 10 ov-error)))))' },

    # The substitution arm. MkTup2 is the one callee that reaches an apply
    # with its type variables still on the wire: a curried call records each
    # node's result with the substitution SO FAR applied, so the inner node
    # carries (fn (tvar 3) (Tup2 int-default (tvar 3))) while the callee
    # still says (fn (tvar 2) (fn (tvar 3) (Tup2 (tvar 2) (tvar 3)))).
    #
    # Corrupting the ARGUMENT's type is what only substitution can catch.
    # Without substitution both comparisons involve tvar 2 and answer
    # TyUnknown, so the whole thing is silent.
    #
    # It must NOT be the inner node's RESULT type that moves: that type is
    # also the OUTER apply's callee, so corrupting it disagrees concretely
    # one node up and is caught with or without this change. Confirmed
    # MISSED before and CAUGHT after.
    #
    @{ Class = 'tvar-substitution'; Kind = 'apply-result-type'
       From  = '(ctd "Tup2" (args (tvar 2) (tvar 3)))))) (name "n" int-default)'
       To    = '(ctd "Tup2" (args (tvar 2) (tvar 3)))))) (name "n" text)' },

    # The argument-side twin of variance-widened-type-arg, and the arm that
    # exists because its absence let a WRONG fix score 21 of 21 on
    # 2026-08-09. The mutation above corrupts the argument to a type of
    # another SORT, so a checker that only asks whether the argument FITS the
    # parameter still catches it. This one corrupts it to a bounded integer
    # that DOES fit int-default, so the fit question answers yes and only the
    # invariance of the tuple's type argument rejects it.
    #
    # That is the `fresh-row-id` shape, which is a real defect the compiler
    # accepted, so a checker blind here is blind to the class this lane
    # found. The rejected fix fixed the callee's variables from the site's
    # own recorded result type; under it this mutation is SILENT and every
    # other arm still passes.
    @{ Class = 'bounded-arg-into-plain-slot'; Kind = 'apply-result-type'
       From  = '(ctd "Tup2" (args (tvar 2) (tvar 3)))))) (name "n" int-default)'
       To    = '(ctd "Tup2" (args (tvar 2) (tvar 3)))))) (name "n" (int 0 10 ov-error))' },

    # A `tvar-spine-substitution` arm stood here and was RETIRED, not lost.
    # It corrupted wrap-kids' declared parameter so a comprehension's result,
    # arriving as (list (tvar 25)), contradicted it. That was a real arm
    # against a compiler that recorded a lambda as the expected type it was
    # handed. Now that lower-lambda records the lambda's ACTUAL type, the
    # argument arrives as (list (sum "Tree")) and an ordinary comparison
    # catches the corruption whether or not any spine substitution exists.
    #
    # Measured, not assumed: with rc-arg-spine forced to the empty map
    # against the fixed compiler, that row is STILL CAUGHT while the row
    # below goes MISSED. An arm caught for a reason it does not name is the
    # decoration this file warns about, so it is gone. Do not re-add it
    # without a compiler that leaves the shape uninstantiated.
    #
    # The arm below covers the substitution now, and it covers BOTH halves:
    # via-if's result variable is fixed at the INNER node from the lambda's
    # body and consumed on the OUTER node's recorded type, so the
    # accumulation across the spine is load-bearing and not only the descent.
    # via-if's lambda body is an `if`, which the compiler records as the bare
    # type variable while both arms carry Tree. Those are the four sites in
    # IR/Lowering.codex that lower-lambda's fix does not reach, because there
    # the body's own type is a variable too and there is nothing concrete to
    # substitute.
    #
    # Confirmed MISSED before and CAUGHT after, against both compilers.
    @{ Class = 'tvar-spine-branch-arms'; Kind = 'apply-arg-type'
       From  = '(name "hold-kids" (fn (list (sum "Tree" (args))) (sum "Tree" (args))))'
       To    = '(name "hold-kids" (fn (list text) (sum "Tree" (args))))' },

    # Stage 2. A literal too large for the bounded parameter it is handed to.
    @{ Class = 'bounds-exceeded-arg'; Kind = 'bounds-exceeded'
       From  = '(name "narrow" (fn (int 0 10 ov-error) (int 0 20 ov-error))) (int-lit 5)'
       To    = '(name "narrow" (fn (int 0 10 ov-error) (int 0 20 ov-error))) (int-lit 500)' },

    # The declared return narrowed below what the body can produce. 0..15 is
    # chosen so the two candidate readings DISAGREE, which 0..5 would not:
    # the body is n * 2 over 0..10, the binary node carries its LEFT OPERAND
    # type 0..10, and 0..10 fits inside 0..15 while the real product 0..20 does
    # not. A rechecker that read the node type would pass this and a rechecker
    # that derives the product fails it. With 0..5 both readings fail and the
    # mutation would score whether or not the derivation exists.
    @{ Class = 'bounds-exceeded-return'; Kind = 'bounds-exceeded'
       From  = '(params (param "n" (int 0 10 ov-error))) (fn (int 0 10 ov-error) (int 0 20 ov-error))'
       To    = '(params (param "n" (int 0 10 ov-error))) (fn (int 0 10 ov-error) (int 0 15 ov-error))' },

    # The design's own class: widen a bounded integer past its declared hi.
    # 0..15 for the same reason as above: it still fits the declared return of
    # 0..20 when read as the node type, and only the derived product 0..30
    # exceeds it. 0..100 would be caught either way and prove nothing about
    # whether the range is being derived at all.
    @{ Class = 'bounds-widened-operand'; Kind = 'bounds-exceeded'
       From  = '(binary mul-int (name "n" (int 0 10 ov-error))'
       To    = '(binary mul-int (name "n" (int 0 15 ov-error))' },

    # The division twin, and it exists because rc-derive-binary had no
    # div-int arm at all: every division abstained as bounds-underived, which
    # is a row the guide's Static Bounds Prover table publishes and the
    # compiler really does prove (`halve` in the corpus compiles).
    #
    # Division makes a range SMALLER, so the mutation cannot work the way the
    # mul arm's does. Widening the DIVIDEND is what pushes the quotient past
    # the declared return: 0..60 halved is 0..30 against a declared 0..10.
    # Without the arm the derivation answers unknown and the verdict is an
    # UNSUPPORTED bounds-underived, which is not the expected kind, so this is
    # MISSED before and CAUGHT after.
    #
    # The unmutated corpus is the other half and it guards the ARITHMETIC
    # rather than the presence of the arm. `halve`'s div-int node records its
    # LEFT OPERAND type (int 0 20), not the true quotient, so a derivation
    # that passed the dividend through unchanged would answer 0..20 against a
    # declared 0..10 and turn the control DISAGREE. Getting the arm merely
    # present is not enough to keep the control green.
    #
    # Sabotage confirmed, 2026-08-09, and the result is the reason to read
    # this: with the derivation replaced by a pass-through the control fails
    # with exactly `derived range 0..20 which does not fit the declared
    # 0..10` AND EVERY MUTATION STILL SCORES, 22 of 22. The kill-rate alone
    # cannot tell correct division from a pass-through, because both derive
    # SOMETHING and every arm here is planted to exceed either way. The
    # control is the only row that separates them.
    @{ Class = 'bounds-widened-dividend'; Kind = 'bounds-exceeded'
       From  = '(binary div-int (name "n" (int 0 20 ov-error))'
       To    = '(binary div-int (name "n" (int 0 60 ov-error))' },

    # The module-constant row, and the largest of R4's bounds classes: 96 of
    # the compiler's 101 bounds-underived findings are a named cdx-* code
    # reaching one of four diagnostic constructors.
    #
    # The mutation moves the CONSTANT'S DEFINITION, not the call site. On the
    # wire the argument is `(name "cdx-sample" int-default)` and its declared
    # type is a plain Integer, which carries the full i64 band, so nothing at
    # the call site distinguishes 200 from 900. Only a checker that resolves
    # the name to its defining literal can tell, which is exactly the
    # capability under test: without it the derivation answers unknown and
    # the verdict is an UNSUPPORTED bounds-underived, not the expected kind.
    # Confirmed MISSED before and CAUGHT after.
    @{ Class = 'bounds-widened-constant'; Kind = 'bounds-exceeded'
       From  = '(def "cdx-sample" "RecheckSubject" (params) int-default (int-lit 200)'
       To    = '(def "cdx-sample" "RecheckSubject" (params) int-default (int-lit 900)' },

    # THIS ARM IS NOT A SENSITIVITY GAIN AND IS NOT MEANT TO BE. It is the
    # pin under a REMOVAL: stage 1 stopped reporting apply-arg-int-bounds,
    # on the ground that an integer argument is admitted by its proven range
    # and RecheckBounds is what decides that. The whole justification for
    # that silence is that the bounds stage really does catch an argument
    # whose range exceeds the parameter, so it is asserted here instead of
    # assumed, and it is CAUGHT both before and after the removal.
    #
    # An abstention is not a kill, so removing one cannot lower the score
    # and the kill-rate can never notice the removal on its own. No arm in
    # this file has ever expected the kind apply-arg-int-bounds. If stage 2
    # coverage is ever weakened, this row is what goes red.
    @{ Class = 'bounds-widened-field-arg'; Kind = 'bounds-exceeded'
       From  = '"level/0" (int 0 200 ov-error)'
       To    = '"level/0" (int 0 900 ov-error)' },

    # A `when` is the match form of `if` and carries the same union rule.
    # The table published the `if` row and never the `when` row, so every
    # match abstained; skip-newlines-pos in Syntax/ParserCore.codex is the
    # real definition that needs it.
    #
    # The arms are a parameter declared 0..20 and the literal 5, so the union
    # is 0..20 and narrowing the declared return to 0..10 exceeds it. Without
    # the arm the derivation answers unknown and the verdict is an UNSUPPORTED
    # bounds-underived, so this is MISSED before and CAUGHT after. 0..10 also
    # excludes the 0..20 arm on its own, which is deliberate: an implementation
    # that took only the FIRST arm would still be caught, and one that took
    # only the literal arm would not, which the control covers by requiring
    # the unmutated union to fit 0..30.
    @{ Class = 'bounds-widened-match-arm'; Kind = 'bounds-exceeded'
       From  = '(def "arm-pick" "RecheckSubject" (params (param "p" (sum "Pick" (args))) (param "n" (int 0 20 ov-error))) (fn (sum "Pick" (args)) (fn (int 0 20 ov-error) (int 0 30 ov-error)))'
       To    = '(def "arm-pick" "RecheckSubject" (params (param "p" (sum "Pick" (args))) (param "n" (int 0 20 ov-error))) (fn (sum "Pick" (args)) (fn (int 0 20 ov-error) (int 0 10 ov-error)))' },

    # Five builtins carry a range their SIGNATURE does not: all are declared
    # to return a plain Integer and the bound is a structural fact about
    # heap-backed quantities under 4 GB. The compiler holds the same five in
    # builtin-return-range; the table did not publish them, so pitch in
    # Core/PhaseAllocator.codex abstained.
    #
    # The discriminating pair is in the guide: `let p = size in p` at this
    # same declared return is CDX2051 while `let p = __heap-save in p` is
    # clean, so the let hides nothing and the builtin is what carries the
    # range. Narrowing the declared return to 0..100 makes 0..4294967295
    # exceed it. MISSED before, CAUGHT after.
    @{ Class = 'bounds-widened-heap-builtin'; Kind = 'bounds-exceeded'
       From  = '(def "heap-mark" "RecheckSubject" (params (param "z" int-default)) (fn int-default (int 0 4294967295 ov-error))'
       To    = '(def "heap-mark" "RecheckSubject" (params (param "z" int-default)) (fn int-default (int 0 100 ov-error))' },

    # Stage 3. The declared effect removed from a signature whose body still
    # performs it. announce's body calls print-line-uni, which carries
    # Console.Write, and Console.Write is covered only by a declared Console.
    @{ Class = 'effect-undeclared'
       From  = '(fn text nothing (row (labels (label "Console" "")) "" -1))'
       To    = '(fn text nothing)' },

    # Stage 3, the grounds table. touch performs port-out-byte, a
    # [Device.Port] intrinsic, behind a pure signature, which is legal only
    # because the chapter grounds Device.Port. Delete the entry from the wire
    # and the discharge is gone, so the call must be reported.
    #
    # This row exists because publishing the table made a whole class of
    # finding go quiet -- 90 of the 97 in the first complete sweep -- and a
    # check that went quiet has to be shown to still fire. Dropping the effect
    # half alone would not do it: "Corpus\nDevice.Port" against a def whose
    # chapter-slug is RecheckSubject must fail to match, so this also pins
    # that the SLUG is compared rather than any entry discharging anything.
    @{ Class = 'grounds-dropped'; Kind = 'effect-undeclared'
       From  = '(grounds "RecheckSubject\nDevice.Port")'
       To    = '(grounds "Elsewhere\nDevice.Port")' },

    # A linear parameter used twice. Exactly-once is the whole discipline and
    # the front end refuses this with CDX2061.
    @{ Class = 'linear-reused'
       From  = '(binary add-int (name "q" int-default) (int-lit 1) int-default)'
       To    = '(binary add-int (name "q" int-default) (name "q" int-default) int-default)' },

    # A linear parameter named by the def but mentioned nowhere in its body,
    # which is the leak half of the same discipline (CDX2063).
    @{ Class = 'linear-unused'
       From  = '(unique "q")'
       To    = '(unique "zz")' }
)

function Invoke-Recheck([string]$IrPath) {
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = $null
    try {
        $proc = Start-Process -FilePath $script:CodexVmBin `
            -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
            -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $RecheckPort)
        $listener.Start()
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $listener.Pending() -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not $listener.Pending()) { $listener.Stop(); return '' }
        $client = $listener.AcceptTcpClient()
        $listener.Stop()
        $ns = $client.GetStream()
        $data = [System.IO.File]::ReadAllBytes($IrPath)
        $ns.Write([BitConverter]::GetBytes([int]($data.Length + 1)), 0, 4)
        $ns.WriteByte(1)
        $off = 0
        while ($off -lt $data.Length) {
            $n = [Math]::Min(65536, $data.Length - $off)
            $ns.Write($data, $off, $n); $ns.Flush(); $off += $n
        }
        $ns.ReadTimeout = 300000
        $resp = [System.Collections.Generic.List[byte]]::new()
        $buf = New-Object byte[] 65536
        while ($true) {
            try { $n = $ns.Read($buf, 0, $buf.Length) } catch { break }
            if ($n -le 0) { break }
            for ($i = 0; $i -lt $n; $i++) { $resp.Add($buf[$i]) }
        }
        $client.Close()
        return [System.Text.Encoding]::ASCII.GetString($resp.ToArray())
    } finally {
        if ($proc -and -not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {} }
        Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    }
}

# -- The control ------------------------------------------------------
Write-Host ''
Write-Host '=== CONTROL: unmutated IR must produce zero DISAGREE ==='
$controlOut = Invoke-Recheck $baseIr
Write-Host $controlOut
# Two stages report two summary lines, so "DISAGREE 0" appearing anywhere is
# not the question: a clean stage 1 beside a disagreeing stage 2 would match it
# and pass a control that had failed. Finding lines begin with the verdict.
#
# The STAGE requirement is not belt and braces. A plug that dies answers an
# EMPTY string, an empty string contains no DISAGREE line, and the control
# therefore PASSED while every mutation below reported MISSED -- a 0 per cent
# kill rate presented as a working harness. That happened here on 2026-08-09
# and cost a cycle before the silence was recognised as a crash rather than
# twenty honest misses.
$controlOk = (-not ($controlOut -match '(?m)^DISAGREE ')) -and ($controlOut -match '(?m)^STAGE ')
if (-not $controlOk) {
    Write-Host 'CONTROL FAILED: the rechecker disagrees with valid IR. Every kill below is suspect.'
}

# -- The mutations ----------------------------------------------------
Write-Host '=== MUTATIONS ==='
$planted = 0; $caught = 0
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($m in $mutations) {
    $class = $m.Class
    $fromB = ConvertTo-Cce $m.From
    $toB   = ConvertTo-Cce $m.To
    if ($null -eq $fromB -or $null -eq $toB) {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'NOT-APPLICABLE (unencodable)' })
        Write-Host ("  {0,-24} NOT-APPLICABLE -- anchor has a character outside the CCE map" -f $class)
        continue
    }
    $hits = Count-Bytes $baseBytes $fromB
    if ($hits -eq 0) {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'NOT-APPLICABLE (anchor absent)' })
        Write-Host ("  {0,-24} NOT-APPLICABLE -- anchor not present in base IR" -f $class)
        continue
    }
    $planted++
    $idx = Find-Bytes $baseBytes $fromB 0
    $tailStart = $idx + $fromB.Length
    $tailLen   = $baseBytes.Length - $tailStart
    $mut = New-Object byte[] ($idx + $toB.Length + $tailLen)
    [System.Array]::Copy($baseBytes, 0, $mut, 0, $idx)
    [System.Array]::Copy($toB, 0, $mut, $idx, $toB.Length)
    [System.Array]::Copy($baseBytes, $tailStart, $mut, $idx + $toB.Length, $tailLen)
    $mutPath = Join-Path $WorkDir "mut-$class.ir"
    [System.IO.File]::WriteAllBytes($mutPath, $mut)
    $out = Invoke-Recheck $mutPath
    [System.IO.File]::WriteAllText((Join-Path $WorkDir "mut-$class.report"), $out)

    # A mutation may name the finding kind it expects when that differs from
    # its own label: three separate bounds mutations all surface as one kind.
    $wantKind = if ($m.ContainsKey('Kind')) { $m.Kind } else { $class }
    $sawClass = $out -match ("DISAGREE .*\[" + [regex]::Escape($wantKind) + "\]")
    $sawAny   = $out -match '(?m)^DISAGREE '
    # A dead plug answers nothing, which is not a miss and must not be counted
    # as one. Distinguish it here or a crash reads as a checker that is merely
    # insensitive.
    if (-not ($out -match '(?m)^STAGE ')) {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'PLUG-DIED' })
        Write-Host ("  {0,-24} PLUG-DIED (no answer -- not a miss)" -f $class)
        continue
    }
    if ($sawClass) {
        $caught++
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'CAUGHT' })
        Write-Host ("  {0,-24} CAUGHT" -f $class)
    } elseif ($sawAny) {
        $caught++
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'CAUGHT (different class)' })
        Write-Host ("  {0,-24} CAUGHT but reported under another class" -f $class)
    } else {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'MISSED' })
        Write-Host ("  {0,-24} MISSED" -f $class)
    }
}

Write-Host ''
Write-Host '=== KILL RATE ==='
Write-Host ("  control (valid IR, zero disagreements) : {0}" -f $(if ($controlOk) { 'PASS' } else { 'FAIL' }))
Write-Host ("  mutations caught / planted             : {0} / {1}" -f $caught, $planted)
if ($planted -gt 0) {
    Write-Host ("  kill rate                              : {0}%" -f [math]::Round(100.0 * $caught / $planted, 1))
}
Write-Host ''
Write-Host 'A class below 100 per cent is a hole in the rechecker, stated here rather'
Write-Host 'than discovered later. Reports are in build-output/kill-rate/.'

if (-not $controlOk) { exit 1 }
if ($planted -eq 0) { exit 1 }
exit 0
