param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string]$CaseDir = '',
    [string]$Only = '',
    [switch]$Passes,  # run WITH the optimizer pipeline instead of -Passes none
    [switch]$Grade,   # grade the instrument itself, then run the real cases
    [switch]$Census,  # census an atom over a corpus of CLEAN compiles
    [switch]$Calibrate,   # -Census: run the two arms that grade the census itself
    [string]$Atom = 'error',  # -Census: the wire atom to count
    [string]$CorpusDir = '',  # -Census: default codex\test
    [int]$Limit = 0   # -Census: 0 is the whole corpus; a cap is REPORTED, never silent
    ,[switch]$Disagree   # census defs where one NAME carries two types, one of them a tvar
    ,[string[]]$Programs = @()   # -Disagree: run these named programs instead of the corpus
    ,[int]$Expect = -1   # -Disagree: refuse if the site count is not this. -1 reports only
)

# Does the IR carry what the checker knew?
#
# Each case is three programs and one wire position:
#
#   a, b     differ in exactly ONE respect and BOTH compile clean
#   knows    a program the checker REFUSES with a named diagnostic, which is
#            what establishes that the checker distinguishes that respect
#   path     the cell on the IR wire to compare between a and b
#
# The verdict follows from those three, and the knows arm is what keeps it
# honest: without it, cells that agree cannot be told apart from a checker that
# never knew the difference either. That is Steve Howell's discriminator (docs/
# Reference/ZigDemandingCustomer_Notes.md) made mechanical -- did the checker
# compute an answer the IR failed to carry, or does the program genuinely not
# constrain one?
#
#   CARRIED       checker knows, cells differ. The wire carried the fact.
#   DROPPED       checker knows, cells agree. The compiler dropped it: upstream.
#   UNCONSTRAINED the knows arm did not refuse, so no claim is made either way.
#   UNSUPPORTED   the reader could not locate the cell. An instrument gap.
#   ERROR         a or b failed to compile, or the wire was absent.
#   LEAK          the lowering-internal no-expectation marker reached the wire.
#                 Always a compiler defect, whatever the case's own cell says.
#
# UNCONSTRAINED and UNSUPPORTED are deliberately NOT passes: a skip reported as
# a pass is indistinguishable from a check that asks and agrees
# (L-CAPABILITY-LOST).
#
# The default is -Passes none, which audits the sentence the author WROTE. That
# is the right reading for this question, because what the checker knew is a
# fact about the author's program. -Passes runs the optimizer pipeline instead
# and audits what actually runs; expect UNSUPPORTED there for any case whose
# subject is inlined away, which is honest rather than a gap -- measured
# 2026-08-27, linear-param and effect-row both go UNSUPPORTED under the
# pipeline because `consume` and `f` no longer exist as defs, while
# lambda-param-type still reports DROPPED. A run that does not say which
# reading it took is not interpretable, so both modes print it.

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ir-wire.ps1')

# Resolved before anything uses it, -Grade included: the kernel default is
# repo-relative, and -Grade hands it to two child invocations.
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repoRoot $Kernel }

if ($Grade) {
    # An arm whose verdicts have never been shown to fail is not evidence
    # (L-FALSIF). This grades the reader, then runs grade-cases/, which is three
    # ablations aimed at one verdict path each: a knows arm that cannot fire
    # must fall to UNCONSTRAINED even though the cells agree; an unlocatable
    # path must report UNSUPPORTED and not agreement; and a pair read at a cell
    # that cannot carry its respect must report DROPPED.
    Write-Output '--- grading the reader ---'
    & (Join-Path $PSScriptRoot 'ir-wire-selftest.ps1') -Kernel $Kernel
    if ($LASTEXITCODE -ne 0) { Write-Output 'GRADE: reader self-test FAILED'; exit 1 }
    Write-Output ''
    Write-Output '--- grading the verdicts ---'
    & $PSCommandPath -Kernel $Kernel -CaseDir (Join-Path $PSScriptRoot 'grade-cases')
    if ($LASTEXITCODE -ne 0) { Write-Output 'GRADE: a verdict path did not fire'; exit 1 }
    Write-Output ''
    Write-Output '--- the cases ---'
    & $PSCommandPath -Kernel $Kernel
    exit $LASTEXITCODE
}


