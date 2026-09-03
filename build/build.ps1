# build.ps1 -- full compiler build, verification, and test.
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    # Non-public "internal" gate (Damian, 2026-08-16). Always proves the seed
    # is a byte-identical self-fixed-point that boots; runs a regression phase
    # ONLY when a file it depends on changed in this workspace. Everything else
    # is caught by the next full gate. A public/release build passes no
    # -Internal and runs every phase.
    [switch]$Internal
)

# On success, prints only a story. On failure, prints technical details.
# Phases: clean -> source -> CDX build -> sign -> canary -> jonquil ->
#         sem-equiv -> text fixed point -> CDX fixed point -> battery ->
#         oracles -> refusals -> plugs -> generators -> decks -> app sweep.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $PSScriptRoot 'output'
$SeedCdx = Join-Path $Repo 'seed\Codex.cdx'
$SutCdx = Join-Path $OutDir 'Sut.cdx'
$CodexSrc = Join-Path $OutDir 'Codex.codex'
$Concat = Join-Path $PSScriptRoot 'concat-codex-self.ps1'
$Compile = Join-Path $PSScriptRoot 'compile.ps1'
$BuildLog = Join-Path $OutDir 'build.log'

# Smart coverage for the internal gate. The CORE phases (clean, source-concat,
# the pre-build guards, cdx-build, sign, canary, the CDX
# fixed point, test-bvt, test-compile, oracles, check-errors) always run: they are what certify the seed is
# a byte-identical self-fixed-point that boots. The regression phases below run
# only when a file they depend on changed in THIS workspace; skipped ones are
# caught by the next full gate. Mapping, by what actually feeds each phase:
#   jonquil                     <- codex/compiler   (codegen)
#   vm-differential             <- codex/compiler or codex/build or build:
#     codegen is its SUBJECT, but its INSTRUMENT is two VM hosts and which
#     host runs is decided in build/vm-config.ps1. Keyed on the subject alone
#     it went silently stale: reek 19219 changed host selection and this phase
#     skipped as not implicated (L-INSTRUMENT). A trigger must cover the files
#     that can change a phase's ANSWER, not only the ones it is about.
#   plug-*                      <- codex/plugs or codex/compiler (codegen feeds plugs)
#   gen-scripts / deck-headroom <- codex/build or build or codex/compiler:
#     both are COMPILED answers, not static ones. gen-scripts compiles every
#     generator with the current kernel and diffs the emitted text, and
#     deck-headroom's per-unit `used` comes from running the compiler while
#     its per-point divisor is demand-check-floor in Core/BuildSettings.codex.
#     Measured: that constant went 648 to 704 MB at reek 19178 and every
#     build-quire unit moved (cdxtope 52 of 64 to 48). Keyed on $tBuild alone
#     a pure compiler change skips the phase that would see it. ~1 change in
#     50 on main is compiler-without-build, so this costs 31 s and 63 s that
#     rarely.
#   app-sweep                   <- apps or codex/compiler (the compiler builds the apps)
#   run-list                    <- tools/codex-vm.c or .exe, or build/check-run-list.ps1:
#     a per-file trigger, which the audit above calls the precise fix and
#     declined for the six existing phases only because widening them to
#     $tBuild fires 11 times in 50. This phase costs 5.7 s, so precision is
#     affordable here: its subject is the -run-list supervisor in codex-vm and
#     its instrument is that script, and no codegen change can move its answer
#     because every arm compares two runs of the SAME kernels.
#   text-stage2 / text-fixedpoint <- the front end and the text printer
#   text-stage1 / sem-equiv     <- WHENEVER THE CORE RUNS (Damian, 2026-09-02).
#     The trigger was $tSemantic, and its residue was the rest of the compiler:
#     a chapter outside Syntax/Ast/CodexEmitter/TextFormat/SourceText/opening/
#     Lowering can break semantic equivalence and no -Internal run could see it
#     (L-NOGATE). $coreRuns is the set that can move the compiler binary, so it
#     is also the set that can move this answer; text-stage1 rides with it
#     because sem-equiv consumes the stage1.codex only it produces. Measured
#     2026-09-02: text-stage1 34.5 s, sem-equiv 59.6 s. A run with the core
#     deferred changes neither side, so it is not made to pay the 96 s.
# test-compile is NOT in that map. It is never skipped, only scoped: with
# -Internal it compiles the test chapters that CITE a chapter changed here
# (11 chapters and 12s for a GopComposite change), and the full gate compiles
# all 1,400 (1,202s at 4 ways). red's ruling 2026-08-20 on the measured cost.
# test-run is not in the map either, for the same reason: it RUNS the ones
# test-compile selected that carry an .expected, and an empty cited set costs
# nothing. Compiling a chapter is not running it, and only running it can see
# a program that emits clean IR and answers the wrong values (L-NOGATE).
# The text leg is conditional as of 2026-08-20 (Damian). Measured at head
# 18157: the four text phases cost 99.4s of a 644.1s gate and the CDX fixed
# point that certifies the shipped artifact costs 25.5s. Their unique subject
# is the .codex printer, which no codegen change can move: cdx-stage1 parses
# the same source, so what the text leg alone proves is that Emit/CodexEmitter
# round-trips it. The residual class is a chapter outside the file set below
# written with a construct the printer mishandles; that is caught by the next
# full gate rather than at the CL, and it is the trade this switch makes.
$SkipPhases = [System.Collections.Generic.HashSet[string]]::new()
$tCompiler = $false
$changedPlugs = @()
$coreRuns = $true
if ($Internal) {
    $changed = @()
    try { $changed = @(p4 opened 2>$null | ForEach-Object { (($_ -split '#')[0]) -replace '^//[^/]+/[^/]+/','' } | Where-Object { $_ }) } catch { }
    $stream = ''
    try { $stream = ((p4 client -o 2>$null | Where-Object { $_ -match '^Stream:' }) -replace '^Stream:\s*', '').Trim() } catch { }
    if ($stream -and $stream -ne '//Codex/main') {
        $ahead = @()
        try { $ahead = @(p4 diff2 -q '//Codex/main/...' ($stream + '/...') 2>$null | Where-Object { $_ -match '^==== ' } | ForEach-Object { $m = [regex]::Match($_, '//[^/\s]+/[^/\s]+/([^#\s]+)'); if ($m.Success) { $m.Groups[1].Value } } | Where-Object { $_ }) } catch { }
        # p4 diff2 reports a file that differs in EITHER DIRECTION, so a
        # stream that is BEHIND main reads another lane's landed files as its
        # own change. Measured 2026-09-02 with NOTHING opened: 'changed here'
        # named three files main was ahead on, one of them under build/, and
        # the whole fixed-point core ran for another lane's work.
        # SUBTRACTING what a merge-down would bring was the first repair and it
        # is WRONG: a code arc gates once at the end, so a stream CL is not
        # gated at submit, and a file changed on BOTH sides would be dropped
        # along with the lane's own change (root, 2026-09-02). Refuse instead.
        # Merging down before a gate was already the rule and had no runner,
        # which is L-BODY's shape; after the merge the union is exact and there
        # is no edge left to reason about.
        $incoming = @()
        try { $incoming = @(p4 merge -n -S $stream -r 2>$null | ForEach-Object { $m = [regex]::Match($_, '^//[^/\s]+/[^/\s]+/([^#\s]+)'); if ($m.Success) { $m.Groups[1].Value } } | Where-Object { $_ }) } catch { }
        if ($incoming.Count -gt 0) {
            Write-Host ('  REFUSED: this stream is BEHIND main by ' + $incoming.Count + ' file(s), so the gate cannot tell your change from another lane''s.') -ForegroundColor Red
            Write-Host ('    ' + (($incoming | Select-Object -First 5) -join ', '))
            Write-Host '    Merge down first. The scope is p4 opened UNION diff2, and diff2 reports a file that differs in EITHER direction.'
            exit 1
        }
        $changed += @($ahead)
    }
    # SOURCE, not the directory: codex/compiler holds 64 .codex and one
    # prose register that every lane edits, and matching the directory made a
    # docs-only CL pay all eight compiler phases (Build.md). A non-source file
    # that DOES decide an answer keeps its own trigger, as app-sweep-baseline does.
    $tCompiler = [bool]($changed | Where-Object { $_ -match '^codex/compiler/.*\.codex$' })
    $tPlugs    = [bool]($changed | Where-Object { $_ -match '^codex/plugs/' })
    # plug-binary and plug-smoke grade a HARDCODED list, so a change to any of
    # the other 46 plugs ran both phases over plugs it did not touch and came
    # back green. Name the plugs that actually changed so they are graded too.
    $changedPlugs = @($changed | ForEach-Object { $m = [regex]::Match($_, '^codex/plugs/([^/]+)/'); if ($m.Success) { $m.Groups[1].Value } } | Where-Object { $_ -and $_ -ne 'common' -and $_ -ne 'test-input' } | Select-Object -Unique)
    $tBuild    = [bool]($changed | Where-Object { $_ -match '^(codex/build/|build/)' })
    $tApps     = [bool]($changed | Where-Object { $_ -match '^apps/' })
    $tFrontEnd = [bool]($changed | Where-Object { $_ -match '^codex/compiler/(Syntax|Ast)/' -or $_ -match '^codex/compiler/Emit/CodexEmitter\.codex$' -or $_ -match '^codex/compiler/Core/(TextFormat|SourceText)\.codex$' })
    $tVm       = [bool]($changed | Where-Object { $_ -match '^tools/codex-vm\.(c|exe)$' -or $_ -match '^build/check-run-list\.ps1$' })
    # A gate runs only the steps the change can affect (Damian, 2026-09-02).
    # $tKernel is the set that can move the COMPILER BINARY, so it is what
    # decides whether the fixed-point core is worth running at all. Wide on
    # the foreword on purpose: most foreword chapters are not in the compiler
    # unit (measured, Foreword--Fat32 is absent and Foreword--Fat16 present),
    # and a trigger too wide costs time where one too narrow ships a
    # miscompile. The two errors are not the same size.
    $tForeword = [bool]($changed | Where-Object { $_ -match '^codex/foreword/' })
    $tSeed     = [bool]($changed | Where-Object { $_ -match '^seed/' })
    $tKernel   = [bool]($tCompiler -or $tForeword -or $tSeed -or $tBuild)
    # The BVT's SUBJECTS are codex/test chapters, hardcoded in bvt.ps1, so a
    # test-only CL that skipped it would skip the phase grading the file it
    # changed. Wider than that list on purpose: reading the list from here
    # couples the two scripts, and 30 s on a test CL is the cheaper error.
    $tTest     = [bool]($changed | Where-Object { $_ -match '^codex/test/' })
    # run-list is here for its SUBJECTS, not its answer: its five kernels are
    # core outputs, and its arms compare two runs of the SAME kernels, so a
    # stale one moves both sides equally but an ABSENT one refuses. The core
    # runs to produce inputs, which is a dependency and not a check.
    $coreRuns = [bool]($tKernel -or $tVm)
    $runPhase = [ordered]@{
        'clean'           = $coreRuns
        'source-concat'   = $coreRuns
        'cdx-build'       = $coreRuns
        'sign'            = $coreRuns
        'canary'          = $coreRuns
        'cdx-stage1'      = $coreRuns
        'cdx-fixedpoint'  = $coreRuns
        'test-bvt'        = ($tKernel -or $tTest)
        'oracles'         = ($tKernel -or $tTest)
        'check-errors'    = ($tKernel -or $tTest)
        'jonquil'         = $tCompiler
        'plug-binary'     = ($tPlugs -or $tCompiler)
        'cross-smoke'     = ($tPlugs -or $tCompiler)
        'plug-smoke'      = ($tPlugs -or $tCompiler)
        'gen-scripts'     = ($tBuild -or $tCompiler)
        'vm-differential' = ($tCompiler -or $tBuild)
        'deck-headroom'   = ($tBuild -or $tCompiler)
        'app-sweep'       = ($tApps -or $tCompiler)
        'run-list'        = $tVm
        'text-stage1'     = $coreRuns
        'sem-equiv'       = $coreRuns
        'text-stage2'     = $tFrontEnd
        'text-fixedpoint' = $tFrontEnd
    }
    foreach ($k in $runPhase.Keys) { if (-not $runPhase[$k]) { [void]$SkipPhases.Add($k) } }
    $ran = @($runPhase.Keys | Where-Object { $runPhase[$_] })
    Write-Host ('  [internal gate] changed here: ' + $(if ($changed.Count) { ($changed | Sort-Object -Unique) -join ', ' } else { 'nothing opened' }))
    Write-Host ('  [internal gate] running: ' + $(if ($ran.Count) { ($ran -join ', ') } else { '(nothing implicated)' }))
    Write-Host ('  [internal gate] deferred to the next full gate: ' + $(if ($SkipPhases.Count) { (@($SkipPhases) | Sort-Object) -join ', ' } else { '(none)' }))
    # THE LINE A CL DESCRIPTION MAY QUOTE. A deferred core does not run
    # SUT === stage1, so a run that skipped it cannot claim the fixed point
    # and must say what it DID grade with instead. Printing the truthful
    # claim here is what stops six agents pasting a proof their run never
    # produced; the alternative was asking them to remember.
    if (-not $coreRuns) {
        $seedHash = (Get-FileHash $SeedCdx -Algorithm SHA256).Hash
        # A locally built seed is not the compiler of record, and grading
        # against one is the stale-kernel wrong answer wearing another hat.
        # Silent when p4 cannot answer: this must not turn a gate red for
        # being offline, only for being wrong.
        $depotSeed = Join-Path ([System.IO.Path]::GetTempPath()) ('depot-seed-' + [System.Guid]::NewGuid().ToString('N') + '.cdx')
        $depotHash = ''
        try { & p4 print -q -o $depotSeed '//Codex/main/seed/Codex.cdx' 2>$null | Out-Null; if (Test-Path $depotSeed) { $depotHash = (Get-FileHash $depotSeed -Algorithm SHA256).Hash } } catch { }
        Remove-Item -Force $depotSeed -ErrorAction SilentlyContinue
        if ($depotHash -and $depotHash -ne $seedHash) {
            Write-Host '  REFUSED: the core is deferred, so this run grades with seed\Codex.cdx -- and that file is NOT the depot seed.' -ForegroundColor Red
            Write-Host ('    workspace ' + $seedHash.Substring(0,16) + '   depot ' + $depotHash.Substring(0,16))
            Write-Host '    Sync the seed, or make a change that runs the core.'
            exit 1
        }
        $SutCdx = $SeedCdx
        Write-Host ('  [internal gate] core SKIPPED; graded with depot seed ' + $seedHash.Substring(0,16))
    }
}

