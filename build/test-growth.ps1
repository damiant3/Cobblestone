# test-growth.ps1 -- source-growth pingpong regression test
#
# The survey-era killer: growing the compiler source by a few KB pushed
# a deck past its formula-sized reservation and silently miscompiled
# (+3 KB was enough at check-mul 40, and the demand-paging work that
# fixed it is archived). This test appends generated ballast to the
# concatenated compiler source and asserts the CDX pingpong stays
# byte-identical AND a diagnostic still prints clean -- the exact class
# of failure that consumed a week in 2026-07.
#
# Usage: build/test-growth.ps1 [-BallastKB 86]
#   Standalone; not part of the default gate (costs two self-compiles,
#   ~50s). Run it when touching the phase allocator, deck floors, the
#   demand-paging boot path, or before a copy-up that grows the
#   compiler source substantially.

param([int]$BallastKB = 86)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutDir = Join-Path $Repo 'build\output'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Concat = Join-Path $PSScriptRoot 'concat-codex-self.ps1'
$Compile = Join-Path $PSScriptRoot 'compile.ps1'
$Seed = Join-Path $Repo 'seed\Codex.cdx'
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
$GrowthSrc = Join-Path $OutDir 'Growth.codex'
$G1 = Join-Path $OutDir 'Growth1.cdx'
$G2 = Join-Path $OutDir 'Growth2.cdx'

function Get-CdxContentHash([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return (($bytes[8..39]) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Invoke-GrowthCompile([string]$Kernel, [string]$Src, [string]$Out, [string]$Log) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Stage0) | Out-Null
    Copy-Item -Force $Kernel $Stage0
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $Compile -Src $Src -Out $Out -Log $Log -Repl 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    return ($LASTEXITCODE -eq 0)
}

# -- 1. Concat the compiler source
& pwsh -NoProfile -File $Concat -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $GrowthSrc | Out-Null
if (-not (Test-Path $GrowthSrc)) { Write-Host 'FAIL: concat produced no output'; exit 1 }
$baseLen = (Get-Item $GrowthSrc).Length

# -- 2. Append ballast: a chapter of generated defs, ~80 bytes each
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Chapter: GrowthBallast')
[void]$sb.AppendLine('')
[void]$sb.AppendLine(' Generated ballast for the source-growth pingpong regression test.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Section: Ballast')
[void]$sb.AppendLine('')
$i = 0
while ($sb.Length -lt $BallastKB * 1024) {
    [void]$sb.AppendLine("  growth-ballast-$i : Integer -> Integer")
    [void]$sb.AppendLine("  growth-ballast-$i (x) = x + $i")
    [void]$sb.AppendLine('')
    $i++
}
Add-Content -Path $GrowthSrc -Value $sb.ToString() -NoNewline -Encoding UTF8
$grownLen = (Get-Item $GrowthSrc).Length
Write-Host "Growth source: $baseLen -> $grownLen bytes (+$([Math]::Round(($grownLen-$baseLen)/1024)) KB, $i ballast defs)"

try {
    # -- 3. Pingpong: seed compiles growth source, that output compiles it again
    Write-Host 'Pass 1: seed compiles the grown source...'
    if (-not (Invoke-GrowthCompile $Seed $GrowthSrc $G1 (Join-Path $OutDir 'growth1.log'))) {
        Write-Host 'FAIL: growth pass 1 did not compile'
        Get-Content (Join-Path $OutDir 'growth1.log') -ErrorAction SilentlyContinue | Select-Object -Last 8 | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host 'Pass 2: pass-1 output compiles the grown source...'
    if (-not (Invoke-GrowthCompile $G1 $GrowthSrc $G2 (Join-Path $OutDir 'growth2.log'))) {
        Write-Host 'FAIL: growth pass 2 did not compile'
        Get-Content (Join-Path $OutDir 'growth2.log') -ErrorAction SilentlyContinue | Select-Object -Last 8 | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    $h1 = Get-CdxContentHash $G1
    $h2 = Get-CdxContentHash $G2
    if ($h1 -ne $h2) {
        Write-Host 'FAIL: growth pingpong -- pass 1 !== pass 2 (the survey-era killer is back)'
        Write-Host "  pass1: $((Get-Item $G1).Length) bytes  $h1"
        Write-Host "  pass2: $((Get-Item $G2).Length) bytes  $h2"
        exit 1
    }
    Write-Host "Growth pingpong byte-identical ($h1)"

    # -- 4. The historic victim: a diagnostic must print clean from the grown compiler
    $diagLog = Join-Path $OutDir 'growth-diag.log'
    Copy-Item -Force $G1 $Stage0
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $Compile -Src (Join-Path $Repo 'codex\test\errors\type-mismatch.codex') -Out (Join-Path $OutDir 'growth-diag.cdx') -Log $diagLog 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    $diagText = Get-Content $diagLog -Raw -ErrorAction SilentlyContinue
    if ($diagText -notmatch 'error CDX2001') {
        Write-Host 'FAIL: grown compiler garbles or drops the CDX2001 diagnostic'
        Get-Content $diagLog -ErrorAction SilentlyContinue | Select-Object -Last 8 | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host 'Diagnostic prints clean from the grown compiler'
    Write-Host "PASS: growth regression (+$BallastKB KB pingpong + diagnostic)"
    exit 0
}
finally {
    # Leave the workspace kernel as the real seed regardless of outcome
    Copy-Item -Force $Seed $Stage0 -ErrorAction SilentlyContinue
}
