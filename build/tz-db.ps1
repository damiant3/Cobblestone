# tz-db.ps1 -- write TZDB.BIN, every zone's TimeZone record and an index,
# from the vendored IANA release as TzSource.codex holds it.
#
# Compiles tools/tz-db.codex with -Kernel, runs it in one guest, and turns
# its hex lines into the file. codex/test/tz-db prints the SHA-256 of the
# same bytes built in the guest; this script prints the file's.
#
#   pwsh build/tz-db.ps1 -Kernel seed\Codex.cdx -Out <TZDB.BIN>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Kernel,
    [Parameter(Mandatory = $true)][string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('tz-db-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $work)
try {
    $cdx = Join-Path $work 'tz-db.cdx'
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $repo 'tools/tz-db.codex') -Out $cdx -Log (Join-Path $work 'compile.log') -Kernel $Kernel *> (Join-Path $work 'compile.txt')
    if (-not (Test-Path -LiteralPath $cdx)) { throw 'tools/tz-db.codex did not compile' }
    $raw = Join-Path $work 'out.txt'
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $cdx -OutFile $raw *> (Join-Path $work 'run.txt')
    $lines = @(Get-Content -LiteralPath $raw | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($lines.Count -lt 2 -or $lines[$lines.Count - 1] -ne 'end') { throw "tz-db tool output is not complete (no final 'end' line)" }
    $hex = ($lines[0..($lines.Count - 2)]) -join ''
    if ($hex.Length % 2 -ne 0 -or $hex -notmatch '^[0-9a-f]+$') { throw 'tz-db tool printed something other than hex' }
    $bytes = [System.Convert]::FromHexString($hex)
    [System.IO.File]::WriteAllBytes($Out, $bytes)
    Write-Host "wrote $Out ($($bytes.Length) bytes) sha256 $((Get-FileHash -LiteralPath $Out).Hash.ToLowerInvariant())"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}