# A compile log ENDS in hundreds of `info CDX4010: bounds proven` lines, so
# its tail is the one slice guaranteed to say nothing about why a build
# failed. Show the lines that decide the build, and keep the log: on
# 2026-07-28 a red cdx-build printed ten blank lines and deleted its only
# evidence, and reconstructing what it had already known cost an hour.
function Show-CompileFailure {
    param([string]$LogFile, [string]$Kept)
    $diag = @()
    if (Test-Path $LogFile) {
        $diag = @(Get-Content $LogFile | Where-Object { $_ -match 'error CDX|CODEGEN-ERRORS|CODEGEN-HALTED' } | Select-Object -First 15)
    }
    if ($diag.Count -gt 0) {
        $diag | ForEach-Object { Write-Host "  $_" }
    } else {
        $tail = @()
        if (Test-Path $LogFile) { $tail = @(Get-Content $LogFile | Select-Object -Last 10) }
        if ($tail.Count -eq 0) {
            Write-Host '  the compile log is EMPTY: the compiler produced no output at all.'
            Write-Host '  That points at the VM or the machine (memory, a killed process)'
            Write-Host '  rather than at a rejected program, which would have left a'
            Write-Host '  diagnostic here. Re-run the same compile by hand before assuming'
            Write-Host '  the source is at fault.'
        } else {
            Write-Host '  no diagnostic in the log. Its last 10 lines:'
            $tail | ForEach-Object { Write-Host "  $_" }
        }
    }
    if ((Test-Path $LogFile) -and $Kept) {
        Copy-Item -Force $LogFile $Kept -ErrorAction SilentlyContinue
        Write-Host "  full log kept at: $Kept"
    }
}


function Invoke-BuildCdx {
    param([string]$InputFile, [string]$Kernel, [string]$Output, [int]$MemMB = 3072)
    $logFile = [System.IO.Path]::GetTempFileName()
    $tmpOut = Join-Path (Split-Path $Output) 'build_cdx_tmp.cdx'
    $stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    if ($Kernel -ne $stage0) { Copy-Item -Force $Kernel $stage0 }
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $Compile -Src $InputFile -Out $tmpOut -Log $logFile -Repl -MemMB $MemMB 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Host ''
        Write-Host 'FAIL: CDX build failed'
        Show-CompileFailure -LogFile $logFile -Kept (Join-Path (Split-Path $Output) 'build-cdx-fail.log')
    } else {
        Move-Item -Force $tmpOut $Output
        # compile.ps1 writes the symbol map to a <out>.map sidecar for every CDX
        # compile, -Repl included, since main 15088. Carry it with the binary it
        # describes, or it stays under the tmp name and the next build overwrites
        # the map of the compiler that just shipped.
        $tmpMap = [System.IO.Path]::ChangeExtension($tmpOut, '.map')
        if (Test-Path $tmpMap) {
            Move-Item -Force $tmpMap ([System.IO.Path]::ChangeExtension($Output, '.map'))
        }
    }
    # RETAINED, by Damian's ruling 14 (2026-08-18): warnings do not gate
    # the build, they are AUDITED AT THE RELEASE GATE, and an audit needs
    # something to read. This log was written and then deleted on success,
    # so the only copy of a correct CDX2064 naming a live miscompilation
    # was discarded on every green build (issue 70, X86_64Boot 3216).
    if (Test-Path $logFile) {
        # info is not warning or error. The audit surface (ruling 14) is
        # warnings and errors, and 980 of this log's 1008 lines were
        # 'info CDX4010: bounds proven', which is the compiler reporting a
        # SUCCESS. Splitting them is what makes the diag log readable; the
        # two files are lossless between them.
        $all = @(Get-Content $logFile)
        [System.IO.File]::WriteAllLines([System.IO.Path]::ChangeExtension($Output, '.info.log'), @($all | Where-Object { $_ -match '(^| )(info|hint|deprecated) CDX' }))
        [System.IO.File]::WriteAllLines([System.IO.Path]::ChangeExtension($Output, '.diag.log'), @($all | Where-Object { $_ -notmatch '(^| )(info|hint|deprecated) CDX' }))
    }
    Remove-Item -Force $logFile, $tmpOut, ([System.IO.Path]::ChangeExtension($tmpOut, '.map')) -ErrorAction SilentlyContinue
    return $ok
}

