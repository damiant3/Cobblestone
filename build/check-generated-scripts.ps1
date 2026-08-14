# Report drift between each codex/build/*Script.codex generator and the
# script it claims to produce.
#
# Hand-written, deliberately, and it is the one script under build/ that
# must stay that way: it has to run when the generators are broken, which
# is exactly when a generated checker would be untrustworthy.
#
# There is NO -Write flag and that omission is load-bearing. Measured
# 2026-08-03, 39 of the 40 generators with a live target had drifted and
# in every case the SHIPPED script was the maintained side and the
# generator was the abandoned one. A bulk regenerate would therefore
# destroy the working scripts, including build.ps1 and test.ps1. Use
# -Diff to read a drift and port it back into the generator by hand.
#
# Usage:
#   check-generated-scripts.ps1                 # table for every generator
#   check-generated-scripts.ps1 -Only test      # one, by emitted name
#   check-generated-scripts.ps1 -Diff test      # show the actual drift
#   check-generated-scripts.ps1 -Update         # rewrite both records below
#
# It answers two questions, and only the first can fail the build.
#   1. Does each generator still emit the script shipped beside it?
#      Record: build/generated-scripts-baseline.txt.
#   2. Which scripts under build/ does no generator emit at all? That is the
#      commoner drift and nothing asked it until 2026-08-14. Record:
#      build/handwritten-scripts.txt. REPORT ONLY -- see the block at the
#      bottom for why a gate there would be wrong.
#
# Exit 1 on a generator that is BROKEN (compile failure, empty output, an
# unhandled-node stub, or emitted text PowerShell cannot parse) or that has
# NEWLY drifted; 0 otherwise. The drift
# already in build/generated-scripts-baseline.txt does not fail the build:
# 26 of 42 were already behind when this became a gate, and a red whose
# answer is always the expected one trains its reader to accept reds.
[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Diff = '',
    [string]$OutRoot = '',
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

$BaseFile = Join-Path $Repo 'build\generated-scripts-baseline.txt'

# -Update writes two records and either may be read-only under Perforce. Writing
# only what changed means the common case (one record moved, the other did not)
# no longer dies halfway through, leaving the first written and the second not.
function Write-Record([string]$Path, [string[]]$Lines, [string]$What) {
    $new = ($Lines -join "`r`n") + "`r`n"
    if ((Test-Path -PathType Leaf $Path) -and ((Get-Content $Path -Raw) -eq $new)) {
        Write-Host "$What unchanged"
        return
    }
    try { Set-Content -Path $Path -Value $new -NoNewline -Encoding utf8; Write-Host "$What updated" }
    catch { Write-Host "$What NOT written (read-only?): $Path"; Write-Host "  p4 edit it and re-run -Update" }
}

# The four generators whose target does not live under build/. A missing
# entry here does not report as an error: the generator simply reads as
# having no live target and is never compared. cvmm-build sat in that
# blind spot from the day it was written until 2026-08-07, emitting a
# script that ships as apps\cvmm\build.ps1.
$AltTarget = @{
    'compile-arm64'  = 'codex\plugs\arm64\compile-arm64.ps1'
    'compile-riscv'  = 'codex\plugs\riscv\compile-riscv.ps1'
    'plug-build-lib' = 'codex\plugs\common\plug-build-lib.ps1'
    'cvmm-build'     = 'apps\cvmm\build.ps1'
}

$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) {
    Write-Host "MISSING: $Stage0"
    Write-Host "Run build/build.ps1, or stage the depot seed: Copy-Item seed\Codex.cdx $Stage0"
    exit 2
}
$digest = (Get-FileHash $Stage0 -Algorithm SHA256).Hash.Substring(0, 16)
Write-Host "compiler: build-output\bare-metal\Codex.cdx [$digest]"

if (-not $OutRoot) { $OutRoot = Join-Path ([System.IO.Path]::GetTempPath()) "genscripts-$PID" }
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

