# sweep-classes.ps1 -- compile every app ENTRY chapter, collect diagnostic
# codes, group by code. A sweep on demand: no boot, no run, no battery.
#
# THE UNIT IS THE ENTRY CHAPTER, NOT THE FILE. A chapter compiled standalone
# cannot see its siblings, so every intra-app reference reports CDX3002 and
# the sweep measures its own harness. Entry chapters cite their siblings;
# compile.ps1 resolves the cite graph and assembles the real unit.
#
# max-errors is 20 and is compiled into the seed, so a unit reporting exactly
# 20 is a LOWER BOUND, not a count.
#
# A cite naming an unregistered quire throws inside compile.ps1 BEFORE the
# compiler runs, so it produces no log at all. That is captured as CITE-FAIL
# rather than being silently counted as clean.
# -Check turns the sweep from a measurement into a PIN. Without it this
# script has never been able to fail: it printed a report and exited 0
# whatever it found, so nothing could gate on it and a regression was
# only ever caught by a human reading the number.
#
# The apps are the extended pin on the compiler -- 265 diverse programs
# exercising the front end -- so what this guards is the compiler, not
# the applications. A unit that stops compiling is a compiler or
# foreword regression until proven otherwise.
#
# The baseline names the units KNOWN not to compile, with the reason.
# -Check fails on any unit dirty that is not in that list. A baseline
# unit that has started compiling is reported but is not a failure, so
# closing one never turns the pin red on the person who closed it; it
# asks them to tighten the baseline instead.
[CmdletBinding()]
param(
    [string]$Filter = '',         # substring match on the relative path
    [int]$Jobs = 4,
    [int]$TimeoutSec = 300,
    [string]$OutDir = '',
    [switch]$Check,               # exit non-zero when a unit regresses
    [int]$Sample = 0,             # compile a strided subset of this many units; 0 sweeps all
    [string]$Baseline = '',
    [string]$Kernel = ''          # compiler to sweep with; default seed\Codex.cdx. build.ps1 passes the SUT.
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Derived, never hardcoded: every agent workspace has a different root, and a
# literal path here points one agent's sweep at another agent's tree.
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

# The kernel is passed to every compile explicitly. This script used to copy
# seed\Codex.cdx over build-output\bare-metal\Codex.cdx and let compile.ps1
# default to it, so inside a gate it swept with the OLD seed and never saw the
# compiler the gate had just built: main 16020 made 'bounded' a keyword and a
# 270-unit sweep passed while apps\radio\RadioStation.codex:574 stopped
# compiling (ExaminersAssay, "The App Sweep Passed While An App Was Broken").
# build.ps1 passes -Kernel $SutCdx; a hand run defaults to the seed and says so.
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
$Kernel = (Resolve-Path $Kernel).Path
Write-Host "kernel: $Kernel [$((Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0, 16))]"

# NOT under build-output: build.ps1's Clean phase deletes that whole tree, so a
# gate run between taking a sweep and reading it destroys the results. Measured
# the hard way. test-output/ is where the battery already keeps what it keeps.
if (-not $OutDir) { $OutDir = Join-Path $Repo 'test-output\clssweep' }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Entry chapters = anything declaring `opening`. Skip generated bundles.
$files = @(
    Get-ChildItem -Path (Join-Path $Repo 'apps') -Recurse -Include '*.codex' -File |
    Where-Object { $_.FullName -notmatch '\\build-output\\' } |
    Where-Object { (Select-String -Path $_.FullName -Pattern '^  opening\s*:' -Quiet) } |
    Sort-Object FullName
)
if ($Filter) { $files = @($files | Where-Object { $_.FullName -like "*$Filter*" }) }

# -Sample takes every Nth unit of the sorted list rather than the first N, so
# the subset spreads across apps instead of stopping at whatever sorts early.
# The stride is derived from the live count, so it needs no maintenance as apps
# are added, and it is DETERMINISTIC: the same tree sweeps the same units, so a
# red is reproducible and a green means the same thing twice.
#
# What it is for: build.ps1 -Internal runs a sample per CL and the release gate
# runs all of them. What it CANNOT do is see a regression confined to units the
# stride skipped, which is the trade, and it is why -Check will not report a
# baseline unit as fixed on a sampled run (below) any more than on a filtered
# one. A compiler regression usually moves a CLASS of construct and so shows up
# in many units at once; one confined to a single unit waits for the full sweep.
$sweepAll = $files.Count
if ($Sample -gt 0 -and $Sample -lt $files.Count) {
    $stride = [math]::Ceiling($files.Count / $Sample)
    $picked = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $files.Count; $i += $stride) { $picked.Add($files[$i]) }
    $files = @($picked)
    Write-Host "Sweep: SAMPLED $($files.Count) of $sweepAll entry chapters (every ${stride}th); the rest are the release gate's"
}