if (-not $CaseDir) { $CaseDir = Join-Path $PSScriptRoot 'cases' }
# Derived from the script's own location, not the working directory: a fleet
# tool that reads its paths out of the cwd runs somebody else's tree, or none
# (L-SHARED).
$repo = $repoRoot
$work = Join-Path $env:TEMP ("irfid-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Force $work | Out-Null

if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repo $Kernel }
if (-not (Test-Path $Kernel)) { Write-Output "MISSING kernel: $Kernel"; exit 2 }
$kernelHash = (Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0, 16)
Write-Output "kernel: $Kernel [$kernelHash]"
Write-Output "passes: $(if ($Passes) { 'pipeline' } else { 'none' })"
Write-Output ''

$script:compileSeconds = 0.0
$script:compileCount = 0

function Invoke-IrCompile {
    # Returns @{ Wire = <text or $null>; Diags = <string[]>; Seconds = <double> }
    #
    # compile.ps1 in -IrUni mode emits no SIZE: line, so it never writes -Out and
    # always falls through to exit 4 with the whole guest output in -Log. The
    # exit code carries no information here; IR-BEGIN/IR-END and the diagnostic
    # lines do. Gating on the exit code would report every case as a failure.
    param([string]$Src, [string]$Tag)

    $log = Join-Path $work "$Tag.log"
    $out = Join-Path $work "$Tag.ir"
    # Named splatting, not an array: compile.ps1 takes -Src/-Out/-Log
    # positionally too, and an array splat binds -Out's value to -PCore.
    $cargs = @{ Src = $Src; Out = $out; Log = $log; Kernel = $Kernel; IrUni = $true }
    if (-not $Passes -and -not $script:casePasses) { $cargs['Passes'] = 'none' }

    # compile.ps1 resolves some of its own paths against the working directory,
    # so pin it rather than requiring the caller to have cd'd to the root.
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $repo
    try { & (Join-Path $repo 'build\compile.ps1') @cargs 2>&1 | Out-Null }
    finally { Pop-Location }
    $sw.Stop()
    $script:compileSeconds += $sw.Elapsed.TotalSeconds
    $script:compileCount++

    $wire = $null
    try { $wire = Get-IrWireText -LogPath $log } catch { $wire = $null }
    $diags = @()
    if (Test-Path $log) {
        $diags = @(Get-Content $log | Where-Object { $_ -match 'error CDX\d+' })
    }
    return @{ Wire = $wire; Diags = $diags; Seconds = $sw.Elapsed.TotalSeconds }
}

function Get-Cell {
    param([string]$Wire, [string]$Path)
    if (-not $Wire) { return $null }
    $tree = ConvertFrom-IrWire -Text $Wire
    $cell = Get-IrCell -Chapter $tree -Path $Path
    if ($null -eq $cell) { return $null }
    return (Format-IrNode $cell)
}

function Get-AtomCensus {
    # One pass over a corpus: compile each program, and for every occurrence of
    # $Atom on the wire report the form it sits in and its slot there.
    #
    # A program that emits no IR is REFUSED and is excluded from the
    # denominator, not counted as clean. The question is what a program the
    # compiler accepted carries, so a refusal is not a sample of it.
    param([object[]]$Corpus, [string]$Atom, [string]$TagPrefix)

    $sites = @{}
    $clean = 0; $refused = 0; $carrying = 0
    foreach ($f in $Corpus) {
        $r = Invoke-IrCompile -Src $f.FullName -Tag ("$TagPrefix-" + $f.BaseName)
        if (-not $r.Wire) { $refused++; continue }
        $clean++
        $tree = ConvertFrom-IrWire -Text $r.Wire
        $hits = @(Find-IrAtomSites -Node $tree -Atom $Atom)
        if ($hits.Count -gt 0) { $carrying++ }
        foreach ($h in $hits) {
            $key = "$($h.Parent)[$($h.Index)]"
            # Decided from THIS hit's own form. `list-expr` has two producers
            # and only the empty one is lower-empty-list's hardcoded record.
            if ($h.Parent -eq 'list-expr' -and $null -ne $h.ParentNode) {
                $elems = Find-IrChild -Node $h.ParentNode -Head 'elems'
                if ($null -ne $elems) {
                    $key += if ($elems.Count -le 1) { ' (empty literal)' } else { ' (populated)' }
                }
            }
            if (-not $sites.ContainsKey($key)) {
                $sites[$key] = [pscustomobject]@{
                    Count = 0; Programs = [System.Collections.Generic.HashSet[string]]::new()
                }
            }
            $sites[$key].Count++
            [void]$sites[$key].Programs.Add($f.BaseName)
        }
    }
    return [pscustomobject]@{
        Sites = $sites; Clean = $clean; Refused = $refused; Carrying = $carrying
    }
}