# What each generator claims to emit, and where that lands. $claimed is the
# same information read the other way round, and it is built BEFORE -Only
# filters $specs, because the inventory below asks about every script in the
# tree and not about the one generator someone selected.
$specs = @()
$claimed = @{}
foreach ($g in (Get-ChildItem (Join-Path $Repo 'codex\build') -Filter '*Script.codex' -File)) {
    $text = Get-Content $g.FullName -Raw
    $m = [regex]::Match($text, 'sh-script\s+"([^"]+)"')
    if (-not $m.Success) { continue }
    $name = $m.Groups[1].Value
    $ext = if ($text -match 'emit-bash') { 'sh' } else { 'ps1' }
    $target = if ($ext -eq 'ps1' -and $AltTarget.ContainsKey($name)) { $AltTarget[$name] } else { "build\$name.$ext" }
    $specs += [pscustomobject]@{
        Generator = $g
        Emits     = $name
        Target    = $target
        Present   = (Test-Path -PathType Leaf (Join-Path $Repo $target))
    }
    $claimed[$target.ToLower()] = $g.Name
}

$wanted = if ($Diff) { $Diff } else { $Only }
if ($wanted) {
    $specs = @($specs | Where-Object { $_.Emits -eq $wanted })
    if ($specs.Count -eq 0) { Write-Host "no generator emits '$wanted'"; exit 2 }
}

# One VM boot compiles the whole set, INCLUDING the generators whose target
# script does not exist. Those were filtered out before compilation until
# 2026-08-06, which meant nothing in the tree had ever compiled them. Two of
# the eleven sites in the parse-error campaign -- and one of its four causes
# in its entirety -- were sitting in that set, and surfaced only because they
# were compiled by hand outside this script. Drift is still asked only of a
# generator with a live target, since there is nothing to compare a dead one
# against, but broken is broken either way.
if ($specs.Count -eq 0) { Write-Host "nothing to check"; exit 0 }

$listFile = Join-Path $OutRoot 'generators.txt'
$specs.Generator.FullName | Set-Content -Path $listFile -Encoding UTF8
& (Join-Path $PSScriptRoot 'test-compile-batch.ps1') -ListFile $listFile -OutRoot $OutRoot *> $null