function Invoke-BuildText {
    param([string]$InputFile, [string]$Kernel, [string]$Output, [int]$TextMemMB = 3072)
    $logFile = [System.IO.Path]::GetTempFileName()
    $tmpOut = Join-Path (Split-Path $Output) 'build_text_tmp.codex'
    $stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
    New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
    if ($Kernel -ne $stage0) { Copy-Item -Force $Kernel $stage0 }

    $inputFile2 = [System.IO.Path]::GetTempFileName()
    $outputFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    try {
        $srcBytes = Normalize-TripleNewlines ([System.IO.File]::ReadAllBytes($InputFile))
        $sb = [System.Text.StringBuilder]::new($srcBytes.Length + 100)
        [void]$sb.Append("TEXT`n")
        [void]$sb.Append([System.Text.Encoding]::UTF8.GetString($srcBytes))
        [void]$sb.Append([char]4)
        [System.IO.File]::WriteAllText($inputFile2, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

        $vmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
        $vmArgs = @('-kernel', $stage0, '-input', $inputFile2, '-output', $outputFile, '-mem', "$TextMemMB", '-headless')
        $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
        $proc.WaitForExit(600000)
        if (-not $proc.HasExited) {
            Stop-VmGraceful -ProcessId $proc.Id
            Write-Host ''; Write-Host 'FAIL: TEXT build timed out'; return $false
        }

        if (-not (Test-Path $outputFile) -or (Get-Item $outputFile).Length -eq 0) {
            Write-Host ''; Write-Host 'FAIL: TEXT build produced no output'
            if (Test-Path $stderrFile) { Get-Content $stderrFile | Select-Object -Last 5 | ForEach-Object { Write-Host "  $_" } }
            return $false
        }

        $raw = [System.IO.File]::ReadAllText($outputFile) -replace "`r", '' -replace "^\x01", ''
        $lines = $raw -split "`n"
        $textLines = [System.Collections.Generic.List[string]]::new()
        $halted = $false
        $textHwm = 0
        foreach ($rl in $lines) {
            if ($rl.StartsWith('CODEGEN-HALTED') -or $rl.StartsWith('CODEGEN-ERRORS')) { $halted = $true; break }
            if ($rl.StartsWith('WD:PHASE-')) { $ci = $rl.LastIndexOf(':'); if ($ci -gt 0) { $pv = [int64]0; if ([int64]::TryParse($rl.Substring($ci + 1), [ref]$pv) -and $pv -gt $textHwm) { $textHwm = $pv } } }
            if (-not $rl.StartsWith('WD:') -and -not $rl.StartsWith('HEAP:') -and -not $rl.StartsWith('STACK:')) { $textLines.Add($rl) }
        }
        $script:LastTextHwm = $textHwm
        if ($halted) {
            Write-Host ''; Write-Host 'FAIL: TEXT build halted with errors'; return $false
        }
        [System.IO.File]::WriteAllText($Output, ($textLines -join "`n"))
        return $true
    } finally {
        Remove-Item -Force $inputFile2, $outputFile, $stderrFile -ErrorAction SilentlyContinue
    }
}

# The CDX header embeds the compiler's own SHA-256 over the binary
# content (text + padding + rodata) at bytes 8-39. This is the
# canonical content identity: the sign step signs exactly these bytes,
# and the header offsets, debug map, and deterministic signature are
# all derived from this content. Comparing it gives an apples-to-apples
# fixed-point test regardless of whether either side is signed.
function Get-CdxContentHash {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return (($bytes[8..39]) | ForEach-Object { $_.ToString('x2') }) -join ''
}

# ===================================================================
$buildTimer = [System.Diagnostics.Stopwatch]::StartNew()
$phaseTimings = [ordered]@{}
function Measure-Phase([string]$Name, [scriptblock]$Block) {
    if ($script:SkipPhases -and $script:SkipPhases.Contains($Name)) {
        Write-Host "  phase '$Name' skipped (internal gate; not implicated by this change)"
        return
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Block
    $sw.Stop()
    $script:phaseTimings[$Name] = $sw.Elapsed
}


Write-Host 'The day is warm, yet there is a cooling breeze.'

# -- clean
Measure-Phase 'clean' {
    $buildOut = Join-Path $Repo 'build-output'
    if (Test-Path $buildOut) { Remove-Item -Recurse -Force $buildOut }
    if (Test-Path $OutDir) {
        $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
        $archive = Join-Path $PSScriptRoot "output-$stamp"
        Rename-Item $OutDir $archive
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Get-ChildItem $Repo -Recurse -Depth 3 -Include '*.bak','*.tmp','*.snap' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.git\\' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# -- source
Measure-Phase 'source-concat' {
    if (-not (Test-Path -PathType Leaf $SeedCdx)) { Write-Host "FAIL: $SeedCdx missing"; exit 1 }
    if (-not (Test-Path -PathType Leaf $Concat))  { Write-Host "FAIL: $Concat missing"; exit 1 }
    & pwsh -NoProfile -File $Concat -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $CodexSrc
    if (-not (Test-Path -PathType Leaf $CodexSrc)) { Write-Host 'FAIL: source concat produced no file'; exit 1 }

    # Guard against untracked-source pollution. concat-codex-self globs
    # *.codex under codex/compiler/, so an untracked stray there (e.g.
    # leftover WIP) is silently baked into the seed, producing a binary that
    # does not match the depot source and is not reproducible. Scope the
    # check to codex/compiler/ ONLY -- plug build-output dirs (codex/plugs/*/
    # build-output/*.codex) hold legitimate untracked artifacts the concat
    # never reads. Match the reconcile ACTION (" - ... add"), not "add"
    # anywhere, so editing a tracked file like list-add.codex does not
    # false-trip. (A real stray, DeckCopy.codex, cost a detour 2026-05-29.)
    try {
        $stray = @(p4 reconcile -n codex/compiler/... 2>$null |
            Where-Object { $_ -match '\.codex' -and $_ -match ' - .*\badd\b' })
        if ($stray.Count -gt 0) {
            Write-Host ''
            Write-Host 'FAIL: untracked .codex file(s) under codex/compiler/ would be baked into the seed:'
            $stray | ForEach-Object { Write-Host "  $_" }
            Write-Host '  p4 add them (so the seed is reproducible) or remove them, then rebuild.'
            exit 1
        }
    } catch {
        Write-Host '  (note: p4 reconcile unavailable; skipped untracked-source guard)'
    }
}

# Check if source constants match the seed -- warn if they differ.
$chkConst = Join-Path $PSScriptRoot 'check-constants.ps1'
if (Test-Path $chkConst) {
    & pwsh -NoProfile -File $chkConst 2>&1 | ForEach-Object { Write-Host "  $_" }
}


# The boot cap-bit table (compiler) and the verified-load cap-bit table (OS
# loader) were two hand-written expansions of one name->bit map, and a drift
# granted a binary different authority by which door it entered. They are one
# table now -- codex/foreword/core/Capability.codex, cited by both quires --
# so check-cap-tables.ps1 had nothing left to compare and is deleted with the
# in-compiler guard (check-cap-vocab-coherent) that had the same fate.

# The foreword `effect <Name> where` declarations are the one part of the
# capability model that cannot be derived from the shared table: a declaration
# is a syntax form, not data, and no table can emit one. So this guard is
# permanent, not interim. Nothing in a compile can do its job either -- an
# arbitrary user compile does not include all the foreword effect modules, so
# an in-compiler check cannot see them all. The intended asymmetry is listed
# in the script.
$chkEffVocab = Join-Path $PSScriptRoot 'check-effect-vocab.ps1'
if (Test-Path $chkEffVocab) {
    & pwsh -NoProfile -File $chkEffVocab 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: foreword effects and capability vocabulary have drifted -- add the counterpart or record the exception in check-effect-vocab.ps1'
        exit 1
    }
}

# A builtin name used to live in three hand-maintained lists across two
# quires, and check-builtin-tables.ps1 reconciled them here. It carries one
# row now (codex/compiler/Types/Builtins.codex: name, type, emitter), and
# resolve / route / emit are all projections of it, so there is nothing left
# for a build-time guard to compare. Script and check are both deleted.

# The gate compiles what is ON DISK, so the workspace has to be what the
# agent thinks it is before any of the rest of this means anything.
# 
# Two failures put a wrong workspace under a green gate, both of them
# documented in the agent Perforce process notes since 2026-07-13, and both of
# them hit again on 2026-07-21 by an agent who had read neither. That is the
# point: this check does not exist because the traps are subtle, it exists
# because DOCUMENTING them did not stop them. `p4 unshelve` leaves files at the
# revision they were shelved at and does not schedule the resolve, so `p4
# resolve -n` says "nothing to resolve" and means it while your copy is missing
# every revision submitted since; and an add whose file is already on disk is
# refused with "Can't clobber writable file" and silently dropped from the
# changelist, which is how four tests were lost while they were named as
# pinned.
# 
# Neither is a conflict, a stale revision the eye can see, or an unresolved
# file, so nothing in the normal flow reports either one. Running the check by
# hand was the plan and the plan failed twice; the gate runs it now.
# 
# Skipped without Perforce (fresh clone, public mirror) -- there is no
# workspace to be wrong.
$chkP4 = Join-Path $PSScriptRoot 'p4-stale-check.ps1'
if ((Test-Path $chkP4) -and (Get-Command p4 -ErrorAction SilentlyContinue) -and
    (Test-Path (Join-Path $Repo '.p4config'))) {
    & pwsh -NoProfile -File $chkP4 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: the workspace does not match the depot -- the gate would compile the wrong source'
        Write-Host '      Fix it with the two commands the check printed. Do not resolve with -ay.'
        exit 1
    }
}

# A sidecar is resolved next to its .codex, so one in the wrong directory
# configures nothing while reading like a decision -- diagnostic-boot's
# "blocks waiting for keyboard input" skip lived a directory above the test,
# applied to nothing, and was quoted as a real skip in ExaminersAssay for
# months. Nothing else can see this: a sidecar that names no test is exactly
# the file no compile and no battery ever opens.
$chkSidecars = Join-Path $PSScriptRoot 'check-sidecars.ps1'
if (Test-Path $chkSidecars) {
    & pwsh -NoProfile -File $chkSidecars 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: a test sidecar names no test'
        exit 1
    }
}

# The diagnostic catalogue (CdxCodes.codex) is read by nobody in the compiler
# -- cdx-lookup has no caller and whole-program DCE prunes the whole table --
# so a code raised with no row, or a row whose Name drifted from its constant,
# is invisible by construction. This reads the catalogue and the raise sites
# and fails the build when they disagree. It found 13 undocumented codes on
# its first run.
$chkCdx = Join-Path $PSScriptRoot 'check-cdx-registry.ps1'
if (Test-Path $chkCdx) {
    & pwsh -NoProfile -File $chkCdx 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: the diagnostic catalogue disagrees with the code that raises it'
        exit 1
    }
}