function Write-AtomCensus {
    param([object]$Census, [string]$Atom)
    Write-Output "compiled clean: $($Census.Clean)    refused, no IR, not in the denominator: $($Census.Refused)"
    Write-Output "programs carrying at least one ``$Atom``: $($Census.Carrying) of $($Census.Clean)"
    Write-Output ''
    if ($Census.Sites.Count -eq 0) { Write-Output '  (no sites)'; return }
    Write-Output ('{0,-40} {1,11}  {2,8}  {3}' -f 'site (enclosing form and slot)', 'occurrences', 'programs', 'example')
    foreach ($k in ($Census.Sites.Keys | Sort-Object { -$Census.Sites[$_].Count }, { $_ })) {
        $v = $Census.Sites[$k]
        Write-Output ('{0,-40} {1,11}  {2,8}  {3}' -f $k, $v.Count, $v.Programs.Count, ($v.Programs | Sort-Object | Select-Object -First 1))
    }
    # The carriers by name, because a count cannot be acted on. The per-site
    # column shows ONE example, so a reader with "12 of 596" and five example
    # names has no way to reach the other seven and no way to pin a test on
    # them. The names are already held per site; only the printing dropped them.
    $carriers = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($k in $Census.Sites.Keys) {
        foreach ($p in $Census.Sites[$k].Programs) { [void]$carriers.Add($p) }
    }
    if ($carriers.Count -gt 0) {
        Write-Output ''
        Write-Output "carriers ($($carriers.Count)): $(($carriers) -join ', ')"
    }
}

