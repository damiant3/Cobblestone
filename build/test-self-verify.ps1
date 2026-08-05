# test-self-verify.ps1 -- Compile and run a self-verification program that checks the seed CDX
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [string]$Seed = (Join-Path $PSScriptRoot '..\seed\Codex.cdx'),
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compile = (Join-Path $PSScriptRoot 'compile.ps1')
$run = (Join-Path $PSScriptRoot 'test-run.ps1')

if ((-not (Test-Path -PathType Leaf $Seed))) {
    throw ([string]'Seed not found: ' + $Seed)
}
$seedBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Seed).Path)
$seedLen = $seedBytes.Length
Write-Host ([string]([string]([string]'Seed: ' + $Seed) + ([string]' (' + $seedLen)) + ' bytes)')


$headerBytes = $seedBytes[0..223]
$headerLiteral = (($headerBytes | ForEach-Object { $_.ToString() }) -join ',')


$src = (@('Chapter: SelfVerifySeed', '  cites Verify chapter CdxBinary', '  cites Foreword chapter Ed25519', '  cites Foreword chapter Sha256', '  cites Foreword chapter Sha512', '  cites Foreword chapter Maybe', '', 'Section: Body', '', '  opening : [Console] Nothing = act', ([string]([string]'    let header = [' + $headerLiteral) + ']'), '    in let magic-ok = cdx-verify-magic header', '    in let pub-key = list-slice header 40 72', '    in let sig = list-slice header 72 136', '    in let content-hash = cdx-read-content-hash header', '    in let sig-valid = ed25519-verify pub-key content-hash sig', '    in let has-author = list-at header 40 + list-at header 41 + list-at header 42 + list-at header 43', '    in act', ([string]([string]'      print-line-uni ("SIZE: ' + $seedLen) + '")'), '      print-line-uni ("MAGIC: " & show magic-ok)', '      print-line-uni ("SIGNATURE: " & show sig-valid)', '      print-line-uni ("AUTHOR-KEY-PRESENT: " & show (has-author > 0))', '      if magic-ok then if sig-valid then', '        print-line-uni "THE SEED VERIFIES ITSELF"', '      else print-line-uni "SIGNATURE INVALID"', '      else print-line-uni "BAD MAGIC"', '    end', '  end') -join "`n")


$tmpSrc = ([string]([System.IO.Path]::GetTempFileName()) + '.codex')
$tmpCdx = ([string]([System.IO.Path]::GetTempFileName()) + '.cdx')
$tmpLog = ([string]([System.IO.Path]::GetTempFileName()) + '.log')
$tmpOut = ([string]([System.IO.Path]::GetTempFileName()) + '.out')

[System.IO.File]::WriteAllText($tmpSrc, $src)
Write-Host ([string]([string]'Generated source: ' + ([System.IO.File]::ReadAllBytes($tmpSrc)).Length) + ' bytes')


Write-Host 'Compiling...' -ForegroundColor Cyan
& 'pwsh' -File $compile -Src $tmpSrc -Out $tmpCdx -Log $tmpLog -PCore $PCore
if ((-not ($LASTEXITCODE -eq 0))) {
    Write-Host 'COMPILE FAILED' -ForegroundColor Red
    Get-Content $tmpLog
    exit 1
}

Write-Host 'Running...' -ForegroundColor Cyan
& 'pwsh' -File $run -Kernel $tmpCdx -OutFile $tmpOut
if ((-not ($LASTEXITCODE -eq 0))) {
    Write-Host 'RUN FAILED' -ForegroundColor Red
    exit 1
}


Get-Content $tmpOut