# The fact store's partition type GUID is written by three producers -- the
# reader in Foreword chapter Gpt, the IMG plug, and build/build-img.ps1 -- and
# a disagreement is silent: the stick carries one type, the guest looks for
# another, and the store refuses every write while reporting nothing.
$chkFactsGuid = Join-Path $PSScriptRoot 'check-facts-guid.ps1'
if (Test-Path $chkFactsGuid) {
    & pwsh -NoProfile -File $chkFactsGuid 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: the fact-store partition type GUID disagrees across writers'
        exit 1
    }
}

# Counts in the docs go stale, and "never carry a count forward" is written in
# three places and enforced in none. check-doc-counts.ps1 is the reader; on a
# clean tree it found six false claims, one of them wrong by thirty tests.
# 
# UNCONDITIONAL since 2026-08-21 (red's ruling). It was opt-in behind an env
# var or a .doc-counts file, and a runner nobody has to invoke is L-BODY's
# shape rather than a gate: main published a seed SHA-256, MD5, content-hash
# prefix and byte count for an artifact it no longer carried, across two seed
# moves in one day, and nothing observed it because no workspace happened to
# carry the file. Measured 2026-08-21: 63 claims, mean 0.52 s over three runs.
$chkCounts = Join-Path $PSScriptRoot 'check-doc-counts.ps1'
# NOT on -Internal (Damian, 2026-09-02): counts drift again before any
# release, so paying 0.5 s per CL to hold them exact between releases buys
# nothing and it was holding the build token. The FULL gate still runs it,
# which is the gate a release passes through.
if ((-not $Internal) -and (Test-Path $chkCounts)) {
    & pwsh -NoProfile -File $chkCounts -Quiet 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: a doc states a count the tree no longer produces'
        Write-Host '      Run build/check-doc-counts.ps1 for the per-claim table.'
        exit 1
    }
}

# IRTextParser calls itself the inverse of IRTextEmitter and is not: the
# emitter can write forms the parser reads as a plausible DEFAULT rather than
# an error. x86-64 never crosses this wire, so no x86 test and no fixed point
# can fail on a divergence -- ir-expr-type on IrNegate answered -(-2.5) as 1.7
# on ARM64 and RISC-V for exactly that reason. Three known holes are recorded
# in build/plug-wire-baseline.txt; this fails the build on a fourth.
$chkWire = Join-Path $PSScriptRoot 'check-plug-types.ps1'
if (Test-Path $chkWire) {
    & pwsh -NoProfile -File $chkWire 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'FAIL: the IR text wire lost a form between emitter and plug parser'
        exit 1
    }
}


Write-Host 'The latest in a series of personal crises seems insurmountable.'
Write-Host 'You are being pulled apart in all directions.'
Write-Host ''

# -- CDX build
Measure-Phase 'cdx-build' {
    if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $SeedCdx -Output $SutCdx)) { exit 1 }
}

Write-Host 'Yet this afternoon walk in the countryside slowly brings relaxation'
Write-Host 'to your harried mind. The soil and strain of modern high-tech living'
Write-Host 'begins to wash off in layers.'
Write-Host ''

# -- sign
if ($coreRuns) { Copy-Item -Force $SutCdx (Join-Path $Repo 'build-output\bare-metal\Codex.cdx') }

Measure-Phase 'sign' {
$SigningKey = 'D:\Projects\signing.key'
if (Test-Path -PathType Leaf $SigningKey) {
    $compileScript = Join-Path $PSScriptRoot 'compile.ps1'
    $runScript = Join-Path $PSScriptRoot 'test-run.ps1'
    $cdxRaw = [System.IO.File]::ReadAllBytes($SutCdx)
    $keyBytes = [System.IO.File]::ReadAllBytes($SigningKey)
    $hashBytes = $cdxRaw[8..39]
    $keyList = ($keyBytes | ForEach-Object { $_.ToString() }) -join ', '
    $hashList = ($hashBytes | ForEach-Object { $_.ToString() }) -join ', '
    $signSrc = Join-Path $OutDir 'cdx-sign-inline.codex'
    $signSrcText = @"
Chapter: CdxSignInline
  cites Foreword chapter Console
  cites Foreword chapter Ed25519
  cites Foreword chapter Sha512
Section: Helpers
  bytes-to-csv : List Integer, Integer, Integer, Text -> Text
  bytes-to-csv (bs) (i) (len) (acc) =
    if i >= len then acc
    else let sep = if i == 0 then "" else ","
    in bytes-to-csv bs (i + 1) len (acc & sep & show (list-at bs i))
Section: Body
  opening : [Console] Nothing = act
    let key = [$keyList]
    in let hash = [$hashList]
    in let pub = ed25519-public-key key
    in let sig = ed25519-sign key pub hash
    in act
      print-line-uni (bytes-to-csv pub 0 32 "")
      print-line-uni (bytes-to-csv sig 0 64 "")
    end
  end
"@
    [System.IO.File]::WriteAllText($signSrc, $signSrcText)
    $signCdx = Join-Path $OutDir 'cdx-sign.cdx'
    $signLog = Join-Path $OutDir 'cdx-sign.log'
    $signOut = Join-Path $OutDir 'cdx-sign.out'
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $compileScript -Src $signSrc -Out $signCdx -Log $signLog 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool compile failed'; Get-Content $signLog -TotalCount 10 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }; exit 1 }
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $runScript -Kernel $signCdx -OutFile $signOut 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: sign tool run failed'; exit 1 }
    $signRaw = [System.IO.File]::ReadAllText($signOut)
    $signClean = $signRaw -replace '[^\x20-\x7E\r\n]', ''
    $signLines = $signClean -split "`n" | Where-Object { $_.Trim() -match '^\d' }
    $pubBytes = $signLines[0].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    $sigBytes = $signLines[1].Split(',') | Where-Object { $_.Trim() -ne '' } | ForEach-Object { [byte]([int]$_.Trim()) }
    if ($pubBytes.Count -ne 32 -or $sigBytes.Count -ne 64) { Write-Host "FAIL: bad sign output (pub=$($pubBytes.Count) sig=$($sigBytes.Count))"; exit 1 }
    for ($i = 0; $i -lt 32; $i++) { $cdxRaw[40 + $i] = $pubBytes[$i] }
    for ($i = 0; $i -lt 64; $i++) { $cdxRaw[72 + $i] = $sigBytes[$i] }
    [System.IO.File]::WriteAllBytes($SutCdx, $cdxRaw)
}
}


Write-Host 'That willow tree near the stream looks comfortable and inviting.'

# -- canary
Measure-Phase 'canary' {
$canarySrc      = Join-Path $Repo 'codex\test\factorial.codex'
$canaryExpected = Join-Path $Repo 'codex\test\factorial.expected'
$canaryCdx      = Join-Path $OutDir 'canary-factorial.cdx'
$canaryLog      = Join-Path $OutDir 'canary-compile.log'
$canaryOut      = Join-Path $OutDir 'canary-run.out'
if (-not (Test-Path -PathType Leaf $canarySrc))      { Write-Host "FAIL: $canarySrc missing"; exit 1 }
if (-not (Test-Path -PathType Leaf $canaryExpected)) { Write-Host "FAIL: $canaryExpected missing"; exit 1 }
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$runScript     = Join-Path $PSScriptRoot 'test-run.ps1'
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File $compileScript -Src $canarySrc -Out $canaryCdx -Log $canaryLog 2>&1 | Out-Null
$ErrorActionPreference = $prev
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: canary compile -- SUT cannot compile factorial.codex'
    Get-Content $canaryLog -TotalCount 20 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
    exit 1
}
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File $runScript -Kernel $canaryCdx -OutFile $canaryOut 2>&1 | Out-Null
$ErrorActionPreference = $prev
$expectedBytes = if (Test-Path $canaryExpected) { ([System.IO.File]::ReadAllText($canaryExpected) -replace "`r",'').TrimEnd() } else { '' }
$actualBytes   = if (Test-Path $canaryOut)      { ([System.IO.File]::ReadAllText($canaryOut) -replace "`r",'').TrimEnd() }    else { '' }
if ($expectedBytes -ne $actualBytes) {
    Write-Host 'FAIL: canary output mismatch'
    Write-Host "  expected: $($expectedBytes.Trim())"
    Write-Host "  got:      $($actualBytes.Trim())"
    exit 1
}
}