Write-Host "Sweep: $($files.Count) entry chapters  (jobs=$Jobs)"
$compile = Join-Path $Repo 'build\compile.ps1'
$sw = [Diagnostics.Stopwatch]::StartNew()

$results = $files | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $f       = $_
    $Repo    = $using:Repo
    $OutDir  = $using:OutDir
    $compile = $using:compile
    $timeout = $using:TimeoutSec
    $kernel  = $using:Kernel

    # ANY UNCAUGHT THROW IN HERE IS A SWEEP-WIDE OUTAGE, NOT ONE FAILED FILE.
    # $ErrorActionPreference is Stop at the top of the script, and a bare
    # ReadAllText on a log a child process was writing can land on a locked
    # file: WaitForExit returns before Windows releases the redirected handle.
    # test-cross-batch.ps1 lost 457 tests to exactly that on 2026-08-18 and
    # stranded five guests at 115,597 CPU-seconds, because the harness that
    # died is also the thing enforcing every child's ceiling. Share-tolerant
    # open plus a short retry; an unreadable log reads as empty, which fails
    # toward a reported error rather than a silent pass.
    function Read-LogShared([string]$path) {
        for ($ri = 0; $ri -lt 5; $ri++) {
            try {
                $fs = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                try {
                    $sr = [System.IO.StreamReader]::new($fs)
                    return $sr.ReadToEnd()
                } finally { $fs.Dispose() }
            } catch { Start-Sleep -Milliseconds 100 }
        }
        return ''
    }

    $rel = $f.FullName.Substring($Repo.Length + 1)
    $tag = ($rel -replace '[\\/]', '_') -replace '\.codex$', ''
    $log = Join-Path $OutDir "$tag.log"
    $err = Join-Path $OutDir "$tag.stderr"
    $cdx = Join-Path $OutDir "$tag.cdx"

    $p = Start-Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', $compile,
        '-Src', $f.FullName, '-Out', $cdx, '-Log', $log, '-Kernel', $kernel
    ) -PassThru -WindowStyle Hidden -RedirectStandardError $err

    if (-not $p.WaitForExit($timeout * 1000)) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        return [pscustomobject]@{ File=$rel; Exit='TIMEOUT'; Errors=0; Capped=$false; Codes=@(); Note='' }
    }
    $exit = "$($p.ExitCode)"

    $codes = @(); $n = 0; $note = ''
    if (Test-Path $log) {
        $txt = Read-LogShared $log
        foreach ($m in [regex]::Matches($txt, 'error CDX(\d+)')) { $codes += $m.Groups[1].Value }
        $h = [regex]::Match($txt, 'CODEGEN-ERRORS:(\d+)')
        if ($h.Success) { $n = [int]$h.Groups[1].Value } else { $n = $codes.Count }
    }
    # No log + nonzero exit = died before the compiler ran (unresolvable cite).
    if ($n -eq 0 -and $exit -ne '0' -and (Test-Path $err)) {
        $etxt = Read-LogShared $err
        if ($etxt -match 'Unresolvable cite: (.+?)(\r|\n|$)') { $exit = 'CITE-FAIL'; $note = $matches[1].Trim() }
        elseif ($etxt.Trim()) { $note = ($etxt.Trim() -split "`n")[0] }
    }
    [pscustomobject]@{ File=$rel; Exit=$exit; Errors=$n; Capped=($n -ge 20); Codes=$codes; Note=$note }
}
$sw.Stop()
Write-Host "elapsed: $([math]::Round($sw.Elapsed.TotalMinutes,1)) min`n"

$results | Sort-Object -Property @{E={$_.Errors};Descending=$true}, File |
    Select-Object File, Exit, Errors, Capped, Note |
    Export-Csv -NoTypeInformation -Path (Join-Path $OutDir '_per-unit.csv')

$byCode = @{}
foreach ($r in $results) { foreach ($c in $r.Codes) {
    if (-not $byCode.ContainsKey($c)) { $byCode[$c] = [pscustomobject]@{ Code=$c; Count=0; Files=@() } }
    $byCode[$c].Count++
    if ($byCode[$c].Files -notcontains $r.File) { $byCode[$c].Files += $r.File }
} }

$clean = @($results | Where-Object { $_.Exit -eq '0' })
$cite  = @($results | Where-Object { $_.Exit -eq 'CITE-FAIL' })
$diag  = @($results | Where-Object { $_.Exit -eq '4' })
$to    = @($results | Where-Object { $_.Exit -eq 'TIMEOUT' })
$other = @($results | Where-Object { $_.Exit -notin @('0','4','TIMEOUT','CITE-FAIL') })

