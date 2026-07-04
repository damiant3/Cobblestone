# Shared Renode executable resolver.
#
# Renode is installed ONCE per box (default C:\Renode) rather than copied into
# every agent workspace. Each workspace keeps only the small board .repl files
# under tools/renode/codex/ (tracked in Perforce); the ~120 MB Renode runtime
# lives box-wide.
#
# Resolution order (first hit wins):
#   1. $env:CODEX_RENODE_HOME\renode.exe   (explicit override)
#   2. C:\Renode\renode.exe                (box-wide default)
#   3. $env:LOCALAPPDATA\Renode\renode.exe (per-user install)
#   4. <Repo>\tools\renode\renode.exe      (legacy per-workspace copy)
#
# To install box-wide: extract the Renode windows-portable-dotnet zip to
# C:\Renode (so C:\Renode\renode.exe exists). v1.16.1 is the tested version.

function Get-RenodeExe {
    param([string]$Repo = (Get-Location).Path)
    $candidates = @()
    if ($env:CODEX_RENODE_HOME) { $candidates += (Join-Path $env:CODEX_RENODE_HOME 'renode.exe') }
    $candidates += 'C:\Renode\renode.exe'
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Renode\renode.exe') }
    $candidates += (Join-Path $Repo 'tools\renode\renode.exe')
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    return $null
}

function Write-RenodeSkip {
    Write-Host "SKIP: Renode not found." -ForegroundColor Yellow
    Write-Host "  Install once box-wide: extract renode-1.16.1.windows-portable-dotnet.zip to C:\Renode"
    Write-Host "  (or set CODEX_RENODE_HOME to a Renode install dir)."
    Write-Host "  Releases: https://github.com/renode/renode/releases/tag/v1.16.1"
}