Write-Host 'You settle beneath it and the buzz of dragonflies and the whisper'
Write-Host 'of the willow''s swaying branches bring a deep peace.'
Write-Host ''

# -- the jonquil: IR quine visibility check. The trusting-trust defense holds
# because a payload that survives the diverse rebuild must reach the readable
# IR and is therefore visible as text. This enforces that: emit the certified
# compiler's own IR and fail if any def re-emits itself (the DDC-QUINE-ARM
# construction). Tripwire only, not the DDC witness; see build/jonquil.ps1.
Measure-Phase 'jonquil' {
    $jonquilScript = Join-Path $PSScriptRoot 'jonquil.ps1'
    & pwsh -NoProfile -File $jonquilScript -Kernel $SutCdx -Src $CodexSrc -WorkDir (Join-Path $OutDir 'quine-check')
    if ($LASTEXITCODE -ne 0) { exit 1 }
}


# -- size sanity check
$seedSize = (Get-Item $SeedCdx).Length
$sutSize = (Get-Item $SutCdx).Length
$drift = [math]::Abs($sutSize - $seedSize)
$maxDrift = [int]($seedSize * 0.05)
if ($coreRuns -and $drift -gt $maxDrift) {
    Write-Host "FAIL: SUT size drifted too far from seed"
    Write-Host "  seed: $seedSize bytes  SUT: $sutSize bytes  drift: $drift (max $maxDrift)"
    exit 1
}

$textStage1 = Join-Path $OutDir 'stage1.codex'
$textStage2 = Join-Path $OutDir 'stage2.codex'

Write-Host 'Searching inward for tranquility and happiness, you close your eyes.'
Measure-Phase 'text-stage1' {
    if (-not (Invoke-BuildText -InputFile $CodexSrc -Kernel $SutCdx -Output $textStage1)) { exit 1 }
    $tHwm = $script:LastTextHwm
    if ($tHwm -le 0) {
        Write-Host ''
        Write-Host 'FAIL: text-stage1 emitted no WD:PHASE telemetry, so the memory contract was NOT measured'
        Write-Host '      A contract that cannot be read is not a contract. Fix the reader, do not skip the check.'
        exit 1
    }
    Write-Host ("  text-stage1 heap hwm {0:N0} bytes ({1} MB) against the 2 GB contract" -f $tHwm, [int]($tHwm / 1MB))
    if ($tHwm -gt 2147483648) {
        Write-Host ''
        Write-Host ("FAIL: the text-mode self-compile peaked at {0:N0} bytes, past the 2 GB memory contract" -f $tHwm)
        Write-Host '      CurrentPlan, THE COMPILER MEMORY CONTRACT: a self-compile needing more is a DEFECT to fix,'
        Write-Host '      never a reason to grow the guests.'
        exit 1
    }
}

Measure-Phase 'sem-equiv' {
    $semEquivScript = Join-Path $PSScriptRoot 'compare-codex-semantic.ps1'
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $semEquivScript -Source $CodexSrc -Stage1 $textStage1 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'FAIL: semantic equivalence -- stage1 does not match source'
        & pwsh -NoProfile -File $semEquivScript -Source $CodexSrc -Stage1 $textStage1
        exit 1
    }
}

Write-Host 'A high-pitched cascading sound like crystal wind-chimes impinges'
Write-Host 'on your floating awareness.'
Write-Host ''

Measure-Phase 'text-stage2' {
    if (-not (Invoke-BuildText -InputFile $textStage1 -Kernel $SutCdx -Output $textStage2)) { exit 1 }
}

Measure-Phase 'text-fixedpoint' {
    $th1 = (Get-FileHash -Algorithm SHA256 $textStage1).Hash
    $th2 = (Get-FileHash -Algorithm SHA256 $textStage2).Hash
    if ($th1 -ne $th2) {
        Write-Host 'FAIL: text round-trip -- stage1 !== stage2'
        Write-Host "  stage1: $((Get-Item $textStage1).Length) bytes  $th1"
        Write-Host "  stage2: $((Get-Item $textStage2).Length) bytes  $th2"
        exit 1
    }
}


Write-Host 'As you open your eyes, you see a shimmering blueness rise from the ground.'
Write-Host ''

$cdxStage1 = Join-Path $OutDir 'stage1.cdx'
$cdxStage2 = Join-Path $OutDir 'stage2.cdx'
# The kernel every later phase grades with. A deferred core leaves
# stage1.cdx holding WHATEVER THE LAST RUN LEFT, which is the -Kernel trap
# CLAUDE.md names: it once reported ~80 of 84 chapters compiling where the
# truth was ~55. The skipped path is pointed at the seed of record instead,
# never at a build output.
$testKernel = if ($coreRuns) { $cdxStage1 } else { $SeedCdx }

Write-Host 'It is difficult to look at the blueness directly. The sound seems'
Write-Host 'to be emanating from this glowing portal.'
Measure-Phase 'cdx-stage1' {
    if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $SutCdx -Output $cdxStage1)) { exit 1 }
}

Measure-Phase 'cdx-fixedpoint' {
    $sutHash = Get-CdxContentHash $SutCdx
    $ch1 = Get-CdxContentHash $cdxStage1
    if ($sutHash -eq $ch1) {
        Write-Host '(SUT === stage1 -- hard fixed point in one pass)'
    } else {
        if (-not (Invoke-BuildCdx -InputFile $CodexSrc -Kernel $cdxStage1 -Output $cdxStage2)) { exit 1 }
        $ch2 = Get-CdxContentHash $cdxStage2
        if ($ch1 -eq $ch2) {
            Write-Host '(SUT !== stage1 -- CONVERGED ON THE SECOND PASS, stage1 === stage2)'
            Write-Host '  The fixed point is STAGE1. build\output\Sut.cdx is the PRE-CONVERGENCE'
            Write-Host '  binary and installing it as the seed ships a compiler that does not'
            Write-Host '  reproduce itself (PerforceProcess 4.3a, P-STAGE2). Converge first:'
            Write-Host '  install build\output\NewSeed.cdx, re-run this gate, THEN install Sut.'
        }
        if ($ch1 -ne $ch2) {
            Write-Host 'FAIL: CDX fixed point -- stage1 !== stage2'
            Write-Host "  stage1: $((Get-Item $cdxStage1).Length) bytes  $ch1"
            Write-Host "  stage2: $((Get-Item $cdxStage2).Length) bytes  $ch2"
            exit 1
        }
    }
}

Write-Host 'Light seems to bend and distort around it, while the sound waves'
Write-Host 'become so intense, they appear to become visible.'
Write-Host ''

if ($coreRuns) { Copy-Item -Force $cdxStage1 (Join-Path $OutDir 'NewSeed.cdx') }

Write-Host 'The portal hangs there for a moment; then with the rush of an'
Write-Host 'imploding vacuum, it sinks into the ground.'

Measure-Phase 'test-bvt' {
    $bvtScript = Join-Path $PSScriptRoot 'bvt.ps1'
    $testOut = Join-Path $OutDir 'test-results.txt'
    & pwsh -NoProfile -File $bvtScript -CodexCdx $testKernel -Jobs 8 > $testOut 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host 'FAIL: BVT'
        Get-Content $testOut | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
}


# -- the differential oracles: operator correctness against the HOST. The
# fixed point cannot see an operator that is uniformly wrong, because it
# only requires the compiler to agree with itself -- which is how Real
# ordering shipped inverted for three months. Damian's call 2026-07-27:
# the pin goes wherever it helps, and the gate is where every codegen
# change passes. Cost measured at 2s + 1s against a 170s gate. The
# collections stay author-owned (reek: oracle-scalar, blu: oracle-vector
# and oracle-cce -- cce joined 2026-07-28 once G1 closed and every gap
# carried a verdict, 1485/1516 with 31 in ruled gaps, 0 unexplained);
# this leg only runs them against the just-proven fixed point.
Measure-Phase 'oracles' {
    foreach ($o in 'oracle-scalar', 'oracle-vector', 'oracle-cce') {
        $olog = Join-Path $OutDir "$o-results.txt"
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot "$o.ps1") -Kernel $testKernel > $olog 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ''
            Write-Host "FAIL: $o disagrees with the host"
            Get-Content $olog | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
            exit 1
        }
    }
}

# -- the refusal set: every test under codex/test/errors must still be
# REFUSED, and with the codes it declares. 203 tests assert a rejection, and
# until 2026-08-17 the gate ran the thirteen that bvt.ps1 names. The rest were
# reachable only from build/test.ps1, which is the battery and refuses to run
# without Damian, so a refusal that stopped happening was caught by nothing an
# agent is allowed to run. bounded-exceeded reached main green in Update 45
# with its premise dead: COMPILER-8 made its accumulator extend in place, the
# declaration it exists to watch fail held instead, and it compiled clean.
# 
# The set is derived from the directory, never listed -- a hand-maintained
# list is how thirteen came to stand for a hundred and seventy-six. It always
# runs, like the BVT and the oracles, because the diagnostic path is what a
# codegen or foreword change moves without moving any other phase. Measured
# 23.4s over 203 refusals, measured at -Jobs 8; re-measure at 4 (L-COUNT).
Measure-Phase 'check-errors' {
    $chkErrors = Join-Path $PSScriptRoot 'check-errors.ps1'
    if (Test-Path $chkErrors) {
        & pwsh -NoProfile -File $chkErrors -Kernel $testKernel -Jobs 8 2>&1 | ForEach-Object { Write-Host "$_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a program the compiler must refuse was not refused as declared'
            exit 1
        }
    }
}

