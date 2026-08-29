# Grade every example the compile page ships, in the page's own module.
#
# The page hands a visitor a dropdown of examples and compiles the chosen one
# in codex-compiler.wasm. Nothing else in the tree ever runs that pairing:
# build-page.ps1 only COPIES examples.json, and build/compile.ps1 is a MORE
# GENEROUS bed than the page -- it bundles the whole foreword where the page's
# unit is flat, so a `cites Foreword chapter MathLib` that resolves under
# compile.ps1 fails here CDX3007. Two examples shipped green under compile.ps1
# and refused on the page before the prelude field was added (reek, 2026-08-27,
# caught by Damian rather than by a runner). This is that runner.
#
# -Calibrate is the half that makes a green mean anything. It mangles each
# subject's `Chapter:` header and requires EVERY example to refuse. A subject
# that still compiles there is one this harness cannot read an answer for, and
# its pass on the real run was free.
[CmdletBinding()]
param(
    [string]$Module,
    [string]$Examples,
    [string]$Page,
    [string[]]$Only,
    [string[]]$Cat,
    [int]$TimeoutSec = 300,
    [switch]$Calibrate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Module)   { $Module   = Join-Path $PSScriptRoot 'build-output\page\codex-compiler.wasm' }
if (-not $Examples) { $Examples = Join-Path $PSScriptRoot 'page\examples.json' }
if (-not $Page)     { $Page     = Join-Path $PSScriptRoot 'page\prism.html' }

if (-not (Get-Command 'wasmtime' -ErrorAction SilentlyContinue)) {
    Write-Host 'REFUSE: wasmtime is not on PATH.'; exit 2
}
foreach ($f in @($Module, $Examples, $Page)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "REFUSE: no $f"; exit 2 }
}

# The deck ladder is DERIVED from the page rather than restated here. The page
# climbs on CDX9002 and a runner that grades at one fixed reservation answers a
# question the page never asks: an example needing 48 reads as refused, and one
# needing more than the ladder's top reads as passing at whatever this file
# happened to say. A copy of the list here would be a second constant to keep
# equal by hand, which is silent when it drifts.
$pageText = [IO.File]::ReadAllText($Page)
$m = [regex]::Match($pageText, 'const\s+DECKS\s*=\s*\[([^\]]*)\]')
if (-not $m.Success) {
    Write-Host "REFUSE: no `const DECKS = [...]` in $Page, so the ladder cannot be derived and grading at a guess would measure this file instead of the page."
    exit 2
}
$DECKS = @($m.Groups[1].Value -split ',' | ForEach-Object { $_.Trim() } |
           Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
if ($DECKS.Count -eq 0) { Write-Host "REFUSE: the DECKS list in $Page parsed to nothing."; exit 2 }

$all = @(Get-Content $Examples -Raw | ConvertFrom-Json)
if ($all.Count -eq 0) { Write-Host "REFUSE: $Examples holds no examples."; exit 2 }

# A filter that selects nothing must REFUSE, not run zero subjects and print
# '0 failed'. That is a screen which cannot fail wearing a pass.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
$Cat  = @($Cat  | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
if ($Only) {
    $unknown = @($Only | Where-Object { $n = $_; -not ($all | Where-Object { $_.name -eq $n }) })
    if ($unknown) { Write-Host ("REFUSE: -Only names no example: {0}" -f ($unknown -join ', ')); exit 2 }
}
if ($Cat) {
    $unknown = @($Cat | Where-Object { $c = $_; -not ($all | Where-Object { $_.cat -eq $c }) })
    if ($unknown) { Write-Host ("REFUSE: -Cat names no category: {0}" -f ($unknown -join ', ')); exit 2 }
}
$subjects = @($all | Where-Object {
    (-not $Only -or $Only -contains $_.name) -and (-not $Cat -or $Cat -contains $_.cat)
})
if ($subjects.Count -eq 0) { Write-Host 'REFUSE: the filter selected no examples.'; exit 2 }

$work = Join-Path $PSScriptRoot 'build-output\example-test'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# A module is only as fresh as its last build. This is a note and not a refusal:
# the page SERVES this module, so grading the shipped bytes is the point even
# when the tree has moved past them. What is dishonest is not saying so.
$concat = Join-Path $Repo 'build\output\Codex.codex'
$stale = ''
if ((Test-Path -PathType Leaf $concat) -and
    ((Get-Item $concat).LastWriteTime -gt (Get-Item $Module).LastWriteTime)) {
    $stale = ' (older than build\output\Codex.codex; rebuild the page to grade head)'
}

Write-Host ("[ex] module  : {0:N0} bytes, {1}{2}" -f (Get-Item $Module).Length,
            (Get-Item $Module).LastWriteTime.ToString('yyyy-MM-dd HH:mm'), $stale)
Write-Host ("[ex] subjects: {0} of {1} in {2}" -f $subjects.Count, $all.Count, (Split-Path $Examples -Leaf))
Write-Host ("[ex] ladder  : decks {0}, derived from {1}" -f ($DECKS -join ', '), (Split-Path $Page -Leaf))
if ($Calibrate) {
    Write-Host '[ex] CALIBRATION: the Chapter header is mangled; every example must REFUSE.'
}

# One compile in the page's module, at one deck reservation. The stdin shape is
# the page's: the mode line, the prelude, the source, and the NUL its readers
# stop on, UTF-8 the way TextEncoder hands it over.
function Invoke-PageCompile([string]$text, [int]$decks, [string]$tag) {
    $inFile  = Join-Path $work "$tag.in"
    $outFile = Join-Path $work "$tag.out"
    $errFile = Join-Path $work "$tag.err"
    foreach ($f in @($outFile, $errFile)) {
        # A harness that reuses a previous run's file reports the PREVIOUS
        # answer whenever a run dies before writing.
        if (Test-Path $f) { Remove-Item $f -Force }
    }
    [IO.File]::WriteAllBytes($inFile,
        [Text.UTF8Encoding]::new($false).GetBytes("IR-UNI decks=$decks`n" + $text + "`0"))
    $p = Start-Process -FilePath 'wasmtime' `
         -ArgumentList @('-W', 'max-wasm-stack=16777216', $Module) -NoNewWindow -PassThru `
         -RedirectStandardInput $inFile -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    if (-not $p) { return @{ verdict = 'NOSTART'; text = ''; trap = 'wasmtime did not start' } }
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill() } catch {}
        return @{ verdict = 'TIMEOUT'; text = ''; trap = "no answer in $TimeoutSec s" }
    }
    $t = if (Test-Path $outFile) { [IO.File]::ReadAllText($outFile) } else { '' }
    $e = ''
    if (Test-Path $errFile) {
        $raw = Get-Content $errFile -Raw
        if ($null -ne $raw) { $e = ([string]$raw).Trim() }
    }
    if ($p.ExitCode -ne 0) { return @{ verdict = 'TRAP'; text = $t; trap = (($e -split "`n")[0]) } }
    if ($t -eq '') { return @{ verdict = 'EMPTY'; text = ''; trap = 'exit 0 and nothing on stdout' } }
    return @{ verdict = ''; text = $t; trap = '' }
}

