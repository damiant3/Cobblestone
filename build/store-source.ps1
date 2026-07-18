# Store a real source file into the fact store on a disk image, signed and
# content-addressed, using the cdx-store tool. This is the ingest half of the
# repository: a file a person names on the command line goes into the store as
# a signed SourceDefinition, with no Perforce in the chain.
#
# The wire is UTF-8 and the store is CCE; cdx-store converts (hashing is not an
# I/O function, so the canonical address is the SHA-256 of the CCE bytes). The
# disk is attached directly so codex-vm's durable writes land in it.
#
# Usage:
#   build/store-source.ps1 -Src codex/test/factorial.codex -Disk store.disk
#   build/store-source.ps1 -Src X -Disk d.img -Path a/b.codex -Quire Q -Chapter C
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Disk,
    [string]$Path = '',
    [string]$Quire = '',
    [string]$Chapter = '',
    [string]$StoreKernel = '',
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

if (-not (Test-Path -PathType Leaf $Src)) { Write-Host "FAIL: source not found: $Src" -ForegroundColor Red; exit 1 }

# Metadata: the path is the store key. Quire defaults to the parent directory,
# chapter to the file's `Chapter:` header — the same conventions the tree uses.
$srcItem = Get-Item $Src
if (-not $Path)  { $Path = (Resolve-Path $Src).Path.Substring($root.Length + 1).Replace('\','/') }
if (-not $Quire) { $Quire = (Split-Path (Split-Path $Src -Parent) -Leaf); if ($Quire) { $Quire = $Quire.Substring(0,1).ToUpper() + $Quire.Substring(1) } else { $Quire = 'Unknown' } }
if (-not $Chapter) {
    foreach ($line in (Get-Content -TotalCount 8 $Src)) {
        if ($line -match '^\s*Chapter:\s*(.+?)\s*$') { $Chapter = $matches[1]; break }
    }
    if (-not $Chapter) { $Chapter = [System.IO.Path]::GetFileNameWithoutExtension($srcItem.Name) }
}

# The store tool, compiled once (build-output/cdx-store.cdx) unless given.
$kArg = @(); if ($Kernel) { $kArg = @('-Kernel', $Kernel) }
if (-not $StoreKernel) {
    $StoreKernel = Join-Path 'build-output' 'cdx-store.cdx'
    if (-not (Test-Path $StoreKernel)) {
        New-Item -ItemType Directory -Force (Split-Path $StoreKernel) | Out-Null
        & pwsh -NoProfile -File 'build/compile.ps1' -Src 'tools/cdx-store.codex' -Out $StoreKernel -Log (Join-Path 'build-output' 'cdx-store.log') @kArg | Out-Null
        if (-not (Test-Path $StoreKernel)) { Write-Host 'FAIL: cdx-store did not compile' -ForegroundColor Red; exit 1 }
    }
}

# Feed: one header line, then the source, then a NUL (read-serial-cce stops on it).
# CRLF is a legacy concern handled at this boundary: CCE has no carriage
# return, so it is stripped before the bytes ever reach the store.
$content = [System.IO.File]::ReadAllText((Resolve-Path $Src)) -replace "`r", ''
$wire = "$Path|$Quire|$Chapter`n$content" + [char]0
$inFile = [System.IO.Path]::GetTempFileName()
$outFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($inFile, $wire, [System.Text.UTF8Encoding]::new($false))

if (-not (Test-Path $Disk)) { [System.IO.File]::WriteAllBytes((Join-Path $root $Disk), (New-Object byte[] 1048576)) }

$vm = Join-Path 'tools' 'codex-vm.exe'
try {
    $p = Start-Process -FilePath $vm -ArgumentList @('-kernel', $StoreKernel, '-input', $inFile, '-output', $outFile, '-disk', $Disk, '-mem', '3072', '-headless') -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(90000)) { try { $p.Kill() } catch {} ; Write-Host 'FAIL: store VM did not exit' -ForegroundColor Red; exit 1 }
    $out = ((Get-Content $outFile -Raw -ErrorAction SilentlyContinue) -replace "`r",'' -replace "^[\x00-\x08\x0e-\x1f]+",'').Trim()
    if ($out -notmatch 'stored [0-9a-f]{64}') { Write-Host "FAIL: store did not confirm: $out" -ForegroundColor Red; exit 1 }
    Write-Host $out
    exit 0
} finally {
    Remove-Item -Force $inFile, $outFile -ErrorAction SilentlyContinue
}