# -- codex-vm -run-list: the supervisor that the BVT runs every test through.
# Its gating was HALF. bvt.ps1 drives every BVT test through -run-list, so a
# regression on the HAPPY path already turns this gate red without any of
# this. What nothing ran is the REFUSAL and ISOLATION half -- a corrupt kernel
# not taking its neighbours, the wall budget stopping one line alone, drop
# attribution, a nested list refused -- and those can rot unseen (L-NOGATE).
# 
# All five arms run rather than only those four: the whole script is 5.7 s in the gate
# measured 2026-08-25, and a switch to select four of five is machinery to
# manage three seconds (L-LESS). It REFUSES rather than skips when a kernel it
# needs is missing, which is why it sits after the oracles: canary, cdx-sign
# and Sut all exist by here.
Measure-Phase 'run-list' {
    $chkRunList = Join-Path $PSScriptRoot 'check-run-list.ps1'
    if (Test-Path $chkRunList) {
        & pwsh -NoProfile -File $chkRunList 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: codex-vm -run-list did not isolate a failing line from its neighbours'
            exit 1
        }
    }
}

# -- the test chapters themselves. No gate phase compiled anything under
# codex/test until 2026-08-20, so a test chapter that stopped compiling was
# UNRUNNABLE and every other instrument stayed green over it: the suite stops
# asking and reports what a suite that asks and agrees reports
# (L-CAPABILITY-LOST). It cost three times before anything measured it --
# widget-tone missed by a signature change TWICE, val's 18220 fixing six
# gop-composite siblings and missing the one directory over, and
# cost/accumulator-corpus uncompiled since 08-16 because its only runner is
# invoked by nothing.
# 
# Never skipped, only SCOPED. -Internal compiles the chapters that CITE what
# changed here; the full gate compiles all of them. Measured: 11 chapters and
# 12s for a GopComposite change, against 1,202s at 4 ways for all 1,400.
# 
# A COMPILER CHANGE IS THE EXCEPTION AND TAKES THE FULL CORPUS (red's ruling
# 2026-08-25, widening reek's proposal from Emit/ to the whole compiler).
# Cite-scoping assumes a change reaches the chapters that CITE it. Nothing
# cites the compiler: it is global by construction, so the scoped set is
# chosen by a relation the subject does not participate in. Main 19551
# shipped a seed that self-verified, passed the BVT, the oracles and 203
# refusals, and could not compile the desk -- test-compile had run ONE
# chapter of 1447 and the corpus was the only witness there was. ~115s on
# compiler CLs, against a fleet pin measured in hours.
Measure-Phase 'test-compile' {
    $chkTest = Join-Path $PSScriptRoot 'check-test-compile.ps1'
    if (Test-Path $chkTest) {
        $tcArgs = @('-Kernel', $testKernel)
        if ((-not $Internal) -or $tCompiler) { $tcArgs += '-Full' }
        & pwsh -NoProfile -File $chkTest @tcArgs 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a chapter under codex/test does not compile'
            exit 1
        }
    }
}

# COMPILING A TEST IS NOT RUNNING IT, and until now the gate only compiled.
# A change that made lazy-smoke emit clean IR and answer heap addresses where
# values were expected passed both instruments and blocked a release
# (L-NOGATE). This RUNS the chapters test-compile just selected -- the ones
# whose source cites a chapter this CL changed -- and grades them against
# their .expected. A TEST RUNS WHEN IT IS LIKELY TO FAIL: the cited set is
# empty on most CLs and this phase costs nothing there.
#
# bvt.ps1 is the runner because the grading is already in it: the .expected
# filter, the sidecars, and the strict-prefix arithmetic that tells a
# TRUNCATED capture from a miscompile (L-SHORT). The subject list is READ
# from check-test-compile.ps1 rather than re-derived, so there is one selector
# and not two that can disagree.
Measure-Phase 'test-run' {
    $subjectList = Join-Path $Repo 'build-output\test-compile-subjects.txt'
    $cited = @()
    if (Test-Path $subjectList) {
        $cited = @(Get-Content $subjectList | Where-Object { $_ } |
                   Where-Object { Test-Path ($_ -replace '\.codex$', '.expected') })
    }
    if ($cited.Count -eq 0) {
        Write-Host '  test-run: OK (no cited chapter carries an .expected)'
    } else {
        $bvtScript = Join-Path $PSScriptRoot 'bvt.ps1'
        & pwsh -NoProfile -File $bvtScript -CodexCdx $testKernel -Jobs 8 -SubjectsFile $subjectList 2>&1 |
            ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a cited test chapter RAN and disagreed with its .expected'
            Write-Host '      Compiling it clean is not the same claim; read the per-test lines above.'
            exit 1
        }
    }
}

# -- binary backend plugs: must build clean with the just-proven compiler.
# Native code-emitting backends only (riscv/arm64/t3isa/elf/pe/img). The
# transpiler/text plugs are secondary outputs and are NOT gated here. Plug
# CDX is untracked build-output, so without this gate a compiler tightening
# silently dark-ships every backend until someone rebuilds one by hand.
# 
# t3isa emits balanced-ternary words for a 27-trit machine and belongs here
# for the same reason as the rest. Only its BUILD is gated: its own gate
# needs an external emulator that lives on one machine, and nothing here
# reaches that.
Measure-Phase 'plug-binary' {
    Copy-Item -Force $SutCdx (Join-Path $Repo 'build-output\bare-metal\Codex.cdx')
    $binaryBackends = @(@('riscv','arm64','t3isa','elf','pe','img') + $changedPlugs | Select-Object -Unique)
    $plugFail = @()
    foreach ($bp in $binaryBackends) {
        $bpDir = Join-Path $Repo "codex\plugs\$bp"
        $bs = Join-Path $bpDir 'build.ps1'
        if (-not (Test-Path $bs)) { $plugFail += "$bp(no-build.ps1)"; continue }
        # A plug can ship more than one binary: spirv's build-bin.ps1 emits
        # spirvbin-plug.cdx, which this phase built and asserted nowhere, so a
        # break in it was ungraded. The name is read off the script's own
        # -PlugName, so a build*.ps1 that declares none is not a plug binary
        # (evidence/build-wasm.ps1, wasm's page builds) and is skipped.
        foreach ($bscript in @(Get-ChildItem $bpDir -Filter 'build*.ps1' -File | Sort-Object Name)) {
            $pn = $bp
            $decl = [regex]::Match((Get-Content $bscript.FullName -Raw), "Build-TranspilerPlug[^\r\n]*-PlugName\s+'([^']+)'")
            if ($decl.Success) { $pn = $decl.Groups[1].Value }
            elseif ($bscript.Name -ne 'build.ps1') { continue }
            & pwsh -NoProfile -File $bscript.FullName *> $null
            $cdx = Join-Path $bpDir "build-output\$pn-plug.cdx"
            $blog = Join-Path $bpDir 'build-output\build.log'
            $bad = -not (Test-Path $cdx)
            if ((Test-Path $blog) -and (Select-String -Path $blog -Pattern 'CODEGEN-ERRORS|error CDX' -Quiet)) { $bad = $true }
            if ($bad) { if ($pn -eq $bp) { $plugFail += $bp } else { $plugFail += "$bp($pn)" } }
        }
    }
    if ($plugFail.Count -gt 0) {
        Write-Host ''
        Write-Host "FAIL: binary plug build -- $($plugFail -join ', ')"
        foreach ($bp in $plugFail) {
            $bpName = $bp.Split('(')[0]
            $blog = Join-Path $Repo "codex\plugs\$bpName\build-output\build.log"
            if (Test-Path $blog) { Get-Content $blog | Select-String 'error CDX|CODEGEN-ERRORS' | Select-Object -First 3 | ForEach-Object { Write-Host "  ${bp}: $($_.Line.Trim())" } }
        }
        exit 1
    }
}

# -- cross-arch execution: the binary leg above proves the arm64 and riscv
# plugs BUILD, and nothing ran a byte of what they emit. That is how CL 8221
# put a PSCI call in every ARM64 program's __start, killed the whole ARM64
# lane on the committed Renode board, and stayed green here for weeks while
# 238 tests failed silently. One program per architecture,
# booted for real. Not the battery -- build/test-cross-batch.ps1 is that, and
# it stays out-of-band.
Measure-Phase 'cross-smoke' {
    $chkCross = Join-Path $PSScriptRoot 'check-cross-smoke.ps1'
    if (Test-Path $chkCross) {
        & pwsh -NoProfile -File $chkCross 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a cross-arch backend stopped executing correctly'
            exit 1
        }
    }
}