# CDX4030 is the pipeline banner and prints on every run, so it is not a
# diagnostic and must not be reported as the reason for anything.
function Get-FirstDiag([string]$t) {
    $d = @($t -split "`n" | Where-Object { $_ -match 'CDX\d{4}' -and $_ -notmatch 'CDX4030' })
    if ($d.Count -eq 0) { return '' }
    return ($d[0].Trim() -replace '\s+', ' ')
}

$rows = @()
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($ex in $subjects) {
    $src = $ex.source
    if ($Calibrate) {
        # Every source carries exactly one `Chapter:` and the sabotage lands on
        # it, so the parser meets it on every subject. Trailing garbage was
        # tried first and is absorbed -- the compiler still emits IR -- which
        # is a sabotage that moves nothing and grades nothing.
        $src = $src -replace 'Chapter:', 'Chpater:'
    }
    $prelude = if ($ex.prelude) { $ex.prelude } else { '' }
    $tag = ($ex.name -replace '[^A-Za-z0-9_-]', '_')

    $r = $null; $used = 0
    foreach ($d in $DECKS) {
        $r = Invoke-PageCompile ($prelude + $src) $d $tag
        $used = $d
        if ($r.verdict -ne '') { break }
        if ($r.text.IndexOf('CDX9002') -lt 0) { break }
    }

    $verdict = $r.verdict; $note = $r.trap
    if ($verdict -eq '') {
        if ($r.text.Contains('IR-BEGIN') -and $r.text.Contains('IR-END')) {
            $verdict = 'OK'
            $note = "decks=$used"
        } elseif ($r.text.IndexOf('CDX9002') -ge 0) {
            # Distinct from an ordinary refusal: the program is not wrong, the
            # page's ladder does not reach it, and the fix is the ladder or the
            # example rather than the source.
            $verdict = 'SHORTDECK'
            $note = "still CDX9002 at the ladder's top (decks=$used)"
        } else {
            $verdict = 'REFUSED'
            $note = Get-FirstDiag $r.text
            if ($note -eq '') { $note = "no IR and no diagnostic at decks=$used" }
        }
    }
    $rows += [pscustomobject]@{ cat = $ex.cat; name = $ex.name; verdict = $verdict; note = $note }
}
$sw.Stop()

Write-Host ''
$lastCat = ''
foreach ($r in $rows) {
    if ($r.cat -ne $lastCat) { Write-Host ("  {0}" -f $r.cat); $lastCat = $r.cat }
    Write-Host ("    {0,-34} {1,-10} {2}" -f $r.name, $r.verdict, $r.note)
}

$ok  = @($rows | Where-Object { $_.verdict -eq 'OK' }).Count
$bad = @($rows | Where-Object { $_.verdict -ne 'OK' }).Count
Write-Host ''
Write-Host ("[ex] {0} compiled, {1} failed, of {2} graded in {3:N0} s" -f `
            $ok, $bad, $rows.Count, $sw.Elapsed.TotalSeconds)

if ($Calibrate) {
    # The arms are inverted: compiling a mangled chapter is the failure, and
    # every subject refusing is the pass.
    if ($ok -gt 0) {
        Write-Host ("[ex] CALIBRATION FAILED: {0} example(s) compiled with a mangled Chapter header, so this harness cannot tell a compiling example from a refused one." -f $ok)
        foreach ($r in ($rows | Where-Object { $_.verdict -eq 'OK' })) { Write-Host ("       {0}" -f $r.name) }
        exit 1
    }
    Write-Host '[ex] CALIBRATION PASSED: no example compiled with a mangled Chapter header.'
    exit 0
}
if ($bad -gt 0) { exit 1 }
exit 0