$rows = @()
$parseErrs = @{}
foreach ($s in $specs) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($s.Generator.Name)
    $dir = Join-Path $OutRoot $stem
    $code = Join-Path $dir '.exitcode'
    if (-not (Test-Path -PathType Leaf $code) -or (Get-Content $code -Raw).Trim() -ne '0') {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = 'COMPILE FAILED'; Lines = 0; Drift = 0 }
        continue
    }
    $cdx = Get-ChildItem $dir -Filter '*.cdx' | Select-Object -First 1
    $emitted = Join-Path $dir 'emitted.txt'

    # The run phase is intermittently empty-handed: a generator compiles
    # clean (exit 0, a full .cdx) and still leaves a zero-byte emitted.txt,
    # which used to fail the whole run. Seen twice on 2026-08-06
    # (test-exception-handler, then boot-arm64), each a match on a re-run.
    #
    # Not reproduced on 2026-08-07 in 240 runs of the boot-arm64 kernel that
    # had just failed -- 40 idle, then 40 against eight concurrent VM boots.
    # So the cause is not this kernel and not VM contention at the depth
    # this box reaches, and it is NOT test-run.ps1's own two empty-output
    # paths either: both write the sweep log, which was silent throughout.
    # Retry once and SAY SO rather than fail on it. A retry that succeeds is
    # reported, never silent -- if this line starts appearing every run, the
    # flake has become a defect and the retry is hiding it.
    $runExit = 0
    $firstExit = 0
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        & (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $cdx.FullName -OutFile $emitted *> $null
        $runExit = $LASTEXITCODE
        if ($attempt -eq 1) { $firstExit = $runExit }
        if ((Test-Path -PathType Leaf $emitted) -and (Get-Item $emitted).Length -gt 0) {
            if ($attempt -gt 1) { Write-Host "  note: $($s.Emits) emitted nothing on attempt 1 (exit $firstExit), succeeded on retry" }
            break
        }
    }
    if (-not (Test-Path -PathType Leaf $emitted) -or (Get-Item $emitted).Length -eq 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "EMITTED NOTHING (x2, exit $runExit)"; Lines = 0; Drift = 0 }
        continue
    }

    # An emitter answers `# <unknown-cmd>` for a node it does not handle,
    # rather than failing, so a node added to ShellTypes and forgotten in
    # BashEmit or KshEmit produces a script that is silently wrong.
    #
    # Re-measured 2026-08-06, later the same day and against the constructor
    # list rather than a carried number: ShellTypes declares 148, PowerShellEmit
    # handles all 148, BashEmit is missing 80 and KshEmit 79. (This said 140
    # and 73 that morning. The counts move; re-derive them, never copy them.)
    # So the arm earns its keep mainly on the .ps1 side. It said here that it
    # CANNOT FIRE for bash, because the one bash generator emits
    # build/test-run.sh, which does not exist, so it was dropped before
    # compilation. That is no longer how the loop runs: every generator is
    # compiled now, target or no target, and testrunBashScript is scanned like
    # the rest. It emits 65 lines and 0 stubs, so its script happens to use
    # only handled constructors. The 80 unhandled arms are therefore still
    # unmeasured here; a second bash generator is what would reach them.
    # Fired deliberately 2026-08-06 by deleting the ScReadBytes arm from
    # PowerShellEmit: the row moved to UNHANDLED NODES (1) and the check
    # failed, so the scan is not decoration.
    $madeText = Get-Content $emitted -Raw
    $stubs = @([regex]::Matches($madeText, '<unknown-(?:cmd|expr)>')).Count
    if ($stubs -gt 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "UNHANDLED NODES ($stubs)"; Lines = 0; Drift = $stubs }
        continue
    }

    # Is the emitted text valid PowerShell at all? Nothing asked until
    # 2026-08-06 and the answer was no for 8 of 42: 59 syntax errors that
    # every green run had passed over, because a generator can compile,
    # emit, carry no stub, sit close to its shipped script, and still print
    # text the parser refuses. ParseFile is the language's own front end, so
    # the answer is mechanically decidable rather than a judgement, and it
    # costs about 40 ms a file. Keyed off the target extension and not the
    # generator name: bash parsed as PowerShell would measure nothing.
    if ($s.Target -like '*.ps1') {
        $perr = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($emitted, [ref]$null, [ref]$perr)
        if ($perr.Count -gt 0) {
            $parseErrs[$s.Emits] = $perr
            $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "PARSE ERRORS ($($perr.Count))"; Lines = 0; Drift = $perr.Count }
            continue
        }
    }

    # A generator with no live target has now been asked every question that
    # can be put to it. Drift is not one of them, so it leaves no row and the
    # counts below read as they always did.
    if (-not $s.Present) { continue }

    $shipped = @(Get-Content (Join-Path $Repo $s.Target))
    $made = @(Get-Content $emitted)
    # Whitespace-only difference is not drift worth porting: the emitter's
    # blank-line and indent conventions differ from the hand-maintained
    # files across the board and would drown every real finding.
    $a = @($shipped | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $b = @($made | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $delta = @(Compare-Object $a $b).Count

    if ($Diff) {
        Write-Host ""
        Write-Host "$($s.Target)  <=  codex\build\$($s.Generator.Name)"
        Write-Host "  '<=' is in the shipped script only, '=>' is what the generator emits."
        Write-Host ""
        Compare-Object $a $b | Format-Table -AutoSize SideIndicator, InputObject | Out-String -Width 200 | Write-Host
        exit ($(if ($delta -gt 0) { 1 } else { 0 }))
    }

    $rows += [pscustomobject]@{
        Emits  = $s.Emits
        Status = if ($delta -eq 0) { 'match' } else { 'DRIFTED' }
        Lines  = $shipped.Count
        Drift  = $delta
    }
}

$rows | Sort-Object Drift -Descending | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$dead = @($specs | Where-Object { -not $_.Present })
if ($dead.Count -gt 0) {
    Write-Host "generators whose target does not exist ($($dead.Count)):"
    foreach ($d in $dead) { Write-Host "  $($d.Generator.Name) -> $($d.Target)" }
}

# The inventory: build/*.ps1 that NO generator claims. Everything above this
# line reads the tree generator-first and cannot see a script that was never
# generated at all, which is the commoner of the two ways this lane drifts --
# an agent writes a new script and does not write the .codex that emits it.
#
# It REPORTS and never fails, deliberately. Measured 2026-08-14: 93 of 135
# scripts under build/ have no generator, and most are meant not to have one
# (probes, arms, interop harnesses, one-offs, and this file). A rule that
# every script needs a generator is not the policy and never was, so a gate
# here would be 93 reds whose answer is always "expected", which is the exact
# failure the drift baseline exists to avoid.
#
# What IS worth a human's attention is a script that appeared since the last
# reading. Same mechanism as the drift baseline: the known set lives in
# build/handwritten-scripts.txt, and only a NEW name prints. Answer it by
# writing the generator, or by recording the name with -Update -- both are
# fine, and which one is right is a judgement no script can make.
if (-not $wanted) {
    $invFile = Join-Path $Repo 'build\handwritten-scripts.txt'
    $onDisk = @(Get-ChildItem (Join-Path $Repo 'build') -Filter '*.ps1' -File |
        Where-Object { -not $claimed.ContainsKey("build\$($_.Name)".ToLower()) } |
        ForEach-Object { $_.Name } | Sort-Object)

    if ($Update) {
        $invHeader = @(
            "# handwritten-scripts.txt -- generated by build/check-generated-scripts.ps1 -Update",
            "#",
            "# Scripts under build/ that no generator in codex/build/ emits. This is an",
            "# INVENTORY, not a debt list: most of these are meant to be hand-written",
            "# (probes, flight arms, interop harnesses, one-offs, and the checker",
            "# itself, which must run when the generators are broken).",
            "#",
            "# The check reports a script that is NOT listed here and never fails on",
            "# one. A new name means somebody added a script; whether it wants a",
            "# generator is a judgement, so answer it either by writing the .codex or",
            "# by recording the name here.",
            ""
        )
        Write-Record $invFile ($invHeader + $onDisk) "inventory ($($onDisk.Count) hand-written script(s))"
    } elseif (Test-Path -PathType Leaf $invFile) {
        $known = @(Get-Content $invFile |
            Where-Object { $_ -notmatch '^\s*#' -and $_ -match '\S' } |
            ForEach-Object { $_.Trim() })
        $fresh = @($onDisk | Where-Object { $known -notcontains $_ })
        $gone  = @($known | Where-Object { $onDisk -notcontains $_ })
        if ($fresh.Count -gt 0) {
            Write-Host ""
            Write-Host "hand-written scripts not in the inventory ($($fresh.Count)) -- report only, not a failure:"
            foreach ($f in $fresh) { Write-Host "  build\$f  (no generator in codex/build/ emits this)" }
            Write-Host "  Write the generator, or record it: build/check-generated-scripts.ps1 -Update"
        }
        if ($gone.Count -gt 0) {
            Write-Host ""
            Write-Host "inventory names $($gone.Count) script(s) that are gone or now generated:"
            foreach ($g2 in $gone) { Write-Host "  $g2 (drop it: -Update)" }
        }
    } else {
        Write-Host ""
        Write-Host "no inventory at $invFile -- run with -Update to record the $($onDisk.Count) hand-written script(s)"
    }
}

# Five ways a generator can be wrong and only one of them is baselineable.
# DRIFTED is a generator that has fallen behind the script it emits, which is
# a campaign and not a defect of the day. The other four are a generator that
# is broken NOW, and none of them is ever an expected answer, so no baseline
# may quiet them.
$broken     = @($rows | Where-Object { $_.Status -ne 'match' -and $_.Status -ne 'DRIFTED' })
$driftedNow = @($rows | Where-Object { $_.Status -eq 'DRIFTED' } | ForEach-Object { $_.Emits } | Sort-Object)

if ($Update) {
    $header = @(
        "# generated-scripts-baseline.txt -- generated by build/check-generated-scripts.ps1 -Update",
        "#",
        "# Generators under codex/build/ that no longer emit the script they are",
        "# shipped beside. Measured 2026-08-03 over all 40 with a live target: in",
        "# every case the SHIPPED script was the maintained side and the generator",
        "# was the abandoned one. So this is a list of generators that have fallen",
        "# behind, NOT of scripts that are wrong -- which is why the fix is to port",
        "# the drift back by hand and why there is no -Write flag.",
        "#",
        "# This file is the KNOWN residue. The check fails on a generator that",
        "# drifts and is not listed here. Shrinking it is the work; read a drift",
        "# with -Diff <name>. Growing it needs a reason in the CL description.",
        "#",
        "# A compile failure, an emitter that produces nothing, an unhandled node",
        "# stub, and emitted text that PowerShell cannot parse are NOT recordable",
        "# here: those fail whatever this file says.",
        ""
    )
    Write-Host ""
    Write-Record $BaseFile ($header + $driftedNow) "baseline ($($driftedNow.Count) known drift(s))"
    exit 0
}

Write-Host ""
Write-Host "Checked $($rows.Count) generators, $($driftedNow.Count) drifted, $($broken.Count) broken, $($dead.Count) with no target."

if ($broken.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- $($broken.Count) generator(s) broken, not merely behind:"
    foreach ($b in $broken) { Write-Host "  $($b.Emits): $($b.Status)" }
    if (@($broken | Where-Object { $_.Status -like 'UNHANDLED NODES*' }).Count -gt 0) {
        Write-Host "  An emitter answers '# <unknown-cmd>' for a node it does not handle rather"
        Write-Host "  than failing, so the script it writes is silently wrong, not absent."
        Write-Host "  Add the arm in BashEmit / KshEmit / the PowerShell emitter."
    }
    if ($parseErrs.Count -gt 0) {
        Write-Host "  Emitted text that PowerShell cannot parse is a script that cannot run"
        Write-Host "  at all. An error count is not a defect count: 59 of these reduced to"
        Write-Host "  four causes and eleven sites, so read the FIRST error of each and"
        Write-Host "  expect the rest to be cascade."
        foreach ($k in ($parseErrs.Keys | Sort-Object)) {
            Write-Host "  $k :"
            foreach ($e in ($parseErrs[$k] | Select-Object -First 3)) {
                Write-Host "    line $($e.Extent.StartLineNumber): $($e.Message)"
            }
        }
    }
    exit 1
}

if (-not (Test-Path -PathType Leaf $BaseFile)) {
    Write-Host "check-generated-scripts: no baseline at $BaseFile -- run with -Update"
    exit 1
}

$baseline = @(Get-Content $BaseFile |
    Where-Object { $_ -notmatch '^\s*#' -and $_ -match '\S' } |
    ForEach-Object { $_.Trim() })

# Intersect with what was actually checked, so -Only does not report every
# generator it did not look at as repaired.
$checked  = @($rows | ForEach-Object { $_.Emits })
$newDrift = @($driftedNow | Where-Object { $baseline -notcontains $_ })
$fixed    = @($baseline | Where-Object { $checked -contains $_ -and $driftedNow -notcontains $_ })

if ($newDrift.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- $($newDrift.Count) generator(s) newly drifted:"
    foreach ($n in $newDrift) { Write-Host "  $n" }
    Write-Host "  Read it:   build/check-generated-scripts.ps1 -Diff $($newDrift[0])"
    Write-Host "  Port the change back into codex/build/, or record it:"
    Write-Host "             build/check-generated-scripts.ps1 -Update"
    exit 1
}

if ($fixed.Count -gt 0) {
    Write-Host "check-generated-scripts: OK -- and $($fixed.Count) baselined generator(s) match again:"
    $fixed | ForEach-Object { Write-Host "  $_ (drop it from the baseline)" }
    exit 0
}

Write-Host "check-generated-scripts: OK ($($driftedNow.Count) known drift(s))"
exit 0