# -- transpiler plug smoke: a representative subset must RUN end-to-end
# (SUT IR -> framed TCP wire -> plug VM -> non-empty target text). The binary
# leg above proves plugs BUILD; this proves the wire protocol and plug runtime
# stay alive -- the class that dark-shipped when 20 run.ps1 senders went
# unframed (CL 7372). Missing plug CDX builds once and caches; a failing run
# gets one rebuild-and-retry (stale binary after IR drift), then fails loudly.
Measure-Phase 'plug-smoke' {
    # This phase calls run.ps1 -Src/-Out, which not every plug accepts: some bind no
    # -Src and exit 1 at parameter binding, so widening by "carries a run.ps1" reds
    # the gate on a plug this phase cannot express. The capability is READ OFF
    # run.ps1 rather than kept as a list, because a list drifts and this cannot.
    $smokeCapable = @()
    foreach ($cp in $changedPlugs) {
        $cpRun = Join-Path $Repo "codex\plugs\$cp\run.ps1"
        if (-not (Test-Path $cpRun)) { continue }
        $cpAst = [System.Management.Automation.Language.Parser]::ParseFile($cpRun, [ref]$null, [ref]$null)
        if ($cpAst.ParamBlock -and @($cpAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Src' }).Count -gt 0) { $smokeCapable += $cp }
    }
    $smokePlugs = @(@('typescript', 'python', 'rust', 'ptx') + $smokeCapable | Select-Object -Unique)
    $smokeSrcs = @('hello', 'record')
    $smokeDir = Join-Path $OutDir 'plug-smoke'
    New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
    # A plug binary is only as fresh as the last hand run of its build.ps1, and
    # 37 of 38 faulted on a record after common/IRTextParser.codex moved while
    # this phase rebuilt only a MISSING binary (plugs-backlog 1.11). Rebuild
    # when the CDX is older than any source the bundle is made from.
    $smokeDeps = @(Get-ChildItem (Join-Path $Repo 'codex\plugs\common') -Filter *.codex)
    foreach ($decl in @('codex\compiler\Core\Name.codex', 'codex\compiler\Core\SourceText.codex', 'codex\compiler\Types\CodexType.codex', 'codex\compiler\Ast\AstNodes.codex', 'codex\compiler\IR\IRChapter.codex')) {
        $smokeDeps += Get-Item (Join-Path $Repo $decl)
    }
    $smokeFail = @()
    foreach ($sp in $smokePlugs) {
        $spBuild = Join-Path $Repo "codex\plugs\$sp\build.ps1"
        $spCdx   = Join-Path $Repo "codex\plugs\$sp\build-output\$sp-plug.cdx"
        $spLog   = Join-Path $smokeDir "$sp-smoke.log"
        $spNewest = ($smokeDeps + @(Get-ChildItem (Join-Path $Repo "codex\plugs\$sp") -Filter *.codex) | ForEach-Object { $_.LastWriteTimeUtc } | Sort-Object -Descending | Select-Object -First 1)
        if (-not (Test-Path $spCdx) -or (Get-Item $spCdx).LastWriteTimeUtc -lt $spNewest) { & pwsh -NoProfile -File $spBuild *> $spLog }
        foreach ($si in $smokeSrcs) {
            $smokeSrc = Join-Path $Repo "codex\plugs\test-input\$si.codex"
            $spOut = Join-Path $smokeDir "$sp-$si.out"
            $ok = $false
            foreach ($attempt in 1..2) {
                Remove-Item -Force $spOut -ErrorAction SilentlyContinue
                & pwsh -NoProfile -File (Join-Path $Repo "codex\plugs\$sp\run.ps1") -Src $smokeSrc -Out $spOut *>> $spLog
                if ($LASTEXITCODE -eq 0 -and (Test-Path $spOut) -and (Get-Item $spOut).Length -gt 0) { $ok = $true; break }
                if ($attempt -eq 1) { & pwsh -NoProfile -File $spBuild *>> $spLog }
            }
            if (-not $ok) { $smokeFail += "$sp/$si" }
        }
    }
    if ($smokeFail.Count -gt 0) {
        Write-Host ''
        Write-Host "FAIL: plug smoke -- $($smokeFail -join ', ') (run.ps1 nonzero or empty output)"
        foreach ($sp in $smokeFail) {
            $spLog = Join-Path $smokeDir "$sp-smoke.log"
            if (Test-Path $spLog) { Get-Content $spLog | Select-Object -Last 5 | ForEach-Object { Write-Host "  ${sp}: $_" } }
        }
        exit 1
    }
# -- the cross-host arm (plugs 1.73 step 3, red's clearance 2026-08-25).
# The QEMU fallback carries all 56 plug runners since 19697 and NOTHING
# guarded it: every cross-host result was a hand run (L-NOGATE). The four
# plugs above already span both launch helpers -- python, typescript and rust
# through Start-PlugVm, ptx file-serial through Invoke-PlugVmFileSerial -- so
# this needs a second PASS, not new subjects.
# 
# BYTE-IDENTICAL is the assertion, and it has to be. Asking only whether the
# run exited 0 is what let csharp sit through its full 1800 s timeout
# undetected while javascript passed beside it; a differential against the
# codex-vm answer is what catches a host that finishes and lies.
# 
# A box with no QEMU SAYS SO and moves on. A silent skip would be a check
# that cannot fail, which is the thing this arm exists to stop.
    if (-not $script:FallbackVmBin) {
        Write-Host '  plug-smoke: cross-host arm SKIPPED, no QEMU on this box (set QEMU_BIN to run it)'
    } else {
        $xFail = @()
        $prevVmHost = $env:CODEX_VM_HOST
        $env:CODEX_VM_HOST = 'qemu'
        try {
            foreach ($sp in $smokePlugs) {
                $spLog = Join-Path $smokeDir "$sp-smoke-qemu.log"
                foreach ($si in $smokeSrcs) {
                    $smokeSrc = Join-Path $Repo "codex\plugs\test-input\$si.codex"
                    $qOut = Join-Path $smokeDir "$sp-$si.qemu.out"
                    $cOut = Join-Path $smokeDir "$sp-$si.out"
                    Remove-Item -Force $qOut -ErrorAction SilentlyContinue
                    & pwsh -NoProfile -File (Join-Path $Repo "codex\plugs\$sp\run.ps1") -Src $smokeSrc -Out $qOut *>> $spLog
                    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $qOut)) { $xFail += "$sp/$si(qemu produced nothing)"; continue }
                    if ((Get-FileHash $qOut -Algorithm SHA256).Hash -ne (Get-FileHash $cOut -Algorithm SHA256).Hash) { $xFail += "$sp/$si(hosts differ)" }
                }
            }
        } finally {
            $env:CODEX_VM_HOST = $prevVmHost
        }
        if ($xFail.Count -gt 0) {
            Write-Host ''
            Write-Host "FAIL: plug smoke cross-host -- $($xFail -join ', ')"
            Write-Host '  codex-vm and the QEMU fallback disagree, or the fallback produced nothing.'
            Write-Host '  A subject that flaps here is a finding about that subject or that host. Record it before quieting it.'
            exit 1
        }
        Write-Host "  plug-smoke: cross-host OK ($($smokePlugs.Count * $smokeSrcs.Count) subjects byte-identical on codex-vm and QEMU)"
    }
}

# -- a plug that ships its own test-*.ps1 is graded by it when THAT plug changes.
# The trigger is derived, not listed: a plug gaining a harness is picked up
# without editing this phase, the same reason plug-smoke reads -Src capability
# off run.ps1 rather than keeping names. Measured 2026-09-02: FOUR plugs ship SIX
# harnesses (evidence, img, ptx, spirv), so this is not the evidence plug alone.
# The evidence one had no caller anywhere -- eight arms, four of them requiring
# the plug to REFUSE a claim it cannot support, and nothing ran them (L-NOGATE).
Measure-Phase 'plug-selftest' {
    $selfHarness = @()
    foreach ($cp in $changedPlugs) {
        $cpDir = Join-Path $Repo "codex\plugs\$cp"
        if (-not (Test-Path -PathType Container $cpDir)) { continue }
        foreach ($h in @(Get-ChildItem $cpDir -Filter 'test-*.ps1' -File)) {
            $selfHarness += [pscustomobject]@{ Plug = $cp; Path = $h.FullName; Name = $h.BaseName }
        }
    }
    if ($selfHarness.Count -eq 0) {
        Write-Host '  plug-selftest: not implicated (no changed plug ships a test-*.ps1)'
    } else {
        # A plug can carry MORE THAN ONE build script, and a harness may refuse
        # rather than build: spirv ships build.ps1 AND build-bin.ps1, and
        # test-emit.ps1 exits 2 with "MISSING plug; run build-bin.ps1" when the
        # second binary is absent. plug-binary only ever runs build.ps1, so
        # without this the phase reds on a missing prerequisite instead of on the
        # plug, which is a false red and the worst kind (measured 2026-09-02).
        foreach ($cp in @($selfHarness | ForEach-Object { $_.Plug } | Select-Object -Unique)) {
            foreach ($b in @(Get-ChildItem (Join-Path $Repo "codex\plugs\$cp") -Filter 'build*.ps1' -File)) {
                & pwsh -NoProfile -File $b.FullName *> (Join-Path $OutDir "plug-selftest-$cp-$($b.BaseName).log")
            }
        }
        $selfFail = @()
        foreach ($sh in $selfHarness) {
            $shLog = Join-Path $OutDir "plug-selftest-$($sh.Plug)-$($sh.Name).log"
            & pwsh -NoProfile -File $sh.Path *> $shLog
            if ($LASTEXITCODE -ne 0) { $selfFail += "$($sh.Plug)/$($sh.Name)" }
        }
        if ($selfFail.Count -gt 0) {
            Write-Host ''
            Write-Host "FAIL: plug selftest -- $($selfFail -join ', ')"
            foreach ($sh in $selfHarness) {
                if ($selfFail -notcontains "$($sh.Plug)/$($sh.Name)") { continue }
                $shLog = Join-Path $OutDir "plug-selftest-$($sh.Plug)-$($sh.Name).log"
                if (Test-Path $shLog) { Get-Content $shLog | Select-Object -Last 6 | ForEach-Object { Write-Host "  $($sh.Plug)/$($sh.Name): $_" } }
            }
            exit 1
        }
        Write-Host "  plug-selftest: OK ($($selfHarness.Count) harness(es) over $(($selfHarness | ForEach-Object { $_.Plug } | Select-Object -Unique) -join ', '))"
    }
}


