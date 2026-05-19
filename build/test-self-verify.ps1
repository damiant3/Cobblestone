[CmdletBinding()]
param(
    [string]$Seed = (Join-Path $PSScriptRoot '..\seed\Codex.cdx'),
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compile = Join-Path $PSScriptRoot 'test-compile.ps1'
$run     = Join-Path $PSScriptRoot 'test-run.ps1'

if (-not (Test-Path $Seed)) { throw "Seed not found: $Seed" }
$seedBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Seed).Path)
$seedLen = $seedBytes.Length
Write-Host "Seed: $Seed ($seedLen bytes)"

$headerBytes = $seedBytes[0..223]
$headerLiteral = ($headerBytes | ForEach-Object { $_.ToString() }) -join ','

$src = @"
Chapter: SelfVerifySeed
  cites Codex chapter General
  cites Verify chapter CdxBinary
  cites Foreword chapter Ed25519
  cites Foreword chapter Sha256
  cites Foreword chapter Sha512
  cites Foreword chapter Maybe

Section: Body

  opening : [Console] Nothing = act
    let header = [$headerLiteral]
    in let magic-ok = cdx-verify-magic header
    in let pub-key = list-slice header 40 72
    in let sig = list-slice header 72 136
    in let content-hash = cdx-read-content-hash header
    in let sig-valid = ed25519-verify pub-key content-hash sig
    in let has-author = list-at header 40 + list-at header 41 + list-at header 42 + list-at header 43
    in act
      print-line ("SIZE: $seedLen")
      print-line ("MAGIC: " ++ show magic-ok)
      print-line ("SIGNATURE: " ++ show sig-valid)
      print-line ("AUTHOR-KEY-PRESENT: " ++ show (has-author > 0))
      if magic-ok then if sig-valid then
        print-line "THE SEED VERIFIES ITSELF"
      else print-line "SIGNATURE INVALID"
      else print-line "BAD MAGIC"
    end
  end
"@

$tmpSrc = [System.IO.Path]::GetTempFileName() + '.codex'
$tmpCdx = [System.IO.Path]::GetTempFileName() + '.cdx'
$tmpLog = [System.IO.Path]::GetTempFileName() + '.log'
$tmpOut = [System.IO.Path]::GetTempFileName() + '.out'

[System.IO.File]::WriteAllText($tmpSrc, $src)
Write-Host "Generated source: $([System.IO.File]::ReadAllBytes($tmpSrc).Length) bytes"

Write-Host "Compiling..." -ForegroundColor Cyan
& pwsh -File $compile -Src $tmpSrc -Out $tmpCdx -Log $tmpLog -PCore $PCore
if ($LASTEXITCODE -ne 0) {
    Write-Host "COMPILE FAILED" -ForegroundColor Red
    Get-Content $tmpLog | Write-Host
    exit 1
}

Write-Host "Running..." -ForegroundColor Cyan
& pwsh -File $run -Kernel $tmpCdx -Out $tmpOut
if ($LASTEXITCODE -ne 0) {
    Write-Host "RUN FAILED" -ForegroundColor Red
    exit 1
}

Write-Host ""
Get-Content $tmpOut