# Read one balanced s-expression starting at $i, or one bare atom. Quoted names
# inside a type (ctd "Foo") hold parens of their own on occasion, so the scan
# tracks the quote state rather than counting parens blind.
function Read-SexprAt {
    param([string]$s, [int]$i)
    if ($i -ge $s.Length) { return '' }
    if ($s[$i] -ne '(') {
        $j = $i
        while ($j -lt $s.Length -and $s[$j] -notin @(' ', ')')) { $j++ }
        return $s.Substring($i, $j - $i)
    }
    $depth = 0; $inq = $false; $j = $i
    while ($j -lt $s.Length) {
        $c = $s[$j]
        if ($inq) {
            if ($c -eq '\') { $j += 2; continue }
            if ($c -eq '"') { $inq = $false }
        } elseif ($c -eq '"') { $inq = $true }
        elseif ($c -eq '(') { $depth++ }
        elseif ($c -eq ')') { $depth--; if ($depth -eq 0) { return $s.Substring($i, $j - $i + 1) } }
        $j++
    }
    return ''
}

# THE STALE-INSTANTIATION SIGNATURE, and it is not an atom, which is why the
# -Census arm cannot see this class at all. When a desugared node is built with
# synthetic-span the checker records nothing for it (record-expr-type skips a
# synthetic span by design), so its lowering asks and gets ErrorTy, and the cell
# keeps whatever polymorphic type came down from the callee. The def then
# DISAGREES WITH ITSELF: one name is bound or used as (tvar N) at one site and
# carries a concrete type at another, in the same def. That self-disagreement is
# the signature, and it is legal-looking: nothing in the text says the checker
# had solved it.
#
# The first cut of this arm asked instead whether a tvar was FREE in its def, and
# that question cannot be asked of this wire at all: lowering strips foralls
# (strip-forall-ty, deep-resolve on ForAllTy), so over 615 programs not one def
# emitted a `forall` token and every flagged def classified BARE by construction,
# list-map-generic included. An instrument that cannot return the other answer is
# not measuring (L-FALSIF). Self-disagreement needs no binder: every name on the
# wire carries its own type, at every site.
#
# Text, not tree: (param "x" T), (let "x" T ...), (var-pat "x" T) and (name "x" T)
# are the four sites where the emitter writes a name beside its type
# (Emit/IRTextEmitter.codex 362, 387, 420, 446), so a per-def scan is enough.
function Find-DisagreeingNames {
    param([string]$Wire)
    $w = $Wire -replace "`r", ''
    $out = @()
    foreach ($m in [regex]::Matches($w, '\(def "([^"]+)"')) {
        $start = $m.Index
        $nxt = $w.IndexOf('(def "', $start + 6)
        $len = if ($nxt -gt 0) { $nxt - $start } else { $w.Length - $start }
        $body = $w.Substring($start, $len)
        $seen = @{}
        foreach ($s in [regex]::Matches($body, '\((?:param|let|var-pat|name) "((?:[^"\\]|\\.)*)" ')) {
            $ty = Read-SexprAt -s $body -i ($s.Index + $s.Length)
            if (-not $ty) { continue }
            $n = $s.Groups[1].Value
            if (-not $seen.ContainsKey($n)) { $seen[$n] = [System.Collections.Generic.HashSet[string]]::new() }
            [void]$seen[$n].Add($ty)
        }
        foreach ($n in $seen.Keys) {
            if ($seen[$n].Count -lt 2) { continue }
            $tv = @($seen[$n] | Where-Object { $_ -match '^\(tvar \d+\)$' })
            $con = @($seen[$n] | Where-Object { $_ -notmatch '^\(tvar \d+\)$' })
            # Two tvars of different id is ordinary alpha-renaming, and two concrete
            # types is a shadowed name; only the mixed case is the signature.
            if ($tv.Count -gt 0 -and $con.Count -gt 0) {
                $out += [pscustomobject]@{
                    Def = $m.Groups[1].Value; Name = $n
                    Tvar = ($tv | Sort-Object | Select-Object -First 1)
                    Concrete = ($con | Sort-Object | Select-Object -First 1)
                }
            }
        }
    }
    $out
}

if ($Disagree) {
    if (-not $CorpusDir) { $CorpusDir = Join-Path $repo 'codex\test' }
    $all = @(Get-ChildItem $CorpusDir -Filter '*.codex' | Sort-Object Name)
    $corpus = $all
    # A smoke over a stride can miss every flagging program and then report zero,
    # which proves the plumbing runs and NOT that the detector fires. -Programs
    # names the set so a known flagger can be in it (L-FALSIF).
    if ($Programs.Count -gt 0) {
        $corpus = @($all | Where-Object { $Programs -contains $_.BaseName })
        $missing = @($Programs | Where-Object { $n = $_; -not ($all | Where-Object { $_.BaseName -eq $n }) })
        if ($missing.Count) { Write-Output ('NOT IN CORPUS: ' + ($missing -join ', ')); exit 2 }
        Write-Output ('named set: ' + $corpus.Count + ' program(s)')
    }
    if ($Limit -gt 0 -and $Limit -lt $all.Count) {
        $step = [Math]::Ceiling($all.Count / $Limit)
        $corpus = @(for ($i = 0; $i -lt $all.Count; $i += $step) { $all[$i] })
        Write-Output "corpus: $($corpus.Count) of $($all.Count), every ${step}th by name. CAPPED by -Limit $Limit."
    }
    $work = Join-Path ([IO.Path]::GetTempPath()) ("irfid-ft-" + [Guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $script:WorkDir = $work
    $clean = 0; $refused = 0; $carrying = 0
    $byDef = @{}
    foreach ($file in $corpus) {
        $r = Invoke-IrCompile -Src $file.FullName -Tag ("ft-" + $file.BaseName)
        if (-not $r.Wire) { $refused++; continue }
        $clean++
        $hits = @(Find-DisagreeingNames -Wire $r.Wire)
        if ($hits.Count -gt 0) { $carrying++ }
        foreach ($h in $hits) {
            if (-not $byDef.ContainsKey($h.Def)) {
                $byDef[$h.Def] = [pscustomobject]@{ Count = 0; Example = @(); Programs = [System.Collections.Generic.HashSet[string]]::new() }
            }
            $byDef[$h.Def].Count++
            # Per program, not one example for the def: __lam_2 in two programs is
            # two different lambdas, and a single stored example attributes one
            # program's disagreement to the other.
            $byDef[$h.Def].Example += @("$($file.BaseName) $($h.Name): $($h.Tvar) vs $($h.Concrete)")
            [void]$byDef[$h.Def].Programs.Add($file.BaseName)
        }
    }
    Write-Output ""
    Write-Output "compiled clean: $clean    refused, no IR, not in the denominator: $refused"
    Write-Output "programs carrying a def that disagrees with itself: $carrying of $clean"
    Write-Output ""
    $defs = @($byDef.Keys)
    $progs = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($k in $defs) { foreach ($p in $byDef[$k].Programs) { [void]$progs.Add($p) } }
    Write-Output ("DISAGREEING defs (one name, a tvar at one site and a concrete type at another): " + $defs.Count + " over " + $progs.Count + " program(s)")
    Write-Output ''
    foreach ($k in ($defs | Sort-Object { -$byDef[$_].Programs.Count })) {
        Write-Output ("  {0,-34} {1,5} sites  {2,4} programs" -f $k, $byDef[$k].Count, $byDef[$k].Programs.Count)
        foreach ($ex in $byDef[$k].Example) { Write-Output ('      ' + $ex) }
        Write-Output ('      ' + (($byDef[$k].Programs | Sort-Object) -join ', '))
    }
    if ($defs.Count -eq 0) {
        Write-Output '  none. The detector returned nothing over this corpus.'
    }
    # The baseline is the guard. A count is not a guard on its own: somebody has
    # to be told when it moves, and the direction that matters is UP. Down is
    # refused too, because a drop nobody predicted is either a fix worth
    # recording or the detector having stopped working (L-FALSIF).
    $sites = 0
    foreach ($k in $defs) { $sites += $byDef[$k].Count }
    Write-Output ''
    Write-Output "sites: $sites"
    if ($Expect -ge 0 -and $sites -ne $Expect) {
        Write-Output "REFUSED: expected $Expect site(s), found $sites. Name the change or fix it."
        try { [System.IO.Directory]::Delete($work, $true) } catch { }
        exit 1
    }
    try { [System.IO.Directory]::Delete($work, $true) } catch { }
    exit 0
}

if ($Census) {
    # COMPILER-30's ruling says that once the ErrorTy split lands, a clean
    # compile carries no `error` on the wire. It still does, and a sample of one
    # program cannot say why. This counts every occurrence over a corpus of
    # programs the compiler ACCEPTED and attributes each to the form it sits in,
    # so the answer is a list of sites somebody can act on rather than a number
    # to quote (L-PARTIAL: count sites, not totals).
    #
    # The census is a MEASUREMENT and does not gate: a nonzero answer is the
    # finding, not a failure, so it exits 0 either way. -Calibrate is the part
    # that makes a reading worth anything, and it is what exits nonzero.
    if (-not $CorpusDir) { $CorpusDir = Join-Path $repo 'codex\test' }
    if (-not (Test-Path $CorpusDir)) { Write-Output "MISSING corpus: $CorpusDir"; exit 2 }

    if ($Calibrate) {
        # An arm whose verdict has never been shown to move is not evidence
        # (L-FALSIF), and a census is the shape most prone to it: "no sites"
        # reads as an all-clear whether the invariant holds or the reader is
        # blind. Two arms over one corpus, aimed in opposite directions.
        #
        # The positive arm is a program that REACHES the site the atom is
        # emitted at, beside a sibling differing in exactly one respect that
        # must come back clean, so the pair also proves the census is reading
        # the program rather than the directory. For `error` that is an empty
        # list literal with NO type expectation, the one input reaching
        # Lowering.codex's `lower-empty-list ... is otherwise ->` record,
        # against a constrained sibling taking the ListTy arm. For `noexpect`
        # it is a single `lazy` cell against the same expression written
        # eagerly.
        #
        # BOTH HALVES OF THE PAIR ARE ATOM-SPECIFIC, so the corpus and the
        # expected site are selected BY ATOM. They were fixed until 2026-08-27
        # and existed only for `error`, while line 237 already passed $Atom
        # through: `-Atom noexpect -Calibrate` therefore asked the empty-list
        # corpus for a sentinel it cannot produce, found none, and reported
        # that the census could not see what it exists to count. The reader was
        # fine and the corpus was aimed elsewhere, which is the same
        # instrument-built-from-the-wrong-subject failure the arms exist to
        # catch, one level up. An atom with NO corpus is refused BY NAME rather
        # than failing as an arm, because "nobody has calibrated this atom" and
        # "the census is blind" are different problems with different fixes.
        #
        # The negative arm asks the same corpus for an atom that is not in the
        # wire's vocabulary. A reader that answers the same way for everything
        # you ask it is the failure this catches, and it is cheap.
        #
        # The site patterns are -like patterns and the brackets MUST be escaped.
        # `fn[2]` unescaped is a character class: it fails to match the literal
        # `fn[2]` and matches `fn2` instead, so an unescaped pattern is an
        # assertion that cannot fire (verified both directions, 2026-08-27).
        $calSite = @{
            'error'    = 'list-expr*empty literal*'
            'noexpect' = 'fn`[2`]'
        }
        # AN ATOM WHOSE DEFECT IS CLOSED CANNOT CALIBRATE ITSELF, AND THAT IS NOT
        # A BROKEN INSTRUMENT -- IT IS THE INSTRUMENT SUCCEEDING.
        #
        # The positive arm above is a SPECIMEN of the defect: for `noexpect` it is
        # a single `lazy` cell that reached the site. COMPILER-32 closed every
        # route to it (2026-08-28, 12 carriers and 108 occurrences to zero over
        # three steps), so the specimen now compiles clean and the arm can never
        # fire again. Left alone it fails forever and reads as "the census went
        # blind", which is the opposite of what happened.
        #
        # Deleting the arm is the wrong repair, because then nothing proves the
        # reader can see anything at all and a zero means nothing (L-FALSIF). So a
        # closed atom BORROWS A LIVE ATOM'S ARM: the reader is proven on an atom
        # that still occurs, against that atom's own corpus and expected site, so
        # the arm can still fail. The closed atom's own corpus becomes a
        # REGRESSION guard instead -- it must come back EMPTY, and a nonzero there
        # means the defect is back.
        #
        # The census's role flips with it, from "count the carriers" to "refuse
        # any carrier". Remove an entry here only when reopening the defect.
        #
        # THIS TABLE WAS INVERTED, AND BOTH HALVES ARE MEASURED (fester,
        # 2026-09-07, seed 39D40A84, one compile of each specimen through
        # `compile.ps1 -IrUni`):
        #
        #   `error` IS closed and was not registered. Steve Howell's PR 101
        #   (absorbed 2026-09-01) files the fresh variable under the
        #   literal's span, so census-calibration\error\empty-unconstrained
        #   now lowers its empty list to `(list-expr (elems) (tvar 269))` and
        #   carries no `error` at all -- while `list-expr` IS in that wire, so
        #   the reader sees fine. Left unregistered its positive arm cannot
        #   fire and the run aborts with "the census cannot see what it exists
        #   to count", which is the exact misreading the comment above warns
        #   about, and it blocked a granted 626-program run.
        #
        #   `noexpect` is NOT closed and was registered as though it were.
        #   census-calibration\noexpect\lazy-cell still emits
        #   `(let "c" noexpect ...)` at head, so that arm fires and is the
        #   live one to borrow.
        #
        # Do not restore the old direction without compiling both specimens
        # first: a table calling an atom closed while its specimen still
        # carries it disables a working arm, and one calling an atom live
        # after it is fixed blocks every census behind a failure that is
        # really a success.
        $calClosed = @{
            'error' = 'noexpect'
        }
        $calRoot = Join-Path $PSScriptRoot 'census-calibration'
        $calDir = Join-Path $calRoot $Atom
        if (-not (Test-Path $calDir)) {
            $known = @(Get-ChildItem $calRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
            Write-Output "NO CALIBRATION CORPUS for atom ``$Atom``: expected $calDir"
            Write-Output "  calibrated atoms: $(if ($known.Count) { $known -join ', ' } else { '(none)' })"
            Write-Output '  A census of an uncalibrated atom is not evidence. Add a carrier and a'
            Write-Output '  non-carrier that differ in exactly one respect, and an expected site.'
            Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
            exit 2
        }
        if (-not $calSite.ContainsKey($Atom)) {
            Write-Output "NO EXPECTED SITE for atom ``$Atom``; add one beside its corpus in `$calSite."
            Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
            exit 2
        }
        $cal = @(Get-ChildItem $calDir -Filter '*.codex' | Sort-Object Name)
        Write-Output "--- calibrating the census on $($cal.Count) program(s) ---"
        Write-Output ''

        $fail = 0

        $isClosed = $calClosed.ContainsKey($Atom)
        $armAtom  = if ($isClosed) { $calClosed[$Atom] } else { $Atom }
        $armDir   = if ($isClosed) { Join-Path $calRoot $armAtom } else { $calDir }
        $armCal   = if ($isClosed) { @(Get-ChildItem $armDir -Filter '*.codex' | Sort-Object Name) } else { $cal }
        $wantSite = $calSite[$armAtom]

        if ($isClosed) {
            Write-Output "``$Atom`` is a CLOSED atom: its own specimen is fixed and can no longer arm."
            Write-Output "  the reader is proven on ``$armAtom`` instead, and ``$Atom`` must be ABSENT."
            Write-Output ''
        }

        Write-Output "positive arm: ``$armAtom`` must be found, and only in the unconstrained program"
        $pos = Get-AtomCensus -Corpus $armCal -Atom $armAtom -TagPrefix 'cal-pos'
        Write-AtomCensus -Census $pos -Atom $armAtom
        $wantHit = @($pos.Sites.Keys | Where-Object { $_ -like $wantSite })
        if ($pos.Refused -gt 0) {
            Write-Output "  FAIL: $($pos.Refused) calibration program(s) did not compile; the arm measured nothing"
            $fail++
        }
        elseif ($wantHit.Count -eq 0) {
            Write-Output "  FAIL: no ``$wantSite`` site fired. The census cannot see what it exists to count."
            $fail++
        }
        elseif ($pos.Carrying -ne 1) {
            Write-Output "  FAIL: $($pos.Carrying) of $($pos.Clean) programs carried it; exactly the unconstrained one should"
            $fail++
        }
        else { Write-Output '  ok: found, and only in the unconstrained program' }
        Write-Output ''

        if ($isClosed) {
            Write-Output "regression arm: ``$Atom`` is closed, so its own specimen must be EMPTY"
            $reg = Get-AtomCensus -Corpus $cal -Atom $Atom -TagPrefix 'cal-reg'
            Write-AtomCensus -Census $reg -Atom $Atom
            if ($reg.Refused -gt 0) {
                Write-Output "  FAIL: $($reg.Refused) specimen program(s) did not compile; the arm measured nothing"
                $fail++
            }
            elseif ($reg.Carrying -ne 0) {
                Write-Output "  FAIL: the specimen carries ``$Atom`` again. The defect this atom tracks is BACK."
                $fail++
            }
            else { Write-Output "  ok: the specimen is clean, so the closure still holds" }
            Write-Output ''
        }

        Write-Output 'negative arm: an atom outside the wire vocabulary must be found NOWHERE'
        $neg = Get-AtomCensus -Corpus $cal -Atom 'zzz-not-a-wire-atom' -TagPrefix 'cal-neg'
        Write-AtomCensus -Census $neg -Atom 'zzz-not-a-wire-atom'
        if ($neg.Carrying -ne 0) {
            Write-Output '  FAIL: the census reported sites for an atom that does not exist. It answers the same way whatever you ask.'
            $fail++
        }
        else { Write-Output '  ok: reported nothing' }
        Write-Output ''

        if ($fail -gt 0) {
            Write-Output "CALIBRATION FAILED ($fail arm(s)). A census run now is not evidence."
            Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
            exit 1
        }
        Write-Output 'CALIBRATION OK: the census fires on the input it is aimed at and is silent on one it is not.'
        Write-Output ''
    }

    $all = @(Get-ChildItem $CorpusDir -Filter '*.codex' | Sort-Object Name)
    $corpus = $all
    # A smoke over a stride can miss every flagging program and then report zero,
    # which proves the plumbing runs and NOT that the detector fires. -Programs
    # names the set so a known flagger can be in it (L-FALSIF).
    if ($Programs.Count -gt 0) {
        $corpus = @($all | Where-Object { $Programs -contains $_.BaseName })
        $missing = @($Programs | Where-Object { $n = $_; -not ($all | Where-Object { $_.BaseName -eq $n }) })
        if ($missing.Count) { Write-Output ('NOT IN CORPUS: ' + ($missing -join ', ')); exit 2 }
        Write-Output ('named set: ' + $corpus.Count + ' program(s)')
    }
    if ($Limit -gt 0 -and $all.Count -gt $Limit) {
        # A cap that is not reported reads as full coverage. Take a SPREAD
        # rather than the alphabetical head: a prefix of one directory is one
        # naming family and one authoring era, which is the corpus shape
        # L-CONSTRUCT is about.
        $step = [Math]::Ceiling($all.Count / $Limit)
        $corpus = @(for ($i = 0; $i -lt $all.Count; $i += $step) { $all[$i] })
        Write-Output "corpus: $($corpus.Count) of $($all.Count) programs in $CorpusDir, every ${step}th by name"
        Write-Output "        CAPPED by -Limit $Limit. $($all.Count - $corpus.Count) program(s) not compiled."
    }
    else {
        Write-Output "corpus: all $($corpus.Count) programs in $CorpusDir"
    }
    Write-Output "atom:   $Atom"
    Write-Output ''

    $c = Get-AtomCensus -Corpus $corpus -Atom $Atom -TagPrefix 'census'
    Write-AtomCensus -Census $c -Atom $Atom
    Write-Output ''
    if ($c.Carrying -eq 0) {
        Write-Output "NO SITES: no program the compiler accepted carries ``$Atom`` on the wire."
    }
    else {
        Write-Output "Each site above is a place the wire says ``$Atom`` for a program the compiler reported clean."
    }
    Write-Output ("compiles: {0} in {1:N1}s" -f $script:compileCount, $script:compileSeconds)
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    exit 0
}

$cases = @(Get-ChildItem -Path $CaseDir -Filter 'case.psd1' -Recurse | Sort-Object FullName)
if ($Only) { $cases = @($cases | Where-Object { $_.Directory.Name -like $Only }) }
if ($cases.Count -eq 0) { Write-Output "no cases under $CaseDir"; exit 2 }

$rows = @()
$unexpected = 0

foreach ($cf in $cases) {
    $dir = $cf.Directory.FullName
    $name = $cf.Directory.Name
    $c = Import-PowerShellDataFile -LiteralPath $cf.FullName
    # A case whose subject exists only after the optimizer ran (an inlined
    # helper, a folded literal) says so with `passes = $true` and is compiled
    # through the pipeline whatever the run's own mode; every other case keeps
    # the run's mode. -Grade does not forward -Passes to its children, so this
    # key is the only way such a case is graded at a release.
    $script:casePasses = [bool]($c.ContainsKey('passes') -and $c.passes)

    $ra = Invoke-IrCompile -Src (Join-Path $dir $c.a) -Tag "$name-a"
    $rb = Invoke-IrCompile -Src (Join-Path $dir $c.b) -Tag "$name-b"

    $verdict = ''
    $evidence = ''

    # The no-expectation marker is a LOWERING-INTERNAL sentinel and must never
    # be recorded as a node's type. It is emitted as its own atom rather than
    # folded into `error` precisely so that a leak is visible here instead of
    # being indistinguishable from a genuine type failure. Any occurrence is a
    # compiler defect regardless of what the case's own cell says.
    $leak = @($ra.Wire, $rb.Wire) | Where-Object { $_ -and $_ -match '\bnoexpect\b' }
    if ($leak.Count -gt 0) {
        $verdict = 'LEAK'
        $evidence = 'the no-expectation marker reached the wire; it is lowering-internal and must never be recorded as a type'
    }
    elseif (-not $ra.Wire -or -not $rb.Wire) {
        $verdict = 'ERROR'
        $which = if (-not $ra.Wire) { 'a' } else { 'b' }
        $d = if (-not $ra.Wire) { $ra.Diags } else { $rb.Diags }
        $evidence = "arm $which emitted no IR: $($d -join '; ')"
    }
    else {
        $ca = Get-Cell -Wire $ra.Wire -Path $c.path
        $cb = Get-Cell -Wire $rb.Wire -Path $c.path

        if ($null -eq $ca -or $null -eq $cb) {
            $verdict = 'UNSUPPORTED'
            $evidence = "reader could not locate $($c.path)"
        }
        else {
            # The knows arm is what licenses a CARRIED/DROPPED claim at all.
            $knows = $false
            $knowsDetail = 'no knows arm'
            if ($c.ContainsKey('knows')) {
                $rk = Invoke-IrCompile -Src (Join-Path $dir $c.knows) -Tag "$name-knows"
                $hit = @($rk.Diags | Where-Object { $_ -match [regex]::Escape($c.knowsCode) })
                if ($hit.Count -gt 0) {
                    $knows = $true
                    $knowsDetail = $c.knowsCode
                } else {
                    $knowsDetail = "expected $($c.knowsCode), got: $(if ($rk.Diags) { ($rk.Diags -join '; ') } else { 'no diagnostic -- the checker ACCEPTED it' })"
                }
            }

            if (-not $knows) {
                $verdict = 'UNCONSTRAINED'
                $evidence = $knowsDetail
            }
            elseif ($ca -ne $cb) {
                $verdict = 'CARRIED'
                $evidence = "$ca  |  $cb"
            }
            else {
                $verdict = 'DROPPED'
                $evidence = "both arms: $ca"
            }
        }
    }

    $expected = $c.expect
    $flag = if ($verdict -eq $expected) { '   ' } else { $unexpected++; '>>>' }
    Write-Output "$flag $($name.PadRight(22)) $($verdict.PadRight(14)) (expected $expected)"
    Write-Output "      respect: $($c.respect)"
    Write-Output "      $($c.path): $evidence"
    Write-Output ''
    $rows += [pscustomobject]@{ Case = $name; Verdict = $verdict; Expected = $expected }
}

$avg = if ($script:compileCount) { $script:compileSeconds / $script:compileCount } else { 0 }
Write-Output "cost: $($script:compileCount) compiles, $([math]::Round($script:compileSeconds,1)) s total, $([math]::Round($avg,1)) s per compile"
Write-Output "cases: $($rows.Count), unexpected: $unexpected"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if ($unexpected -gt 0) { exit 1 }
exit 0