# -- the Shell DSL generators: codex/build/*Script.codex compile with the
# just-proven compiler and emit the scripts the build itself runs on. Nothing
# observed that. The deck-short miscompile emitted CORRUPT generator output
# from a clean compile, and it sat silent because no gate ever compared a
# generator against the script beside it; the corruption was found by eye.
# This leg is that comparison, and it also catches the quieter half: an
# emitter answers `# <unknown-cmd>` for a node it does not handle instead of
# failing, so a node added to ShellTypes and forgotten in BashEmit produces a
# script that is wrong rather than missing.
# 
# 26 of 42 generators were already behind their shipped script when this
# became a gate, and porting each drift back by hand is a campaign. Those are
# recorded in build/generated-scripts-baseline.txt and do not fail the build.
# A compile failure, an empty emission, an unhandled-node stub, and any drift
# that is not in that file do. Cost measured 2026-08-06: 61s.
Measure-Phase 'gen-scripts' {
    $chkGen = Join-Path $PSScriptRoot 'check-generated-scripts.ps1'
    if (Test-Path $chkGen) {
        & pwsh -NoProfile -File $chkGen 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a Shell DSL generator is broken or has newly drifted from the script it emits'
            exit 1
        }
    }
}

# -- two independent VM hosts, one source, compared byte for byte. This is a
# TRUST check, not a correctness one: QEMU is a third-party binary whose
# Authenticode signature cannot discriminate a good build from a hostile one,
# because the publisher's signing certificate expired 2023-09-12 and the
# binaries are still signed with it. Verification returns the same failure in
# both worlds and there is nothing left to revoke, so the signature carries no
# information. Two hosts that share no code agreeing on the output does carry
# some. It skips silently on a machine with only one host, which is the normal
# case away from this one. 6s.
Measure-Phase 'vm-differential' {
    $chkDiff = Join-Path $PSScriptRoot 'check-vm-differential.ps1'
    if (Test-Path $chkDiff) {
        & pwsh -NoProfile -File $chkDiff 2>&1 | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: the two VM hosts disagree on the compiler output'
            Write-Host '      Detail: pwsh build/check-vm-differential.ps1'
            exit 1
        }
    }
}

# -- deck headroom. These 47 units are every one in the tree under 2.0x margin;
# the next tightest is 2.13x, in codex/foreword, which this does not cover.
# 1.25 trips codex/build at 52 points and the compiler's own unit at 80, about
# 20 per cent above each one's current requirement. Rationale, corpus numbers
# and the cost of raising the floor are in ProportionalDecks.md.
# 
# It runs HERE because it needs the compiler this run built and must not touch
# build-output\bare-metal\Codex.cdx: by this point SUT === seed is proven, and
# app-sweep below clobbers the bare-metal copy. 23s over 47 units.
Measure-Phase 'deck-headroom' {
    $chkDeck = Join-Path $PSScriptRoot 'deck-headroom.ps1'
    if (Test-Path $chkDeck) {
        # -Fresh: the script serves cached logs without it.
        $dkOut = @(& pwsh -NoProfile -File $chkDeck -Quire 'codex\build' -WithSelf -MinMargin 1.25 `
              -Tag 'gate' -Top 5 -Jobs 8 -Fresh 2>&1 | ForEach-Object { "$_" })
        $dkCode = $LASTEXITCODE
# A filter at the pipe keeps the COUNT and discards the SHAPE: the unit names
# print as two spaces and a path and match no alternative, so every failure
# here named a number of units and never which ones. On failure print it whole.
        if ($dkCode -ne 0) { $dkOut | ForEach-Object { Write-Host "  $_" } }
        else { $dkOut | Where-Object { $_ -match '^\s+margin\s+\d|OK, tightest' } | ForEach-Object { Write-Host "  $_" } }
        if ($dkCode -ne 0) {
            Write-Host 'FAIL: a unit has grown into its deck reservation'
            Write-Host '      Detail: pwsh build/deck-headroom.ps1 -Quire codex\build -WithSelf -Fresh'
            exit 1
        }
# The plug bundles, which were in NO corpus until now: a plug's unit is
# its assembled bundle under build-output, and every other mode here
# skips build-output on purpose. arm64 ran out of room with nothing
# reporting it (plugs 1.98). Each is measured at the -Decks its own
# build.ps1 passes, not at the derivation, or this asks a question the
# build never asks.
        $dkpOut = @(& pwsh -NoProfile -File $chkDeck -Plugs -MinMargin 1.25 `
              -Tag 'gate-plugs' -Top 5 -Jobs 8 -Fresh 2>&1 | ForEach-Object { "$_" })
        $dkpCode = $LASTEXITCODE
        if ($dkpCode -ne 0) { $dkpOut | ForEach-Object { Write-Host "  $_" } }
        else { $dkpOut | Where-Object { $_ -match '^\s+margin\s+\d|OK, tightest' } | ForEach-Object { Write-Host "  $_" } }
        if ($dkpCode -ne 0) {
# NOT necessarily a deck verdict: the script separates a unit that ran and
# refused from one that produced no compiler output at all, and prints the
# exit code, log size and phase count for each. Read those before concluding
# a reservation has grown.
            Write-Host 'FAIL: the plug deck check did not pass -- read the per-unit evidence above'
            Write-Host '      Detail: pwsh build/deck-headroom.ps1 -Plugs -Fresh (and compare -Jobs 1)'
            exit 1
        }
    }
}

# -- the apps are the extended pin on the compiler: 267 entry chapters
# against build/app-sweep-baseline.txt, which names the units known not to
# compile and the reason. Anything dirty and not in that file is a compiler
# or foreword regression. The script existed with -Check and was invoked by
# NOTHING, so it caught nothing: the cons-typed-as-its-element miscompile
# (main 13839) turned RadioStationMain dirty the day CL 13483 landed and
# sat a full day unobserved.
# 
# It runs LAST and sweeps with -Kernel $SutCdx, the compiler this run built.
# It used to let the sweep default to seed\Codex.cdx, which inside a gate is
# the OLD compiler whenever the change moved the seed: main 16020 (a new
# keyword) passed a 270-unit sweep here and broke apps/radio on main.
# Cost measured 2026-08-06: 191s of a 517s gate.
Measure-Phase 'app-sweep' {
    $sweep = Join-Path $PSScriptRoot 'sweep-app-classes.ps1'
    if (Test-Path $sweep) {
        # -Internal sweeps a strided 30 of the 270; the release gate sweeps all
        # of them. 151.7s of the 644.1s gate measured at head 18157, and a
        # compiler regression usually moves a class of construct rather than one
        # unit, so the stride keeps most of the signal. What it cannot see waits
        # for the full sweep, which is the trade this makes.
        # A COMPILER change keeps the STRIDE and an apps change takes the
        # CITE-SCOPED set (Damian, 2026-09-02). Nothing cites the compiler --
        # it is global by construction -- so a scoped set on a compiler CL is
        # chosen by a relation the subject does not participate in, comes out
        # EMPTY, and reads as green. That is red's ruling for test-compile's
        # full corpus, one phase over.
        # @(...) around the if, not $(...): $(...) UNWRAPS a one-element array to
        # its element, so the -CiteScoped branch became the STRING '-CiteScoped'
        # and @sweepArgs splatted it per character, binding -TimeoutSec from 'C'
        # (67). Every apps-only CL's app-sweep was red from 21659 until this.
        # The compiler branch has two elements and stayed an array, which is why
        # only half the phase was broken.
        $sweepArgs = @()
        if ($Internal) { $sweepArgs = @(if ($tCompiler) { @('-Sample', '30') } else { @('-CiteScoped') }) }
        $swOut = @(& pwsh -NoProfile -File $sweep -Check -Jobs 8 -Kernel $SutCdx @sweepArgs 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        if ($code -ne 0) {
            Write-Host ''
            Write-Host 'FAIL: an app entry chapter regressed against build\app-sweep-baseline.txt'
            $swOut | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
            Write-Host '  per-unit detail: test-output\clssweep\_per-unit.csv'
            exit 1
        }
        $swOut | Where-Object { $_ -match 'units:|CHECK|elapsed:' } | ForEach-Object { Write-Host "  $_" }
    }
}

Write-Host 'Something remains suspended in mid-air for a moment before falling'
Write-Host 'to earth with a heavy thud.'
Write-Host ''

Write-Host 'Somewhat shaken by this vision, you rise to your feet to investigate.'
Write-Host 'A crude circle of stones surrounds the spot where the portal appeared.'
Write-Host 'There is something glinting in the grass.'
Write-Host ''
Write-Host 'You pick it up. It is a compiler.'
Write-Host 'It is completely self-contained and needs no other tools to function.'
Write-Host 'On the handle is inscribed: "CODEX".'
Write-Host ''

$buildTimer.Stop()
Write-Host '-- Phase Timings ----------------------------------'
$maxName = ($phaseTimings.Keys | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
foreach ($kv in $phaseTimings.GetEnumerator()) {
    $secs = $kv.Value.TotalSeconds
    $pad = $kv.Key.PadRight($maxName)
    Write-Host ("  {0}  {1,7:N1}s" -f $pad, $secs)
}
Write-Host ("  {0}  {1,7:N1}s" -f 'TOTAL'.PadRight($maxName), $buildTimer.Elapsed.TotalSeconds)
Write-Host ''

# Update constants hash to match the new seed.
if (Test-Path $chkConst) {
    & pwsh -NoProfile -File $chkConst -Update 2>&1 | ForEach-Object { Write-Host "  $_" }
}

exit 0