Write-Host ("units: {0}   CLEAN: {1}   diagnostics: {2}   cite-fail: {3}   other-exit: {4}   timeout: {5}" -f `
    $results.Count, $clean.Count, $diag.Count, $cite.Count, $other.Count, $to.Count)
Write-Host ("capped at max-errors=20 (lower bounds): {0}" -f @($results | Where-Object { $_.Capped }).Count)
Write-Host ("total diagnosed errors: {0}`n" -f (($results | Measure-Object -Property Errors -Sum).Sum))

Write-Host "diagnostic codes, by total occurrences:"
$byCode.Values | Sort-Object Count -Descending | ForEach-Object {
    "  {0,-9} {1,6} errors  {2,4} units" -f "CDX$($_.Code)", $_.Count, $_.Files.Count
}
$byCode.Values | Sort-Object Count -Descending |
    Select-Object Code, Count, @{N='Units';E={$_.Files.Count}}, @{N='FileList';E={$_.Files -join ';'}} |
    Export-Csv -NoTypeInformation -Path (Join-Path $OutDir '_by-code.csv')

if ($cite.Count) {
    Write-Host "`ncite failures (never reached the compiler):"
    $cite | ForEach-Object { "  {0}`n      {1}" -f $_.File, $_.Note }
}
Write-Host "`nwrote $OutDir\_per-unit.csv and _by-code.csv"

if ($Check) {
    if (-not $Baseline) { $Baseline = Join-Path $Repo 'build\app-sweep-baseline.txt' }
    if (-not (Test-Path $Baseline)) {
        Write-Host "`nCHECK: no baseline at $Baseline"
        exit 2
    }

    # One unit per line, path first, '#' comments and blanks ignored. The
    # reason is prose after the path and is for the reader, not the check.
    $expected = @{}
    foreach ($line in Get-Content $Baseline) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $expected[($t -split '\s{2,}|\t')[0].Trim()] = $true
    }

    $bad = @($results | Where-Object { $_.Exit -ne '0' })

    # A unit that failed with NO diagnostics did not fail the compiler, it
    # fell over. Measured 2026-07-20: at -Jobs 6 five units exited 4 with
    # zero errors, the guest faulting on wild MMIO addresses, and all five
    # compiled clean alone -- host contention between concurrent VMs. So
    # re-run those one at a time before believing them, or the pin is
    # flaky and gets ignored, which is worse than no pin.
    #
    # That measurement predates the fix for its own cause by two days: the
    # box's DDR5 was on an XMP profile it was not stable at until 2026-07-22
    # (ExaminersAssay "The parallelism default"). The default went 6 -> 8 on
    # 2026-08-02 by Damian's ruling. This re-run stays regardless -- it is
    # what makes any slot count safe, and it is cheap when nothing is dirty.
    $suspect = @($bad | Where-Object { $_.Errors -eq 0 -and $_.Exit -eq '4' })
    if ($suspect.Count) {
        Write-Host "`nCHECK: $($suspect.Count) unit(s) failed with no diagnostics; re-running alone"
        foreach ($s in $suspect) {
            $tmpLog = Join-Path $OutDir ('recheck-' + ($s.File -replace '[\\/\.]', '_') + '.log')
            $tmpOut = [System.IO.Path]::ChangeExtension($tmpLog, '.cdx')
            & pwsh (Join-Path $Repo 'build\compile.ps1') -Src $s.File -Out $tmpOut -Log $tmpLog -Kernel $Kernel *> $null
            $n = @(Select-String -Path $tmpLog -Pattern 'error CDX' -EA SilentlyContinue).Count
            if ($n -eq 0 -and (Test-Path $tmpOut)) {
                Write-Host "  $($s.File): clean alone -- contention, not a regression"
                $bad = @($bad | Where-Object { $_.File -ne $s.File })
            } else {
                Write-Host "  $($s.File): still fails alone ($n errors)"
            }
        }
    }

    $regressed = @($bad | Where-Object { -not $expected.ContainsKey($_.File) })
    $fixed     = @($expected.Keys | Where-Object { $f = $_; -not ($bad | Where-Object { $_.File -eq $f }) })

    # A filtered or sampled run cannot see most of the baseline, so every unit
    # it did not compile would look "fixed". Only a full run may say that.
    if ($fixed.Count -and -not $Filter -and $Sample -le 0) {
        Write-Host "`nCHECK: $($fixed.Count) baseline unit(s) now compile -- tighten $Baseline"
        $fixed | ForEach-Object { "  $_" }
    }

    if ($regressed.Count) {
        Write-Host "`nCHECK FAILED: $($regressed.Count) unit(s) regressed"
        $regressed | ForEach-Object { "  {0}  ({1} errors)  {2}" -f $_.File, $_.Errors, (($_.Codes | Sort-Object -Unique | ForEach-Object { "CDX$_" }) -join ' ') }
        exit 1
    }

    Write-Host "`nCHECK OK: $($clean.Count) clean, $($bad.Count) known-dirty, 0 regressions"
    exit 0
}
