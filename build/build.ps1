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
# the pre-build guards, cdx-build, sign, canary, the text round-trip, the CDX
# fixed point, test-bvt, oracles, check-errors) always run: they are what certify the seed is
# a byte-identical self-fixed-point that boots. The regression phases below run
# only when a file they depend on changed in THIS workspace; skipped ones are
# caught by the next full gate. Mapping, by what actually feeds each phase:
#   jonquil / vm-differential  <- codex/compiler   (codegen and the DDC witness)
#   plug-*                      <- codex/plugs or codex/compiler (codegen feeds plugs)
#   gen-scripts / deck-headroom <- codex/build or build (the generators + their quire)
#   app-sweep                   <- apps or codex/compiler (the compiler builds the apps)
$SkipPhases = [System.Collections.Generic.HashSet[string]]::new()
if ($Internal) {
    $changed = @()
    try { $changed = @(p4 opened 2>$null | ForEach-Object { (($_ -split '#')[0]) -replace '^//[^/]+/[^/]+/','' } | Where-Object { $_ }) } catch { }
    $tCompiler = [bool]($changed | Where-Object { $_ -match '^codex/compiler/' })
    $tPlugs    = [bool]($changed | Where-Object { $_ -match '^codex/plugs/' })
    $tBuild    = [bool]($changed | Where-Object { $_ -match '^(codex/build/|build/)' })
    $tApps     = [bool]($changed | Where-Object { $_ -match '^apps/' })
    $runPhase = [ordered]@{
        'jonquil'         = $tCompiler
        'plug-binary'     = ($tPlugs -or $tCompiler)
        'cross-smoke'     = ($tPlugs -or $tCompiler)
        'plug-smoke'      = ($tPlugs -or $tCompiler)
        'gen-scripts'     = $tBuild
        'vm-differential' = $tCompiler
        'deck-headroom'   = $tBuild
        'app-sweep'       = ($tApps -or $tCompiler)
    }
    foreach ($k in $runPhase.Keys) { if (-not $runPhase[$k]) { [void]$SkipPhases.Add($k) } }
    $ran = @($runPhase.Keys | Where-Object { $runPhase[$_] })
    Write-Host ('  [internal gate] changed here: ' + $(if ($changed.Count) { ($changed | Sort-Object -Unique) -join ', ' } else { 'nothing opened' }))
    Write-Host ('  [internal gate] core + BVT always, plus: ' + $(if ($ran.Count) { ($ran -join ', ') } else { '(nothing implicated)' }))
    Write-Host ('  [internal gate] deferred to the next full gate: ' + $(if ($SkipPhases.Count) { (@($SkipPhases) | Sort-Object) -join ', ' } else { '(none)' }))
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
        foreach ($rl in $lines) {
            if ($rl.StartsWith('CODEGEN-HALTED') -or $rl.StartsWith('CODEGEN-ERRORS')) { $halted = $true; break }
            if (-not $rl.StartsWith('WD:') -and -not $rl.StartsWith('HEAP:') -and -not $rl.StartsWith('STACK:')) { $textLines.Add($rl) }
        }
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
# OPT-IN, and deliberately so. Turning it on for everyone is a decision about
# everyone's gate. It is on when either is true:
#   $env:CODEX_CHECK_DOC_COUNTS = '1'      per run
#   a file named .doc-counts in the repo root   per agent, per workspace
# The second is what makes an A/B arm: one workspace carries the file, another
# does not, and the difference is visible in what comes out.
$chkCounts = Join-Path $PSScriptRoot 'check-doc-counts.ps1'
$countsOn = ($env:CODEX_CHECK_DOC_COUNTS -eq '1') -or (Test-Path (Join-Path $Repo '.doc-counts'))
if ($countsOn -and (Test-Path $chkCounts)) {
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
Copy-Item -Force $SutCdx (Join-Path $Repo 'build-output\bare-metal\Codex.cdx')

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
if ($drift -gt $maxDrift) {
    Write-Host "FAIL: SUT size drifted too far from seed"
    Write-Host "  seed: $seedSize bytes  SUT: $sutSize bytes  drift: $drift (max $maxDrift)"
    exit 1
}

$textStage1 = Join-Path $OutDir 'stage1.codex'
$textStage2 = Join-Path $OutDir 'stage2.codex'

Write-Host 'Searching inward for tranquility and happiness, you close your eyes.'
Measure-Phase 'text-stage1' {
    if (-not (Invoke-BuildText -InputFile $CodexSrc -Kernel $SutCdx -Output $textStage1)) { exit 1 }
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
$testKernel = $cdxStage1

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

Copy-Item -Force $cdxStage1 (Join-Path $OutDir 'NewSeed.cdx')

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
# REFUSED, and with the codes it declares. 176 tests assert a rejection, and
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
# 22s over 176 refusals at -Jobs 8.
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
    $binaryBackends = @('riscv','arm64','t3isa','elf','pe','img')
    $plugFail = @()
    foreach ($bp in $binaryBackends) {
        $bs = Join-Path $Repo "codex\plugs\$bp\build.ps1"
        if (-not (Test-Path $bs)) { $plugFail += "$bp(no-build.ps1)"; continue }
        & pwsh -NoProfile -File $bs *> $null
        $cdx = Join-Path $Repo "codex\plugs\$bp\build-output\$bp-plug.cdx"
        $blog = Join-Path $Repo "codex\plugs\$bp\build-output\build.log"
        $bad = -not (Test-Path $cdx)
        if ((Test-Path $blog) -and (Select-String -Path $blog -Pattern 'CODEGEN-ERRORS|error CDX' -Quiet)) { $bad = $true }
        if ($bad) { $plugFail += $bp }
    }
    if ($plugFail.Count -gt 0) {
        Write-Host ''
        Write-Host "FAIL: binary plug build -- $($plugFail -join ', ')"
        foreach ($bp in $plugFail) {
            $blog = Join-Path $Repo "codex\plugs\$bp\build-output\build.log"
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
    $smokePlugs = @('typescript', 'python', 'rust', 'ptx')
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
        & pwsh -NoProfile -File $chkDeck -Quire 'codex\build' -WithSelf -MinMargin 1.25 `
              -Tag 'gate' -Top 5 -Jobs 8 -Fresh 2>&1 |
            Where-Object { $_ -match 'FAIL|^\s+margin\s+\d|OK, tightest' } |
            ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'FAIL: a unit has grown into its deck reservation'
            Write-Host '      Detail: pwsh build/deck-headroom.ps1 -Quire codex\build -WithSelf -Fresh'
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
        $swOut = @(& pwsh -NoProfile -File $sweep -Check -Jobs 8 -Kernel $SutCdx 2>&1 | ForEach-Object { "$_" })
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
